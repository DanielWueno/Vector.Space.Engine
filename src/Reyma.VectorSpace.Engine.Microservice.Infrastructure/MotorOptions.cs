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

    /// <summary>Origen 'proyecto' desde URL Git (1.7.6): lista blanca y limites del clonado.</summary>
    public GitOptions Git { get; set; } = new();

    /// <summary>Perfil de LLM para el resumen de negocio (1.7.7). Vacio/sin <see cref="LlmOptions.UrlBase"/> = resumen no disponible en esta instancia.</summary>
    public LlmOptions Llm { get; set; } = new();

    /// <summary>Fase 2 reanudable de resumen de negocio (1.7.7): perfil de embeddings del vector <c>dense-resumen</c> y tope de tokens por corrida.</summary>
    public ResumenOptions Resumen { get; set; } = new();
}

/// <summary>
/// Un origen Git admitido: esquema, host y puerto exactos, y opcionalmente un prefijo de ruta (p. ej. la organizacion
/// de Azure DevOps, <c>/GR-PlasticosAdministrativo/</c>). Sin comodines ni sufijos de dominio (1.7.3): un comodin es
/// lo que deja pasar <c>dev.azure.com.atacante.example</c>.
/// </summary>
public sealed class OrigenGitPermitidoOptions
{
    public string Esquema { get; set; } = "https";

    public string Host { get; set; } = "";

    public int Puerto { get; set; } = 443;

    public string? PrefijoRuta { get; set; }
}

/// <summary>
/// Seccion <c>Motor:Git</c> (1.7.6): lista blanca de origenes Git permitidos (validada SIN tocar la red, antes de
/// cualquier conexion) y limites del clonado superficial endurecido (<c>ClonadorGit</c>). Sin ningun origen permitido,
/// toda URL se rechaza (el origen 'proyecto' por URL Git queda deshabilitado de facto, no es un error de arranque).
/// </summary>
public sealed class GitOptions
{
    public List<OrigenGitPermitidoOptions> OrigenesPermitidos { get; set; } = [];

    /// <summary>Ejecutable de git del sistema (en la imagen de produccion, el que agrega el Dockerfile).</summary>
    public string Git { get; set; } = "git";

    /// <summary>Profundidad del <c>--depth</c> del clonado superficial.</summary>
    public int Profundidad { get; set; } = 1;

    /// <summary>
    /// Tope duro de tamano del checkout en disco (medido mientras clona, no solo al terminar): git se mata al
    /// rebasarlo. Limite logico por ahora (1.7.3 midio que el proceso se mata igual, con bytes ya escritos en disco);
    /// un volumen con cuota real de filesystem es la mitigacion completa, ver docs/despliegue/runbook.md.
    /// </summary>
    public long MaxBytesCheckout { get; set; } = 200L * 1024 * 1024;

    /// <summary>Plazo del clonado completo; git se mata al rebasarlo (1.7.3: ~5 s de margen sobre el plazo medido).</summary>
    public int PlazoSegundos { get; set; } = 120;

    /// <summary>Directorio base para los checkouts temporales; cada clonado usa una subcarpeta propia por Guid.</summary>
    public string DirectorioTemporal { get; set; } = Path.Combine(Path.GetTempPath(), "vse-git-checkout");

    /// <summary>
    /// Nombre de la variable de entorno que trae el PAT de la identidad NO personal del servicio frente a Azure
    /// DevOps (cuenta de servicio; ver docs/evaluaciones/1.7.3/decision.md y el runbook). Vacio/no configurada: el
    /// clonado no agrega ninguna cabecera de autorizacion propia y deja que el <c>credential.helper</c> ya
    /// configurado en el host resuelva la autenticacion -- valido SOLO para la verificacion puntual de 1.7.6 con la
    /// credencial personal cacheada del usuario (Windows Git Credential Manager), corriendo en el host, nunca en el
    /// contenedor de produccion (ahi no existe ese credential manager y no hay identidad personal que usar).
    /// </summary>
    public string NombreVariableIdentidad { get; set; } = "VSE_GIT_PAT";
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

    /// <summary>
    /// Coseno minimo de la busqueda en el espacio <c>resumen</c> (item 1.20, <c>dense-resumen</c>). 0 (por defecto) =
    /// sin umbral: <see cref="UmbralSimilitud"/> se calibro para el vector del codigo (1.10) y no se traslada a otro
    /// espacio y otro modelo; un valor sin medir no debe recortar resultados en silencio. Cada despliegue lo fija si
    /// lo calibra.
    /// </summary>
    public float UmbralSimilitudResumen { get; set; }
}

