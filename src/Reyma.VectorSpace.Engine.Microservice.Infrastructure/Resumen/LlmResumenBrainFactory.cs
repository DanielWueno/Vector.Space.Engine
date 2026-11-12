using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Reyma.Exceptions.Api;

namespace Reyma.VectorSpace.Engine.Microservice.Infrastructure.Resumen;

/// <summary>Un perfil de LLM ya resuelto: su nombre canonico (nunca null/vacio) y el cliente listo para usarlo.</summary>
public sealed record PerfilLlmResuelto(string Nombre, ILlmResumenBrain Brain);

/// <summary>
/// Resuelve el <see cref="ILlmResumenBrain"/> de un perfil de LLM por nombre, mismo patron que
/// <c>IVectorizationBrainFactory</c> para embeddings: el perfil por defecto (<see cref="LlmOptions.Nombre"/>) reutiliza
/// el singleton ya registrado (y sustituible en pruebas); los adicionales de <c>Motor:Llm:Perfiles</c> se resuelven
/// contra su propio HttpClient nombrado, registrado en <see cref="MotorServiceCollectionExtensions.AddMotorVectorial"/>.
/// </summary>
public interface ILlmResumenBrainFactory
{
    /// <summary>Nombre del perfil cuando una ingesta no pide ninguno explicito.</summary>
    string PerfilPorDefecto { get; }

    /// <summary>Todos los perfiles declarados en la configuracion de esta instancia: el por defecto mas los adicionales.</summary>
    IReadOnlyCollection<string> NombresDisponibles { get; }

    /// <summary>
    /// Resuelve <paramref name="perfilSolicitado"/> (null o vacio = perfil por defecto). Lanza
    /// <see cref="BadRequestException"/> con la lista de perfiles disponibles si el nombre no esta registrado.
    /// </summary>
    PerfilLlmResuelto Resolver(string? perfilSolicitado);
}

internal sealed class LlmResumenBrainFactory(
    IHttpClientFactory httpClientFactory, IOptions<MotorOptions> options, ILoggerFactory loggerFactory, ILlmResumenBrain porDefecto)
    : ILlmResumenBrainFactory
{
    public string PerfilPorDefecto => options.Value.Llm.Nombre;

    public IReadOnlyCollection<string> NombresDisponibles => options.Value.Llm.NombresDePerfiles();

    public PerfilLlmResuelto Resolver(string? perfilSolicitado)
    {
        if (string.IsNullOrWhiteSpace(perfilSolicitado) || perfilSolicitado == PerfilPorDefecto)
            return new PerfilLlmResuelto(PerfilPorDefecto, porDefecto);

        var llm = options.Value.Llm;
        if (!llm.Perfiles.ContainsKey(perfilSolicitado))
            throw new BadRequestException(
                $"El perfil de LLM '{perfilSolicitado}' no esta registrado. Perfiles disponibles: {string.Join(", ", NombresDisponibles)}.");

        var brain = new LlmResumenBrain(httpClientFactory, NombreCliente(perfilSolicitado), llm.Efectivo(perfilSolicitado),
            loggerFactory.CreateLogger<LlmResumenBrain>());
        return new PerfilLlmResuelto(perfilSolicitado, brain);
    }

    /// <summary>Nombre del HttpClient nombrado de un perfil adicional. El por defecto usa <see cref="LlmResumenBrain.HttpClientName"/>.</summary>
    internal static string NombreCliente(string perfil) => $"motor-llm:{perfil}";
}
