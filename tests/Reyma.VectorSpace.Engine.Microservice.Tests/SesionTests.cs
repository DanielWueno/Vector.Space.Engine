using System.Net;
using Reyma.VectorSpace.Engine.Microservice.Business.Models.Responses;
using static Reyma.VectorSpace.Engine.Microservice.Tests.IdentidadDePrueba;

namespace Reyma.VectorSpace.Engine.Microservice.Tests;

/// <summary>
/// La identidad heredada de la plantilla, de punta a punta: un token valido resuelve el usuario actual con las
/// extensiones corporativas, pasando por el JwtBearer real (incluido su mapeo de claims).
/// </summary>
[Collection("Api")]
public sealed class SesionTests(ApiFactory factory)
{
    [Fact]
    public async Task SinTokenDevuelve401()
    {
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/api/sesion");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Contains(response.Headers.WwwAuthenticate, header => header.Scheme == "Bearer");
    }

    [Fact]
    public async Task TokenValidoResuelveElUsuarioActual()
    {
        using var client = factory.ConIdentidad().Cliente(
            Token(sub: SubAna, usuario: "ana", roles: ["lector", "analista"], cliente: ClienteOrg));
        using var response = await client.GetAsync("/api/sesion");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var usuario = await response.Leer<UsuarioActualResponse>();
        Assert.Equal(SubAna.ToString(), usuario.UserId);
        Assert.Equal("ana", usuario.UserName);
        Assert.Equal(ClienteOrg.ToString(), usuario.ClientId);
        Assert.Equal(["analista", "lector"], usuario.Roles.Order());
    }
}
