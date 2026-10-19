using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Reyma.Mvc.Base.Controllers;
using Reyma.VectorSpace.Engine.Microservice.Business.Models.Responses;
using Reyma.VectorSpace.Engine.Microservice.Business.Queries.Sesion;

namespace Reyma.VectorSpace.Engine.Microservice.Api.Controllers;

/// <summary>
/// Datos del login del usuario en sesion, desde los claims (heredado del ejemplo 'usuario' de la plantilla).
/// Sirve para comprobar la identidad y para saber que valores escribir en ControlDeAcceso.
/// </summary>
[Route("[controller]")]
public sealed class SesionController : BaseController
{
    [HttpGet]
    [Authorize]
    public async Task<ActionResult<UsuarioActualResponse>> Get(CancellationToken cancellationToken) =>
        Ok(await Mediator.Send(new GetUsuarioActualQuery(), cancellationToken));
}
