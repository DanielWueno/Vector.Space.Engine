namespace Reyma.VectorSpace.Engine.Microservice.Business.Models.Requests;

// Cuerpos HTTP. La coleccion viaja siempre en la ruta, nunca en el cuerpo, para que no haya dos valores que comparar.
// Las propiedades son anulables a proposito: un cuerpo incompleto llega a la validacion y responde 400 con el motivo.
// Los <summary> de cada propiedad son la descripcion que muestra Swagger: deben explicarse solos, sin remitir a otra
// clase ni a un item del plan, y decir de donde sale cada valor valido (GET api/catalogo/*).

/// <summary>Textos sueltos a vectorizar con el perfil de la coleccion. No escribe nada en la coleccion.</summary>
public sealed class CalcularEmbeddingsRequest
{
    /// <summary>Textos a vectorizar: de 1 a 64, cada uno no vacio y de hasta 8192 caracteres.</summary>
    public List<string>? Textos { get; set; }

    /// <summary>
    /// Opcional. Nombre del perfil de embeddings (modelo + dimension) con el que se calculan los vectores; valores
    /// validos en GET api/catalogo/perfiles-embeddings. Ausente = el perfil guardado en la coleccion o, si no existe,
    /// el perfil por defecto. Un perfil no registrado responde 400; uno distinto al de la coleccion, 409.
    /// </summary>
    public string? Perfil { get; set; }
}

/// <summary>Busqueda semantica en una coleccion: devuelve los fragmentos (chunks) mas parecidos a la consulta.</summary>
[Newtonsoft.Json.JsonConverter(typeof(BuscarRequestConverter))]
public sealed class BuscarRequest
{
    /// <summary>Texto de la pregunta o consulta, en lenguaje natural o con nombres de codigo. Hasta 2048 caracteres.</summary>
    public string? Consulta { get; set; }

    /// <summary>Cuantos resultados devolver, entre 1 y 100. Por defecto 10.</summary>
    public int? TopK { get; set; }

    /// <summary>
    /// Opcional y avanzado. Candidatos por rama antes de fusionar (40 a 1200), solo con espacio <c>codigo</c> y modo
    /// de busqueda FusionNativa; en otro caso responde 400. Ausente = max(4*topK, 40).
    /// </summary>
    [Newtonsoft.Json.JsonConverter(typeof(EnteroEstrictoConverter))]
    public int? PrefetchPorRama { get; set; }

    /// <summary>
    /// Opcional. Perfil de embeddings con el que se vectoriza la consulta; debe ser el mismo con el que se creo la
    /// coleccion (409 si no). Ausente = el de la coleccion. Valores en GET api/catalogo/perfiles-embeddings; el de
    /// cada coleccion en GET api/colecciones.
    /// </summary>
    public string? Perfil { get; set; }

    /// <summary>
    /// Opcional. De 1 a 50 nombres de simbolo (clase, metodo, etc.): solo devuelve fragmentos que definan alguno de
    /// ellos (igualdad exacta). Ausente = sin filtro.
    /// </summary>
    public List<string>? SimbolosDefinidos { get; set; }

    /// <summary>
    /// Opcional. De 1 a 50 modulos: cada uno exige que el namespace del fragmento sea ese valor o empiece por el
    /// seguido de <c>.</c>, o que su ruta sea ese valor o empiece por el seguido de <c>/</c> (sin normalizar
    /// mayusculas). Varios modulos se combinan con AND. Acota la busqueda; no es un control de acceso.
    /// </summary>
    [Newtonsoft.Json.JsonConverter(typeof(ListaDeCadenasEstrictaConverter))]
    public List<string>? Modulos { get; set; }

    /// <summary>
    /// Opcional. Solo fragmentos de este lenguaje: CSharp, TypeScript, JavaScript, Xaml, Sql, Markdown, PlainText o
    /// Unknown (nombre exacto). Otro valor responde 400.
    /// </summary>
    [Newtonsoft.Json.JsonConverter(typeof(CadenaEstrictaConverter))]
    public string? Lenguaje { get; set; }

