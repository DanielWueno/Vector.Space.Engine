namespace Reyma.VectorSpace.Engine.Microservice.Infrastructure;

/// <summary>
/// Seccion <c>Motor</c>: donde estan Qdrant y el sidecar de embeddings, y como se fragmenta y se busca. Los valores por
/// defecto son las decisiones medidas (1.6: arctic-embed-m-v2.0 a 256d por TEI; 1.11: chunks de 1024 sin recorte;
/// 1.10: busqueda densa sola). Sin <see cref="EmbeddingsOptions.UrlBase"/> el servicio no arranca.
/// </summary>
public sealed class MotorOptions
{
    public const string Seccion = "Motor";

    /// <summary>
    /// Nombre del perfil de embeddings que describen las propiedades de <see cref="EmbeddingsOptions"/> (1.6: arctic
    /// a 256d por TEI). Es el perfil que se usa cuando una solicitud no pide ninguno (1.7.4) y el que se asume en las
    /// colecciones creadas antes de que existiera esta nocion de perfil (sin metadata de perfil en Qdrant).
    /// </summary>
    public const string PerfilEmbeddingsPorDefecto = "tei-arctic-256";

    public QdrantConexionOptions Qdrant { get; set; } = new();

    public EmbeddingsOptions Embeddings { get; set; } = new();

    public IngestaOptions Ingesta { get; set; } = new();

    public BusquedaOptions Busqueda { get; set; } = new();
}

public sealed class QdrantConexionOptions
{
    public string Host { get; set; } = "localhost";

    /// <summary>Puerto gRPC (el cliente .NET no usa el REST para los vectores).</summary>
    public int PuertoGrpc { get; set; } = 6334;

    /// <summary>
    /// Puerto REST (1.7.4): la metadata de coleccion (perfil de embeddings, modelo, dimension) se lee y escribe por
    /// REST porque el cliente gRPC de Qdrant 1.19 no expone el campo <c>metadata</c> de la coleccion.
    /// </summary>
    public int PuertoRest { get; set; } = 6333;

    public bool Https { get; set; }

    /// <summary>Plazo de cada llamada gRPC/REST; sin el, un Qdrant colgado retiene la solicitud indefinidamente.</summary>
    public int TiempoMaximoSegundos { get; set; } = 120;

    /// <summary>Opcional. Va por variable de entorno o user-secrets (Motor__Qdrant__ApiKey), nunca en appsettings.</summary>
    public string? ApiKey { get; set; }
}

/// <summary>Lo que necesita un perfil de embeddings para llamar al sidecar (TEI o AltheIA) e interpretar su respuesta.</summary>
public interface IPerfilEmbeddingsConfig
{
    string Ruta { get; }

    string Modelo { get; }

    int Dimension { get; }

    string PrefijoConsulta { get; }

    int TextosPorLlamada { get; }
}

public sealed class EmbeddingsOptions : IPerfilEmbeddingsConfig
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

    /// <summary>
    /// Perfiles adicionales de embeddings (1.7.4), seleccionables por nombre junto con el perfil por defecto que
    /// describen las propiedades de esta misma clase (<see cref="MotorOptions.PerfilEmbeddingsPorDefecto"/>). Clave =
    /// nombre del perfil (p. ej. <c>altheia-bge-m3-256</c>, ver docs/evaluaciones/1.7.3/decision.md); no puede repetir
    /// el nombre del perfil por defecto.
    /// </summary>
    public Dictionary<string, PerfilEmbeddingsOptions> Perfiles { get; set; } = new();
}

/// <summary>Un perfil de embeddings adicional, registrado por nombre en <see cref="EmbeddingsOptions.Perfiles"/>.</summary>
public sealed class PerfilEmbeddingsOptions : IPerfilEmbeddingsConfig
{
    /// <summary>URL base del servidor de este perfil (TEI local o AltheIA). Obligatoria.</summary>
    public string? UrlBase { get; set; }

    public string Ruta { get; set; } = "v1/embeddings";

    public string Modelo { get; set; } = "";

    public int Dimension { get; set; }

    public string PrefijoConsulta { get; set; } = "";

    public int TextosPorLlamada { get; set; } = 16;

    public int TiempoMaximoSegundos { get; set; } = 600;

    /// <summary>Opcional. Va por variable de entorno o user-secrets, nunca en appsettings.</summary>
    public string? ApiKey { get; set; }

    /// <summary>Opcional: igual que <see cref="EmbeddingsOptions.RutaCertificadoCA"/> pero para este perfil.</summary>
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
