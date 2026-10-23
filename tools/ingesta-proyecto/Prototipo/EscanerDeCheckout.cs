namespace IngestaProyecto.Prototipo;

/// <summary>Lo que el escaner emite por archivo (equivale al RawArtifact del origen, sin lenguaje ni fecha).</summary>
public sealed record ArchivoEscaneado(string RutaRelativa, string RutaAbsoluta, long Bytes);

/// <summary>Perfil de escaneo: mismos valores que ScanProfile.DotNetEnterprise de Rag.Context.Engine.</summary>
public sealed record PerfilEscaneo(IReadOnlySet<string> Extensiones, IReadOnlyList<string> Excluir, long MaxBytesPorArchivo)
{
    public static PerfilEscaneo DotNetEnterprise { get; } = new(
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { ".cs", ".ts", ".tsx", ".js", ".jsx", ".xaml", ".sql", ".md", ".txt", ".json", ".xml", ".csproj" },
        ["bin", "obj", "node_modules", ".git", ".vs", "__pycache__", ".idea", "packages", "migrations", "Migrations"],
        524_288);
}

/// <summary>
/// FileSystemIngestionScanner de Rag.Context.Engine adaptado para correr del lado del servicio sobre un checkout o una
/// extraccion temporal que no controla. Mismo orden de recorrido, mismos filtros (extension, tamano, directorios
/// excluidos, .gitignore + .ragignore con IgnoreRules copiado tal cual). Una sola diferencia, de seguridad: no sigue
/// enlaces simbolicos ni junctions, ni de archivo ni de directorio. El origen los sigue (DirectoryInfo.EnumerateDirectories
/// los devuelve como directorios normales), y un enlace dentro de un repo ajeno apuntaria fuera de la raiz.
/// </summary>
public static class EscanerDeCheckout
{
    public sealed record Omision(string Ruta, string Motivo);

    public static List<ArchivoEscaneado> Escanear(string raiz, PerfilEscaneo perfil, List<Omision>? omisiones = null)
    {
        var root = new DirectoryInfo(raiz);
        if (!root.Exists) throw new DirectoryNotFoundException($"No existe la raiz: {raiz}");
        var ignore = IgnoreRules.Load(raiz);
        var salida = new List<ArchivoEscaneado>();
        Recorrer(root, root.FullName, perfil, ignore, salida, omisiones);
        return salida;
    }

    private static void Recorrer(DirectoryInfo dir, string raiz, PerfilEscaneo perfil, IgnoreRules ignore,
        List<ArchivoEscaneado> salida, List<Omision>? omisiones)
    {
        IEnumerable<FileInfo> archivos;
        try { archivos = dir.EnumerateFiles(); }
        catch (UnauthorizedAccessException) { return; }

        foreach (var archivo in archivos)
        {
            if (!perfil.Extensiones.Contains(archivo.Extension)) continue;
            var relativa = Path.GetRelativePath(raiz, archivo.FullName);
            if (archivo.Attributes.HasFlag(FileAttributes.ReparsePoint) || archivo.LinkTarget is not null)
            {
                omisiones?.Add(new Omision(relativa, "enlace simbolico"));
                continue;
            }
            if (archivo.Length > perfil.MaxBytesPorArchivo) continue;
            if (ignore.IsIgnored(relativa, isDirectory: false)) continue;
            salida.Add(new ArchivoEscaneado(relativa, archivo.FullName, archivo.Length));
        }

        IEnumerable<DirectoryInfo> subdirectorios;
        try { subdirectorios = dir.EnumerateDirectories(); }
        catch (UnauthorizedAccessException) { return; }

        foreach (var sub in subdirectorios)
        {
            if (perfil.Excluir.Any(p => string.Equals(sub.Name, p, StringComparison.OrdinalIgnoreCase))) continue;
            if (sub.Attributes.HasFlag(FileAttributes.ReparsePoint) || sub.LinkTarget is not null)
            {
                omisiones?.Add(new Omision(Path.GetRelativePath(raiz, sub.FullName), "enlace simbolico o junction"));
                continue;
            }
            if (!ignore.HasNegations && ignore.IsIgnored(Path.GetRelativePath(raiz, sub.FullName), isDirectory: true)) continue;
            Recorrer(sub, raiz, perfil, ignore, salida, omisiones);
        }
    }
}
