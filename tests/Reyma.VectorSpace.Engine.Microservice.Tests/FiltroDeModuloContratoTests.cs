using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Newtonsoft.Json;
using Reyma.VectorSpace.Engine.Microservice.Api;
using Reyma.VectorSpace.Engine.Microservice.Infrastructure.VectorStore;
using static Reyma.VectorSpace.Engine.Microservice.Tests.IdentidadDePrueba;

namespace Reyma.VectorSpace.Engine.Microservice.Tests;

/// <summary>
/// Item 1.21.1: contrato de <c>modulos</c> en <c>POST .../busquedas</c> sin Qdrant (validacion con
/// <see cref="MotorDePrueba"/>) y equivalencia de <see cref="ModulePrefixes.Derive"/> con el predicado literal de la ficha.
/// </summary>
[Collection("Api")]
public sealed class FiltroDeModuloContratoTests(ApiFactory factory)
{
    private static readonly string TokenAna = Token(sub: SubAna, usuario: "ana");

    private (WebApplicationFactory<Program> Api, MotorDePrueba Motor) ConMotor()
    {
        var motor = new MotorDePrueba();
        return (factory.ConIdentidad(servicios: MotorDePrueba.Registrar(motor)), motor);
    }

    private static Task<HttpResponseMessage> Post(HttpClient client, string json) =>
        client.PostAsync("/api/colecciones/propia/busquedas", new StringContent(json, System.Text.Encoding.UTF8, "application/json"));

    public static TheoryData<string> CuerposInvalidos() => new()
    {
        """{"consulta":"hola","modulos":[]}""",
        """{"consulta":"hola","modulos":[""]}""",
        """{"consulta":"hola","modulos":["  "]}""",
        """{"consulta":"hola","modulos":["alpha","\t"]}""",
        """{"consulta":"hola","modulos":"alpha"}""",
        """{"consulta":"hola","modulos":[1]}""",
        """{"consulta":"hola","modulos":[true]}""",
        """{"consulta":"hola","modulos":[null]}""",
        """{"consulta":"hola","modulos":["alpha",null]}""",
        """{"consulta":"hola","modulos":[["alpha"]]}""",
        """{"consulta":"hola","modulos":[{"a":1}]}""",
        """{"consulta":"hola","modulos":{}}""",
        """{"consulta":"hola","modulos":1}""",
        """{"consulta":"hola","modulos":true}""",
        JsonConvert.SerializeObject(new { consulta = "hola", modulos = Enumerable.Range(0, 51).Select(i => $"m{i}") }),
    };

    [Theory]
    [MemberData(nameof(CuerposInvalidos))]
    public async Task ModulosInvalidosResponden400SinLlegarAlMotor(string json)
    {
        var (api, motor) = ConMotor();
        using var client = api.Cliente(TokenAna);
        using var response = await Post(client, json);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(motor.Llamadas);
    }

    public static TheoryData<string, string[]?> CuerposValidos() => new()
    {
        { """{"consulta":"hola"}""", null },
        { """{"consulta":"hola","modulos":null}""", null },
        { """{"consulta":"hola","modulos":["alpha"]}""", ["alpha"] },
        // Sin recorte ni normalizacion: lo que no es blanco llega tal cual, mayusculas y espacios incluidos.
        { """{"consulta":"hola","modulos":[" Alpha ","alpha/child","alpha.Foo"]}""", [" Alpha ", "alpha/child", "alpha.Foo"] },
        {
            JsonConvert.SerializeObject(new { consulta = "hola", modulos = Enumerable.Range(0, 50).Select(i => $"m{i}") }),
            Enumerable.Range(0, 50).Select(i => $"m{i}").ToArray()
        },
    };

    [Theory]
    [MemberData(nameof(CuerposValidos))]
    public async Task ModulosValidosLleganAlMotorSinCambiosYAusentesSonNull(string json, string[]? esperado)
    {
        var (api, motor) = ConMotor();
        using var client = api.Cliente(TokenAna);
        using var response = await Post(client, json);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Single(motor.Llamadas);
        if (esperado is null)
            Assert.Null(motor.UltimosModulos);
        else
            Assert.Equal(esperado, motor.UltimosModulos);
    }

