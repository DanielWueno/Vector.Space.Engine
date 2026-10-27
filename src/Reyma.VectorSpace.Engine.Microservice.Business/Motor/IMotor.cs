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
    Task<BusquedaResponse> BuscarAsync(string coleccion, string consulta, int topK, string? perfil, CancellationToken cancellationToken);
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
/// Conteos de una ingesta de proyecto ya terminada (1.7.5). <see cref="ResumenesGenerados"/>/<see cref="ResumenesPendientes"/>/
/// <see cref="ResumenTokensConsumidos"/> (1.7.7): siempre 0 en una coleccion sin resumen habilitado.
/// </summary>
public sealed record ResultadoIngestaProyecto(
    int Indexados, int SinCambios, int Omitidos, int DocumentosEliminados,
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
}
