using MediatR;
using Reyma.VectorSpace.Engine.Microservice.Business.Colecciones;
using Reyma.VectorSpace.Engine.Microservice.Business.Commands.Colecciones;
using Reyma.VectorSpace.Engine.Microservice.Business.Models.Responses;
using Reyma.VectorSpace.Engine.Microservice.Business.Motor;
using Reyma.VectorSpace.Engine.Microservice.Business.Validacion;

namespace Reyma.VectorSpace.Engine.Microservice.Business.Queries.Colecciones;

public sealed record BuscarEnColeccionQuery(string Coleccion, string? Consulta, int? TopK)
    : IRequest<BusquedaResponse>, ISolicitudDeColeccion, IValidable
{
    public OperacionColeccion Operacion => OperacionColeccion.Consultar;

    public IEnumerable<string> Errores()
    {
        if (string.IsNullOrWhiteSpace(Consulta))
            yield return "Consulta es obligatoria.";
        else if (Consulta.Length > Limites.MaxCaracteresConsulta)
            yield return $"Consulta admite como maximo {Limites.MaxCaracteresConsulta} caracteres.";
        if (TopK is < 1 or > Limites.MaxTopK)
            yield return $"TopK debe estar entre 1 y {Limites.MaxTopK}.";
    }

    public sealed class BuscarEnColeccionQueryHandler(IMotorDeBusqueda motor)
        : IRequestHandler<BuscarEnColeccionQuery, BusquedaResponse>
    {
        public Task<BusquedaResponse> Handle(BuscarEnColeccionQuery request, CancellationToken cancellationToken) =>
            motor.BuscarAsync(request.Coleccion, request.Consulta!, request.TopK ?? Limites.TopKPorDefecto, cancellationToken);
    }
}
