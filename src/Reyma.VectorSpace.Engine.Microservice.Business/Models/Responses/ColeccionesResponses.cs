using Reyma.VectorSpace.Engine.Microservice.Business.Colecciones;

namespace Reyma.VectorSpace.Engine.Microservice.Business.Models.Responses;

/// <summary>Colecciones que alcanza el usuario en sesion y con que operaciones.</summary>
public sealed record ColeccionesAccesiblesResponse(IReadOnlyList<ColeccionAccesibleResponse> Colecciones);

public sealed record ColeccionAccesibleResponse(string Nombre, IReadOnlyList<OperacionColeccion> Operaciones);

/// <summary>
/// Vectores densos en el orden de los textos recibidos. <see cref="Modelo"/> y <see cref="Dimension"/> los fija el
/// perfil de la coleccion (item 1.6), no el cliente.
/// </summary>
public sealed record EmbeddingsResponse(string Coleccion, string Modelo, int Dimension, IReadOnlyList<float[]> Vectores);

public sealed record BusquedaResponse(string Coleccion, IReadOnlyList<ResultadoBusquedaResponse> Resultados);

public sealed record ResultadoBusquedaResponse(string Id, float Puntuacion, string Ruta, string Texto);

/// <summary>
/// Resultado de una ingesta. En el MVP es sincrona y vuelve con <see cref="Estado"/> "completada"; el identificador y
/// el estado existen para que una ingesta en cola (item 1.7) use la misma forma con 202 sin romper a los clientes.
/// Recibidos = indexados + sin cambios + omitidos. Omitido: extension fuera del perfil de ingesta o tamano mayor que su
/// limite, los mismos filtros por archivo que el escaner de Rag.Context.Engine (item 1.4).
/// </summary>
public sealed record IngestaResponse(
    Guid IngestaId,
    string Coleccion,
    string Estado,
    int DocumentosRecibidos,
    int DocumentosIndexados,
    int DocumentosSinCambios,
    int DocumentosOmitidos = 0);
