using MediatR;
using Reyma.VectorSpace.Engine.Microservice.Business.Commands.Colecciones;
using Reyma.VectorSpace.Engine.Microservice.Business.Models.Responses;
using Reyma.VectorSpace.Engine.Microservice.Business.Motor;
using Reyma.VectorSpace.Engine.Microservice.Business.Validacion;

namespace Reyma.VectorSpace.Engine.Microservice.Business.Queries.Colecciones;

public sealed record BuscarEnColeccionQuery(string Coleccion, string? Consulta, int? TopK, string? Perfil)
    : IRequest<BusquedaResponse>, IValidable
{
    public IEnumerable<string> Errores()
    {
        if (string.IsNullOrWhiteSpace(Consulta))
            yield return "Consulta es obligatoria.";
        else if (Consulta.Length > Limites.MaxCaracteresConsulta)
            yield return $"Consulta admite como maximo {Limites.MaxCaracteresConsulta} caracteres.";
        if (TopK is < 1 or > Limites.MaxTopK)
            yield return $"TopK debe estar entre 1 y {Limites.MaxTopK}.";

        if (Perfil is not null && Perfil.Trim().Length == 0)
            yield return "Perfil no puede ser una cadena vacia si se especifica.";
        else if (Perfil is { Length: > 0 } && Perfil.Length > Limites.MaxCaracteresPerfil)
            yield return $"Perfil admite como maximo {Limites.MaxCaracteresPerfil} caracteres.";
    }

    public sealed class BuscarEnColeccionQueryHandler(IMotorDeBusqueda motor)
        : IRequestHandler<BuscarEnColeccionQuery, BusquedaResponse>
    {
        public Task<BusquedaResponse> Handle(BuscarEnColeccionQuery request, CancellationToken cancellationToken) =>
            motor.BuscarAsync(request.Coleccion, request.Consulta!, request.TopK ?? Limites.TopKPorDefecto, request.Perfil, cancellationToken);
    }
}