    /// <summary>
    /// Opcional. Busqueda de texto sobre el namespace del fragmento (no sobre la ruta). Vacio o ausente = sin filtro.
    /// Para fronteras exactas de modulo usar <c>modulos</c>.
    /// </summary>
    [Newtonsoft.Json.JsonConverter(typeof(CadenaEstrictaConverter))]
    public string? Namespace { get; set; }

    /// <summary>
    /// Opcional. Que se compara con la consulta: <c>codigo</c> (por defecto) compara contra el codigo de cada
    /// fragmento; <c>resumen</c> compara contra su resumen de negocio en lenguaje natural, solo en colecciones creadas
    /// con <c>resumen=true</c> (409 si no). Con <c>resumen</c>, <c>perfil</c> debe ir ausente.
    /// </summary>
    public string? Espacio { get; set; }
}

/// <summary>Ingesta sincrona de documentos enviados en el cuerpo (hasta 100 por solicitud).</summary>
public sealed class IngestaRequest
{
    /// <summary>
    /// Opcional. Forma de fragmentar e identificar los documentos. Ausente = ingesta nativa del servicio. Valores en
    /// GET api/catalogo/perfiles-ingesta; hoy solo <c>rag-local-1.12</c>, que exige <c>repositorioLogico</c>. Se fija
    /// al crear la coleccion: omitirlo o cambiarlo despues responde 409.
    /// </summary>
    [Newtonsoft.Json.JsonConverter(typeof(CadenaEstrictaConverter))]
    public string? PerfilIngesta { get; set; }

    /// <summary>
    /// Solo con <c>perfilIngesta</c> (ambos o ninguno). Nombre logico del repositorio, de 1 a 128 caracteres sin
    /// <c>/</c> ni <c>\</c>, que identifica los documentos en lugar del nombre de la coleccion (la coleccion es donde
    /// se guardan; el repositorio logico, como se llaman). No se puede cambiar una vez creada la coleccion.
    /// </summary>
    [Newtonsoft.Json.JsonConverter(typeof(CadenaEstrictaConverter))]
    public string? RepositorioLogico { get; set; }

    /// <summary>De donde sale el contenido a indexar. Obligatorio.</summary>
    public OrigenIngestaRequest? Origen { get; set; }

    /// <summary>
    /// Opcional. Perfil de embeddings (modelo + dimension) de la coleccion; valores en
    /// GET api/catalogo/perfiles-embeddings. Se fija en la primera ingesta, la que crea la coleccion; ausente = el
    /// perfil por defecto. Despues, otro perfil responde 409 sin escribir. Un perfil no registrado responde 400.
    /// </summary>
    public string? Perfil { get; set; }

    /// <summary>
    /// Opcional. true = ademas del codigo, guardar un resumen de negocio en lenguaje natural de cada fragmento
    /// (escrito por un LLM, ver <c>perfilLlm</c>) para buscar con <c>espacio=resumen</c>. Solo cuenta en la primera
    /// ingesta, la que crea la coleccion: no se puede agregar despues (409). En una coleccion que ya lo tiene, los
    /// fragmentos nuevos se resumen siempre, venga o no este campo. Gasta cuota de tokens del LLM.
    /// </summary>
    public bool? Resumen { get; set; }

    /// <summary>
    /// Opcional. Perfil de LLM que escribe los resumenes de negocio de esta ingesta (solo tiene efecto si la coleccion
    /// tiene resumen). Valores en GET api/catalogo/perfiles-llm; ausente = el perfil por defecto. Un perfil no
    /// registrado, o pedirlo en una instancia sin resumen habilitado, responde 400.
    /// </summary>
    public string? PerfilLlm { get; set; }
}

/// <summary>
/// De donde sale el contenido. <c>documentos</c>: el contenido viaja en el cuerpo. <c>ruta</c>: reservado, hoy
/// responde 501. Son excluyentes: se envia <see cref="Documentos"/> o <see cref="Ruta"/>, no ambos.
/// </summary>
public sealed class OrigenIngestaRequest
{
    public const string TipoDocumentos = "documentos";
    public const string TipoRuta = "ruta";

