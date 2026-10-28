using Reyma.VectorSpace.Engine.Microservice.Business.Models.Requests;
using Reyma.VectorSpace.Engine.Microservice.Business.Models.Responses;

namespace Reyma.VectorSpace.Engine.Microservice.Business.Motor;

// Puertos del contrato HTTP hacia el motor. Los implementa MotorVectorial en el proyecto Infrastructure (item 1.4:
// pipeline de ingesta portado, SparseTokenizer, cliente HTTP del sidecar de embeddings y adaptador Qdrant).
// Reciben la coleccion ya autorizada: no repiten el control de acceso.

// perfil: nombre del perfil de embeddings a usar (1.7.4); null = perfil por defecto de la coleccion/instancia.

public interface IMotorDeEmbeddings
{
    Task<EmbeddingsResponse> CalcularAsync(string coleccion, IReadOnlyList<string> textos, string? perfil, CancellationToken cancellationToken);
}

public interface IMotorDeBusqueda
{
    /// <summary>
    /// simbolosDefinidos (item 1.18): filtro exacto opcional, null/vacio = sin filtro (mismos resultados que antes
    /// de este item). No vacio = solo puntos cuyo defined_symbols contenga alguno de esos nombres.
    /// </summary>
    Task<BusquedaResponse> BuscarAsync(
        string coleccion, string consulta, int topK, string? perfil, IReadOnlyList<string>? simbolosDefinidos, CancellationToken cancellationToken);
}

public interface IMotorDeIngesta
{
    /// <summary>resumen: opt-in del resumen de negocio (1.7.7); solo tiene efecto al crear la coleccion.</summary>
    Task<IngestaResponse> IngerirAsync(
        string coleccion, IReadOnlyList<DocumentoRequest> documentos, string? perfil, bool resumen, CancellationToken cancellationToken);
}

/// <summary>
/// Origen 'proyecto' desde un .zip subido (1.7.5). Separado de <see cref="IMotorDeIngesta"/> porque el comando que lo
/// orquesta necesita dos pasos distintos: leer y validar el .zip es rapido y sincrono (un .zip invalido o malicioso
/// responde 400 sin tocar el limite de admision ni crear un registro de estado); procesarlo es el mismo pipeline de
/// ingesta de siempre, mas el barrido de documentos que ya no estan en el .zip, y corre en segundo plano porque un
/// proyecto completo tarda minutos u horas (1.7.3).
/// </summary>
public interface IMotorDeIngestaProyecto
{
    /// <summary>Lee y valida el .zip (limites de tamano, entradas, ratio, Zip Slip, enlaces); 400 si no es admisible.</summary>
    IReadOnlyList<DocumentoRequest> LeerProyectoZip(byte[] zip, string? carpetaRaiz);

    /// <summary>Ingesta normal mas el barrido de documentos ausentes, acotado a lo que el .zip trajo esta vez.</summary>
    /// <param name="resumen">Ver <see cref="IMotorDeIngesta.IngerirAsync"/> (1.7.7).</param>
    Task<ResultadoIngestaProyecto> ProcesarAsync(
        string coleccion, IReadOnlyList<DocumentoRequest> documentos, string? perfil, bool resumen, CancellationToken cancellationToken);
}

/// <summary>
/// Conteos de una ingesta de proyecto ya terminada (1.7.5). <see cref="Recibidos"/> (item 1.17): el origen Git no lo
/// conoce de forma sincrona (a diferencia del .zip, que lo lee antes de admitir), asi que <c>IngerirProyectoGitCommand</c>
/// crea el registro de estado con 0 y dependia de este campo para corregirlo al terminar; sin el, quedaba en 0 para
/// siempre, rompiendo la invariante "Recibidos = Indexados + SinCambios + Omitidos". <see cref="ResumenesGenerados"/>/
/// <see cref="ResumenesPendientes"/>/<see cref="ResumenTokensConsumidos"/> (1.7.7): siempre 0 en una coleccion sin
/// resumen habilitado.
/// </summary>
public sealed record ResultadoIngestaProyecto(
    int Recibidos, int Indexados, int SinCambios, int Omitidos, int DocumentosEliminados,
    int ResumenesGenerados = 0, int ResumenesPendientes = 0, int ResumenTokensConsumidos = 0);

