using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Reyma.VectorSpace.Engine.Microservice.Persistence;

namespace Reyma.VectorSpace.Engine.Microservice.Tests;

[Collection("Api")]
public sealed class ArranqueTests(ApiFactory factory)
{
    [Fact]
    public void ArrancaConConfiguracionMinimaYContextoVacio()
    {
        Assert.Equal("Testing", factory.Services.GetRequiredService<IHostEnvironment>().EnvironmentName);
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        Assert.Equal("Npgsql.EntityFrameworkCore.PostgreSQL", context.Database.ProviderName);
        Assert.Equal(ApiFactory.ConnectionString, context.Database.GetConnectionString());
        Assert.DoesNotContain(typeof(ApplicationDbContext).GetProperties(),
            property => property.DeclaringType == typeof(ApplicationDbContext)
                && property.PropertyType.IsGenericType
                && property.PropertyType.GetGenericTypeDefinition() == typeof(DbSet<>));
    }
}
