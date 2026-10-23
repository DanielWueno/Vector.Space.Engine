using System.Buffers.Binary;
using System.IO.Compression;
using System.Net;
using System.Text;
using Microsoft.AspNetCore.Mvc.Testing;
using Newtonsoft.Json.Linq;
using Qdrant.Client;
using Qdrant.Client.Grpc;
using Reyma.VectorSpace.Engine.Microservice.Api;
using static Reyma.VectorSpace.Engine.Microservice.Tests.IdentidadDePrueba;

namespace Reyma.VectorSpace.Engine.Microservice.Tests.Motor;

/// <summary>
/// Item 1.7.5, criterio literal: un .zip valido responde 202 y termina con los mismos puntos que el equivalente por
/// 'documentos'; los zips maliciosos de tools/ingesta-proyecto se rechazan sin escribir en la coleccion; un archivo
/// borrado del proyecto pierde sus puntos en la siguiente ingesta. Contra Qdrant y TEI reales de docker-compose.motor.yml
/// (mismos puertos que MotorIntegracionTests). Las pruebas rapidas de validacion/autorizacion (sin motor) estan en
/// IngestaProyectoZipTests.cs.
/// </summary>
[Collection("Api")]
public sealed class IngestaProyectoZipIntegracionTests(ApiFactory factory)
{
    private const string Clase = """
        namespace Demo.Pedidos;

        public class ServicioPedidos
        {
            public decimal CalcularTotal(IEnumerable<decimal> lineas)
            {
                var total = 0m;
                foreach (var linea in lineas) total += linea;
                return total;
            }
        }
        """;

    private const string Guia = """
        # Guia de pedidos

        ## Cancelacion

        Un pedido se puede cancelar durante las primeras veinticuatro horas desde su creacion, sin costo para el cliente.
        """;

    private static readonly string TokenAna = Token(sub: SubAna, usuario: "ana");
    private readonly QdrantClient _qdrant = new("127.0.0.1", 16334);

    private WebApplicationFactory<Program> Api(params string[] colecciones)
    {
        var config = new Dictionary<string, string?>
        {
            ["Motor:Qdrant:PuertoGrpc"] = "16334",
            ["Motor:Qdrant:PuertoRest"] = "16333",
            ["Motor:Embeddings:UrlBase"] = "http://127.0.0.1:18080",
        };
        for (var i = 0; i < colecciones.Length; i++)
        {
            config[$"ControlDeAcceso:Concesiones:{i}:Coleccion"] = colecciones[i];
            config[$"ControlDeAcceso:Concesiones:{i}:Operaciones:0"] = "Consultar";
            config[$"ControlDeAcceso:Concesiones:{i}:Operaciones:1"] = "Ingestar";
            config[$"ControlDeAcceso:Concesiones:{i}:Usuarios:0"] = "ana";
        }
        return factory.ConIdentidad(config);
    }

    private static string NuevaColeccion() => "it-1-7-5-" + Guid.NewGuid().ToString("N")[..10];

    private sealed record Punto(string Ruta, string Hash, long Linea);

