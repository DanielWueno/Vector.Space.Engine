using Reyma.VectorSpace.Engine.Microservice.Infrastructure.Model;

namespace Reyma.VectorSpace.Engine.Microservice.Infrastructure.Scanning;

/// <summary>Un archivo admitido por el perfil de escaneo, ya leido a memoria.</summary>
public sealed record DocumentoDeCheckout(string Ruta, string Contenido);

/// <summary>
/// Portado de tools/ingesta-proyecto/Prototipo/EscanerDeCheckout.cs (1.7.3), adaptado al <see cref="ScanProfile"/> del
/// motor (en vez del <c>PerfilEscaneo</c> propio del prototipo) y a leer el contenido en memoria (como
/// <c>LectorZip</c>) en vez de solo listar rutas: el origen 'proyecto' por URL Git (1.7.6) necesita documentos, no
/// solo nombres de archivo.
///
/// FileSystemIngestionScanner de Rag.Context.Engine adaptado para correr del lado del servicio sobre un checkout que
/// no controla. Mismo orden de recorrido, mismos filtros (extension, tamano, directorios excluidos, .gitignore +
/// .ragignore con <see cref="IgnoreRules"/>). Una sola diferencia, de seguridad: no sigue enlaces simbolicos ni
/// junctions, ni de archivo ni de directorio (1.7.3: el origen los sigue y un enlace dentro de un repo ajeno
/// apuntaria fuera de la raiz).
/// </summary>
public static class EscanerDeCheckout
{
    public sealed record ResultadoEscaneo(IReadOnlyList<DocumentoDeCheckout> Documentos, IReadOnlyList<(string Ruta, string Motivo)> Omitidos);

    public static ResultadoEscaneo Escanear(string raiz, ScanProfile perfil)
    {
        var root = new DirectoryInfo(raiz);
        if (!root.Exists) throw new DirectoryNotFoundException($"No existe la raiz: {raiz}");
        var ignore = IgnoreRules.Load(raiz);
        var documentos = new List<DocumentoDeCheckout>();
        var omitidos = new List<(string, string)>();
        Recorrer(root, root.FullName, perfil, ignore, documentos, omitidos);
        return new ResultadoEscaneo(documentos, omitidos);
    }

    private static void Recorrer(DirectoryInfo dir, string raiz, ScanProfile perfil, IgnoreRules ignore,
        List<DocumentoDeCheckout> documentos, List<(string, string)> omitidos)
    {
        IEnumerable<FileInfo> archivos;
        try { archivos = dir.EnumerateFiles(); }
        catch (UnauthorizedAccessException) { return; }

        foreach (var archivo in archivos)
        {
            if (!perfil.AllowedExtensions.Contains(archivo.Extension)) continue;
            var relativa = Path.GetRelativePath(raiz, archivo.FullName).Replace('\\', '/');
            if (archivo.Attributes.HasFlag(FileAttributes.ReparsePoint) || archivo.LinkTarget is not null)
            {
                omitidos.Add((relativa, "enlace simbolico"));
                continue;
            }
            if (archivo.Length > perfil.MaxFileSizeBytes) continue;
            if (ignore.IsIgnored(relativa, isDirectory: false)) continue;

            string contenido;
            try { contenido = File.ReadAllText(archivo.FullName); }
            catch (IOException ex) { omitidos.Add((relativa, $"error de lectura ({ex.Message})")); continue; }
            documentos.Add(new DocumentoDeCheckout(relativa, contenido));
        }

        IEnumerable<DirectoryInfo> subdirectorios;
        try { subdirectorios = dir.EnumerateDirectories(); }
        catch (UnauthorizedAccessException) { return; }

        foreach (var sub in subdirectorios)
        {
            if (perfil.ExcludePatterns.Any(p => string.Equals(sub.Name, p, StringComparison.OrdinalIgnoreCase))) continue;
            if (sub.Attributes.HasFlag(FileAttributes.ReparsePoint) || sub.LinkTarget is not null)
            {
                omitidos.Add((Path.GetRelativePath(raiz, sub.FullName).Replace('\\', '/'), "enlace simbolico o junction"));
                continue;
            }
            if (!ignore.HasNegations && ignore.IsIgnored(Path.GetRelativePath(raiz, sub.FullName).Replace('\\', '/'), isDirectory: true)) continue;
            Recorrer(sub, raiz, perfil, ignore, documentos, omitidos);
        }
    }
}
