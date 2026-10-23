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
/// Contrato de embeddings/ingesta. Todas las acciones exigen token valido; el acceso por coleccion lo decide la
/// tabla ControlDeAcceso en Business (403 si no la otorga). Ver docs/servicio/contrato-api.md.
/// </summary>
[Route("[controller]")]
public sealed class ColeccionesController : BaseController
{
    /// <summary>Colecciones que alcanza el usuario en sesion.</summary>
    [HttpGet]
    [Authorize]
    [ProducesResponseType(typeof(ColeccionesAccesiblesResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<ColeccionesAccesiblesResponse>> Get(CancellationToken cancellationToken) =>
        Ok(await Mediator.Send(new GetColeccionesAccesiblesQuery(), cancellationToken));

    /// <summary>Embeddings densos de textos sueltos con el perfil de la coleccion; no escribe en ella.</summary>
    [HttpPost("{coleccion}/embeddings")]
    [Authorize]
    [ProducesResponseType(typeof(EmbeddingsResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status501NotImplemented)]
    public async Task<ActionResult<EmbeddingsResponse>> PostEmbeddings(
        string coleccion, [FromBody] CalcularEmbeddingsRequest? request, CancellationToken cancellationToken) =>
        Ok(await Mediator.Send(new CalcularEmbeddingsCommand(coleccion, request?.Textos), cancellationToken));

    /// <summary>Busqueda semantica en la coleccion.</summary>
    [HttpPost("{coleccion}/busquedas")]
    [Authorize]
    [ProducesResponseType(typeof(BusquedaResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status501NotImplemented)]
    public async Task<ActionResult<BusquedaResponse>> PostBusqueda(
        string coleccion, [FromBody] BuscarRequest? request, CancellationToken cancellationToken) =>
        Ok(await Mediator.Send(new BuscarEnColeccionQuery(coleccion, request?.Consulta, request?.TopK), cancellationToken));

    /// <summary>Ingesta de documentos en la coleccion (origen 'documentos'; 'ruta' reservado, 501).</summary>
    [HttpPost("{coleccion}/ingestas")]
    [Authorize]
    // 1.7.1: el default de Kestrel (30 000 000 bytes) queda por debajo de lo que el contrato admite
    // (100 documentos de hasta 1 MiB). Limites.MaxBytesPorIngesta cubre ese maximo con margen para JSON.
    [RequestSizeLimit(Limites.MaxBytesPorIngesta)]
    [ProducesResponseType(typeof(IngestaResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status413PayloadTooLarge)]
    [ProducesResponseType(StatusCodes.Status501NotImplemented)]
    public async Task<ActionResult<IngestaResponse>> PostIngesta(
        string coleccion, [FromBody] IngestaRequest? request, CancellationToken cancellationToken) =>
        Ok(await Mediator.Send(new IngerirCommand(coleccion, request?.Origen), cancellationToken));
}
