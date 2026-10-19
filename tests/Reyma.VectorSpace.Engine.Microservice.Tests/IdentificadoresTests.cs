using Reyma.VectorSpace.Engine.Microservice.Business;

namespace Reyma.VectorSpace.Engine.Microservice.Tests;

public sealed class IdentificadoresTests
{
    [Fact]
    public void NombreEsNamespaceValidoSinInfijoDuplicado()
    {
        Assert.Matches(@"^[A-Za-z][A-Za-z0-9]*(\.[A-Za-z][A-Za-z0-9]*)+$", ServicioInfo.Nombre);
        Assert.False(ServicioInfo.Nombre.EndsWith(".Microservice", StringComparison.Ordinal),
            "-n no debe terminar en .Microservice.");
    }

    [Fact]
    public void RutaBaseEsValida() =>
        Assert.Matches(@"^/[a-z0-9]+(-[a-z0-9]+)*(/[a-z0-9]+(-[a-z0-9]+)*)*$", ServicioInfo.RutaBase);

    [Fact]
    public void NombreApiEsValido() =>
        Assert.Matches(@"^[a-z0-9]+(-[a-z0-9]+)*(\.[a-z0-9]+(-[a-z0-9]+)*)+$", ServicioInfo.NombreApi);

    [Fact]
    public void PrefijoPermisosEsValido() =>
        Assert.Matches(@"^[A-Z0-9]+(_[A-Z0-9]+)*(\.[A-Z0-9]+(_[A-Z0-9]+)*)*$", ServicioInfo.PrefijoPermisos);
}
