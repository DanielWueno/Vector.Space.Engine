using MediatR;
using Reyma.VectorSpace.Engine.Microservice.Business.Colecciones;
using Reyma.VectorSpace.Engine.Microservice.Business.Models.Requests;
using Reyma.VectorSpace.Engine.Microservice.Business.Models.Responses;
using Reyma.VectorSpace.Engine.Microservice.Business.Motor;
using Reyma.VectorSpace.Engine.Microservice.Business.Validacion;

namespace Reyma.VectorSpace.Engine.Microservice.Business.Commands.Colecciones;

public sealed record IngerirCommand(string Coleccion, OrigenIngestaRequest? Origen, string? Perfil, bool? Resumen = null)
    : IRequest<IngestaResponse>, ISolicitudDeColeccion, IValidable
{
    public OperacionColeccion Operacion => OperacionColeccion.Ingestar;

    public IEnumerable<string> Errores()
    {
        if (Perfil is not null && Perfil.Trim().Length == 0)
            yield return "Perfil no puede ser una cadena vacia si se especifica.";
        else if (Perfil is { Length: > 0 } && Perfil.Length > Limites.MaxCaracteresPerfil)
            yield return $"Perfil admite como maximo {Limites.MaxCaracteresPerfil} caracteres.";

        if (Origen is null)
        {
            yield return "Origen es obligatorio.";
            yield break;
        }

        switch (Origen.Tipo)
        {
            case OrigenIngestaRequest.TipoDocumentos:
                if (Origen.Ruta is not null)
                    yield return "Un origen 'documentos' no lleva Ruta.";
                foreach (var error in ErroresDocumentos(Origen.Documentos))
                    yield return error;
                break;

            case OrigenIngestaRequest.TipoRuta:
                if (Origen.Documentos is not null)
                    yield return "Un origen 'ruta' no lleva Documentos.";
                if (string.IsNullOrWhiteSpace(Origen.Ruta))
                    yield return "Un origen 'ruta' necesita Ruta.";
                break;

            default:
                yield return $"Origen.Tipo debe ser '{OrigenIngestaRequest.TipoDocumentos}' o '{OrigenIngestaRequest.TipoRuta}'.";
                break;
        }
    }

    private static IEnumerable<string> ErroresDocumentos(List<DocumentoRequest>? documentos)
    {
        if (documentos is null || documentos.Count == 0)
        {
            yield return "Documentos es obligatorio y no puede estar vacio.";
            yield break;
        }

        if (documentos.Count > Limites.MaxDocumentosPorIngesta)
            yield return $"Documentos admite como maximo {Limites.MaxDocumentosPorIngesta} elementos.";
        if (documentos.Any(d => d is null || string.IsNullOrWhiteSpace(d.Ruta)))
            yield return "Cada documento necesita Ruta.";
        else if (documentos.Any(d => d.Ruta!.Length > Limites.MaxCaracteresRuta))
            yield return $"Cada Ruta admite como maximo {Limites.MaxCaracteresRuta} caracteres.";
        else if (documentos.Select(d => d.Ruta).Distinct(StringComparer.Ordinal).Count() != documentos.Count)
            yield return "Las rutas de los documentos no pueden repetirse en una misma ingesta.";
        if (documentos.Any(d => d?.Contenido is null))
            yield return "Cada documento necesita Contenido (puede ser una cadena vacia).";
        else if (documentos.Any(d => d.Contenido!.Length > Limites.MaxCaracteresPorDocumento))
            yield return $"Cada Contenido admite como maximo {Limites.MaxCaracteresPorDocumento} caracteres.";
    }

    public sealed class IngerirCommandHandler(IMotorDeIngesta motor, ILimiteDeIngestas limite)
        : IRequestHandler<IngerirCommand, IngestaResponse>
    {
        public async Task<IngestaResponse> Handle(IngerirCommand request, CancellationToken cancellationToken)
        {
            // La alternativa de 1.7 (disparar sobre una ruta accesible al servicio) queda en el contrato pero sin
            // implementar: leer rutas del servidor exige antes decidir raices permitidas por coleccion.
            if (request.Origen!.Tipo == OrigenIngestaRequest.TipoRuta)
                throw new NoImplementadoException("El origen 'ruta' esta reservado para el modelo de consumo de ingesta (item 1.7).");

            // 1.7: despues de autorizar y validar (behaviors), asi un 403 o un 400 no ocupan lugar en la cola.
            using var permiso = await limite.EntrarAsync(cancellationToken);
            return await motor.IngerirAsync(request.Coleccion, request.Origen.Documentos!, request.Perfil, request.Resumen == true, cancellationToken);
        }
    }
}
