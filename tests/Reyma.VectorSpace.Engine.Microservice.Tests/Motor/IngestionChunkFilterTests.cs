using Microsoft.Extensions.Logging.Abstractions;
using Reyma.VectorSpace.Engine.Microservice.Infrastructure.Abstractions;
using Reyma.VectorSpace.Engine.Microservice.Infrastructure.Model;
using Reyma.VectorSpace.Engine.Microservice.Infrastructure.Chunking;
using Reyma.VectorSpace.Engine.Microservice.Infrastructure.Ingestion;
using Xunit;

namespace Reyma.VectorSpace.Engine.Microservice.Tests.Motor;

public class IngestionChunkFilterTests
{
    [Theory]
    [InlineData("public interface ICombProvider { void Create(); }", "ICombProvider")]
    [InlineData("public interface IEntidadReport : IEntidad { }", "IEntidadReport")]
    [InlineData("public class Example { public int Value; }", "Example")]
    [InlineData("public static class Example { }", "Example")]
    [InlineData("public struct Example { }", "Example")]
    [InlineData("public record Example;", "Example")]
    public async Task DeclaracionCSharpCorta_SeConserva(string source, string typeName)
    {
        var chunks = await ChunkAsync(source, SourceLanguage.CSharp);
        var declaration = Assert.Single(chunks,
            c => c.Type == ChunkType.Class && c.DefinedSymbols.Contains(typeName));

        Assert.InRange(declaration.Content.Trim().Length, 1, 59);
        Assert.True(WithShortDeclarations(declaration));
    }

    [Theory]
    [InlineData("export class Example {\n}\n")]
    [InlineData("@Injectable()\nexport class Example {\n}\n")]
    public async Task DeclaracionTypeScriptCorta_SeConserva(string source)
    {
        var chunks = await ChunkAsync(source, SourceLanguage.TypeScript);
        var declaration = Assert.Single(chunks, c => c.Type == ChunkType.Class);

        Assert.InRange(declaration.Content.Trim().Length, 1, 59);
        Assert.True(WithShortDeclarations(declaration));
    }

    [Fact]
    public async Task CampoConstructorMetodoYPropiedadCortos_NoSeEximenConLaClase()
    {
        const string source = """
            public class Example
            {
                private int count;
                public Example() { }
                public void Run() { }
                public int Value { get; set; }
            }
            """;

        var chunks = await ChunkAsync(source, SourceLanguage.CSharp);
        Assert.Equal(5, chunks.Count);
        Assert.Contains(chunks, c => c.Type == ChunkType.Class && c.DefinedSymbols.Contains("count"));
        Assert.Contains(chunks, c => c.Type == ChunkType.Constructor);
        Assert.Contains(chunks, c => c.Type == ChunkType.Method);
        Assert.Contains(chunks, c => c.Type == ChunkType.Property);
        Assert.All(chunks, c => Assert.InRange(c.Content.Trim().Length, 1, 59));

        var kept = Assert.Single(chunks, WithShortDeclarations);
        Assert.Equal(new[] { "Example" }, kept.DefinedSymbols);
        Assert.Equal(ChunkType.Class, kept.Type);
    }

    [Theory]
    [InlineData(ChunkType.Class)]
    [InlineData(ChunkType.Interface)]
    public void Excepcion_ExigeElSimboloDelTipo(ChunkType type)
    {
        var chunk = CreateChunk(type, "public interface Example");
        Assert.True(WithShortDeclarations(chunk));
        Assert.False(WithShortDeclarations(chunk with { DefinedSymbols = [] }));
        Assert.False(WithShortDeclarations(chunk with { DefinedSymbols = ["Other"] }));
        Assert.False(WithShortDeclarations(chunk with { DefinedSymbols = ["example"] }));
        Assert.False(WithShortDeclarations(chunk with
        {
            Metadata = chunk.Metadata with { ClassName = null }
        }));
    }

    [Fact]
    public void LimiteDeContenidoCrudo_SeConservaParaTodosLosTipos()
    {
        foreach (var type in Enum.GetValues<ChunkType>())
        {
            var chunk = CreateChunk(type, new string('x', 59)) with { DefinedSymbols = [] };
            Assert.False(WithShortDeclarations(chunk));
            Assert.False(WithShortDeclarations(chunk with
            {
                Content = " \r\n\t" + new string('x', 59) + "\t\n "
            }));
            Assert.True(WithShortDeclarations(chunk with { Content = new string('x', 60) }));
            Assert.True(WithShortDeclarations(chunk with { Content = new string('x', 61) }));
        }
    }

    [Fact]
    public void ContenidoVacio_NoSeIndexaAunqueDeclareUnSimbolo()
    {
        foreach (var type in Enum.GetValues<ChunkType>())
        {
            var chunk = CreateChunk(type, "");
            Assert.False(WithShortDeclarations(chunk));
            Assert.False(WithShortDeclarations(chunk with { Content = " \r\n\t " }));
        }
    }

    [Fact]
    public void ConfiguracionPorDefecto_ConservaElFiltroAnterior()
    {
        // Porte: el origen lo leia de IngestionOptions (false por defecto); aqui es una constante del pipeline.
        var indexShortTypeDeclarations = DocumentIngestionPipeline.IndexShortTypeDeclarations;
        Assert.False(indexShortTypeDeclarations);
        foreach (var type in Enum.GetValues<ChunkType>())
        {
            var chunk = CreateChunk(type, "public class Example");
            Assert.False(DocumentIngestionPipeline.IsIndexable(chunk, indexShortTypeDeclarations));
            Assert.True(DocumentIngestionPipeline.IsIndexable(
                chunk with { Content = new string('x', 60) }, indexShortTypeDeclarations));
        }
    }

    private static bool WithShortDeclarations(CodeChunk chunk) =>
        DocumentIngestionPipeline.IsIndexable(chunk, indexShortTypeDeclarations: true);

    private static CodeChunk CreateChunk(ChunkType type, string content) =>
        ChunkBuilder.Create(
            Artifact(SourceLanguage.CSharp, content.Length),
            content,
            enrichedContent: new string('h', 200) + "\n" + content,
            type,
            startLine: 1,
            endLine: 1,
            repositoryName: "test",
            className: "Example",
            definedSymbols: ["Example"]);

    private static RawArtifact Artifact(SourceLanguage language, int length) =>
        new(
            AbsolutePath: language == SourceLanguage.CSharp ? "/tmp/example.cs" : "/tmp/example.ts",
            RelativePath: language == SourceLanguage.CSharp ? "example.cs" : "example.ts",
            Language: language,
            LastModified: DateTimeOffset.UnixEpoch,
            SizeBytes: length);

    private static async Task<IReadOnlyList<CodeChunk>> ChunkAsync(string source, SourceLanguage language)
    {
        IChunkingStrategy strategy = language == SourceLanguage.CSharp
            ? new RoslynCSharpChunkingStrategy(NullLogger<RoslynCSharpChunkingStrategy>.Instance)
            : new TypeScriptChunkingStrategy(NullLogger<TypeScriptChunkingStrategy>.Instance);

        var chunks = new List<CodeChunk>();
        await foreach (var chunk in strategy.ChunkAsync(
            Artifact(language, source.Length), source, ChunkingOptions.Default))
            chunks.Add(chunk);
        return chunks;
    }
}
