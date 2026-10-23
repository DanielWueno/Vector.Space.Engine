using MediatR;
using Reyma.VectorSpace.Engine.Microservice.Business.Colecciones;
using Reyma.VectorSpace.Engine.Microservice.Business.Models.Responses;
using Reyma.VectorSpace.Engine.Microservice.Business.Motor;
using Reyma.VectorSpace.Engine.Microservice.Business.Validacion;

namespace Reyma.VectorSpace.Engine.Microservice.Business.Commands.Colecciones;

/// <summary>
/// Embeddings densos de textos sueltos con el perfil de modelo de la coleccion. Superficie separada de la ingesta:
/// no escribe nada en la coleccion.
/// </summary>
public sealed record CalcularEmbeddingsCommand(string Coleccion, IReadOnlyList<string>? Textos, string? Perfil)
    : IRequest<EmbeddingsResponse>, ISolicitudDeColeccion, IValidable
{
    public OperacionColeccion Operacion => OperacionColeccion.Consultar;

    public IEnumerable<string> Errores()
    {
        if (Textos is null || Textos.Count == 0)
            yield return "Textos es obligatorio y no puede estar vacio.";
        else if (Textos.Count > Limites.MaxTextosPorEmbedding)
            yield return $"Textos admite como maximo {Limites.MaxTextosPorEmbedding} elementos.";
        else if (Textos.Any(string.IsNullOrWhiteSpace))
            yield return "Ningun texto puede estar vacio.";
        else if (Textos.Any(t => t.Length > Limites.MaxCaracteresPorTexto))
            yield return $"Cada texto admite como maximo {Limites.MaxCaracteresPorTexto} caracteres.";

        if (Perfil is not null && Perfil.Trim().Length == 0)
            yield return "Perfil no puede ser una cadena vacia si se especifica.";
        else if (Perfil is { Length: > 0 } && Perfil.Length > Limites.MaxCaracteresPerfil)
            yield return $"Perfil admite como maximo {Limites.MaxCaracteresPerfil} caracteres.";
    }

    public sealed class CalcularEmbeddingsCommandHandler(IMotorDeEmbeddings motor)
        : IRequestHandler<CalcularEmbeddingsCommand, EmbeddingsResponse>
    {
        public Task<EmbeddingsResponse> Handle(CalcularEmbeddingsCommand request, CancellationToken cancellationToken) =>
            motor.CalcularAsync(request.Coleccion, request.Textos!, request.Perfil, cancellationToken);
    }
}