    private async Task<Dictionary<string, Punto>> Puntos(string coleccion)
    {
        var puntos = new Dictionary<string, Punto>();
        if (!await _qdrant.CollectionExistsAsync(coleccion)) return puntos;
        var r = await _qdrant.ScrollAsync(coleccion, limit: 1000, payloadSelector: new WithPayloadSelector
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

    private static byte[] Zip(params (string Ruta, string Contenido)[] documentos)
    {
        using var memoria = new MemoryStream();
        using (var archivo = new ZipArchive(memoria, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (ruta, contenido) in documentos)
            {
                var entrada = archivo.CreateEntry(ruta);
                using var escritor = new StreamWriter(entrada.Open());
                escritor.Write(contenido);
            }
        }
        return memoria.ToArray();
    }

    private static MultipartFormDataContent Multipart(byte[] zip) =>
        new() { { new ByteArrayContent(zip), "zip", "proyecto.zip" } };

    private static async Task<JObject> SubirZipYEsperarCompletada(HttpClient client, string coleccion, byte[] zip)
    {
        using var subida = await client.PostAsync($"/api/colecciones/{coleccion}/ingestas/proyecto", Multipart(zip));
        Assert.True(subida.StatusCode == HttpStatusCode.Accepted, $"{(int)subida.StatusCode}: {await subida.Content.ReadAsStringAsync()}");
        var ingestaId = (string)(JObject.Parse(await subida.Content.ReadAsStringAsync()))["ingestaId"]!;

        for (var intento = 0; intento < 100; intento++)
        {
            using var estado = await client.GetAsync($"/api/colecciones/{coleccion}/ingestas/{ingestaId}");
            Assert.Equal(HttpStatusCode.OK, estado.StatusCode);
            var cuerpo = JObject.Parse(await estado.Content.ReadAsStringAsync());
            var valor = (string)cuerpo["estado"]!;
            if (valor == "completada") return cuerpo;
            Assert.NotEqual("error", valor);
            await Task.Delay(300);
        }
        throw new TimeoutException($"La ingesta '{ingestaId}' no termino a tiempo.");
    }

    [RequiereMotorLocalFact]
    public async Task UnZipValidoTerminaConLosMismosPuntosQueElOrigenDocumentos()
    {
        string porZip = NuevaColeccion(), porDocumentos = NuevaColeccion();
        using var client = Api(porZip, porDocumentos).Cliente(TokenAna);
        try
        {
            var cuerpo = await SubirZipYEsperarCompletada(client, porZip,
                Zip(("src/ServicioPedidos.cs", Clase), ("docs/guia.md", Guia)));
            Assert.Equal(2, (int)cuerpo["documentosIndexados"]!);
            Assert.Equal(0, (int)cuerpo["documentosSinCambios"]!);

            using (var viaDocumentos = await client.PostJson($"/api/colecciones/{porDocumentos}/ingestas", new
            {
                origen = new
                {
                    tipo = "documentos",
                    documentos = new[] { new { ruta = "src/ServicioPedidos.cs", contenido = Clase }, new { ruta = "docs/guia.md", contenido = Guia } }
                }
            }))
                Assert.Equal(HttpStatusCode.OK, viaDocumentos.StatusCode);

            Assert.Equal(Contenido(await Puntos(porDocumentos)), Contenido(await Puntos(porZip)));

            // Segunda pasada del mismo zip: nada cambia.
            var segunda = await SubirZipYEsperarCompletada(client, porZip, Zip(("src/ServicioPedidos.cs", Clase), ("docs/guia.md", Guia)));
            Assert.Equal(0, (int)segunda["documentosIndexados"]!);
            Assert.Equal(2, (int)segunda["documentosSinCambios"]!);
        }
        finally
        {
            await Borrar(porZip, porDocumentos);
        }
    }

    [RequiereMotorLocalFact]
    public async Task UnArchivoBorradoDelProyectoPierdeSusPuntosEnLaSiguienteIngesta()
    {
        var coleccion = NuevaColeccion();
        using var client = Api(coleccion).Cliente(TokenAna);
        try
        {
            await SubirZipYEsperarCompletada(client, coleccion, Zip(("src/ServicioPedidos.cs", Clase), ("docs/guia.md", Guia)));
            var inicial = await Puntos(coleccion);
            Assert.Contains(inicial.Values, p => p.Ruta == "src/ServicioPedidos.cs");
            Assert.Contains(inicial.Values, p => p.Ruta == "docs/guia.md");

            // El siguiente .zip del "proyecto" ya no trae ServicioPedidos.cs: el barrido debe quitar sus puntos.
            var cuerpo = await SubirZipYEsperarCompletada(client, coleccion, Zip(("docs/guia.md", Guia)));
            Assert.Equal(1, (int)cuerpo["documentosEliminados"]!);

            var final = await Puntos(coleccion);
            Assert.All(final.Values, p => Assert.Equal("docs/guia.md", p.Ruta));
            Assert.DoesNotContain(final.Values, p => p.Ruta == "src/ServicioPedidos.cs");
        }
        finally
        {
            await Borrar(coleccion);
        }
    }

    // --- Zips maliciosos (tools/ingesta-proyecto/Prototipo/ZipsDePrueba.cs): ninguno debe crear la coleccion. ---

    private static byte[] ZipSlip()
    {
        using var memoria = new MemoryStream();
        using (var z = new ZipArchive(memoria, ZipArchiveMode.Create, leaveOpen: true))
        {
            Escribir(z, "../../escape-slip-1.md", "si esto se indexa, la ruta escapo de la raiz (1.7.5)");
            Escribir(z, "docs/../../../escape-slip-2.md", "idem");
            Escribir(z, "..\\..\\escape-slip-3.md", "idem");
            Escribir(z, "/tmp/escape-slip-4.md", "idem");
            Escribir(z, "C:/escape-slip-5.md", "idem");
        }
        return memoria.ToArray();
    }

    private static byte[] ZipConRatioAbsurdo()
    {
        // Una entrada .md de 500 000 ceros (bajo el tope por archivo de 512 KiB del perfil), que comprime a muy pocos
        // bytes: ratio muy por encima de 100, rechazada sin que importe que quepa en el tope por archivo.
        using var memoria = new MemoryStream();
        using (var z = new ZipArchive(memoria, ZipArchiveMode.Create, leaveOpen: true))
        {
            var e = z.CreateEntry("docs/ceros.md", CompressionLevel.SmallestSize);
            using var s = e.Open();
            s.Write(new byte[500_000]);
        }
        return memoria.ToArray();
    }

    private static byte[] ZipConDemasiadasEntradas()
    {
        using var memoria = new MemoryStream();
        using (var z = new ZipArchive(memoria, ZipArchiveMode.Create, leaveOpen: true))
            for (var i = 0; i < 20_001; i++)
                z.CreateEntry($"d{i % 100}/f{i}.txt", CompressionLevel.NoCompression);
        return memoria.ToArray();
    }

    private static byte[] ZipConCabeceraMentirosa()
    {
        // Cabeceras (local y central) que declaran 100 bytes para una entrada que en realidad descomprime a ~2 MiB.
        using var memoria = new MemoryStream();
        using (var z = new ZipArchive(memoria, ZipArchiveMode.Create, leaveOpen: true))
        {
            var e = z.CreateEntry("src/miente.cs", CompressionLevel.SmallestSize);
            using var s = e.Open();
            var linea = Encoding.ASCII.GetBytes("// linea repetida para que comprima mucho y descomprima a mas de lo declarado\n");
            for (long escrito = 0; escrito < 2L * 1024 * 1024; escrito += linea.Length) s.Write(linea);
        }
        var datos = memoria.ToArray();
        for (var i = 0; i + 4 <= datos.Length; i++)
        {
            var firma = BinaryPrimitives.ReadUInt32LittleEndian(datos.AsSpan(i));
            if (firma == 0x04034b50) BinaryPrimitives.WriteUInt32LittleEndian(datos.AsSpan(i + 22), 100); // local
            else if (firma == 0x02014b50) BinaryPrimitives.WriteUInt32LittleEndian(datos.AsSpan(i + 24), 100); // central
        }
        return datos;
    }

    /// <summary>
    /// 1.7.3, seccion (2): la cabecera mentirosa no logra amplificar nada porque .NET corta el flujo de
    /// <c>ZipArchiveEntry.Open()</c> en el tamano declarado (100 bytes), no en el real (~2 MiB); por eso no esta en
    /// <see cref="ZipsMaliciosos"/> (no se rechaza con 400: se admite un documento minusculo e inofensivo).
    /// </summary>
    [RequiereMotorLocalFact]
    public async Task CabeceraMentirosaNoAmplificaDatosMasAllaDeLoQueDnetLee()
    {
        var coleccion = NuevaColeccion();
        using var client = Api(coleccion).Cliente(TokenAna);
        try
        {
            var cuerpo = await SubirZipYEsperarCompletada(client, coleccion, ZipConCabeceraMentirosa());
            Assert.Equal(1, (int)cuerpo["documentosRecibidos"]!);

            var puntos = await Puntos(coleccion);
            // El documento completo (truncado a 100 bytes reales por .NET) no llega al minimo indexable de 60
            // caracteres tras el enriquecido, asi que puede quedar sin chunks; lo que importa es que no hay 2 MiB
            // de contenido en la coleccion por esta via.
            Assert.True(puntos.Count <= 1);
        }
        finally
        {
            await Borrar(coleccion);
        }
    }

    private static byte[] ZipConEnlaceSimbolico()
    {
        using var memoria = new MemoryStream();
        using (var z = new ZipArchive(memoria, ZipArchiveMode.Create, leaveOpen: true))
        {
            var e = z.CreateEntry("docs/enlace.md");
            e.ExternalAttributes = unchecked((int)(0xA1FFu << 16)); // lrwxrwxrwx (S_IFLNK)
            using var s = e.Open();
            s.Write(Encoding.UTF8.GetBytes("../../../../../../etc/hostname"));
        }
        return memoria.ToArray();
    }

    private static byte[] ZipDemasiadoGrande()
    {
        // Un .zip real no hace falta: LectorZip rechaza por Stream.Length antes de intentar abrirlo como ZipArchive.
        return new byte[100L * 1024 * 1024 + 1];
    }

    private static void Escribir(ZipArchive z, string nombre, string texto)
    {
        var e = z.CreateEntry(nombre);
        using var s = e.Open();
        s.Write(Encoding.UTF8.GetBytes(texto));
    }

    // "cabecera-mentirosa" no esta aqui: no se rechaza con 400, se neutraliza de otra forma (ver
    // CabeceraMentirosaNoAmplificaDatosMasAllaDeLoQueDnetLee).
    public static TheoryData<string, Func<byte[]>> ZipsMaliciosos() => new()
    {
        { "slip", ZipSlip },
        { "ratio-absurdo", ZipConRatioAbsurdo },
        { "demasiadas-entradas", ZipConDemasiadasEntradas },
        { "enlace-simbolico", ZipConEnlaceSimbolico },
        { "demasiado-grande", ZipDemasiadoGrande },
    };

    [RequiereMotorLocalTheory]
    [MemberData(nameof(ZipsMaliciosos))]
    public async Task ZipMaliciosoSeRechazaSinCrearLaColeccion(string _, Func<byte[]> generador)
    {
        var coleccion = NuevaColeccion();
        using var client = Api(coleccion).Cliente(TokenAna);
        try
        {
            using var response = await client.PostAsync($"/api/colecciones/{coleccion}/ingestas/proyecto", Multipart(generador()));
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.False(await _qdrant.CollectionExistsAsync(coleccion));
        }
        finally
        {
            await Borrar(coleccion);
        }
    }
}
