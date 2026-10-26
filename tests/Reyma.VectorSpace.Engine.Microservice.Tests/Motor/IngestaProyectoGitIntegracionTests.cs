using System.Diagnostics;
using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Newtonsoft.Json.Linq;
using Qdrant.Client;
using Qdrant.Client.Grpc;
using Reyma.VectorSpace.Engine.Microservice.Api;
using Reyma.VectorSpace.Engine.Microservice.Infrastructure.Git;
using static Reyma.VectorSpace.Engine.Microservice.Tests.IdentidadDePrueba;

namespace Reyma.VectorSpace.Engine.Microservice.Tests.Motor;

/// <summary>
/// Requiere <see cref="RequiereMotorLocalFactAttribute.Variable"/>=1 (docker-compose.motor.yml) y
/// <see cref="Variable"/>=1, ademas de que el credential helper de Git del host ya tenga cacheada una credencial
/// valida para dev.azure.com (verificacion puntual de 1.7.6, ver el ledger: no hay todavia PAT de cuenta de
/// servicio). Corre en el host real (no en un contenedor Linux: ahi no existe el credential manager de Windows).
/// </summary>
public sealed class RequiereGitRealFactAttribute : FactAttribute
{
    public const string Variable = "VSE_GIT_REAL";

    public RequiereGitRealFactAttribute()
    {
        if (Environment.GetEnvironmentVariable(RequiereMotorLocalFactAttribute.Variable) != "1")
            Skip = $"Requiere {RequiereMotorLocalFactAttribute.Variable}=1 y docker-compose.motor.yml levantado.";
        else if (Environment.GetEnvironmentVariable(Variable) != "1")
            Skip = $"Requiere {Variable}=1: clona de verdad contra Azure DevOps real (GR-PlasticosAdministrativo/" +
                   "BusinessSuite/Auditoria.Microservice.Api) con el credential helper ya cacheado del host.";
    }
}

/// <summary>
/// Item 1.7.6, verificacion literal de la ficha, contra el repo real de prueba fijado por el usuario
/// (GR-PlasticosAdministrativo/BusinessSuite/Auditoria.Microservice.Api, ya clonado de solo lectura en
/// D:\Git\Auditoria.Microservice.Api). No modifica ni empuja nada a ese repo: solo clona (lectura) a un checkout
/// temporal propio que se borra siempre.
/// </summary>
[Collection("Api")]
public sealed class IngestaProyectoGitIntegracionTests(ApiFactory factory)
{
    private const string UrlReal = "https://dev.azure.com/GR-PlasticosAdministrativo/BusinessSuite/_git/Auditoria.Microservice.Api";
    private const string PrefijoOrganizacion = "/GR-PlasticosAdministrativo/";

    // Vida larga y valida desde ya: el clonado+ingesta real de este archivo puede tardar mas de los 10 min por
    // defecto de IdentidadDePrueba.Token (medido: 15 min con el backend candle de TEI en CPU, mas lento que el ORT
    // optimizado). 'emitido' explicito porque el offset por defecto (fin-20min) dejaria el token invalido-todavia
    // si solo se alarga 'expira'.
    private static readonly string TokenAna = Token(sub: SubAna, usuario: "ana",
        emitido: DateTime.UtcNow.AddMinutes(-1), expira: DateTime.UtcNow.AddHours(2));
    private readonly QdrantClient _qdrant = new("127.0.0.1", 16334);

    private WebApplicationFactory<Program> Api(params string[] colecciones)
    {
        var config = new Dictionary<string, string?>
        {
            ["Motor:Qdrant:PuertoGrpc"] = "16334",
            ["Motor:Qdrant:PuertoRest"] = "16333",
            ["Motor:Embeddings:UrlBase"] = "http://127.0.0.1:18080",
            ["Motor:Git:OrigenesPermitidos:0:Esquema"] = "https",
            ["Motor:Git:OrigenesPermitidos:0:Host"] = "dev.azure.com",
            ["Motor:Git:OrigenesPermitidos:0:Puerto"] = "443",
            ["Motor:Git:OrigenesPermitidos:0:PrefijoRuta"] = PrefijoOrganizacion,
            ["Motor:Git:PlazoSegundos"] = "60",
        };
        return factory.ConIdentidad(config);
    }

    private static string NuevaColeccion() => "it-1-7-6-" + Guid.NewGuid().ToString("N")[..10];

    private sealed record Punto(string Ruta, string Hash, long Linea);

