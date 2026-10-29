using System.Net.Http.Headers;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Qdrant.Client;
using Reyma.VectorSpace.Engine.Microservice.Business.Motor;
using Reyma.VectorSpace.Engine.Microservice.Infrastructure.Abstractions;
using Reyma.VectorSpace.Engine.Microservice.Infrastructure.Chunking;
using Reyma.VectorSpace.Engine.Microservice.Infrastructure.Ingestion;
using Reyma.VectorSpace.Engine.Microservice.Infrastructure.Resumen;
using Reyma.VectorSpace.Engine.Microservice.Infrastructure.Vectorization;
using Reyma.VectorSpace.Engine.Microservice.Infrastructure.VectorStore;

namespace Reyma.VectorSpace.Engine.Microservice.Infrastructure;

public static class MotorServiceCollectionExtensions
{
    /// <summary>
    /// Registra el motor portado detras de los puertos de Business. Valida la seccion <c>Motor</c> al arrancar: sin URL
    /// del sidecar, o con limites incoherentes, el servicio no arranca en vez de fallar en la primera solicitud.
    /// </summary>
    public static IServiceCollection AddMotorVectorial(this IServiceCollection services, IConfiguration config)
    {
        services.AddOptions<MotorOptions>()
            .Bind(config.GetSection(MotorOptions.Seccion))
            .Validate(o => Uri.TryCreate(o.Embeddings.UrlBase, UriKind.Absolute, out var u) && (u.Scheme == "http" || u.Scheme == "https"),
                "Motor:Embeddings:UrlBase debe ser una URL http(s) absoluta del sidecar de embeddings.")
            .Validate(o => o.Embeddings.Dimension > 0, "Motor:Embeddings:Dimension debe ser positiva.")
            .Validate(o => !string.IsNullOrWhiteSpace(o.Embeddings.Modelo), "Motor:Embeddings:Modelo es obligatorio.")
            .Validate(o => o.Embeddings.TextosPorLlamada is > 0 and <= 64, "Motor:Embeddings:TextosPorLlamada debe estar entre 1 y 64.")
            .Validate(o => o.Embeddings.TiempoMaximoSegundos > 0, "Motor:Embeddings:TiempoMaximoSegundos debe ser positivo.")
            .Validate(o => string.IsNullOrWhiteSpace(o.Embeddings.RutaCertificadoCA) || File.Exists(o.Embeddings.RutaCertificadoCA),
                "Motor:Embeddings:RutaCertificadoCA debe apuntar a un archivo existente si se define.")
            // 1.7.4: perfiles adicionales de embeddings, con las mismas reglas que el perfil por defecto de arriba.
            .Validate(o => !o.Embeddings.Perfiles.ContainsKey(MotorOptions.PerfilEmbeddingsPorDefecto),
                $"Motor:Embeddings:Perfiles no puede redefinir el perfil por defecto ('{MotorOptions.PerfilEmbeddingsPorDefecto}').")
            .Validate(o => o.Embeddings.Perfiles.Values.All(p => Uri.TryCreate(p.UrlBase, UriKind.Absolute, out var u) && (u.Scheme == "http" || u.Scheme == "https")),
                "Cada perfil de Motor:Embeddings:Perfiles necesita una UrlBase http(s) absoluta.")
            .Validate(o => o.Embeddings.Perfiles.Values.All(p => p.Dimension > 0), "Cada perfil de Motor:Embeddings:Perfiles necesita Dimension positiva.")
            .Validate(o => o.Embeddings.Perfiles.Values.All(p => !string.IsNullOrWhiteSpace(p.Modelo)), "Cada perfil de Motor:Embeddings:Perfiles necesita Modelo.")
            .Validate(o => o.Embeddings.Perfiles.Values.All(p => p.TextosPorLlamada is > 0 and <= 64),
                "Cada perfil de Motor:Embeddings:Perfiles necesita TextosPorLlamada entre 1 y 64.")
            .Validate(o => o.Embeddings.Perfiles.Values.All(p => p.TiempoMaximoSegundos > 0),
                "Cada perfil de Motor:Embeddings:Perfiles necesita TiempoMaximoSegundos positivo.")
            .Validate(o => o.Embeddings.Perfiles.Values.All(p => string.IsNullOrWhiteSpace(p.RutaCertificadoCA) || File.Exists(p.RutaCertificadoCA)),
                "Cada RutaCertificadoCA de Motor:Embeddings:Perfiles debe existir si se define.")
            .Validate(o => o.Ingesta.MaxTokensPorChunk > 0 && o.Ingesta.TokensDeSolape >= 0 && o.Ingesta.TokensDeSolape < o.Ingesta.MaxTokensPorChunk,
                "Motor:Ingesta: MaxTokensPorChunk positivo y TokensDeSolape entre 0 y MaxTokensPorChunk.")
            .Validate(o => o.Ingesta.LoteDeChunks > 0, "Motor:Ingesta:LoteDeChunks debe ser positivo.")
            .Validate(o => o.Busqueda.UmbralSimilitud is >= 0f and < 1f, "Motor:Busqueda:UmbralSimilitud debe estar en [0, 1).")
            .Validate(o => o.Busqueda.UmbralSimilitudResumen is >= 0f and < 1f, "Motor:Busqueda:UmbralSimilitudResumen debe estar en [0, 1).")
            .Validate(o => !string.IsNullOrWhiteSpace(o.Qdrant.Host) && o.Qdrant.PuertoGrpc > 0 && o.Qdrant.PuertoRest > 0 && o.Qdrant.TiempoMaximoSegundos > 0,
                "Motor:Qdrant: Host, PuertoGrpc, PuertoRest y TiempoMaximoSegundos son obligatorios.")
            // 1.7.6: origen 'proyecto' por URL Git. Sin ningun origen permitido, el servicio igual arranca (toda URL
            // se rechaza en ListaBlancaGit, no es un error de configuracion); lo que si falla rapido es un origen
            // permitido mal declarado o limites de clonado incoherentes.
            .Validate(o => o.Git.OrigenesPermitidos.All(p => p.Esquema is "https" or "http" && !string.IsNullOrWhiteSpace(p.Host) && p.Puerto > 0),
                "Cada Motor:Git:OrigenesPermitidos necesita Esquema ('https' o 'http'), Host y Puerto positivo.")
            .Validate(o => !string.IsNullOrWhiteSpace(o.Git.Git), "Motor:Git:Git (ejecutable) es obligatorio.")
            .Validate(o => o.Git.Profundidad >= 1, "Motor:Git:Profundidad debe ser al menos 1.")
            .Validate(o => o.Git.MaxBytesCheckout > 0, "Motor:Git:MaxBytesCheckout debe ser positivo.")
            .Validate(o => o.Git.PlazoSegundos > 0, "Motor:Git:PlazoSegundos debe ser positivo.")
            .Validate(o => !string.IsNullOrWhiteSpace(o.Git.DirectorioTemporal), "Motor:Git:DirectorioTemporal es obligatorio.")
            .Validate(o => !string.IsNullOrWhiteSpace(o.Git.NombreVariableIdentidad), "Motor:Git:NombreVariableIdentidad es obligatorio.")
            // 1.7.7: resumen de negocio, opt-in. Vacio Motor:Resumen:PerfilEmbeddings = la fase 2 no esta disponible
            // en esta instancia (activarla responde 400); el resto solo se valida cuando si esta configurado.
            .Validate(o => string.IsNullOrWhiteSpace(o.Resumen.PerfilEmbeddings) ||
                           o.Resumen.PerfilEmbeddings == MotorOptions.PerfilEmbeddingsPorDefecto ||
                           o.Embeddings.Perfiles.ContainsKey(o.Resumen.PerfilEmbeddings),
                "Motor:Resumen:PerfilEmbeddings debe ser el perfil de embeddings por defecto o uno declarado en Motor:Embeddings:Perfiles.")
            .Validate(o => string.IsNullOrWhiteSpace(o.Resumen.PerfilEmbeddings) ||
                           (Uri.TryCreate(o.Llm.UrlBase, UriKind.Absolute, out var u) && (u.Scheme == "http" || u.Scheme == "https")),
                "Motor:Llm:UrlBase debe ser una URL http(s) absoluta cuando Motor:Resumen:PerfilEmbeddings esta configurado.")
            .Validate(o => string.IsNullOrWhiteSpace(o.Resumen.PerfilEmbeddings) || !string.IsNullOrWhiteSpace(o.Llm.Modelo),
                "Motor:Llm:Modelo es obligatorio cuando Motor:Resumen:PerfilEmbeddings esta configurado.")
            .Validate(o => o.Llm.MaxTokens > 0, "Motor:Llm:MaxTokens debe ser positivo.")
            .Validate(o => o.Llm.TiempoMaximoSegundos > 0, "Motor:Llm:TiempoMaximoSegundos debe ser positivo.")
            .Validate(o => string.IsNullOrWhiteSpace(o.Llm.RutaCertificadoCA) || File.Exists(o.Llm.RutaCertificadoCA),
                "Motor:Llm:RutaCertificadoCA debe apuntar a un archivo existente si se define.")
            .Validate(o => o.Resumen.TopeTokensPorCorrida > 0, "Motor:Resumen:TopeTokensPorCorrida debe ser positivo.")
            .Validate(o => o.Resumen.TamanoPaginaPendientes > 0, "Motor:Resumen:TamanoPaginaPendientes debe ser positivo.")
            .ValidateOnStart();

        services.AddSingleton(sp =>
        {
            var o = sp.GetRequiredService<IOptions<MotorOptions>>().Value.Qdrant;
            return new QdrantClient(o.Host, o.PuertoGrpc, o.Https, string.IsNullOrWhiteSpace(o.ApiKey) ? null : o.ApiKey,
                TimeSpan.FromSeconds(o.TiempoMaximoSegundos));
        });

        services.AddHttpClient(TeiVectorizationBrain.HttpClientName, (sp, http) =>
            {
                var o = sp.GetRequiredService<IOptions<MotorOptions>>().Value.Embeddings;
                http.BaseAddress = new Uri(o.UrlBase!.TrimEnd('/') + "/");
                http.Timeout = TimeSpan.FromSeconds(o.TiempoMaximoSegundos);
                // 1.13: perfil AltheIA. Vacio = sin cabecera, igual que el perfil TEI local de hoy.
                if (!string.IsNullOrWhiteSpace(o.ApiKey))
                    http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", o.ApiKey);
            })
            .ConfigurePrimaryHttpMessageHandler(sp =>
            {
                var o = sp.GetRequiredService<IOptions<MotorOptions>>().Value.Embeddings;
                var handler = new HttpClientHandler();
                if (!string.IsNullOrWhiteSpace(o.RutaCertificadoCA))
                {
                    // ValidateOnStart ya exigio que el archivo exista; aqui se carga una sola vez por handler.
                    var caAdicional = X509CertificateLoader.LoadCertificateFromFile(o.RutaCertificadoCA);
                    handler.ServerCertificateCustomValidationCallback = (_, cert, chain, errors) =>
                        ValidaConCaAdicional(cert, chain, errors, caAdicional);
                }
                return handler;
            });

        services.AddSingleton<IVectorizationBrain, TeiVectorizationBrain>();

        // 1.7.4: un HttpClient nombrado por cada perfil adicional declarado en Motor:Embeddings:Perfiles, con el
        // mismo patron de clave Bearer y CA adicional que el perfil por defecto. El perfil por defecto reutiliza el
        // HttpClient y el IVectorizationBrain ya registrados arriba (TeiVectorizationBrain.HttpClientName).
        var nombresDePerfilesAdicionales = config.GetSection($"{MotorOptions.Seccion}:Embeddings:Perfiles").GetChildren()
            .Select(seccion => seccion.Key)
            .ToArray();
        foreach (var nombrePerfil in nombresDePerfilesAdicionales)
        {
            services.AddHttpClient(VectorizationBrainFactory.NombreCliente(nombrePerfil), (sp, http) =>
                {
                    var o = ObtenerPerfilAdicional(sp, nombrePerfil);
                    http.BaseAddress = new Uri(o.UrlBase!.TrimEnd('/') + "/");
                    http.Timeout = TimeSpan.FromSeconds(o.TiempoMaximoSegundos);
                    if (!string.IsNullOrWhiteSpace(o.ApiKey))
                        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", o.ApiKey);
                })
                .ConfigurePrimaryHttpMessageHandler(sp =>
                {
                    var o = ObtenerPerfilAdicional(sp, nombrePerfil);
                    var handler = new HttpClientHandler();
                    if (!string.IsNullOrWhiteSpace(o.RutaCertificadoCA))
                    {
                        var caAdicional = X509CertificateLoader.LoadCertificateFromFile(o.RutaCertificadoCA);
                        handler.ServerCertificateCustomValidationCallback = (_, cert, chain, errors) =>
                            ValidaConCaAdicional(cert, chain, errors, caAdicional);
                    }
                    return handler;
                });
        }
        services.AddSingleton<IVectorizationBrainFactory>(sp => new VectorizationBrainFactory(
            sp.GetRequiredService<IHttpClientFactory>(),
            sp.GetRequiredService<IOptions<MotorOptions>>(),
            sp.GetRequiredService<ILoggerFactory>(),
            sp.GetRequiredService<IVectorizationBrain>()));

        // 1.7.4: HttpClient REST del propio Qdrant, solo para la metadata de perfil de coleccion (el cliente gRPC de
        // Qdrant 1.19 no la expone).
        services.AddHttpClient(QdrantVectorStore.RestHttpClientName, (sp, http) =>
        {
            var o = sp.GetRequiredService<IOptions<MotorOptions>>().Value.Qdrant;
            http.BaseAddress = new Uri($"{(o.Https ? "https" : "http")}://{o.Host}:{o.PuertoRest}/");
            http.Timeout = TimeSpan.FromSeconds(o.TiempoMaximoSegundos);
            if (!string.IsNullOrWhiteSpace(o.ApiKey))
                http.DefaultRequestHeaders.Add("api-key", o.ApiKey);
        });

        // 1.7.7: HttpClient del perfil de LLM (chat-completions), mismo patron Bearer+CA que embeddings. El
        // delegado de configuracion (y por tanto Motor:Llm:UrlBase) solo se evalua la primera vez que algo pide
        // CreateClient(HttpClientName) -- LlmResumenBrain lo hace de forma perezosa (ver su comentario), nunca en
        // su constructor, para que una instancia sin resumen configurado (UrlBase vacio) no falle al construir
        // MotorVectorial en cada solicitud.
        services.AddHttpClient(LlmResumenBrain.HttpClientName, (sp, http) =>
            {
                var o = sp.GetRequiredService<IOptions<MotorOptions>>().Value.Llm;
                http.BaseAddress = new Uri(o.UrlBase!.TrimEnd('/') + "/");
                http.Timeout = TimeSpan.FromSeconds(o.TiempoMaximoSegundos);
                if (!string.IsNullOrWhiteSpace(o.ApiKey))
                    http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", o.ApiKey);
            })
            .ConfigurePrimaryHttpMessageHandler(sp =>
            {
                var o = sp.GetRequiredService<IOptions<MotorOptions>>().Value.Llm;
                var handler = new HttpClientHandler();
                if (!string.IsNullOrWhiteSpace(o.RutaCertificadoCA))
                {
                    var caAdicional = X509CertificateLoader.LoadCertificateFromFile(o.RutaCertificadoCA);
                    handler.ServerCertificateCustomValidationCallback = (_, cert, chain, errors) =>
                        ValidaConCaAdicional(cert, chain, errors, caAdicional);
                }
                return handler;
            });
        services.AddSingleton<ILlmResumenBrain, LlmResumenBrain>();
        services.AddSingleton<ICacheDeResumenes, CacheDeResumenes>();
        services.AddSingleton<ResumenPipeline>();

        services.AddSingleton<ISparseTokenizer, SparseTokenizer>();

        // Como el origen: cada estrategia con lenguaje propio se descubre en el ensamblado; el resto cae al fallback.
        services.AddSingleton<FallbackChunkingStrategy>();
        var strategyTypes = typeof(IChunkingStrategy).Assembly.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false } &&
                        typeof(IChunkingStrategy).IsAssignableFrom(t) &&
                        t != typeof(FallbackChunkingStrategy));
        foreach (var type in strategyTypes)
        {
            services.AddSingleton(type);
            services.AddSingleton(typeof(IChunkingStrategy), sp => sp.GetRequiredService(type));
        }
        services.AddSingleton<ChunkingStrategyRouter>();

        services.AddSingleton(sp => new QdrantVectorStore(
            sp.GetRequiredService<QdrantClient>(),
            sp.GetRequiredService<IHttpClientFactory>().CreateClient(QdrantVectorStore.RestHttpClientName),
            sp.GetRequiredService<ILogger<QdrantVectorStore>>()));
        services.AddSingleton<QdrantSemanticRetriever>();
        services.AddSingleton<DocumentIngestionPipeline>();

        services.AddSingleton<MotorVectorial>();
        services.AddSingleton<IMotorDeEmbeddings>(sp => sp.GetRequiredService<MotorVectorial>());
        services.AddSingleton<IMotorDeBusqueda>(sp => sp.GetRequiredService<MotorVectorial>());
        services.AddSingleton<IMotorDeIngesta>(sp => sp.GetRequiredService<MotorVectorial>());
        services.AddSingleton<IMotorDeIngestaProyecto>(sp => sp.GetRequiredService<MotorVectorial>());
        services.AddSingleton<IMotorDeIngestaProyectoGit>(sp => sp.GetRequiredService<MotorVectorial>());
        services.AddSingleton<IMotorDeColecciones>(sp => sp.GetRequiredService<MotorVectorial>());

        return services;
    }

    /// <summary>Readiness con las dependencias reales del motor: Qdrant y el sidecar de embeddings.</summary>
    public static IHealthChecksBuilder AddMotorVectorialChecks(this IHealthChecksBuilder builder, params string[] tags) =>
        builder
            .AddCheck<QdrantHealthCheck>("Qdrant", tags: tags, timeout: TimeSpan.FromSeconds(5))
            .AddCheck<EmbeddingsHealthCheck>("Embeddings", tags: tags, timeout: TimeSpan.FromSeconds(5));

    /// <summary>
    /// 1.13: valida la cadena normalmente y, ademas, confia en <paramref name="caAdicional"/> (AltheIA detras de un
    /// proxy/CA corporativo, p.ej. Netskope). Nunca acepta sin validar: un error que no sea "cadena no confiable"
    /// (p.ej. nombre de host no coincide o certificado vencido) se rechaza igual que sin CA adicional; solo el caso
    /// de cadena no confiable se reintenta reconstruyendo la cadena con un almacen de confianza personalizado que
    /// incluye ese CA, y se exige que <see cref="X509Chain.Build"/> tenga exito.
    /// </summary>
    private static bool ValidaConCaAdicional(X509Certificate2? cert, X509Chain? chain, SslPolicyErrors errors, X509Certificate2 caAdicional)
    {
        if (errors == SslPolicyErrors.None)
            return true;
        if (cert is null || chain is null)
            return false;
        if ((errors & ~SslPolicyErrors.RemoteCertificateChainErrors) != SslPolicyErrors.None)
            return false;

        // Conserva los intermedios ya descubiertos (si los hubo) para que el nuevo build pueda enlazarlos al CA.
        foreach (var elemento in chain.ChainElements)
            if (!elemento.Certificate.Equals(cert))
                chain.ChainPolicy.ExtraStore.Add(elemento.Certificate);

        chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
        chain.ChainPolicy.CustomTrustStore.Clear();
        chain.ChainPolicy.CustomTrustStore.Add(caAdicional);

        return chain.Build(cert);
    }

    /// <summary>Configuracion de un perfil adicional ya validada por <c>ValidateOnStart</c> (1.7.4).</summary>
    private static PerfilEmbeddingsOptions ObtenerPerfilAdicional(IServiceProvider sp, string nombrePerfil) =>
        sp.GetRequiredService<IOptions<MotorOptions>>().Value.Embeddings.Perfiles[nombrePerfil];
}
