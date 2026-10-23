using System.Net.Http.Headers;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Reyma.VectorSpace.Engine.Microservice.Business;

namespace IngestaProyecto.Banco;

/// <summary>Perfil de embeddings con el que arranca una instancia del servicio (un solo modelo por instancia, como hoy).</summary>
public sealed record PerfilEmbeddings(string Nombre, string UrlBase, string Modelo, int Dimension, string PrefijoConsulta, bool AltheIA)
{
    public static PerfilEmbeddings TeiArctic(string url) => new("tei-arctic-256", url, "Snowflake/snowflake-arctic-embed-m-v2.0", 256, "query: ", false);
    public static PerfilEmbeddings AltheiaBgeM3(int dimension) => new($"altheia-bge-m3-{dimension}", "https://altheia", "bge-m3", dimension, "", true);
}

/// <summary>
/// La API del servicio en Kestrel real (copia de la de tools/consumo-ingesta), con emisor JWT de prueba y concesiones
/// sobre las colecciones it-1-7-3-*. Motor real contra el Qdrant dado y el perfil de embeddings dado. Las credenciales de
/// AltheIA solo se pasan por nombre de variable a la configuracion; nunca se imprimen.
/// </summary>
public sealed class ServicioEnProceso : WebApplicationFactory<Reyma.VectorSpace.Engine.Microservice.Api.Program>
{
    public const string Prefijo = "it-1-7-3-";
    private const string Emisor = "https://ingesta-proyecto.prueba.invalid";
    private static readonly SymmetricSecurityKey Clave = new(RandomNumberGenerator.GetBytes(32));
    private readonly Dictionary<string, string?> _valores;

    public ServicioEnProceso(string qdrantHost, int qdrantGrpc, PerfilEmbeddings perfil)
    {
        _valores = new Dictionary<string, string?>
        {
            ["ASPNETCORE_BASEPATH"] = "", ["AllowedOrigins"] = "",
            ["IdentityServerAuthenticationOptions:Authority"] = "", ["PermissionsValidatorOptions:ApiUri"] = "",
            ["IntegrityServiceOptions:ApiUri"] = "",
            ["Serilog:MinimumLevel:Default"] = "Fatal", ["Serilog:MinimumLevel:Override:Microsoft"] = "Fatal",
            ["Serilog:MinimumLevel:Override:System"] = "Fatal",
            ["Motor:Qdrant:Host"] = qdrantHost, ["Motor:Qdrant:PuertoGrpc"] = qdrantGrpc.ToString(),
            ["Motor:Embeddings:UrlBase"] = perfil.UrlBase,
            ["Motor:Embeddings:Modelo"] = perfil.Modelo,
            ["Motor:Embeddings:Dimension"] = perfil.Dimension.ToString(),
            ["Motor:Embeddings:PrefijoConsulta"] = perfil.PrefijoConsulta,
        };
        if (perfil.AltheIA)
        {
            _valores["Motor:Embeddings:ApiKey"] = Environment.GetEnvironmentVariable("ALTHEIA_KEY");
            _valores["Motor:Embeddings:RutaCertificadoCA"] = Environment.GetEnvironmentVariable("ALTHEIA_CERT");
        }
        string[] colecciones = ["vse", "rce", "vse-zip", "mezcla", "resumen", "metadatos", "tenant"];
        for (var i = 0; i < colecciones.Length; i++)
        {
            _valores[$"ControlDeAcceso:Concesiones:{i}:Coleccion"] = Prefijo + colecciones[i];
            _valores[$"ControlDeAcceso:Concesiones:{i}:Operaciones:0"] = "Consultar";
            _valores[$"ControlDeAcceso:Concesiones:{i}:Operaciones:1"] = "Ingestar";
            _valores[$"ControlDeAcceso:Concesiones:{i}:Usuarios:0"] = "banco";
        }
        UseKestrel(0);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseContentRoot(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "..",
            "src", "Reyma.VectorSpace.Engine.Microservice.Api")));
        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.Sources.Clear();
            config.AddJsonFile("appsettings.json", optional: false);
            config.AddInMemoryCollection(_valores);
        });
        builder.ConfigureTestServices(services =>
            services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, o =>
            {
                o.TokenValidationParameters.ValidIssuer = Emisor;
                o.TokenValidationParameters.IssuerSigningKey = Clave;
            }));
    }

    public HttpClient Cliente()
    {
        StartServer();
        var client = CreateClient();
        client.Timeout = TimeSpan.FromHours(4);
        var token = new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = Emisor, Audience = ServicioInfo.NombreApi,
            Claims = new Dictionary<string, object> { ["sub"] = Guid.NewGuid().ToString(), ["preferred_username"] = "banco" },
            NotBefore = DateTime.UtcNow.AddMinutes(-1), IssuedAt = DateTime.UtcNow.AddMinutes(-1), Expires = DateTime.UtcNow.AddHours(6),
            SigningCredentials = new SigningCredentials(Clave, SecurityAlgorithms.HmacSha256)
        });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    /// <summary>El HttpClient nombrado del cliente de embeddings de 1.13 (BaseAddress, Bearer y CA de configuracion).</summary>
    public HttpClient ClienteDelSidecar()
    {
        StartServer();
        return Services.GetRequiredService<IHttpClientFactory>()
            .CreateClient(Reyma.VectorSpace.Engine.Microservice.Infrastructure.Vectorization.TeiVectorizationBrain.HttpClientName);
    }
}
