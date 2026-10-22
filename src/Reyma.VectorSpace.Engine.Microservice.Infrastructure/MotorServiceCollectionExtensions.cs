using System.Net.Http.Headers;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Qdrant.Client;
using Reyma.VectorSpace.Engine.Microservice.Business.Motor;
using Reyma.VectorSpace.Engine.Microservice.Infrastructure.Abstractions;
using Reyma.VectorSpace.Engine.Microservice.Infrastructure.Chunking;
using Reyma.VectorSpace.Engine.Microservice.Infrastructure.Ingestion;
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
            .Validate(o => o.Ingesta.MaxTokensPorChunk > 0 && o.Ingesta.TokensDeSolape >= 0 && o.Ingesta.TokensDeSolape < o.Ingesta.MaxTokensPorChunk,
                "Motor:Ingesta: MaxTokensPorChunk positivo y TokensDeSolape entre 0 y MaxTokensPorChunk.")
            .Validate(o => o.Ingesta.LoteDeChunks > 0, "Motor:Ingesta:LoteDeChunks debe ser positivo.")
            .Validate(o => o.Busqueda.UmbralSimilitud is >= 0f and < 1f, "Motor:Busqueda:UmbralSimilitud debe estar en [0, 1).")
            .Validate(o => !string.IsNullOrWhiteSpace(o.Qdrant.Host) && o.Qdrant.PuertoGrpc > 0 && o.Qdrant.TiempoMaximoSegundos > 0,
                "Motor:Qdrant: Host, PuertoGrpc y TiempoMaximoSegundos son obligatorios.")
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

        services.AddSingleton<QdrantVectorStore>();
        services.AddSingleton<QdrantSemanticRetriever>();
        services.AddSingleton<DocumentIngestionPipeline>();

        services.AddSingleton<MotorVectorial>();
        services.AddSingleton<IMotorDeEmbeddings>(sp => sp.GetRequiredService<MotorVectorial>());
        services.AddSingleton<IMotorDeBusqueda>(sp => sp.GetRequiredService<MotorVectorial>());
        services.AddSingleton<IMotorDeIngesta>(sp => sp.GetRequiredService<MotorVectorial>());

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
}
