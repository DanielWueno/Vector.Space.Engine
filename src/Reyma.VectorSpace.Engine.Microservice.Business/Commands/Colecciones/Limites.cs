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
}
