using System.Net;
using static Reyma.VectorSpace.Engine.Microservice.Tests.IdentidadDePrueba;

namespace Reyma.VectorSpace.Engine.Microservice.Tests;

/// <summary>
/// 1.7.7, criterio "no 500": pedir el opt-in de resumen en una instancia que no lo tiene configurado
/// (<c>Motor:Resumen:PerfilEmbeddings</c> vacio, el valor por defecto) responde 400 con un mensaje claro, sin tocar
/// Qdrant. No necesita <c>docker-compose.motor.yml</c>: el motor real esta registrado (no un <c>MotorDePrueba</c> fake),
/// pero el guardia que produce el 400 corre antes de cualquier llamada real al vector store (ver
/// <c>DocumentIngestionPipeline.IngestLockedAsync</c>), con Qdrant apuntando al puerto de descarte de <see cref="ApiFactory"/>.
/// </summary>
[Collection("Api")]
public sealed class ResumenTests(ApiFactory factory)
{
    private static readonly string TokenAna = Token(sub: SubAna, usuario: "ana");

    [Fact]
    public async Task ResumenNoConfiguradoEnEstaInstanciaResponde400SinTocarElVectorStore()
    {
        using var client = factory.ConIdentidad().Cliente(TokenAna);

        using var respuesta = await client.PostJson("/api/colecciones/propia/ingestas", new
        {
            origen = new
            {
                tipo = "documentos",
                documentos = new[] { new { ruta = "a.md", contenido = "Contenido de prueba suficientemente largo para superar el minimo indexable del pipeline." } }
            },
            resumen = true
        });

        Assert.Equal(HttpStatusCode.BadRequest, respuesta.StatusCode);
        var cuerpo = await respuesta.Content.ReadAsStringAsync();
        Assert.Contains("Motor:Resumen:PerfilEmbeddings", cuerpo);
    }

    [Fact]
    public async Task SinPedirResumenLaIngestaSigueIgualQueAntesDeEsteItem()
    {
        using var client = factory.ConIdentidad().Cliente(TokenAna);

        // Sin "resumen" en el cuerpo: el campo nuevo es opt-in puro, no cambia el comportamiento de 1.7.1-1.7.6.
        // Esta solicitud si llega a intentar tocar Qdrant (puerto de descarte): falla por el motor no disponible
        // (503, mismo comportamiento que ColeccionesTests.ConElRegistroRealYDependenciasCaidasResponde503...),
        // nunca por el opt-in de resumen (400) -- confirma que el campo ausente no dispara el guardia nuevo.
        using var respuesta = await client.PostJson("/api/colecciones/propia/ingestas", new
        {
            origen = new
            {
                tipo = "documentos",
                documentos = new[] { new { ruta = "a.md", contenido = "Contenido de prueba suficientemente largo para superar el minimo indexable del pipeline." } }
            }
        });

        Assert.Equal(HttpStatusCode.ServiceUnavailable, respuesta.StatusCode);
    }
}