/// <summary>
/// Seccion <c>Motor:Llm</c> (1.7.7): perfil de LLM para el resumen de negocio de un chunk, por la misma ruta
/// OpenAI-compatible de AltheIA que <see cref="EmbeddingsOptions"/>, pero para <c>chat/completions</c>. 1.7.3 midio
/// contra AltheIA real: <c>qwen3.8-27b-fast</c> con <c>max_tokens</c> 160 responde 30/30; <c>qwen3.8-27b-deep</c> (el
/// perfil de LLM por defecto de 1.13) razona por defecto y gasta los 160 tokens sin escribir el resumen (0/30) salvo
/// que se envie <c>chat_template_kwargs.enable_thinking=false</c> (entonces 30/30, igual que <c>fast</c>).
/// </summary>
public sealed class LlmOptions
{
    /// <summary>URL base del servidor de chat-completions (AltheIA). Vacio = resumen de negocio no disponible en esta instancia.</summary>
    public string? UrlBase { get; set; }

    /// <summary>Ruta OpenAI-compatible de chat-completions.</summary>
    public string Ruta { get; set; } = "v1/chat/completions";

    /// <summary>1.7.3: modelo que responde sin razonar con el limite de tokens del prompt de resumen.</summary>
    public string Modelo { get; set; } = "qwen3.8-27b-fast";

    /// <summary>Igual que el origen (OllamaBusinessSummaryGenerator): debe caber holgadamente el resumen (1 a 3 oraciones).</summary>
    public int MaxTokens { get; set; } = 160;

    public double Temperatura { get; set; } = 0.15;

    public double TopP { get; set; } = 0.95;

    /// <summary>
    /// Null (por defecto, perfil <c>fast</c>): no se envia el campo <c>chat_template_kwargs.enable_thinking</c>.
    /// <c>false</c> (obligatorio con un modelo <c>deep</c>, 1.7.3): fuerza respuesta directa sin razonar; sin esto
    /// <c>deep</c> gasta los <c>MaxTokens</c> razonando y devuelve una respuesta vacia (medido, 0/30).
    /// </summary>
    public bool? EnableThinking { get; set; }

    public int TiempoMaximoSegundos { get; set; } = 120;

    /// <summary>Opcional. Va por variable de entorno o user-secrets (Motor__Llm__ApiKey), nunca en appsettings.</summary>
    public string? ApiKey { get; set; }

    /// <summary>Opcional: igual que <see cref="EmbeddingsOptions.RutaCertificadoCA"/> pero para el perfil de LLM.</summary>
    public string? RutaCertificadoCA { get; set; }
}

/// <summary>
/// Seccion <c>Motor:Resumen</c> (1.7.7): fase 2 reanudable de resumen de negocio, opt-in al crear la coleccion (el
/// vector <c>dense-resumen</c> no se puede agregar despues, 1.7.3). Vacio <see cref="PerfilEmbeddings"/> = el
/// resumen no esta disponible en esta instancia (pedirlo responde 400, nunca 500).
/// </summary>
public sealed class ResumenOptions
{
    /// <summary>
    /// Nombre de un perfil de embeddings YA declarado (<see cref="MotorOptions.PerfilEmbeddingsPorDefecto"/> o una
    /// clave de <see cref="EmbeddingsOptions.Perfiles"/>) que sirve el vector <c>dense-resumen</c>. 1.7.3: bge-m3
    /// 1024d via AltheIA, SIN truncar (no es Matryoshka). Vacio = resumen no disponible en esta instancia.
    /// </summary>
    public string? PerfilEmbeddings { get; set; }

    /// <summary>
    /// Tope de tokens de LLM (prompt + respuesta, segun <c>usage.total_tokens</c> de AltheIA) que esta instancia
    /// gasta en UNA corrida de la fase de resumen antes de cortar, dejando el resto marcado <c>resumen_pending</c>
    /// para una corrida posterior (1.7.7, criterio 4). Configuracion, nunca un valor fijo en codigo: frente a la
    /// cuota real de AltheIA (1 000 000 tokens/dia, no verificado si los embeddings cuentan, 1.7.3), cada despliegue
    /// decide cuanto de esa cuota dedica a resumir por invocacion.
    /// </summary>
    public int TopeTokensPorCorrida { get; set; } = 100_000;

    /// <summary>Chunks pendientes de resumen leidos por pagina de Qdrant (scroll) en cada corrida.</summary>
    public int TamanoPaginaPendientes { get; set; } = 64;
}
