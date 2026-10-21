namespace Reyma.VectorSpace.Engine.Microservice.Infrastructure.Abstractions;

/// <summary>
/// Vector denso de textos. En Rag.Context.Engine lo calcula un ONNX en proceso (OnnxVectorizationBrain); aqui es un
/// cliente HTTP del sidecar decidido en 1.8/1.6. Documentos y consultas se separan porque el modelo usa un prefijo
/// solo para consultas. Vectores siempre de <see cref="EmbeddingDimensions"/> y con norma L2 = 1.
/// </summary>
public interface IVectorizationBrain
{
    string ModelName { get; }

    int EmbeddingDimensions { get; }

    Task<IReadOnlyList<float[]>> EmbedDocumentsAsync(IReadOnlyList<string> texts, CancellationToken cancellationToken);

    Task<float[]> EmbedQueryAsync(string text, CancellationToken cancellationToken);
}
