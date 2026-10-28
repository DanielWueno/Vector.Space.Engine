namespace Reyma.VectorSpace.Engine.Microservice.Business.Models.Requests;

// Cuerpos HTTP. La coleccion viaja siempre en la ruta, nunca en el cuerpo, para que no haya dos valores que comparar.
// Las propiedades son anulables a proposito: un cuerpo incompleto llega a la validacion y responde 400 con el motivo.

public sealed class CalcularEmbeddingsRequest
{
    public List<string>? Textos { get; set; }

    /// <summary>
    /// Perfil de embeddings a usar (1.7.4). Vacio/ausente = perfil por defecto o, si la coleccion ya existe, el que
    /// tiene guardado en su metadata. Un perfil no registrado responde 400; uno distinto al de una coleccion
    /// existente responde 409, aunque coincida la dimension.
    /// </summary>
    public string? Perfil { get; set; }
}

public sealed class BuscarRequest
{
    public string? Consulta { get; set; }

    /// <summary>Por defecto 10; entre 1 y 100 (ver Limites).</summary>
    public int? TopK { get; set; }

    /// <summary>Ver <see cref="CalcularEmbeddingsRequest.Perfil"/>.</summary>
    public string? Perfil { get; set; }

    /// <summary>
    /// Filtro exacto opcional por simbolos definidos (item 1.18): 1 a 50 cadenas no vacias; fuera de ese rango, 400.
    /// Acota la busqueda a puntos cuyo <c>defined_symbols</c> contenga alguno de estos nombres, con el mismo umbral y
    /// desempate deterministas que ya usa la busqueda. Ausente: la busqueda no cambia respecto a antes de este item.
    /// No implementa salto de simbolo ni reranking: filtra por igualdad sobre un indice keyword ya existente, el
    /// resto (que simbolos sembrar, con que peso) es responsabilidad del consumidor.
    /// </summary>
    public List<string>? SimbolosDefinidos { get; set; }
}

public sealed class IngestaRequest
{
    public OrigenIngestaRequest? Origen { get; set; }

    /// <summary>
    /// Perfil de embeddings de la coleccion (1.7.4). Se fija al crearla (primera ingesta) y queda en su metadata de
    /// Qdrant; vacio/ausente = perfil por defecto (<c>tei-arctic-256</c>). Una ingesta posterior con otro perfil
    /// responde 409 sin escribir, aunque coincida la dimension; un perfil no registrado responde 400 con los
    /// perfiles disponibles.
    /// </summary>
    public string? Perfil { get; set; }

    /// <summary>
    /// Opt-in del resumen de negocio (1.7.7): solo tiene efecto en la primera ingesta, la que crea la coleccion (el
    /// vector <c>dense-resumen</c> no se puede agregar despues). Ausente/false en una coleccion nueva = sin resumen.
    /// true sobre una coleccion existente que no lo tiene: 409 con un mensaje claro. Sobre una coleccion que ya lo
    /// tiene, da igual el valor: el resumen sigue automaticamente (es un atributo de la coleccion).
    /// </summary>
    public bool? Resumen { get; set; }
}

/// <summary>
/// De donde sale el contenido. <c>documentos</c>: el contenido viaja en el cuerpo (MVP, llamadas chicas).
/// <c>ruta</c>: el servicio lee una ruta a la que ya tiene acceso; reservado para el item 1.7 y hoy responde 501.
/// Son excluyentes: se envia <see cref="Documentos"/> o <see cref="Ruta"/>, no ambos.
/// </summary>
public sealed class OrigenIngestaRequest
{
    public const string TipoDocumentos = "documentos";
    public const string TipoRuta = "ruta";

    public string? Tipo { get; set; }

    public List<DocumentoRequest>? Documentos { get; set; }

    public string? Ruta { get; set; }
}

public sealed class DocumentoRequest
{
    /// <summary>Ruta relativa del documento dentro del origen; identifica al documento en la coleccion.</summary>
    public string? Ruta { get; set; }

    public string? Contenido { get; set; }
}

/// <summary>
/// Cuerpo de <c>POST {coleccion}/ingestas/git</c> (origen 'proyecto' desde URL Git, item 1.7.6). La URL debe estar en
/// la lista blanca de origenes Git permitidos (<c>Motor:Git:OrigenesPermitidos</c>); nunca lleva usuario, contrasena
/// ni query/fragmento (el servicio usa su propia identidad, nunca la de quien llama).
/// </summary>
public sealed class IngerirProyectoGitRequest
{
    public string? Url { get; set; }

    /// <summary>Igual que el origen .zip: quita el envoltorio "repo-rama/" si aplica.</summary>
    public string? CarpetaRaiz { get; set; }

    /// <summary>Ver <see cref="CalcularEmbeddingsRequest.Perfil"/>.</summary>
    public string? Perfil { get; set; }

    /// <summary>Ver <see cref="IngestaRequest.Resumen"/> (1.7.7).</summary>
    public bool? Resumen { get; set; }
}
