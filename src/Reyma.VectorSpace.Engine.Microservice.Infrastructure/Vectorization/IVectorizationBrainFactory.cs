using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Reyma.Exceptions.Api;
using Reyma.VectorSpace.Engine.Microservice.Infrastructure.Abstractions;

namespace Reyma.VectorSpace.Engine.Microservice.Infrastructure.Vectorization;

/// <summary>Un perfil de embeddings ya resuelto: su nombre canonico (nunca null/vacio) y el motor listo para usarlo.</summary>
public sealed record PerfilVectorizacionResuelto(string Nombre, IVectorizationBrain Brain);

/// <summary>
/// Resuelve el <see cref="IVectorizationBrain"/> de un perfil de embeddings por nombre (1.7.4). El perfil por defecto
/// (<see cref="MotorOptions.PerfilEmbeddingsPorDefecto"/>) reutiliza el motor singleton ya registrado por el porte de
/// 1.4/1.8/1.13 (mismo <see cref="HttpClient"/> nombrado de siempre); los perfiles adicionales declarados en
/// <c>Motor:Embeddings:Perfiles</c> (p. ej. el de AltheIA/bge-m3) se resuelven contra su propio HttpClient nombrado,
/// registrado en <see cref="MotorServiceCollectionExtensions.AddMotorVectorial"/>.
/// </summary>
public interface IVectorizationBrainFactory
{
    /// <summary>Nombre del perfil cuando una solicitud no pide ninguno explicito.</summary>
    string PerfilPorDefecto { get; }

    /// <summary>Todos los perfiles declarados en la configuracion de esta instancia: el por defecto mas los adicionales.</summary>
    IReadOnlyCollection<string> NombresDisponibles { get; }

    /// <summary>
    /// Resuelve <paramref name="perfilSolicitado"/> (null o vacio = perfil por defecto). Lanza
    /// <see cref="BadRequestException"/> con la lista de perfiles disponibles si el nombre no esta registrado en la
    /// configuracion de esta instancia del servicio (1.7.4, criterio 6: 400, nunca 503).
    /// </summary>
    PerfilVectorizacionResuelto Resolver(string? perfilSolicitado);
}

internal sealed class VectorizationBrainFactory : IVectorizationBrainFactory
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IOptions<MotorOptions> _options;
    private readonly ILoggerFactory _loggerFactory;
    private readonly IVectorizationBrain _porDefecto;

    public VectorizationBrainFactory(
        IHttpClientFactory httpClientFactory, IOptions<MotorOptions> options, ILoggerFactory loggerFactory, IVectorizationBrain porDefecto)
    {
        _httpClientFactory = httpClientFactory;
        _options = options;
        _loggerFactory = loggerFactory;
        _porDefecto = porDefecto;
    }

    public string PerfilPorDefecto => MotorOptions.PerfilEmbeddingsPorDefecto;

    public IReadOnlyCollection<string> NombresDisponibles => TeiVectorizationBrain.NombresDePerfiles(_options.Value);

    public PerfilVectorizacionResuelto Resolver(string? perfilSolicitado)
    {
        if (string.IsNullOrWhiteSpace(perfilSolicitado) || perfilSolicitado == PerfilPorDefecto)
            return new PerfilVectorizacionResuelto(PerfilPorDefecto, _porDefecto);

        if (!_options.Value.Embeddings.Perfiles.TryGetValue(perfilSolicitado, out var configuracion))
            throw new BadRequestException(
                $"El perfil de embeddings '{perfilSolicitado}' no esta registrado. Perfiles disponibles: {string.Join(", ", NombresDisponibles)}.");

        var http = _httpClientFactory.CreateClient(NombreCliente(perfilSolicitado));
        var brain = new TeiVectorizationBrain(http, configuracion, NombresDisponibles, _loggerFactory.CreateLogger<TeiVectorizationBrain>());
        return new PerfilVectorizacionResuelto(perfilSolicitado, brain);
    }

    /// <summary>Nombre del HttpClient nombrado de un perfil adicional (1.7.4). El por defecto usa <see cref="TeiVectorizationBrain.HttpClientName"/>.</summary>
    internal static string NombreCliente(string perfil) => $"motor-embeddings:{perfil}";
}
