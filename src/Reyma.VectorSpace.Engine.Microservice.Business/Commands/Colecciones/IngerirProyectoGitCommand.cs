using MediatR;
using Microsoft.Extensions.Logging;
using Reyma.VectorSpace.Engine.Microservice.Business.Colecciones;
using Reyma.VectorSpace.Engine.Microservice.Business.Models.Requests;
using Reyma.VectorSpace.Engine.Microservice.Business.Models.Responses;
using Reyma.VectorSpace.Engine.Microservice.Business.Motor;
using Reyma.VectorSpace.Engine.Microservice.Business.Validacion;

namespace Reyma.VectorSpace.Engine.Microservice.Business.Commands.Colecciones;

/// <summary>
/// Origen 'proyecto' desde una URL Git (1.7.6, decidido en 1.7.3). Mismo patron asincrono que
/// <see cref="IngerirProyectoZipCommand"/> (202 + <see cref="IRegistroDeIngestasProyecto"/> + polling por
/// <c>GetEstadoIngestaQuery</c>): validar la URL contra la lista blanca es sincrono e instantaneo (no toca la red);
/// clonar, escanear y vectorizar corre en segundo plano porque un repo real tarda segundos a minutos (1.7.3).
/// </summary>
public sealed record IngerirProyectoGitCommand(string Coleccion, string? Url, string? CarpetaRaiz, string? Perfil, bool? Resumen = null)
    : IRequest<IngestaResponse>, ISolicitudDeColeccion, IValidable
{
    public OperacionColeccion Operacion => OperacionColeccion.Ingestar;

    public IEnumerable<string> Errores()
    {
        if (string.IsNullOrWhiteSpace(Url))
            yield return "Url es obligatoria.";
        else if (Url.Length > Limites.MaxCaracteresUrlGit)
            yield return $"Url admite como maximo {Limites.MaxCaracteresUrlGit} caracteres.";

        if (CarpetaRaiz is not null && CarpetaRaiz.Length > Limites.MaxCaracteresRuta)
            yield return $"CarpetaRaiz admite como maximo {Limites.MaxCaracteresRuta} caracteres.";

        if (Perfil is not null && Perfil.Trim().Length == 0)
            yield return "Perfil no puede ser una cadena vacia si se especifica.";
        else if (Perfil is { Length: > 0 } && Perfil.Length > Limites.MaxCaracteresPerfil)
            yield return $"Perfil admite como maximo {Limites.MaxCaracteresPerfil} caracteres.";
    }

    public sealed class IngerirProyectoGitCommandHandler(
        IMotorDeIngestaProyectoGit motor,
        ILimiteDeIngestas limite,
        IRegistroDeIngestasProyecto registro,
        ILogger<IngerirProyectoGitCommandHandler> logger)
        : IRequestHandler<IngerirProyectoGitCommand, IngestaResponse>
    {
        public async Task<IngestaResponse> Handle(IngerirProyectoGitCommand request, CancellationToken cancellationToken)
        {
            // 1. Validar la URL contra la lista blanca ANTES de tocar la red o el limite de admision: una URL fuera
            // de la lista (u otro origen no permitido: localhost, IP interna, otro esquema, etc.) responde
            // BadRequestException (400) aqui, sin abrir ningun socket, sin crear registro de estado ni tocar el cupo.
            var uri = motor.ValidarUrlGit(request.Url);

            // 2. Admision: mismo cupo global que 'documentos' y el .zip (ambos origenes compiten por el mismo
            // limite del sidecar de embeddings).
            var permiso = await limite.EntrarAsync(cancellationToken);
            var ingestaId = registro.Crear(request.Coleccion, documentosRecibidos: 0);

            // 3. Clonar + escanear + vectorizar corre en segundo plano, fuera del ciclo de vida de esta solicitud HTTP
            // (CancellationToken.None): cerrar la conexion del cliente no debe abortar una ingesta ya admitida.
            _ = ProcesarEnSegundoPlanoAsync(request.Coleccion, uri, request.CarpetaRaiz, request.Perfil, request.Resumen == true, ingestaId, permiso);

            return new IngestaResponse(ingestaId, request.Coleccion, EstadosIngestaProyecto.Pendiente,
                DocumentosRecibidos: 0, DocumentosIndexados: 0, DocumentosSinCambios: 0);
        }

        private async Task ProcesarEnSegundoPlanoAsync(
            string coleccion, Uri uri, string? carpetaRaiz, string? perfil, bool resumen, Guid ingestaId, IDisposable permiso)
        {
            try
            {
                registro.MarcarProcesando(ingestaId);
                var resultado = await motor.ProcesarGitAsync(coleccion, uri, carpetaRaiz, perfil, resumen, CancellationToken.None);
                registro.MarcarCompletada(ingestaId, resultado);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Fallo la ingesta de proyecto por URL Git '{IngestaId}' en '{Coleccion}'.", ingestaId, coleccion);
                registro.MarcarError(ingestaId, ex.Message);
            }
            finally
            {
                permiso.Dispose();
            }
        }
    }
}
