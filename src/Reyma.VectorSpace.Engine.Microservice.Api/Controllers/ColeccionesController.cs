using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Reyma.Mvc.Base.Controllers;
using Reyma.VectorSpace.Engine.Microservice.Business.Commands.Colecciones;
using Reyma.VectorSpace.Engine.Microservice.Business.Models.Requests;
using Reyma.VectorSpace.Engine.Microservice.Business.Models.Responses;
using Reyma.VectorSpace.Engine.Microservice.Business.Queries.Colecciones;

namespace Reyma.VectorSpace.Engine.Microservice.Api.Controllers;

/// <summary>
/// Contrato de embeddings/ingesta. Todas las acciones exigen token valido (item 1.14 retiro la autorizacion por
/// coleccion: ya no hay 403 por tenencia). Ver docs/servicio/contrato-api.md.
/// </summary>
[Route("[controller]")]
public sealed class ColeccionesController : BaseController
{
    /// <summary>
    /// Lista las colecciones existentes en el vector store, con su perfil de embeddings si lo tiene guardado (item
    /// 1.16): resuelve el caso de no saber que colecciones hay sin acceso directo a Qdrant. Nunca 404: sin
    /// colecciones responde 200 con una lista vacia.
    /// </summary>
    [HttpGet]
    [Authorize]
    [ProducesResponseType(typeof(IReadOnlyList<ColeccionInfoResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<IReadOnlyList<ColeccionInfoResponse>>> GetColecciones(CancellationToken cancellationToken) =>
        Ok(await Mediator.Send(new GetColeccionesQuery(), cancellationToken));

    /// <summary>
    /// Lista las ingestas de proyecto registradas, en cualquier estado (item 1.15): util cuando se perdio el
    /// <c>ingestaId</c> que devolvieron <see cref="PostIngestaProyectoZip"/>/<see cref="PostIngestaProyectoGit"/> (por
    /// ejemplo, otra persona del equipo ingesto a la misma coleccion). Sin <paramref name="coleccion"/>, trae las de
    /// cualquier coleccion (item 1.16); con ella, solo las de esa. Nunca 404: sin resultados responde 200 con una
    /// lista vacia.
    /// </summary>
    [HttpGet("ingestas")]
    [Authorize]
    [ProducesResponseType(typeof(IReadOnlyList<IngestaResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<IReadOnlyList<IngestaResponse>>> GetIngestas(
        [FromQuery] string? coleccion, CancellationToken cancellationToken) =>
        Ok(await Mediator.Send(new GetIngestasQuery(coleccion), cancellationToken));

    /// <summary>Ingesta de documentos en la coleccion (origen 'documentos'; 'ruta' reservado, 501).</summary>
    [HttpPost("{coleccion}/ingestas")]
    [Authorize]
    // 1.7.1: el default de Kestrel (30 000 000 bytes) queda por debajo de lo que el contrato admite
    // (100 documentos de hasta 1 MiB). Limites.MaxBytesPorIngesta cubre ese maximo con margen para JSON.
    [RequestSizeLimit(Limites.MaxBytesPorIngesta)]
    [ProducesResponseType(typeof(IngestaResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status413PayloadTooLarge)]
    [ProducesResponseType(StatusCodes.Status501NotImplemented)]
    public async Task<ActionResult<IngestaResponse>> PostIngesta(
        string coleccion, [FromBody] IngestaRequest? request, CancellationToken cancellationToken) =>
        Ok(await Mediator.Send(new IngerirCommand(coleccion, request?.Origen, request?.Perfil, request?.Resumen), cancellationToken));

    /// <summary>
    /// Ingesta de un proyecto completo subido como .zip (origen 'proyecto', item 1.7.5); asincrona: responde 202 con
    /// el mismo identificador que se consulta en <see cref="GetEstadoIngesta"/>. <paramref name="carpetaRaiz"/> quita
    /// el envoltorio "repo-rama/" de un "descargar como zip"; <paramref name="perfil"/> es el perfil de embeddings
    /// (1.7.4); <paramref name="resumen"/> es el opt-in del resumen de negocio (1.7.7, ver <see cref="IngestaRequest.Resumen"/>).
    /// </summary>
    [HttpPost("{coleccion}/ingestas/proyecto")]
    [Authorize]
    // 1.7.5: el .zip admite hasta 100 MiB (LimitesZip.MaxBytesZip); el margen de Limites.MaxBytesZipProyecto cubre
    // el resto del multipart.
    [RequestSizeLimit(Limites.MaxBytesZipProyecto)]
    [ProducesResponseType(typeof(IngestaResponse), StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status413PayloadTooLarge)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<IngestaResponse>> PostIngestaProyectoZip(
        string coleccion, IFormFile? zip, [FromForm] string? carpetaRaiz, [FromForm] string? perfil, [FromForm] bool? resumen,
        CancellationToken cancellationToken)
    {
        byte[]? contenido = null;
        if (zip is not null)
        {
            using var memoria = new MemoryStream();
            await zip.CopyToAsync(memoria, cancellationToken);
            contenido = memoria.ToArray();
        }

        var respuesta = await Mediator.Send(new IngerirProyectoZipCommand(coleccion, contenido, carpetaRaiz, perfil, resumen), cancellationToken);
        return Accepted($"/api/colecciones/{coleccion}/ingestas/{respuesta.IngestaId}", respuesta);
    }

    /// <summary>
    /// Ingesta de un proyecto completo desde una URL de repositorio Git (origen 'proyecto', item 1.7.6); asincrona,
    /// mismo contrato que el .zip (202 + el mismo <see cref="GetEstadoIngesta"/>). <paramref name="request"/>.Url debe
    /// estar en la lista blanca de origenes Git permitidos (<c>Motor:Git:OrigenesPermitidos</c>); una URL fuera de la
    /// lista responde 400 sin conexion al destino.
    /// </summary>
    [HttpPost("{coleccion}/ingestas/git")]
    [Authorize]
    [ProducesResponseType(typeof(IngestaResponse), StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<IngestaResponse>> PostIngestaProyectoGit(
        string coleccion, [FromBody] IngerirProyectoGitRequest? request, CancellationToken cancellationToken)
    {
        var respuesta = await Mediator.Send(
            new IngerirProyectoGitCommand(coleccion, request?.Url, request?.CarpetaRaiz, request?.Perfil, request?.Resumen), cancellationToken);
        return Accepted($"/api/colecciones/{coleccion}/ingestas/{respuesta.IngestaId}", respuesta);
    }

    /// <summary>Estado de una ingesta de proyecto (item 1.7.5); 404 si no existe o no es de esta coleccion.</summary>
    [HttpGet("{coleccion}/ingestas/{ingestaId:guid}")]
    [Authorize]
    [ProducesResponseType(typeof(IngestaResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IngestaResponse>> GetEstadoIngesta(
        string coleccion, Guid ingestaId, CancellationToken cancellationToken) =>
        Ok(await Mediator.Send(new GetEstadoIngestaQuery(coleccion, ingestaId), cancellationToken));

    /// <summary>Embeddings densos de textos sueltos con el perfil de la coleccion; no escribe en ella.</summary>
    [HttpPost("{coleccion}/embeddings")]
    [Authorize]
    [ProducesResponseType(typeof(EmbeddingsResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status501NotImplemented)]
    public async Task<ActionResult<EmbeddingsResponse>> PostEmbeddings(
        string coleccion, [FromBody] CalcularEmbeddingsRequest? request, CancellationToken cancellationToken) =>
        Ok(await Mediator.Send(new CalcularEmbeddingsCommand(coleccion, request?.Textos, request?.Perfil), cancellationToken));

    /// <summary>Busqueda semantica en la coleccion.</summary>
    [HttpPost("{coleccion}/busquedas")]
    [Authorize]
    [ProducesResponseType(typeof(BusquedaResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status501NotImplemented)]
    public async Task<ActionResult<BusquedaResponse>> PostBusqueda(
        string coleccion, [FromBody] BuscarRequest? request, CancellationToken cancellationToken) =>
        Ok(await Mediator.Send(new BuscarEnColeccionQuery(coleccion, request?.Consulta, request?.TopK, request?.Perfil), cancellationToken));
}
