namespace Reyma.VectorSpace.Engine.Microservice.Business.Commands.Colecciones;

/// <summary>
/// Limites del contrato. Un repo entero se ingiere en varias solicitudes de hasta 100 documentos (1.7); cuantas
/// ingestas corren y esperan a la vez lo fija <see cref="LimiteDeIngestasOptions"/>.
/// </summary>
public static class Limites
{
    public const int MaxTextosPorEmbedding = 64;
    public const int MaxCaracteresPorTexto = 8_192;

    public const int MaxCaracteresConsulta = 2_048;
    public const int TopKPorDefecto = 10;
    public const int MaxTopK = 100;

    public const int MaxDocumentosPorIngesta = 100;
    public const int MaxCaracteresRuta = 1_024;
    public const int MaxCaracteresPorDocumento = 1_048_576;

    /// <summary>
    /// Limite de cuerpo HTTP para <c>POST {coleccion}/ingestas</c> (1.7.1). El default de Kestrel
    /// (30 000 000 bytes) quedaba por debajo de lo que el contrato ya admite: 100 documentos de hasta
    /// 1 MiB cada uno son 104 857 600 bytes de contenido. Se suma ~20% de margen para la estructura JSON
    /// (claves, comillas, escapes de la ruta y del contenido) en vez de medir el peor caso exacto.
    /// </summary>
    public const long MaxBytesPorIngesta = 125_000_000;
}