/// <summary>
/// Origen 'proyecto' desde una URL Git (1.7.6, decidido en 1.7.3). Mismo patron de dos pasos que
/// <see cref="IMotorDeIngestaProyecto"/>: validar la URL contra la lista blanca es sincrono e instantaneo (no toca la
/// red; una URL fuera de la lista responde 400 antes de crear ningun registro de estado ni tocar el limite de
/// admision); clonar (superficial y endurecido), escanear el checkout y ejecutar el mismo pipeline de ingesta de
/// proyecto (con barrido de ausentes) tarda segundos a minutos y corre en segundo plano, igual que el .zip.
/// </summary>
public interface IMotorDeIngestaProyectoGit
{
    /// <summary>
    /// Valida la URL contra la lista blanca de origenes Git permitidos (esquema, host, puerto y prefijo de ruta
    /// exactos) sin resolver DNS ni abrir ninguna conexion. 400 si la URL no es admisible.
    /// </summary>
    Uri ValidarUrlGit(string? url);

    /// <summary>
    /// Clona superficialmente la URL ya validada en un checkout temporal (con tope de tamano y plazo), lo escanea con
    /// el mismo perfil de escaneo del motor y ejecuta el mismo pipeline de ingesta de proyecto que el .zip (con
    /// barrido de documentos ausentes). El checkout temporal se borra al terminar, con exito o con error.
    /// </summary>
    /// <param name="resumen">Ver <see cref="IMotorDeIngesta.IngerirAsync"/> (1.7.7).</param>
    Task<ResultadoIngestaProyecto> ProcesarGitAsync(
        string coleccion, Uri url, string? carpetaRaiz, string? perfil, bool resumen, CancellationToken cancellationToken);
}

/// <summary>
/// Catalogo de colecciones existentes en el vector store (item 1.16): hallazgo del usuario al revisar el listado de
/// ingestas por coleccion (1.15) -- sin este puerto, no hay forma de saber que colecciones existen sin acceso directo
/// a Qdrant (ni <see cref="IMotorDeIngestaProyecto"/> ni el registro en memoria de ingestas lo saben: una coleccion
/// pudo crearse en una instancia anterior, o nunca haber pasado por el origen 'proyecto').
/// </summary>
public interface IMotorDeColecciones
{
    /// <summary>
    /// Nombre de cada coleccion existente, con su perfil de embeddings si lo tiene guardado en su metadata (1.7.4);
    /// null en los campos de perfil si la coleccion no la tiene (creada antes de 1.7.4 y nunca tocada desde entonces).
    /// </summary>
    Task<IReadOnlyList<ColeccionInfoResponse>> ListarAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Documentos indexados en una coleccion (item 1.19): ruta, huella (<c>document_fingerprint</c>) y cantidad de
    /// puntos (chunks) de cada uno, agrupados por su clave de identidad (<c>file_path</c>). Hallazgo al revisar
    /// <see cref="ListarRutasDocumentadasAsync"/> (ese metodo solo trae rutas e ids, para el barrido de ausentes del
    /// origen 'proyecto') y <see cref="GetDocumentPointsAsync"/> (trae la huella, pero solo de rutas pedidas
    /// explicitamente): ninguno de los dos expone un listado legible por coleccion. No es el "manifiesto" que el
    /// item 1.4 declaro no portado; es un listado de solo lectura, mas simple, sin paginar (catalogo de
    /// descubrimiento, no ruta caliente, igual criterio que <see cref="ListarAsync"/>). La huella de un documento es
    /// null si sus puntos no comparten una sola huella (mismo criterio de "cambiado" que
    /// <c>DocumentIngestionPipeline.IsUnchanged</c>). Coleccion inexistente: lista vacia, nunca 404.
    /// </summary>
    Task<IReadOnlyList<DocumentoIndexadoResponse>> ListarDocumentosAsync(string coleccion, CancellationToken cancellationToken);
}
