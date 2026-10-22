namespace Reyma.VectorSpace.Engine.Microservice.Infrastructure;

/// <summary>
/// Seccion <c>Motor</c>: donde estan Qdrant y el sidecar de embeddings, y como se fragmenta y se busca. Los valores por
/// defecto son las decisiones medidas (1.6: arctic-embed-m-v2.0 a 256d por TEI; 1.11: chunks de 1024 sin recorte;
/// 1.10: busqueda densa sola). Sin <see cref="EmbeddingsOptions.UrlBase"/> el servicio no arranca.
/// </summary>
public sealed class MotorOptions
{
    public const string Seccion = "Motor";

    public QdrantConexionOptions Qdrant { get; set; } = new();

    public EmbeddingsOptions Embeddings { get; set; } = new();

    public IngestaOptions Ingesta { get; set; } = new();

    public BusquedaOptions Busqueda { get; set; } = new();
}

public sealed class QdrantConexionOptions
{
    public string Host { get; set; } = "localhost";

    /// <summary>Puerto gRPC (el cliente .NET no usa el REST).</summary>
    public int PuertoGrpc { get; set; } = 6334;

    public bool Https { get; set; }

    /// <summary>Plazo de cada llamada gRPC; sin el, un Qdrant colgado retiene la solicitud indefinidamente.</summary>
    public int TiempoMaximoSegundos { get; set; } = 120;

    /// <summary>Opcional. Va por variable de entorno o user-secrets (Motor__Qdrant__ApiKey), nunca en appsettings.</summary>
    public string? ApiKey { get; set; }
}

public sealed class EmbeddingsOptions
{
    /// <summary>URL base del sidecar (TEI). Obligatoria.</summary>
    public string? UrlBase { get; set; }

    /// <summary>Ruta OpenAI-compatible. TEI: <c>v1/embeddings</c>; Infinity: <c>embeddings</c> (1.8).</summary>
    public string Ruta { get; set; } = "v1/embeddings";

    /// <summary>Nombre que se envia al sidecar y que se reporta como modelo de la coleccion.</summary>
    public string Modelo { get; set; } = "Snowflake/snowflake-arctic-embed-m-v2.0";

    /// <summary>Dimension guardada (Matryoshka): el cliente trunca a este tamano y normaliza L2 (1.6, 1.8).</summary>
    public int Dimension { get; set; } = 256;

    /// <summary>
    /// Prefijo de arctic-embed-m-v2.0 solo para consultas; los documentos van sin prefijo (1.6). Configurable a
    /// cadena vacia para servidores que no necesiten prefijo de consulta (1.13: perfil AltheIA/bge-m3).
    /// </summary>
    public string PrefijoConsulta { get; set; } = "query: ";

    /// <summary>Textos por llamada HTTP; TEI rechaza mas que su --max-client-batch-size.</summary>
    public int TextosPorLlamada { get; set; } = 16;

    /// <summary>Tiempo maximo de una llamada. Un lote de chunks largos en CPU tarda decenas de segundos (1.11).</summary>
    public int TiempoMaximoSegundos { get; set; } = 600;

    /// <summary>
    /// Opcional (1.13). Clave Bearer del servidor de IA corporativo AltheIA. Va por variable de entorno o
    /// user-secrets (Motor__Embeddings__ApiKey), nunca en appsettings. Vacio = sin cabecera Authorization,
    /// igual que el perfil TEI local de hoy.
    /// </summary>
    public string? ApiKey { get; set; }

    /// <summary>
    /// Opcional (1.13). Ruta local a un certificado CA adicional en el que confiar al validar TLS contra el
    /// sidecar, para cuando esta detras de un proxy/CA corporativo (AltheIA detras de Netskope, ver item 1.5).
    /// Si se define, debe existir en disco: el servicio no arranca si no se puede cargar. Vacio = validacion
    /// TLS estandar, sin CA adicional (perfil TEI local de hoy).
    /// </summary>
    public string? RutaCertificadoCA { get; set; }
}

public sealed class IngestaOptions
{
    /// <summary>1.11: 1024 (el CLI de Rag.Context.Engine lo fija en 512 en codigo).</summary>
    public int MaxTokensPorChunk { get; set; } = 1024;

    public int TokensDeSolape { get; set; } = 64;

    /// <summary>Chunks por lote de embeddings + upsert, como BatchSize del CLI.</summary>
    public int LoteDeChunks { get; set; } = 32;
}

public enum ModoBusqueda
{
    /// <summary>1.10: rama densa exacta sola. Es el defecto porque ninguna fusion gano preguntas sin perder otras.</summary>
    Denso,

    /// <summary>RRF nativa denso + sparse-code de Rag.Context.Engine, conservada como rama opcional (1.10).</summary>
    FusionNativa
}

public sealed class BusquedaOptions
{
    public ModoBusqueda Modo { get; set; } = ModoBusqueda.Denso;

    /// <summary>Coseno minimo de la rama densa; el mismo 0,10 del retriever y del banco de 1.6/1.10/1.11.</summary>
    public float UmbralSimilitud { get; set; } = 0.10f;
}
