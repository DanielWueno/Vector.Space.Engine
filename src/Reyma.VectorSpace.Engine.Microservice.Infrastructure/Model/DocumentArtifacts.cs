using System.Text;

namespace Reyma.VectorSpace.Engine.Microservice.Infrastructure.Model;

/// <summary>
/// Lo que el escaner de Rag.Context.Engine (FileSystemIngestionScanner) decide por archivo, aplicado a un documento que
/// llega por HTTP: lenguaje por extension, y omitir lo que su perfil no admite por extension o por tamano.
/// Lo que decide por directorio (exclusiones como bin/ u obj/, reglas .ragignore) no aplica: aqui el cliente elige que
/// documentos envia. Leer rutas del servidor queda para el origen 'ruta' (item 1.7).
/// </summary>
public static class DocumentArtifacts
{
    // Copia del ExtensionMap de FileSystemIngestionScanner.
    private static readonly Dictionary<string, SourceLanguage> ExtensionMap =
        new(StringComparer.OrdinalIgnoreCase)
    {
        [".cs"]    = SourceLanguage.CSharp,
        [".ts"]    = SourceLanguage.TypeScript,
        [".tsx"]   = SourceLanguage.TypeScript,
        [".js"]    = SourceLanguage.JavaScript,
        [".jsx"]   = SourceLanguage.JavaScript,
        [".xaml"]  = SourceLanguage.Xaml,
        [".sql"]   = SourceLanguage.Sql,
        [".md"]    = SourceLanguage.Markdown,
        [".txt"]   = SourceLanguage.PlainText,
        [".json"]  = SourceLanguage.PlainText,
        [".xml"]   = SourceLanguage.PlainText,
        [".csproj"]= SourceLanguage.PlainText,
    };

    /// <summary>
    /// El artefacto que el escaner habria emitido para este documento, o null si lo habria omitido. La ruta se conserva
    /// tal cual llega: el Id del chunk normaliza separadores (ChunkBuilder.BuildIdentityKey), pero el encabezado del
    /// texto enriquecido usa la ruta literal, igual que en el origen.
    /// </summary>
    public static RawArtifact? TryCreate(string relativePath, string content, ScanProfile profile, DateTimeOffset lastModified)
    {
        var extension = Path.GetExtension(relativePath);
        if (!profile.AllowedExtensions.Contains(extension))
            return null;

        // El escaner compara el tamano en disco; aqui el contenido ya llego decodificado y se mide en UTF-8.
        var sizeBytes = Encoding.UTF8.GetByteCount(content);
        if (sizeBytes > profile.MaxFileSizeBytes)
            return null;

        return new RawArtifact(
            AbsolutePath: relativePath,
            RelativePath: relativePath,
            Language: ExtensionMap.GetValueOrDefault(extension, SourceLanguage.Unknown),
            LastModified: lastModified,
            SizeBytes: sizeBytes);
    }
}
