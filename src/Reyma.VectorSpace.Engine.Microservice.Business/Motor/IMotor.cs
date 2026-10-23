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
    Task<IngestaResponse> IngerirAsync(string coleccion, IReadOnlyList<DocumentoRequest> documentos, string? perfil, CancellationToken cancellationToken);
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
    Task<ResultadoIngestaProyecto> ProcesarAsync(
        string coleccion, IReadOnlyList<DocumentoRequest> documentos, string? perfil, CancellationToken cancellationToken);
}

/// <summary>Conteos de una ingesta de proyecto ya terminada (1.7.5).</summary>
public sealed record ResultadoIngestaProyecto(int Indexados, int SinCambios, int Omitidos, int DocumentosEliminados);
