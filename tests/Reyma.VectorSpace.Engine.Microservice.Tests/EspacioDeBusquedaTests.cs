using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Reyma.VectorSpace.Engine.Microservice.Api;
using Reyma.VectorSpace.Engine.Microservice.Business.Motor;
using static Reyma.VectorSpace.Engine.Microservice.Tests.IdentidadDePrueba;

namespace Reyma.VectorSpace.Engine.Microservice.Tests;

/// <summary>
/// Item 1.20: campo <c>espacio</c> de <c>POST .../busquedas</c>, sin <c>docker-compose.motor.yml</c>. La validacion
/// del cuerpo (valor admitido, <c>perfil</c> junto con <c>resumen</c>) corre con un <see cref="MotorDePrueba"/>; el 400
/// por instancia sin resumen configurado corre con el motor real y Qdrant en el puerto de descarte de
/// <see cref="ApiFactory"/>: si llegara a tocar Qdrant responderia 503, no 400.
/// </summary>
[Collection("Api")]
public sealed class EspacioDeBusquedaTests(ApiFactory factory)
{
    private static readonly string TokenAna = Token(sub: SubAna, usuario: "ana");

    private (WebApplicationFactory<Program> Api, MotorDePrueba Motor) ConMotor()
    {
        var motor = new MotorDePrueba();
        return (factory.ConIdentidad(servicios: MotorDePrueba.Registrar(motor)), motor);
    }

    public static TheoryData<string> CuerposInvalidos() => new()
    {
        """{"consulta":"hola","espacio":"otro"}""",
        """{"consulta":"hola","espacio":""}""",
        """{"consulta":"hola","espacio":"Resumen"}""",
        // El perfil de la consulta en el espacio resumen lo fija la instancia: enviarlo es un 400, no se ignora.
        """{"consulta":"hola","espacio":"resumen","perfil":"tei-arctic-256"}""",
    };

    [Theory]
    [MemberData(nameof(CuerposInvalidos))]
    public async Task EspacioInvalidoOResumenConPerfilResponde400SinLlegarAlMotor(string json)
    {
        var (api, motor) = ConMotor();
        using var client = api.Cliente(TokenAna);
        using var response = await client.PostAsync("/api/colecciones/propia/busquedas",
            new StringContent(json, System.Text.Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(motor.Llamadas);
    }

    public static TheoryData<string, EspacioDeBusqueda> EspaciosValidos() => new()
    {
        { """{"consulta":"hola"}""", EspacioDeBusqueda.Codigo },
        { """{"consulta":"hola","espacio":"codigo"}""", EspacioDeBusqueda.Codigo },
        { """{"consulta":"hola","espacio":"codigo","perfil":"tei-arctic-256"}""", EspacioDeBusqueda.Codigo },
        { """{"consulta":"hola","espacio":"resumen"}""", EspacioDeBusqueda.Resumen },
    };

    [Theory]
    [MemberData(nameof(EspaciosValidos))]
    public async Task ElEspacioLlegaAlMotorYAusenteEsCodigo(string json, EspacioDeBusqueda esperado)
    {
        var (api, motor) = ConMotor();
        using var client = api.Cliente(TokenAna);
        using var response = await client.PostAsync("/api/colecciones/propia/busquedas",
            new StringContent(json, System.Text.Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Single(motor.Llamadas);
        Assert.Equal(esperado, motor.UltimoEspacio);
    }

    [Fact]
    public async Task ResumenNoConfiguradoEnEstaInstanciaResponde400SinTocarQdrantNiElSidecar()
    {
        // Explicito: la configuracion de Development puede traer un perfil de resumen; aqui se quiere la instancia sin el.
        using var client = factory.ConIdentidad(new Dictionary<string, string?> { ["Motor:Resumen:PerfilEmbeddings"] = "" })
            .Cliente(TokenAna);

        using var resumen = await client.PostJson("/api/colecciones/propia/busquedas", new { consulta = "hola", espacio = "resumen" });
        Assert.Equal(HttpStatusCode.BadRequest, resumen.StatusCode);
        Assert.Contains("Motor:Resumen:PerfilEmbeddings", await resumen.Content.ReadAsStringAsync());

        // Control: la misma instancia, en el espacio de codigo, si llega a Qdrant (caido) y responde 503.
        using var codigo = await client.PostJson("/api/colecciones/propia/busquedas", new { consulta = "hola", espacio = "codigo" });
        Assert.Equal(HttpStatusCode.ServiceUnavailable, codigo.StatusCode);
    }
}