    /// <summary>Predicado literal de la ficha 1.21.1, evaluado sobre los campos originales.</summary>
    public static bool Predicado(string modulo, string? ns, string? ruta) =>
        (ns is not null && (string.Equals(ns, modulo, StringComparison.Ordinal) || ns.StartsWith(modulo + ".", StringComparison.Ordinal))) ||
        (ruta is not null && (string.Equals(ruta, modulo, StringComparison.Ordinal) || ruta.StartsWith(modulo + "/", StringComparison.Ordinal)));

    public static TheoryData<string?, string?> Puntos() => new()
    {
        { "alpha", "x/a.cs" },
        { "alpha.Foo", "x/b.cs" },
        { "other.Space", "alpha/child/Foo.cs" },
        { "alpha-private", "y/n.cs" },
        { "Alpha", "y/n.cs" },
        { "Company.alpha", "y/n.cs" },
        { "zeta", "src/alpha/Foo.cs" },
        { null, null },
        { null, "readme.md" },
        { "", "" },
        { "alpha..Bar", "alpha//double.md" },
        { ".alpha", "/alpha/x.md" },
        { "alpha.", "alpha/" },
        { "a.b.c.d", "a/b/c/d.cs" },
        { "alpha beta.x", "dir with space/x.md" },
    };

    private static readonly string[] Modulos =
    [
        "alpha", "alpha.Foo", "alpha/child", "alpha/child/Foo.cs", "Alpha", "alpha-private", "Company", "Company.alpha",
        "src", "src/alpha", "x", "alpha.", "alpha/", "alpha..Bar", "alpha//double.md", ".alpha", "/alpha", "a", "a.b",
        "a/b/c", "a.b.c.d", "b", "alpha beta", "dir with space", " alpha", "alpha ", "readme.md", "zeta", "other",
    ];

    [Theory]
    [MemberData(nameof(Puntos))]
    public void LosPrefijosDerivadosEquivalenAlPredicadoOriginal(string? ns, string? ruta)
    {
        var prefijos = ModulePrefixes.Derive(ns, ruta);
        Assert.Equal(prefijos.Distinct(StringComparer.Ordinal).Count(), prefijos.Count);
        Assert.Equal(prefijos.OrderBy(p => p, StringComparer.Ordinal), prefijos);
        foreach (var modulo in Modulos.Concat(prefijos))
            Assert.True(Predicado(modulo, ns, ruta) == prefijos.Contains(modulo, StringComparer.Ordinal),
                $"modulo '{modulo}', ns '{ns}', ruta '{ruta}': predicado {Predicado(modulo, ns, ruta)}, prefijos [{string.Join(", ", prefijos)}]");
    }

    [Fact]
    public void LosCasosLiteralesDeLaFichaSeComportanComoDice()
    {
        Assert.Contains("alpha", ModulePrefixes.Derive("alpha", null));
        Assert.Contains("alpha", ModulePrefixes.Derive("alpha.Foo", null));
        Assert.Contains("alpha", ModulePrefixes.Derive(null, "alpha/child/Foo.cs"));
        Assert.DoesNotContain("alpha", ModulePrefixes.Derive("alpha-private", "alpha-private/x.cs"));
        Assert.DoesNotContain("alpha", ModulePrefixes.Derive("Alpha", null));
        Assert.DoesNotContain("alpha", ModulePrefixes.Derive("Company.alpha", null));
        Assert.DoesNotContain("alpha", ModulePrefixes.Derive(null, "src/alpha/Foo.cs"));
        Assert.Empty(ModulePrefixes.Derive(null, null));
        Assert.Equal(["alpha", "alpha.", "alpha..Bar"], ModulePrefixes.Derive("alpha..Bar", null));
    }
}
