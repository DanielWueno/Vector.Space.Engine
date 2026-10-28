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

    /// <summary>Filtro opcional por simbolos definidos en una busqueda (item 1.18): 1 a 50 cadenas no vacias.</summary>
    public const int MaxSimbolosDefinidosPorConsulta = 50;

    public const int MaxDocumentosPorIngesta = 100;
    public const int MaxCaracteresRuta = 1_024;
    public const int MaxCaracteresPorDocumento = 1_048_576;

    /// <summary>Nombre de perfil de embeddings (1.7.4): los nombres reales son cortos (ej. "tei-arctic-256").</summary>
    public const int MaxCaracteresPerfil = 100;

    /// <summary>
    /// Limite de cuerpo HTTP para <c>POST {coleccion}/ingestas</c> (1.7.1). El default de Kestrel
    /// (30 000 000 bytes) quedaba por debajo de lo que el contrato ya admite: 100 documentos de hasta
    /// 1 MiB cada uno son 104 857 600 bytes de contenido. Se suma ~20% de margen para la estructura JSON
    /// (claves, comillas, escapes de la ruta y del contenido) en vez de medir el peor caso exacto.
    /// </summary>
    public const long MaxBytesPorIngesta = 125_000_000;

    /// <summary>
    /// Limite de cuerpo HTTP para <c>POST {coleccion}/ingestas/proyecto</c> (1.7.5): el .zip subido admite hasta
    /// 100 MiB (<c>LimitesZip.MaxBytesZip</c>), mas margen para el resto del multipart (cabeceras, carpetaRaiz,
    /// perfil).
    /// </summary>
    public const long MaxBytesZipProyecto = 110_000_000;

    /// <summary>
    /// Longitud maxima de la URL Git del origen 'proyecto' (1.7.6), igual que <c>ListaBlancaGit.MaxCaracteresUrl</c>
    /// del prototipo de 1.7.3: una URL mas larga ya es sospechosa antes de validarla contra la lista blanca.
    /// </summary>
    public const int MaxCaracteresUrlGit = 2_048;
}
