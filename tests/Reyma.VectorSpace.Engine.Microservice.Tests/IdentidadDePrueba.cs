using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Newtonsoft.Json;
using Reyma.VectorSpace.Engine.Microservice.Api;
using Reyma.VectorSpace.Engine.Microservice.Business;
using Reyma.VectorSpace.Engine.Microservice.Business.Models.Requests;
using Reyma.VectorSpace.Engine.Microservice.Business.Models.Responses;
using Reyma.VectorSpace.Engine.Microservice.Business.Motor;

namespace Reyma.VectorSpace.Engine.Microservice.Tests;

/// <summary>
/// Emisor JWT de prueba sobre el pipeline JwtBearer real de la API: solo cambia de donde salen el emisor y la clave
/// (la Authority corporativa no se consulta en tests). Audiencia, vigencia, firma y mapeo de claims quedan como en
/// produccion, asi que lo que estas pruebas observan de los claims es lo que vera el servicio con un token real.
/// </summary>
internal static class IdentidadDePrueba
{
    public const string Emisor = "https://emisor.prueba.invalid";
    public const string ClaimCliente = "https://schemas.reyma.com.mx/identity/claims/cli_id";

    public static readonly SymmetricSecurityKey Clave = new(RandomNumberGenerator.GetBytes(32));
    public static readonly SymmetricSecurityKey ClaveAjena = new(RandomNumberGenerator.GetBytes(32));

    public static readonly Guid SubAna = Guid.Parse("a0000000-0000-0000-0000-00000000000a");
    public static readonly Guid SubCarla = Guid.Parse("c0000000-0000-0000-0000-00000000000c");
    public static readonly Guid ClienteOrg = Guid.Parse("e0000000-0000-0000-0000-00000000000e");

    public static WebApplicationFactory<Program> ConIdentidad(
        this WebApplicationFactory<Program> factory,
        IDictionary<string, string?>? configuracion = null,
        Action<IServiceCollection>? servicios = null) =>
        factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(configuracion ?? new Dictionary<string, string?>()));
            builder.ConfigureTestServices(services =>
            {
                services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, opts =>
                {
                    opts.TokenValidationParameters.ValidIssuer = Emisor;
                    opts.TokenValidationParameters.IssuerSigningKey = Clave;
                });
                servicios?.Invoke(services);
            });
        });

    public static string Token(
        Guid? sub = null,
        string? usuario = null,
        IEnumerable<string>? roles = null,
        Guid? cliente = null,
        string audiencia = ServicioInfo.NombreApi,
        string emisor = Emisor,
        SecurityKey? clave = null,
        DateTime? expira = null,
        DateTime? emitido = null)
    {
        var claims = new Dictionary<string, object>();
        if (sub is not null)
            claims["sub"] = sub.Value.ToString();
        if (usuario is not null)
            claims["preferred_username"] = usuario;
        if (roles is not null)
            claims["role"] = roles.ToArray();
        if (cliente is not null)
            claims[ClaimCliente] = cliente.Value.ToString();

        var fin = expira ?? DateTime.UtcNow.AddMinutes(10);
        // Por defecto el token "nace" 20 min antes de expirar (comportamiento previo, sin cambios para quien no pase
        // 'emitido'). Un caller que necesite vigencia larga y valida desde ya (p. ej. una prueba de red real que
        // puede tardar mas de 20 min) pasa 'emitido' explicito en vez de depender de ese offset fijo.
        var inicio = emitido ?? fin.AddMinutes(-20);
        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = emisor,
            Audience = audiencia,
            Claims = claims,
            NotBefore = inicio,
            IssuedAt = inicio,
            Expires = fin,
            SigningCredentials = new SigningCredentials(clave ?? Clave, SecurityAlgorithms.HmacSha256)
        });
    }

    public static HttpClient Cliente(this WebApplicationFactory<Program> factory, string? token)
    {
        var client = factory.CreateClient();
        if (token is not null)
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    public static Task<HttpResponseMessage> PostJson(this HttpClient client, string path, object? body) =>
        client.PostAsync(path, new StringContent(JsonConvert.SerializeObject(body), Encoding.UTF8, "application/json"));

    public static async Task<T> Leer<T>(this HttpResponseMessage response) =>
        JsonConvert.DeserializeObject<T>(await response.Content.ReadAsStringAsync())!;
}

/// <summary>Motor de prueba: registra que coleccion le llego, para comprobar que una denegacion nunca lo alcanza.</summary>
public sealed class MotorDePrueba : IMotorDeEmbeddings, IMotorDeBusqueda, IMotorDeIngesta
{
    public ConcurrentQueue<(string Operacion, string Coleccion, object Datos)> Llamadas { get; } = new();

    public Task<EmbeddingsResponse> CalcularAsync(string coleccion, IReadOnlyList<string> textos, string? perfil, CancellationToken cancellationToken)
    {
        Llamadas.Enqueue(("embeddings", coleccion, textos));
        return Task.FromResult(new EmbeddingsResponse(coleccion, "modelo-prueba", 3, textos.Select(_ => new[] { 1f, 0f, 0f }).ToArray()));
    }

    public Task<BusquedaResponse> BuscarAsync(string coleccion, string consulta, int topK, string? perfil, CancellationToken cancellationToken)
    {
        Llamadas.Enqueue(("busqueda", coleccion, topK));
        return Task.FromResult(new BusquedaResponse(coleccion, [new ResultadoBusquedaResponse("id-1", 0.9f, "a.md", "texto")]));
    }

    public Task<IngestaResponse> IngerirAsync(string coleccion, IReadOnlyList<DocumentoRequest> documentos, string? perfil, bool resumen, CancellationToken cancellationToken)
    {
        Llamadas.Enqueue(("ingesta", coleccion, documentos.Count));
        return Task.FromResult(new IngestaResponse(Guid.NewGuid(), coleccion, "completada", documentos.Count, documentos.Count, 0));
    }

    public static Action<IServiceCollection> Registrar(MotorDePrueba motor) => services =>
    {
        services.AddSingleton<IMotorDeEmbeddings>(motor);
        services.AddSingleton<IMotorDeBusqueda>(motor);
        services.AddSingleton<IMotorDeIngesta>(motor);
    };
}
