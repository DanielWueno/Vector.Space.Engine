namespace Reyma.VectorSpace.Engine.Microservice.Business.Models.Responses;

/// <summary>
/// Vectores densos en el orden de los textos recibidos. <see cref="Modelo"/> y <see cref="Dimension"/> los fija el
/// perfil de la coleccion (item 1.6), no el cliente.
/// </summary>
public sealed record EmbeddingsResponse(string Coleccion, string Modelo, int Dimension, IReadOnlyList<float[]> Vectores);

public sealed record BusquedaResponse(string Coleccion, IReadOnlyList<ResultadoBusquedaResponse> Resultados);

/// <summary>
/// Una coleccion existente en el vector store (item 1.16), para saber que colecciones hay sin necesitar acceso
/// directo a Qdrant. <see cref="PerfilEmbeddings"/>/<see cref="Modelo"/>/<see cref="Dimension"/> salen de la
/// metadata guardada en la coleccion (item 1.7.4); los tres null si la coleccion no la tiene (creada antes de
/// 1.7.4 y nunca tocada desde entonces).
/// </summary>
public sealed record ColeccionInfoResponse(string Coleccion, string? PerfilEmbeddings, string? Modelo, int? Dimension);

/// <summary>
/// Un resultado de busqueda (item 1.18 amplia los campos). <see cref="LineaInicio"/>/<see cref="LineaFin"/>/
/// <see cref="Lenguaje"/>/<see cref="TipoChunk"/>/<see cref="ContentHash"/>: metadatos de chunk que la ingesta
/// escribe en todo punto; null solo si el punto viene de antes de este item y no los tiene. <see cref="Namespace"/>/
/// <see cref="Clase"/>/<see cref="Metodo"/>: null cuando el chunk no aplica (p. ej. Markdown). Dato, no resumen: el
/// texto resumido por hit es una decision futura (1.20), <see cref="ContentHash"/> no lo reemplaza.
/// <see cref="SimbolosDefinidos"/>/<see cref="SimbolosConsumidos"/>: lista vacia, nunca null, cuando el payload no
/// los tiene (el chunk no define/consume ningun simbolo, o la estrategia de chunking no los extrae).
/// </summary>
public sealed record ResultadoBusquedaResponse(
    string Id, float Puntuacion, string Ruta, string Texto,
    int? LineaInicio = null, int? LineaFin = null, string? Lenguaje = null, string? TipoChunk = null, string? ContentHash = null,
    string? Namespace = null, string? Clase = null, string? Metodo = null,
    IReadOnlyList<string>? SimbolosDefinidos = null, IReadOnlyList<string>? SimbolosConsumidos = null)
{
    public IReadOnlyList<string> SimbolosDefinidos { get; init; } = SimbolosDefinidos ?? Array.Empty<string>();
    public IReadOnlyList<string> SimbolosConsumidos { get; init; } = SimbolosConsumidos ?? Array.Empty<string>();
}

/// <summary>
/// Resultado de una ingesta. El origen 'documentos' (MVP) es sincrono y vuelve con <see cref="Estado"/> "completada";
/// el origen 'proyecto' desde .zip (1.7.5) es asincrono: responde con "pendiente"/"procesando" (conteos en 0) y el
/// cliente consulta <c>GET .../ingestas/{ingestaId}</c> hasta ver "completada" con los conteos finales, o "error".
/// Recibidos = indexados + sin cambios + omitidos. Omitido: extension fuera del perfil de ingesta o tamano mayor que su
/// limite, los mismos filtros por archivo que el escaner de Rag.Context.Engine (item 1.4).
/// <see cref="DocumentosEliminados"/> (1.7.5): documentos que ya no estan en el proyecto, cuyos puntos se barrieron
/// tras la ingesta; siempre 0 en el origen 'documentos' (no barre nada por su cuenta, ver item 1.7.2).
/// <see cref="ResumenesGenerados"/>/<see cref="ResumenesPendientes"/>/<see cref="ResumenTokensConsumidos"/> (1.7.7):
/// solo tienen sentido en una coleccion con el resumen de negocio habilitado; siempre 0 en una sin el. Una corrida
/// que corta por el tope de tokens deja <see cref="ResumenesPendientes"/> mayor que 0, reanudable en la siguiente
/// ingesta sobre la misma coleccion (no repite los ya resumidos).
/// <see cref="MensajeError"/> (item 1.15): solo tiene valor cuando <see cref="Estado"/> es "error" (el mensaje de la
/// excepcion que corto el procesamiento en segundo plano); null en cualquier otro estado.
/// </summary>
public sealed record IngestaResponse(
    Guid IngestaId,
    string Coleccion,
    string Estado,
    int DocumentosRecibidos,
    int DocumentosIndexados,
    int DocumentosSinCambios,
    int DocumentosOmitidos = 0,
    int DocumentosEliminados = 0,
    int ResumenesGenerados = 0,
    int ResumenesPendientes = 0,
    int ResumenTokensConsumidos = 0,
    string? MensajeError = null);
