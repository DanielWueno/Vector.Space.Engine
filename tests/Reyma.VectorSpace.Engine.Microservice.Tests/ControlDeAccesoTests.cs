using System.Reflection;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Reyma.VectorSpace.Engine.Microservice.Api.Controllers;
using Reyma.VectorSpace.Engine.Microservice.Business;
using Reyma.VectorSpace.Engine.Microservice.Business.Colecciones;
using static Reyma.VectorSpace.Engine.Microservice.Tests.IdentidadDePrueba;

namespace Reyma.VectorSpace.Engine.Microservice.Tests;

[Collection("Api")]
public sealed class ControlDeAccesoTests(ApiFactory factory)
{
    private static ConcesionColeccion Valida() => new()
    {
        Coleccion = "propia",
        Operaciones = [OperacionColeccion.Consultar],
        Usuarios = ["ana"]
    };

    public static TheoryData<string> ConcesionesInvalidas() => new()
    {
        "nombre-mayusculas", "nombre-vacio", "nombre-salto-de-linea", "sin-operaciones", "operacion-desconocida",
        "sin-sujetos", "usuario-vacio", "usuario-guid-vacio", "cliente-guid-vacio", "rol-vacio",
    };

    [Theory]
    [MemberData(nameof(ConcesionesInvalidas))]
    public void ValidadorRechazaConcesionesQueOtorgarianDeMas(string caso)
    {
        var c = Valida();
        switch (caso)
        {
            case "nombre-mayusculas": c.Coleccion = "Propia"; break;
            case "nombre-vacio": c.Coleccion = ""; break;
            case "nombre-salto-de-linea": c.Coleccion = "propia\n"; break;
            case "sin-operaciones": c.Operaciones = []; break;
            case "operacion-desconocida": c.Operaciones = [(OperacionColeccion)99]; break;
            case "sin-sujetos": c.Usuarios = []; break;
            case "usuario-vacio": c.Usuarios = [" "]; break;
            case "usuario-guid-vacio": c.Usuarios = [Guid.Empty.ToString()]; break;
            case "cliente-guid-vacio": c.Clientes = [Guid.Empty]; break;
            case "rol-vacio": c.Roles = [""]; break;
        }

        var resultado = new ControlDeAccesoOptionsValidator().Validate(null, new ControlDeAccesoOptions { Concesiones = [c] });

        Assert.True(resultado.Failed, caso);
    }

    [Fact]
    public void ValidadorAceptaUnaConcesionCompletaYLaTablaVacia()
    {
        var validador = new ControlDeAccesoOptionsValidator();

        Assert.True(validador.Validate(null, new ControlDeAccesoOptions { Concesiones = [Valida()] }).Succeeded);
        Assert.True(validador.Validate(null, new ControlDeAccesoOptions()).Succeeded);
    }

    [Fact]
    public void ElServicioNoArrancaConUnaTablaInvalida()
    {
        var api = factory.ConIdentidad(new Dictionary<string, string?>
        {
            ["ControlDeAcceso:Concesiones:0:Coleccion"] = "propia",
            ["ControlDeAcceso:Concesiones:0:Operaciones:0"] = "Consultar",
            ["ControlDeAcceso:Concesiones:0:Clientes:0"] = Guid.Empty.ToString(),
        });

        Assert.Throws<OptionsValidationException>(() => api.CreateClient());
    }

    [Fact]
    public void ElServicioNoArrancaConConcesionesEscritasComoTexto()
    {
        var api = factory.ConIdentidad(new Dictionary<string, string?>
        {
            ["ControlDeAcceso:Concesiones:0"] = "prueba-propia",
            ["ControlDeAcceso:Concesiones:1"] = "prueba-ajena",
        });

        var error = Assert.Throws<OptionsValidationException>(() => api.CreateClient());
        Assert.Contains("debe ser un objeto", error.Message);
    }

    [Theory]
    [InlineData("rag-engine", true)]
    [InlineData("micro-repo", true)]
    [InlineData("a", true)]
    [InlineData("propia_2", true)]
    [InlineData("-propia", false)]
    [InlineData("propia\n", false)]
    [InlineData("propia ", false)]
    [InlineData("Propia", false)]
    [InlineData("pro/pia", false)]
    [InlineData("propia..", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void ReglaDeNombre(string? nombre, bool valido) => Assert.Equal(valido, NombreColeccion.EsValido(nombre));

    [Fact]
    public void LaReglaDeNombreAceptaHasta63Caracteres()
    {
        Assert.True(NombreColeccion.EsValido(new string('a', 63)));
        Assert.False(NombreColeccion.EsValido(new string('a', 64)));
    }

    // Guardas estructurales: que una solicitud o accion nueva no quede fuera del control sin que nadie lo note.

    [Fact]
    public void TodaSolicitudConColeccionPasaPorElControlDeAcceso()
    {
        var solicitudes = typeof(BusinessAssembly).Assembly.GetTypes()
            .Where(t => !t.IsAbstract && typeof(IBaseRequest).IsAssignableFrom(t))
            .ToArray();

        Assert.NotEmpty(solicitudes);
        foreach (var tipo in solicitudes.Where(t => t.GetProperty("Coleccion") is not null))
            Assert.True(typeof(ISolicitudDeColeccion).IsAssignableFrom(tipo), $"{tipo.Name} tiene Coleccion pero no implementa ISolicitudDeColeccion.");
    }

    [Fact]
    public void TodaAccionExigeUsuarioAutenticadoYNingunaEsAnonima()
    {
        var acciones = typeof(ColeccionesController).Assembly.GetTypes()
            .Where(t => typeof(ControllerBase).IsAssignableFrom(t) && !t.IsAbstract)
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Select(m => (Controller: t, Accion: m)))
            .ToArray();

        Assert.NotEmpty(acciones);
        foreach (var (controller, accion) in acciones)
        {
            Assert.NotEmpty(accion.GetCustomAttributes<AuthorizeAttribute>());
            Assert.Empty(accion.GetCustomAttributes<AllowAnonymousAttribute>());
            Assert.Empty(controller.GetCustomAttributes<AllowAnonymousAttribute>());
        }
    }
}
