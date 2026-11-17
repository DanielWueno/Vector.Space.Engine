using Microsoft.Extensions.Options;
using Reyma.Exceptions.Api;
using Reyma.VectorSpace.Engine.Microservice.Business.Models.Responses;
using Reyma.VectorSpace.Engine.Microservice.Business.Motor;
using Reyma.VectorSpace.Engine.Microservice.Infrastructure.Resumen;
using Reyma.VectorSpace.Engine.Microservice.Infrastructure.Vectorization;

namespace Reyma.VectorSpace.Engine.Microservice.Infrastructure.Catalogo;

/// <summary>
/// <see cref="IMotorDeCatalogo"/> sobre la misma <see cref="MotorOptions"/> que usan las fabricas de perfiles para
/// validar cada solicitud: lo que lista es exactamente lo que se acepta, sin una segunda lista que mantener. Cada
/// perfil se sondea en paralelo con <see cref="SondaDeServidor"/> y un plazo corto, para que un servidor caido no
/// retenga la respuesta.
/// </summary>
internal sealed class CatalogoDelMotor(
    IHttpClientFactory httpClientFactory, IOptions<MotorOptions> options, ILlmResumenBrainFactory llmFactory) : IMotorDeCatalogo
{
    /// <summary>Plazo de cada sondeo. Las llamadas reales tienen el suyo (minutos); esto es solo un "responde?".</summary>
    internal TimeSpan PlazoSonda { get; init; } = TimeSpan.FromSeconds(5);

    private bool ResumenHabilitado => !string.IsNullOrWhiteSpace(options.Value.Resumen.PerfilEmbeddings);

    public async Task<IReadOnlyList<PerfilEmbeddingsInfoResponse>> ListarPerfilesEmbeddingsAsync(CancellationToken cancellationToken)
    {
        var o = options.Value;
        var perfiles = new List<(string Nombre, IPerfilEmbeddingsConfig Config, string Cliente)>
        {
            (MotorOptions.PerfilEmbeddingsPorDefecto, o.Embeddings, TeiVectorizationBrain.HttpClientName)
        };
        perfiles.AddRange(o.Embeddings.Perfiles.Select(kv =>
            (kv.Key, (IPerfilEmbeddingsConfig)kv.Value, VectorizationBrainFactory.NombreCliente(kv.Key))));

        var sondas = await Task.WhenAll(perfiles.Select(p =>
            SondaDeServidor.SondearAsync(httpClientFactory.CreateClient(p.Cliente), p.Config.Ruta, p.Config.Modelo, PlazoSonda, cancellationToken)));

        return perfiles.Zip(sondas, (p, s) => new PerfilEmbeddingsInfoResponse(
                p.Nombre, p.Config.Modelo, p.Config.Dimension,
                EsPorDefecto: p.Nombre == MotorOptions.PerfilEmbeddingsPorDefecto,
                UsadoPorResumen: p.Nombre == o.Resumen.PerfilEmbeddings,
                s.Disponible, s.ModeloVerificado, s.VentanaTokens, s.Detalle))
            .ToList();
    }

    public async Task<IReadOnlyList<PerfilLlmInfoResponse>> ListarPerfilesLlmAsync(CancellationToken cancellationToken)
    {
        var llm = options.Value.Llm;
        var perfiles = new List<(string Nombre, IPerfilLlmConfig Config, string? UrlBase, string Cliente)>
        {
            (llm.Nombre, llm, llm.UrlBase, LlmResumenBrain.HttpClientName)
        };
        perfiles.AddRange(llm.Perfiles.Keys.Select(nombre =>
        {
            var efectivo = llm.Efectivo(nombre);
            return (nombre, (IPerfilLlmConfig)efectivo, efectivo.UrlBase, LlmResumenBrainFactory.NombreCliente(nombre));
        }));

        var sondas = await Task.WhenAll(perfiles.Select(p => SondearLlmAsync(p.UrlBase, p.Cliente, p.Config, cancellationToken)));

        return perfiles.Zip(sondas, (p, s) => new PerfilLlmInfoResponse(
                p.Nombre, p.Config.Modelo, p.Config.MaxTokens, p.Config.EnableThinking,
                EsPorDefecto: p.Nombre == llm.Nombre,
                s.Disponible, s.ModeloVerificado, s.VentanaTokens, s.Detalle))
            .ToList();
    }

    private Task<ResultadoSonda> SondearLlmAsync(string? urlBase, string cliente, IPerfilLlmConfig config, CancellationToken ct)
    {
        // Sin resumen habilitado, el HttpClient del LLM puede no tener servidor (UrlBase vacio): crearlo fallaria.
        if (!ResumenHabilitado)
            return Task.FromResult(new ResultadoSonda(false, false, null,
                "El resumen de negocio no esta habilitado en esta instancia: ningun perfil de LLM se usa."));
        if (string.IsNullOrWhiteSpace(urlBase))
            return Task.FromResult(new ResultadoSonda(false, false, null, "El perfil no tiene servidor configurado."));
        return SondaDeServidor.SondearAsync(httpClientFactory.CreateClient(cliente), config.Ruta, config.Modelo, PlazoSonda, ct);
    }

    public IReadOnlyList<OrigenGitInfoResponse> ListarOrigenesGit() =>
        options.Value.Git.OrigenesPermitidos
            .Select(o =>
            {
                var puerto = (o.Esquema, o.Puerto) is ("https", 443) or ("http", 80) ? "" : $":{o.Puerto}";
                var prefijo = string.IsNullOrWhiteSpace(o.PrefijoRuta) ? "/" : "/" + o.PrefijoRuta.Trim('/') + "/";
                return new OrigenGitInfoResponse(o.Esquema, o.Host, o.Puerto, o.PrefijoRuta,
                    $"{o.Esquema}://{o.Host}{puerto}{prefijo}<ruta-del-repositorio>");
            })
            .ToList();

    public void ValidarPerfilLlm(string? perfilLlm)
    {
        if (string.IsNullOrWhiteSpace(perfilLlm))
            return;
        if (!ResumenHabilitado)
            throw new BadRequestException("PerfilLlm no aplica: el resumen de negocio no esta habilitado en esta instancia.");
        llmFactory.Resolver(perfilLlm); // 400 con los perfiles disponibles si no esta registrado
    }
}