    private async Task<Dictionary<string, Punto>> Puntos(string coleccion)
    {
        var puntos = new Dictionary<string, Punto>();
        if (!await _qdrant.CollectionExistsAsync(coleccion)) return puntos;
        var r = await _qdrant.ScrollAsync(coleccion, limit: 5000, payloadSelector: new WithPayloadSelector
        {
            Include = new PayloadIncludeSelector { Fields = { "relative_path", "content_hash", "start_line" } }
        });
        foreach (var p in r.Result)
            puntos[p.Id.Uuid] = new Punto(p.Payload["relative_path"].StringValue, p.Payload["content_hash"].StringValue,
                p.Payload["start_line"].IntegerValue);
        return puntos;
    }

    private static IEnumerable<Punto> Contenido(Dictionary<string, Punto> puntos) =>
        puntos.Values.OrderBy(p => p.Ruta, StringComparer.Ordinal).ThenBy(p => p.Linea).ThenBy(p => p.Hash, StringComparer.Ordinal);

    private async Task Borrar(params string[] colecciones)
    {
        foreach (var c in colecciones)
            if (await _qdrant.CollectionExistsAsync(c))
                await _qdrant.DeleteCollectionAsync(c);
    }

    // Repo real (212 archivos candidatos antes de filtrar), TEI en CPU (~1-2 chunks/s medido en items previos):
    // margen generoso, no un numero inventado para que la prueba "pase rapido".
    private static async Task<JObject> EsperarEstado(HttpClient client, string coleccion, string ingestaId, int intentosMax = 2400)
    {
        for (var intento = 0; intento < intentosMax; intento++)
        {
            using var estado = await client.GetAsync($"/api/colecciones/{coleccion}/ingestas/{ingestaId}");
            Assert.Equal(HttpStatusCode.OK, estado.StatusCode);
            var cuerpo = JObject.Parse(await estado.Content.ReadAsStringAsync());
            var valor = (string)cuerpo["estado"]!;
            if (valor is "completada" or "error") return cuerpo;
            await Task.Delay(500);
        }
        throw new TimeoutException($"La ingesta '{ingestaId}' no termino a tiempo.");
    }

    [RequiereGitRealFact]
    public async Task ClonaLaUrlRealYTerminaConLosMismosPuntosQueElZipDelMismoCommit()
    {
        string porGit = NuevaColeccion(), porZip = NuevaColeccion();
        using var client = Api(porGit, porZip).Cliente(TokenAna);
        try
        {
            using var subida = await client.PostJson($"/api/colecciones/{porGit}/ingestas/git", new { url = UrlReal });
            Assert.True(subida.StatusCode == HttpStatusCode.Accepted, $"{(int)subida.StatusCode}: {await subida.Content.ReadAsStringAsync()}");
            var ingestaId = (string)JObject.Parse(await subida.Content.ReadAsStringAsync())["ingestaId"]!;

            var cuerpo = await EsperarEstado(client, porGit, ingestaId);
            Assert.Equal("completada", (string)cuerpo["estado"]!);
            var documentosIndexados = (int)cuerpo["documentosIndexados"]!;
            Assert.True(documentosIndexados > 0, "El clonado real no indexo ningun documento.");

            // Mismo commit que el clonado real que acaba de correr, exportado como .zip desde el clon local de
            // solo lectura ya existente (D:\Git\Auditoria.Microservice.Api): no se vuelve a tocar la red para esto,
            // y se fija el commit exacto en vez de asumir que nadie empujo nada entre los dos pasos de esta prueba.
            var head = EjecutarGit("rev-parse", "HEAD").Trim();
            var zip = Path.Combine(Path.GetTempPath(), $"vse-it-1-7-6-{Guid.NewGuid():N}.zip");
            try
            {
                EjecutarGit("archive", "--format=zip", "-o", zip, head);
                var zipBytes = await File.ReadAllBytesAsync(zip);

                using var multipart = new MultipartFormDataContent { { new ByteArrayContent(zipBytes), "zip", "repo.zip" } };
                using var subidaZip = await client.PostAsync($"/api/colecciones/{porZip}/ingestas/proyecto", multipart);
                Assert.True(subidaZip.StatusCode == HttpStatusCode.Accepted, $"{(int)subidaZip.StatusCode}: {await subidaZip.Content.ReadAsStringAsync()}");
                var ingestaIdZip = (string)JObject.Parse(await subidaZip.Content.ReadAsStringAsync())["ingestaId"]!;
                var cuerpoZip = await EsperarEstado(client, porZip, ingestaIdZip);
                Assert.Equal("completada", (string)cuerpoZip["estado"]!);

                var puntosGit = await Puntos(porGit);
                var puntosZip = await Puntos(porZip);
                Assert.Equal(Contenido(puntosZip), Contenido(puntosGit));
            }
            finally
            {
                File.Delete(zip);
            }

            // Segunda pasada del mismo clonado: todo "sin cambios", mismos puntos.
            using var segundaSubida = await client.PostJson($"/api/colecciones/{porGit}/ingestas/git", new { url = UrlReal });
            Assert.Equal(HttpStatusCode.Accepted, segundaSubida.StatusCode);
            var segundaId = (string)JObject.Parse(await segundaSubida.Content.ReadAsStringAsync())["ingestaId"]!;
            var segundoCuerpo = await EsperarEstado(client, porGit, segundaId);
            Assert.Equal("completada", (string)segundoCuerpo["estado"]!);
            Assert.Equal(0, (int)segundoCuerpo["documentosIndexados"]!);
        }
        finally
        {
            await Borrar(porGit, porZip);
        }
    }