    /// <summary><c>documentos</c> (contenido en <c>documentos</c>) o <c>ruta</c> (reservado, 501).</summary>
    public string? Tipo { get; set; }

    /// <summary>
    /// Con tipo <c>documentos</c>: de 1 a 100 documentos con ruta unica y contenido de hasta 1 MiB cada uno. Enviar un
    /// documento con contenido vacio borra sus fragmentos de la coleccion.
    /// </summary>
    public List<DocumentoRequest>? Documentos { get; set; }

    /// <summary>Con tipo <c>ruta</c>: reservado, hoy responde 501.</summary>
    public string? Ruta { get; set; }
}

/// <summary>Un documento a indexar.</summary>
public sealed class DocumentoRequest
{
    /// <summary>
    /// Ruta relativa del documento (p. ej. <c>src/Ventas/Factura.cs</c>): lo identifica dentro de la coleccion, asi
    /// que reenviar la misma ruta lo actualiza. Su extension decide como se fragmenta. Hasta 1024 caracteres.
    /// </summary>
    public string? Ruta { get; set; }

    /// <summary>Texto completo del documento (no fragmentos). Vacio = borrar el documento de la coleccion.</summary>
    public string? Contenido { get; set; }
}

/// <summary>
/// Ingesta asincrona de un repositorio completo clonado desde Git por el servicio, con su propia identidad. La URL debe
/// pertenecer a un origen permitido (GET api/catalogo/origenes-git) y nunca lleva usuario, contrasena, query ni
/// fragmento.
/// </summary>
public sealed class IngerirProyectoGitRequest
{
    /// <summary>
    /// Opcional. Forma de fragmentar e identificar los documentos; ausente = ingesta nativa. Valores en
    /// GET api/catalogo/perfiles-ingesta. Exige <c>repositorioLogico</c>.
    /// </summary>
    [Newtonsoft.Json.JsonConverter(typeof(CadenaEstrictaConverter))]
    public string? PerfilIngesta { get; set; }

    /// <summary>
    /// Solo con <c>perfilIngesta</c> (ambos o ninguno). Nombre logico del repositorio (1 a 128 caracteres, sin
    /// <c>/</c> ni <c>\</c>) que identifica los documentos en vez del nombre de la coleccion. Inmutable.
    /// </summary>
    [Newtonsoft.Json.JsonConverter(typeof(CadenaEstrictaConverter))]
    public string? RepositorioLogico { get; set; }

    /// <summary>
    /// URL https del repositorio a clonar, p. ej. <c>https://dev.azure.com/organizacion/proyecto/_git/repositorio</c>.
    /// Obligatoria. Solo se aceptan los origenes de GET api/catalogo/origenes-git; otra responde 400 sin conectarse.
    /// </summary>
    public string? Url { get; set; }

    /// <summary>
    /// Opcional. Subcarpeta del repositorio a indexar (p. ej. <c>src</c>); ausente = el repositorio entero. Lo que
    /// queda fuera no se indexa y, si ya estaba en la coleccion, se borra.
    /// </summary>
    public string? CarpetaRaiz { get; set; }

    /// <summary>
    /// Opcional. Perfil de embeddings de la coleccion (GET api/catalogo/perfiles-embeddings); se fija al crearla.
    /// Ausente = el perfil por defecto o el que ya tiene la coleccion.
    /// </summary>
    public string? Perfil { get; set; }

    /// <summary>
    /// Opcional. true = guardar tambien un resumen de negocio de cada fragmento (para <c>espacio=resumen</c>). Solo
    /// cuenta al crear la coleccion. Gasta cuota de tokens del LLM.
    /// </summary>
    public bool? Resumen { get; set; }

    /// <summary>
    /// Opcional. Perfil de LLM que escribe los resumenes de esta ingesta (GET api/catalogo/perfiles-llm); ausente = el
    /// por defecto.
    /// </summary>
    public string? PerfilLlm { get; set; }
}
