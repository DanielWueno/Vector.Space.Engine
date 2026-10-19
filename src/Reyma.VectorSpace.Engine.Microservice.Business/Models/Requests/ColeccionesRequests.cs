namespace Reyma.VectorSpace.Engine.Microservice.Business.Models.Requests;

// Cuerpos HTTP. La coleccion viaja siempre en la ruta, nunca en el cuerpo, para que no haya dos valores que comparar.
// Las propiedades son anulables a proposito: un cuerpo incompleto llega a la validacion y responde 400 con el motivo.

public sealed class CalcularEmbeddingsRequest
{
    public List<string>? Textos { get; set; }
}

public sealed class BuscarRequest
{
    public string? Consulta { get; set; }

    /// <summary>Por defecto 10; entre 1 y 100 (ver Limites).</summary>
    public int? TopK { get; set; }
}

public sealed class IngestaRequest
{
    public OrigenIngestaRequest? Origen { get; set; }
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
