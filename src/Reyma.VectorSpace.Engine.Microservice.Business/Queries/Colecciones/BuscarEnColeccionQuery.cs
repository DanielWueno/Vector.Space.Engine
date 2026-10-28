using MediatR;
using Reyma.VectorSpace.Engine.Microservice.Business.Commands.Colecciones;
using Reyma.VectorSpace.Engine.Microservice.Business.Models.Responses;
using Reyma.VectorSpace.Engine.Microservice.Business.Motor;
using Reyma.VectorSpace.Engine.Microservice.Business.Validacion;

namespace Reyma.VectorSpace.Engine.Microservice.Business.Queries.Colecciones;

/// <summary>
/// <see cref="SimbolosDefinidos"/> (item 1.18): filtro exacto opcional, 1 a 50 cadenas no vacias (fuera de ese rango,
/// 400). Ausente: la busqueda se comporta igual que antes de este item (mismos ids, orden y puntuaciones). No
/// implementa salto de simbolo, tope de semillas ni refusion: eso es responsabilidad del consumidor.
/// </summary>
public sealed record BuscarEnColeccionQuery(string Coleccion, string? Consulta, int? TopK, string? Perfil, IReadOnlyList<string>? SimbolosDefinidos = null)
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

        if (SimbolosDefinidos is not null)
        {
            if (SimbolosDefinidos.Count is < 1 or > Limites.MaxSimbolosDefinidosPorConsulta)
                yield return $"SimbolosDefinidos admite entre 1 y {Limites.MaxSimbolosDefinidosPorConsulta} cadenas.";
            else if (SimbolosDefinidos.Any(string.IsNullOrWhiteSpace))
                yield return "SimbolosDefinidos no admite cadenas vacias.";
        }
    }

    public sealed class BuscarEnColeccionQueryHandler(IMotorDeBusqueda motor)
        : IRequestHandler<BuscarEnColeccionQuery, BusquedaResponse>
    {
        public Task<BusquedaResponse> Handle(BuscarEnColeccionQuery request, CancellationToken cancellationToken) =>
            motor.BuscarAsync(request.Coleccion, request.Consulta!, request.TopK ?? Limites.TopKPorDefecto, request.Perfil,
                request.SimbolosDefinidos, cancellationToken);
    }
}
