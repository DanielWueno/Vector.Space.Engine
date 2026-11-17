using Reyma.Exceptions.Api;
using Reyma.VectorSpace.Engine.Microservice.Business.Models.Responses;

namespace Reyma.VectorSpace.Engine.Microservice.Business.Motor;

public interface IPerfilDeIngestaColeccion
{
    Task VerificarPerfilDeIngestaAsync(string coleccion, PerfilDeIngesta? perfil, CancellationToken cancellationToken);
}

/// <summary>Opt-in de representacion compatible; no cambia el perfil de embeddings ni el ranking.</summary>
public sealed record PerfilDeIngesta(string Nombre, string RepositorioLogico)
{
    public const string RagLocal112 = "rag-local-1.12";

    /// <summary>
    /// Valores que admite <c>perfilIngesta</c> (GET api/catalogo/perfiles-ingesta). <see cref="Errores"/> valida
    /// contra esta misma lista, asi que el catalogo no puede anunciar un perfil que la ingesta rechace.
    /// </summary>
    public static IReadOnlyList<PerfilIngestaInfoResponse> Catalogo { get; } =
    [
        new(RagLocal112,
            "Fragmenta igual que el control rag-local de 1.12: chunks de 512 tokens con solape de 64 (la ingesta " +
            "nativa usa 1024 tokens). Las 5 lineas de contexto padre y la division con solape del control solo " +
            "forman parte de la huella del documento: no cambian el corte. Los " +
            "documentos quedan identificados por repositorioLogico en vez de por el nombre de la coleccion. No cambia " +
            "el perfil de embeddings ni el ranking. Se fija al crear la coleccion: omitirlo o cambiarlo despues responde 409.",
            RequiereRepositorioLogico: true)
    ];

    public static IEnumerable<string> Errores(string? perfil, string? repositorio)
    {
        if (perfil is null && repositorio is null)
            yield break;
        if (!Catalogo.Any(p => p.Nombre == perfil))
            yield return $"PerfilIngesta solo admite {string.Join(", ", Catalogo.Select(p => $"'{p.Nombre}'"))}; " +
                         "omitir ambos campos conserva el perfil nativo (ver GET api/catalogo/perfiles-ingesta).";
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
