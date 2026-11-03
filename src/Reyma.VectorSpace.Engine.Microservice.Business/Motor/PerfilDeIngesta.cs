using Reyma.Exceptions.Api;

namespace Reyma.VectorSpace.Engine.Microservice.Business.Motor;

public interface IPerfilDeIngestaColeccion
{
    Task VerificarPerfilDeIngestaAsync(string coleccion, PerfilDeIngesta? perfil, CancellationToken cancellationToken);
}

/// <summary>Opt-in de representacion compatible; no cambia el perfil de embeddings ni el ranking.</summary>
public sealed record PerfilDeIngesta(string Nombre, string RepositorioLogico)
{
    public const string RagLocal112 = "rag-local-1.12";

    public static IEnumerable<string> Errores(string? perfil, string? repositorio)
    {
        if (perfil is null && repositorio is null)
            yield break;
        if (perfil != RagLocal112)
            yield return $"PerfilIngesta solo admite '{RagLocal112}'; omitir ambos campos conserva el perfil nativo.";
        if (string.IsNullOrWhiteSpace(repositorio) || repositorio.Length > 128 ||
            repositorio.Any(c => char.IsControl(c) || c is '/' or '\\'))
            yield return "RepositorioLogico requiere 1..128 caracteres, sin separadores de ruta ni caracteres de control.";
    }

    public static PerfilDeIngesta? Crear(string? perfil, string? repositorio)
    {
        var errores = Errores(perfil, repositorio).ToArray();
        if (errores.Length != 0)
            throw new BadRequestException(string.Join(" ", errores));
        return perfil is null ? null : new(perfil, repositorio!);
    }
}
