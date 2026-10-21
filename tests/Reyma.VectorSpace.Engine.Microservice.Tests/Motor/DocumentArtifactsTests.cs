using Reyma.VectorSpace.Engine.Microservice.Infrastructure.Chunking;
using Reyma.VectorSpace.Engine.Microservice.Infrastructure.Model;

namespace Reyma.VectorSpace.Engine.Microservice.Tests.Motor;

/// <summary>Lo que el escaner del origen decide por archivo, aplicado a documentos recibidos por HTTP (1.4).</summary>
public sealed class DocumentArtifactsTests
{
    private static readonly DateTimeOffset Ahora = DateTimeOffset.UnixEpoch;

    [Theory]
    [InlineData("src/A.cs", SourceLanguage.CSharp)]
    [InlineData("SRC\\B.CS", SourceLanguage.CSharp)]
    [InlineData("web/app.tsx", SourceLanguage.TypeScript)]
    [InlineData("web/app.js", SourceLanguage.JavaScript)]
    [InlineData("docs/leeme.md", SourceLanguage.Markdown)]
    [InlineData("db/proc.sql", SourceLanguage.Sql)]
    [InlineData("ui/Vista.xaml", SourceLanguage.Xaml)]
    [InlineData("App.csproj", SourceLanguage.PlainText)]
    public void ExtensionDelPerfilDaElLenguajeDelEscaner(string ruta, SourceLanguage esperado)
    {
        var artefacto = DocumentArtifacts.TryCreate(ruta, "contenido", ScanProfile.DotNetEnterprise, Ahora);

        Assert.NotNull(artefacto);
        Assert.Equal(esperado, artefacto.Language);
        Assert.Equal(ruta, artefacto.RelativePath);
    }

    [Theory]
    [InlineData("script.py")]
    [InlineData("config.yaml")]
    [InlineData("sin-extension")]
    public void ExtensionFueraDelPerfilSeOmite(string ruta) =>
        Assert.Null(DocumentArtifacts.TryCreate(ruta, "contenido", ScanProfile.DotNetEnterprise, Ahora));

    [Fact]
    public void ElLimiteDeTamanoSeMideEnBytesUtf8()
    {
        var limite = (int)ScanProfile.DotNetEnterprise.MaxFileSizeBytes;

        Assert.NotNull(DocumentArtifacts.TryCreate("a.md", new string('a', limite), ScanProfile.DotNetEnterprise, Ahora));
        Assert.Null(DocumentArtifacts.TryCreate("a.md", new string('a', limite + 1), ScanProfile.DotNetEnterprise, Ahora));
        // 'ñ' ocupa dos bytes: la mitad de caracteres ya llega al limite.
        Assert.Null(DocumentArtifacts.TryCreate("a.md", new string('ñ', limite / 2 + 1), ScanProfile.DotNetEnterprise, Ahora));
    }

    [Fact]
    public void LaIdentidadNoDependeDelSeparadorPeroLaRutaSeConservaLiteral()
    {
        var unix = DocumentArtifacts.TryCreate("src/A.cs", "x", ScanProfile.DotNetEnterprise, Ahora)!;
        var windows = DocumentArtifacts.TryCreate("src\\A.cs", "x", ScanProfile.DotNetEnterprise, Ahora)!;

        Assert.Equal(
            ChunkBuilder.BuildIdentityKey("repo", unix.RelativePath),
            ChunkBuilder.BuildIdentityKey("repo", windows.RelativePath));
        Assert.Equal("src\\A.cs", windows.RelativePath);
    }
}
