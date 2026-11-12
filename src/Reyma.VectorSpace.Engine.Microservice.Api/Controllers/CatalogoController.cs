using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Reyma.Mvc.Base.Controllers;
using Reyma.VectorSpace.Engine.Microservice.Business.Models.Responses;
using Reyma.VectorSpace.Engine.Microservice.Business.Queries.Catalogo;

namespace Reyma.VectorSpace.Engine.Microservice.Api.Controllers;

/// <summary>
/// Que valores acepta esta instancia en los campos de ingesta y busqueda (<c>perfil</c>, <c>perfilLlm</c>,
/// <c>perfilIngesta</c>, URL Git). Sale de la configuracion y el codigo con que arranco el servicio, los mismos que
/// validan cada solicitud: no hay que recordar ni documentar a mano que existe. Requiere token valido.
/// </summary>
[Route("[controller]")]
public sealed class CatalogoController : BaseController
{
    /// <summary>Perfiles de embeddings: valores validos del campo <c>perfil</c>.</summary>
    /// <remarks>
    /// Un perfil de embeddings es el modelo y la dimension con que se vectoriza una coleccion; se fija al crearla. Cada
    /// perfil se sondea en vivo (sin gastar tokens): <c>disponible=false</c> significa que una ingesta con ese perfil
    /// fallaria ahora, y <c>detalle</c> dice por que.
    /// </remarks>
    /// <response code="200">Todos los perfiles declarados, el por defecto primero.</response>
    [HttpGet("perfiles-embeddings")]
    [Authorize]
    [ProducesResponseType(typeof(IReadOnlyList<PerfilEmbeddingsInfoResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<IReadOnlyList<PerfilEmbeddingsInfoResponse>>> GetPerfilesEmbeddings(CancellationToken cancellationToken) =>
        Ok(await Mediator.Send(new GetPerfilesEmbeddingsQuery(), cancellationToken));

    /// <summary>Perfiles de LLM: valores validos del campo <c>perfilLlm</c>.</summary>
    /// <remarks>
    /// El LLM escribe el resumen de negocio de cada fragmento en colecciones creadas con <c>resumen=true</c>. Cada
    /// perfil se sondea en vivo contra el servidor (lista de modelos habilitados para la credencial del servicio, sin
    /// gastar tokens). Si el resumen no esta habilitado en esta instancia, todos salen con <c>disponible=false</c>.
    /// </remarks>
    /// <response code="200">Todos los perfiles declarados, el por defecto primero.</response>
    [HttpGet("perfiles-llm")]
    [Authorize]
    [ProducesResponseType(typeof(IReadOnlyList<PerfilLlmInfoResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<IReadOnlyList<PerfilLlmInfoResponse>>> GetPerfilesLlm(CancellationToken cancellationToken) =>
        Ok(await Mediator.Send(new GetPerfilesLlmQuery(), cancellationToken));

    /// <summary>Perfiles de ingesta: valores validos del campo <c>perfilIngesta</c>.</summary>
    /// <remarks>
    /// Omitir <c>perfilIngesta</c> (y <c>repositorioLogico</c>) usa la ingesta nativa del servicio, que es lo habitual.
    /// Estos perfiles son alternativas opt-in para compatibilidad con otro indexador.
    /// </remarks>
    /// <response code="200">Perfiles opt-in admitidos.</response>
    [HttpGet("perfiles-ingesta")]
    [Authorize]
    [ProducesResponseType(typeof(IReadOnlyList<PerfilIngestaInfoResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<IReadOnlyList<PerfilIngestaInfoResponse>>> GetPerfilesIngesta(CancellationToken cancellationToken) =>
        Ok(await Mediator.Send(new GetPerfilesIngestaQuery(), cancellationToken));

    /// <summary>Origenes Git permitidos: de donde acepta clonar <c>POST colecciones/{coleccion}/ingestas/git</c>.</summary>
    /// <remarks>Esquema, host, puerto y prefijo de ruta deben coincidir exactamente; otra URL responde 400.</remarks>
    /// <response code="200">Origenes permitidos; vacia si esta instancia no acepta ingestas por Git.</response>
    [HttpGet("origenes-git")]
    [Authorize]
    [ProducesResponseType(typeof(IReadOnlyList<OrigenGitInfoResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<IReadOnlyList<OrigenGitInfoResponse>>> GetOrigenesGit(CancellationToken cancellationToken) =>
        Ok(await Mediator.Send(new GetOrigenesGitQuery(), cancellationToken));
}
