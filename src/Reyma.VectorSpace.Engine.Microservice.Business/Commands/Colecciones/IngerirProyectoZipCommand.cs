using MediatR;
using Microsoft.Extensions.Logging;
using Reyma.VectorSpace.Engine.Microservice.Business.Models.Requests;
using Reyma.VectorSpace.Engine.Microservice.Business.Models.Responses;
using Reyma.VectorSpace.Engine.Microservice.Business.Motor;
using Reyma.VectorSpace.Engine.Microservice.Business.Validacion;

namespace Reyma.VectorSpace.Engine.Microservice.Business.Commands.Colecciones;

/// <summary>
/// Origen 'proyecto' desde un .zip subido (1.7.5, decidido en 1.7.3). A diferencia de <see cref="IngerirCommand"/>
/// (origen 'documentos', sincrono), esta ingesta es asincrona: el handler valida el .zip, admite la solicitud contra
/// <see cref="ILimiteDeIngestas"/> y devuelve de inmediato un <see cref="IngestaResponse"/> en estado "pendiente"
/// (el controller la convierte en 202); el trabajo pesado (chunking, vectorizacion, barrido de ausentes) corre en
/// segundo plano y actualiza <see cref="IRegistroDeIngestasProyecto"/>, consultable por <c>GetEstadoIngestaQuery</c>.
/// </summary>
public sealed record IngerirProyectoZipCommand(string Coleccion, byte[]? Zip, string? CarpetaRaiz, string? Perfil, bool? Resumen = null)
    : IRequest<IngestaResponse>, IValidable
{
    public IEnumerable<string> Errores()
    {
        if (Zip is null || Zip.Length == 0)
            yield return "Zip es obligatorio y no puede estar vacio.";
        else if (Zip.Length > Limites.MaxBytesZipProyecto)
            yield return $"El .zip admite como maximo {Limites.MaxBytesZipProyecto} bytes.";

        if (CarpetaRaiz is not null && CarpetaRaiz.Length > Limites.MaxCaracteresRuta)
            yield return $"CarpetaRaiz admite como maximo {Limites.MaxCaracteresRuta} caracteres.";

        if (Perfil is not null && Perfil.Trim().Length == 0)
            yield return "Perfil no puede ser una cadena vacia si se especifica.";
        else if (Perfil is { Length: > 0 } && Perfil.Length > Limites.MaxCaracteresPerfil)
            yield return $"Perfil admite como maximo {Limites.MaxCaracteresPerfil} caracteres.";
    }

    public sealed class IngerirProyectoZipCommandHandler(
        IMotorDeIngestaProyecto motor,
        ILimiteDeIngestas limite,
        IRegistroDeIngestasProyecto registro,
        ILogger<IngerirProyectoZipCommandHandler> logger)
        : IRequestHandler<IngerirProyectoZipCommand, IngestaResponse>
    {
        public async Task<IngestaResponse> Handle(IngerirProyectoZipCommand request, CancellationToken cancellationToken)
        {
            // 1. Leer y validar el .zip: rapido (<0,1 s para un repo real, medido en 1.7.3) y sincrono. Un .zip
            // invalido o malicioso lanza BadRequestException (400) aqui, sin tocar el limite de admision ni crear
            // ningun registro de estado.
            var documentos = motor.LeerProyectoZip(request.Zip!, request.CarpetaRaiz);

            // 2. Admision: mismo limite global que el origen 'documentos' (1.7), para que ambos orígenes compitan por
            // el mismo cupo del sidecar de embeddings. Si esta saturado, EntrarAsync lanza IngestasSaturadasException
            // (429) antes de crear ningun registro de estado.
            var permiso = await limite.EntrarAsync(cancellationToken);
            var ingestaId = registro.Crear(request.Coleccion, documentos.Count);

            // 3. El trabajo pesado corre en segundo plano, fuera del ciclo de vida de esta solicitud HTTP (CancellationToken.None):
            // cerrar la conexion del cliente no debe abortar una ingesta que ya se admitio.
            _ = ProcesarEnSegundoPlanoAsync(request.Coleccion, documentos, request.Perfil, request.Resumen == true, ingestaId, permiso);

            return new IngestaResponse(ingestaId, request.Coleccion, EstadosIngestaProyecto.Pendiente,
                documentos.Count, DocumentosIndexados: 0, DocumentosSinCambios: 0);
        }

        private async Task ProcesarEnSegundoPlanoAsync(
            string coleccion, IReadOnlyList<DocumentoRequest> documentos, string? perfil, bool resumen, Guid ingestaId, IDisposable permiso)
        {
            try
            {
                registro.MarcarProcesando(ingestaId);
                var resultado = await motor.ProcesarAsync(coleccion, documentos, perfil, resumen, CancellationToken.None);
                registro.MarcarCompletada(ingestaId, resultado);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Fallo la ingesta de proyecto '{IngestaId}' en '{Coleccion}'.", ingestaId, coleccion);
                registro.MarcarError(ingestaId, ex.Message);
            }
            finally
            {
                permiso.Dispose();
            }
        }
    }
}
