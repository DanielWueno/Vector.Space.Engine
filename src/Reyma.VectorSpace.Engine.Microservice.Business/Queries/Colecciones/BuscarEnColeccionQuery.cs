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
/// <see cref="Espacio"/> (item 1.20): <c>"codigo"</c> (por defecto, ausente) o <c>"resumen"</c>; cualquier otro valor,
/// 400. Con <c>"resumen"</c> no se admite <see cref="Perfil"/>: el perfil de esa consulta lo fija la instancia
/// (<c>Motor:Resumen:PerfilEmbeddings</c>), y un perfil enviado responde 400 en vez de ignorarse en silencio.
/// </summary>
public sealed record BuscarEnColeccionQuery(
    string Coleccion, string? Consulta, int? TopK, string? Perfil, IReadOnlyList<string>? SimbolosDefinidos = null, string? Espacio = null)
    : IRequest<BusquedaResponse>, IValidable
{
    public const string EspacioCodigo = "codigo";
    public const string EspacioResumen = "resumen";

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

        if (Espacio is not null and not EspacioCodigo and not EspacioResumen)
            yield return $"Espacio admite '{EspacioCodigo}' (por defecto) o '{EspacioResumen}'.";
        else if (Espacio == EspacioResumen && Perfil is not null)
            yield return $"Perfil no se admite con Espacio '{EspacioResumen}': lo fija la configuracion del servicio (Motor:Resumen:PerfilEmbeddings).";
    }

    public sealed class BuscarEnColeccionQueryHandler(IMotorDeBusqueda motor)
        : IRequestHandler<BuscarEnColeccionQuery, BusquedaResponse>
    {
        public Task<BusquedaResponse> Handle(BuscarEnColeccionQuery request, CancellationToken cancellationToken) =>
            motor.BuscarAsync(request.Coleccion, request.Consulta!, request.TopK ?? Limites.TopKPorDefecto, request.Perfil,
                request.SimbolosDefinidos,
                request.Espacio == EspacioResumen ? EspacioDeBusqueda.Resumen : EspacioDeBusqueda.Codigo,
                cancellationToken);
    }
}
