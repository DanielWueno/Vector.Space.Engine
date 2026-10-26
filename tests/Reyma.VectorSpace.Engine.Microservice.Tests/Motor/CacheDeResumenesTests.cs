using Reyma.VectorSpace.Engine.Microservice.Infrastructure.Resumen;

namespace Reyma.VectorSpace.Engine.Microservice.Tests.Motor;

/// <summary>
/// Cache de resumenes por content_hash + version de prompt + modelo (1.7.7, criterio 3): clave compuesta, sin falsos
/// positivos si cambia cualquiera de las tres partes.
/// </summary>
public sealed class CacheDeResumenesTests
{
    [Fact]
    public void UnaClaveNuevaNoEstaEnLaCache()
    {
        var cache = new CacheDeResumenes();
        Assert.False(cache.TryObtener("hash-1", "v1", "modelo-x", out _));
    }

    [Fact]
    public void GuardarYLeerLaMismaClaveDevuelveLoGuardado()
    {
        var cache = new CacheDeResumenes();
        cache.Guardar("hash-1", "v1", "modelo-x", new ResumenCacheEntrada(SinContenidoDeNegocio: false, Texto: "Resumen de prueba."));

        Assert.True(cache.TryObtener("hash-1", "v1", "modelo-x", out var entrada));
        Assert.False(entrada.SinContenidoDeNegocio);
        Assert.Equal("Resumen de prueba.", entrada.Texto);
    }

    [Theory]
    [InlineData("hash-2", "v1", "modelo-x")] // content_hash distinto
    [InlineData("hash-1", "v2", "modelo-x")] // version de prompt distinta
    [InlineData("hash-1", "v1", "modelo-y")] // modelo distinto
    public void CualquierParteDistintaDeLaClaveEsUnaEntradaDistinta(string hash, string version, string modelo)
    {
        var cache = new CacheDeResumenes();
        cache.Guardar("hash-1", "v1", "modelo-x", new ResumenCacheEntrada(false, "Resumen de prueba."));

        Assert.False(cache.TryObtener(hash, version, modelo, out _));
    }

    [Fact]
    public void UnChunkSinContenidoDeNegocioSeGuardaYSeLeeIgual()
    {
        var cache = new CacheDeResumenes();
        cache.Guardar("hash-1", "v1", "modelo-x", new ResumenCacheEntrada(SinContenidoDeNegocio: true, Texto: "SIN_CONTENIDO_DE_NEGOCIO"));

        Assert.True(cache.TryObtener("hash-1", "v1", "modelo-x", out var entrada));
        Assert.True(entrada.SinContenidoDeNegocio);
    }
}
