using System.Net;
using System.Text;
using Newtonsoft.Json;
using Reyma.VectorSpace.Engine.Microservice.Infrastructure.Model;
using static Reyma.VectorSpace.Engine.Microservice.Tests.IdentidadDePrueba;

namespace Reyma.VectorSpace.Engine.Microservice.Tests;

[Collection("Api")]
public sealed class FiltrosTextualesContratoTests(ApiFactory factory)
{
    [Fact]
    public async Task LenguajesCanonicosYNamespaceLiteralLleganAlMotor()
    {
        var motor = new MotorDePrueba();
        var api = factory.ConIdentidad(servicios: MotorDePrueba.Registrar(motor));
        using var client = api.Cliente(Token(sub: SubAna));
        foreach (var lenguaje in Enum.GetNames<SourceLanguage>())
        {
            using var response = await client.PostJson("/api/colecciones/propia/busquedas",
                new { consulta = "texto", lenguaje, @namespace = " Alpha..Foo/Bar ", modulos = new[] { "Alpha" } });
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(lenguaje, motor.UltimoLenguaje);
            Assert.Equal(" Alpha..Foo/Bar ", motor.UltimoNamespace);
            Assert.Equal(["Alpha"], motor.UltimosModulos);
        }
        Assert.Equal(Enum.GetNames<SourceLanguage>().Length, motor.Llamadas.Count);
    }

    [Fact]
    public async Task AusentesNullYNamespaceBlancoSonAdmitidos()
    {
        var motor = new MotorDePrueba();
        var api = factory.ConIdentidad(servicios: MotorDePrueba.Registrar(motor));
        using var client = api.Cliente(Token(sub: SubAna));
        foreach (var json in new[]
        {
            """{"consulta":"texto"}""",
            """{"consulta":"texto","lenguaje":null,"namespace":null}""",
            """{"consulta":"texto","namespace":""}""",
            """{"consulta":"texto","namespace":" \t\r\n"}"""
        })
        {
            using var response = await client.PostAsync("/api/colecciones/propia/busquedas",
                new StringContent(json, Encoding.UTF8, "application/json"));
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Null(motor.UltimoLenguaje);
            Assert.True(string.IsNullOrWhiteSpace(motor.UltimoNamespace));
        }
        Assert.Equal(4, motor.Llamadas.Count);
    }

    [Fact]
    public async Task NamespaceConFormaDeFechaONumeroSigueSiendoTextoLiteral()
    {
        var motor = new MotorDePrueba();
        var api = factory.ConIdentidad(servicios: MotorDePrueba.Registrar(motor));
        using var client = api.Cliente(Token(sub: SubAna));
        foreach (var texto in new[] { "2026-11-03T09:00:00.000-06:00", "0", "true" })
        {
            using var response = await client.PostJson("/api/colecciones/propia/busquedas",
                new { consulta = "texto", @namespace = texto });
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(texto, motor.UltimoNamespace);
        }
    }

    [Fact]
    public async Task ValoresYTiposInvalidosResponden400AntesDelMotor()
    {
        var motor = new MotorDePrueba();
        var api = factory.ConIdentidad(servicios: MotorDePrueba.Registrar(motor));
        using var client = api.Cliente(Token(sub: SubAna));
        foreach (var json in new[] { "null", "[]", "false", "1", "\"texto\"" })
        {
            using var response = await client.PostAsync("/api/colecciones/propia/busquedas",
                new StringContent(json, Encoding.UTF8, "application/json"));
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Empty(motor.Llamadas);
        }
        foreach (var campo in new[] { "lenguaje", "namespace" })
        {
            var valores = new List<string> { "0", "1.5", "true", "false", "{}", "[]", """["alpha"]""" };
            if (campo == "lenguaje")
                valores.AddRange(new[] { "", " \t", "csharp", "CSHARP", " CSharp", "CSharp ", "C#", "0", "CSharp, Sql" }
                    .Select(JsonConvert.SerializeObject));
            foreach (var valor in valores)
            {
                using var response = await client.PostAsync("/api/colecciones/propia/busquedas",
                    new StringContent($$"""{"consulta":"texto","{{campo}}":{{valor}}}""", Encoding.UTF8, "application/json"));
                Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
                Assert.Empty(motor.Llamadas);
            }
        }
    }

    [Fact]
    public async Task LosFiltrosNoRetiranAutenticacionHttp()
    {
        var motor = new MotorDePrueba();
        var api = factory.ConIdentidad(servicios: MotorDePrueba.Registrar(motor));
        using var client = api.Cliente(null);
        using var response = await client.PostJson("/api/colecciones/propia/busquedas",
            new { consulta = "texto", lenguaje = "CSharp", @namespace = "alpha" });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Empty(motor.Llamadas);
    }
}
