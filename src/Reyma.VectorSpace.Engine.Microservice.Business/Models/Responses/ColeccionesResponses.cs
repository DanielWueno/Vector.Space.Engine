namespace Reyma.VectorSpace.Engine.Microservice.Business.Models.Responses;

/// <summary>
/// Vectores densos en el orden de los textos recibidos. <see cref="Modelo"/> y <see cref="Dimension"/> los fija el
/// perfil de la coleccion (item 1.6), no el cliente.
/// </summary>
public sealed record EmbeddingsResponse(string Coleccion, string Modelo, int Dimension, IReadOnlyList<float[]> Vectores);

public sealed record BusquedaResponse(string Coleccion, IReadOnlyList<ResultadoBusquedaResponse> Resultados);

public sealed record ResultadoBusquedaResponse(string Id, float Puntuacion, string Ruta, string Texto);

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
    int ResumenTokensConsumidos = 0);
