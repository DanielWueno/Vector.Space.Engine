using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Hosting;

using Serilog;

namespace Reyma.VectorSpace.Engine.Microservice.Api;

public class Program
{
    public static void Main(string[] args)
    {
        CreateHostBuilder(args).Build().Run();
    }

    public static IHostBuilder CreateHostBuilder(string[] args) =>
        Host.CreateDefaultBuilder(args)
            // Item 1.5/1.13: overlay local, nunca versionado (ver .gitignore: appsettings.*.local.json), para
            // secretos de desarrollo (Motor:Llm:ApiKey/RutaCertificadoCA, Motor:Embeddings:Perfiles:*:ApiKey/
            // RutaCertificadoCA) sin depender de variables de entorno de usuario ni de User Secrets.
            .ConfigureAppConfiguration((context, config) =>
                config.AddJsonFile($"appsettings.{context.HostingEnvironment.EnvironmentName}.local.json", optional: true, reloadOnChange: true))
            .ConfigureWebHostDefaults(webBuilder =>
            {
                webBuilder.UseStartup<Startup>();
            })
            .UseSerilog();
}