    [RequiereGitRealFact]
    public async Task UnaUrlFueraDeLaListaBlancaSeRechazaSinConexionAlDestino()
    {
        // Rechazo por lista blanca (mecanismo distinto al control de acceso, ya retirado en 1.14), confirmado aqui
        // con la lista blanca REAL de este host (solo GR-PlasticosAdministrativo); el caso sin red ya esta probado
        // en ListaBlancaGitTests e IngestaProyectoGitTests.
        using var cliente = Api().Cliente(TokenAna);
        using var response = await cliente.PostJson("/api/colecciones/propia-git-rechazo/ingestas/git",
            new { url = "https://dev.azure.com/otra-organizacion-no-permitida/Proyecto/_git/repo" });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [RequiereGitRealFact]
    public async Task ClonadoConCuotaDiminutaFallaSinLlenarElDiscoDelHost()
    {
        var checkout = Path.Combine(Path.GetTempPath(), "vse-git-it-1-7-6-" + Guid.NewGuid().ToString("N"));
        try
        {
            var resultado = await ClonadorGit.ClonarAsync(new Uri(UrlReal), checkout,
                new OpcionesClonado { MaxBytes = 65_536, Plazo = TimeSpan.FromSeconds(60) });

            Assert.False(resultado.Exito);
            Assert.Contains("maximo", resultado.Motivo);
            // El tope es un limite de proceso, no de filesystem (documentado en contrato-api.md): puede haber
            // sobrepaso, pero acotado -- no equivale a "se lleno el disco del host".
            Assert.True(resultado.Bytes < 50_000_000, $"El clonado escribio {resultado.Bytes} bytes pese al tope de 65536.");
        }
        finally
        {
            ClonadorGit.Borrar(checkout);
        }
    }

    [RequiereGitRealFact]
    public void LaIdentidadNoApareceEnArgumentosNiEnGitConfigDelCheckout()
    {
        var checkout = Path.Combine(Path.GetTempPath(), "vse-git-it-1-7-6-id-" + Guid.NewGuid().ToString("N"));
        try
        {
            // Sin CabeceraAutorizacion: el clonado real de esta verificacion puntual deja la autenticacion al
            // credential helper del host (Windows Git Credential Manager ya cacheado para dev.azure.com).
            var resultado = ClonadorGit.ClonarAsync(new Uri(UrlReal), checkout,
                new OpcionesClonado { MaxBytes = 200L * 1024 * 1024, Plazo = TimeSpan.FromSeconds(60) }).GetAwaiter().GetResult();
            Assert.True(resultado.Exito, resultado.Motivo + " " + resultado.Stderr);

            var configPath = Path.Combine(checkout, ".git", "config");
            Assert.True(File.Exists(configPath));
            var config = File.ReadAllText(configPath);
            Assert.DoesNotContain("Authorization", config, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Basic ", config, StringComparison.Ordinal);
            Assert.DoesNotContain("extraheader", config, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("password", config, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            ClonadorGit.Borrar(checkout);
        }
    }

    private static string EjecutarGit(params string[] args)
    {
        var psi = new ProcessStartInfo("git") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, WorkingDirectory = @"D:\Git\Auditoria.Microservice.Api" };
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi)!;
        var salida = p.StandardOutput.ReadToEnd();
        p.WaitForExit();
        if (p.ExitCode != 0) throw new InvalidOperationException($"git {string.Join(' ', args)} fallo: {p.StandardError.ReadToEnd()}");
        return salida;
    }
}
