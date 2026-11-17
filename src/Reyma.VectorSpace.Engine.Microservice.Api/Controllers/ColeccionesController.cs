using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Reyma.Mvc.Base.Controllers;
using Reyma.VectorSpace.Engine.Microservice.Api.Models;
using Reyma.VectorSpace.Engine.Microservice.Business.Commands.Colecciones;
using Reyma.VectorSpace.Engine.Microservice.Business.Models.Requests;
using Reyma.VectorSpace.Engine.Microservice.Business.Models.Responses;
using Reyma.VectorSpace.Engine.Microservice.Business.Queries.Colecciones;

namespace Reyma.VectorSpace.Engine.Microservice.Api.Controllers;

/// <summary>
/// Colecciones del vector store: indexar proyectos o documentos y buscar en ellos. Una coleccion es un indice con
/// nombre (el de la ruta, p. ej. <c>ventas-api</c>) que se crea en su primera ingesta. Los valores validos de
/// <c>perfil</c>, <c>perfilLlm</c>, <c>perfilIngesta</c> y de las URL Git estan en GET api/catalogo/*. Todas las
/// acciones exigen token valido. Contrato completo: docs/servicio/contrato-api.md.
/// </summary>
// Item 1.14 retiro la autorizacion por coleccion: ya no hay 403 por tenencia.
[Route("[controller]")]
public sealed class ColeccionesController : BaseController
{
    /// <summary>Lista las colecciones que existen, con el perfil con que se creo cada una.</summary>
    /// <remarks>
    /// Sirve para saber que colecciones hay sin acceso directo a Qdrant, y con que <c>perfil</c> buscar en cada una.
    /// Los campos de perfil son null en colecciones creadas antes de que se guardara esa informacion.
    /// </remarks>
    /// <response code="200">Lista de colecciones; vacia si no hay ninguna (nunca 404).</response>
    /// <response code="503">El vector store no responde.</response>
    [HttpGet]
    [Authorize]
    [ProducesResponseType(typeof(IReadOnlyList<ColeccionInfoResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<IReadOnlyList<ColeccionInfoResponse>>> GetColecciones(CancellationToken cancellationToken) =>
        Ok(await Mediator.Send(new GetColeccionesQuery(), cancellationToken));

    /// <summary>Lista las ingestas de proyecto (.zip o Git) registradas, en cualquier estado.</summary>
    /// <remarks>
    /// Sirve para recuperar un <c>ingestaId</c> perdido (p. ej. lo lanzo otra persona). Las ingestas viven en memoria
    /// de esta instancia: tras un reinicio no aparecen, lo que no significa que no hayan terminado.
    /// </remarks>
    /// <param name="coleccion">Opcional: solo las ingestas de esta coleccion. Ausente = de todas.</param>
    /// <response code="200">Lista de ingestas; vacia si no hay (nunca 404). Sin orden garantizado.</response>
    [HttpGet("ingestas")]
    [Authorize]
    [ProducesResponseType(typeof(IReadOnlyList<IngestaResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<IReadOnlyList<IngestaResponse>>> GetIngestas(
        [FromQuery] string? coleccion, CancellationToken cancellationToken) =>
        Ok(await Mediator.Send(new GetIngestasQuery(coleccion), cancellationToken));

    /// <summary>Indexa documentos enviados en el cuerpo (hasta 100 por solicitud), de forma sincrona.</summary>
    /// <remarks>
    /// Para un repositorio completo conviene <c>ingestas/proyecto</c> (.zip) o <c>ingestas/git</c>. Reenviar un
    /// documento sin cambios no lo revectoriza; enviarlo con contenido vacio lo borra. Puede esperar en cola: usar un
    /// timeout de cliente de al menos 10 minutos y reintentar el 429 tras <c>Retry-After</c>.
    /// </remarks>
    /// <param name="coleccion">Coleccion destino; se crea en la primera ingesta.</param>
    /// <param name="request">Documentos y opciones de la ingesta.</param>
    /// <response code="200">Ingesta completada, con sus conteos.</response>
    /// <response code="400">Cuerpo invalido o perfil no registrado; el mensaje dice cual.</response>
    /// <response code="409">El perfil, el resumen o el perfil de ingesta chocan con los de la coleccion existente.</response>
    /// <response code="413">El cuerpo supera el tamano maximo.</response>
    /// <response code="429">Demasiadas ingestas en curso; reintentar tras Retry-After.</response>
    /// <response code="501">Origen <c>ruta</c>, reservado.</response>
    [HttpPost("{coleccion}/ingestas")]
    [Authorize]
    // 1.7.1: el default de Kestrel (30 000 000 bytes) queda por debajo de lo que el contrato admite
    // (100 documentos de hasta 1 MiB). Limites.MaxBytesPorIngesta cubre ese maximo con margen para JSON.
    [RequestSizeLimit(Limites.MaxBytesPorIngesta)]
    [ProducesResponseType(typeof(IngestaResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status413PayloadTooLarge)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    [ProducesResponseType(StatusCodes.Status501NotImplemented)]
    public async Task<ActionResult<IngestaResponse>> PostIngesta(
        string coleccion, [FromBody] IngestaRequest? request, CancellationToken cancellationToken) =>
        Ok(await Mediator.Send(new IngerirCommand(coleccion, request?.Origen, request?.Perfil, request?.Resumen,
            request?.PerfilIngesta, request?.RepositorioLogico, request?.PerfilLlm), cancellationToken));

    /// <summary>Indexa un proyecto completo subido como .zip, de forma asincrona.</summary>
    /// <remarks>
    /// Responde 202 de inmediato con un <c>ingestaId</c>; el avance se consulta en
    /// GET <c>{coleccion}/ingestas/{ingestaId}</c> hasta ver <c>completada</c> o <c>error</c>. El .zip se toma como el
    /// proyecto entero: los documentos de la coleccion que ya no vienen en el se borran.
    /// </remarks>
    /// <param name="coleccion">Coleccion destino (p. ej. el nombre del repositorio); se crea en la primera ingesta.</param>
    /// <param name="form">Archivo .zip y opciones; ver la descripcion de cada campo.</param>
    /// <response code="202">Ingesta admitida, en estado <c>pendiente</c> (conteos en 0).</response>
    /// <response code="400">.zip ausente, invalido o sin documentos admitidos, carpetaRaiz que no coincide, o perfil no registrado.</response>
    /// <response code="409">El perfil de ingesta choca con el de la coleccion existente.</response>
    /// <response code="413">El .zip supera los 100 MiB.</response>
    /// <response code="429">Demasiadas ingestas en curso; reintentar tras Retry-After.</response>
    [HttpPost("{coleccion}/ingestas/proyecto")]
    [Authorize]
    [Consumes("multipart/form-data")]
    // 1.7.5: el .zip admite hasta 100 MiB (LimitesZip.MaxBytesZip); el margen de Limites.MaxBytesZipProyecto cubre
    // el resto del multipart.
    [RequestSizeLimit(Limites.MaxBytesZipProyecto)]
    [ProducesResponseType(typeof(IngestaResponse), StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status413PayloadTooLarge)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<IngestaResponse>> PostIngestaProyectoZip(
        string coleccion, [FromForm] IngestaProyectoZipForm form, CancellationToken cancellationToken)
    {
        byte[]? contenido = null;
        if (form.Zip is not null)
        {
            using var memoria = new MemoryStream();
            await form.Zip.CopyToAsync(memoria, cancellationToken);
            contenido = memoria.ToArray();
        }

        var respuesta = await Mediator.Send(new IngerirProyectoZipCommand(coleccion, contenido, form.CarpetaRaiz, form.Perfil, form.Resumen,
            form.PerfilIngesta, form.RepositorioLogico, form.PerfilLlm), cancellationToken);
        return Accepted($"/api/colecciones/{coleccion}/ingestas/{respuesta.IngestaId}", respuesta);
    }

    /// <summary>Indexa un repositorio completo clonandolo desde Git, de forma asincrona.</summary>
    /// <remarks>
    /// Mismo contrato que el .zip (202 + consulta de estado). El servicio clona con su propia identidad; la URL debe
    /// pertenecer a un origen de GET api/catalogo/origenes-git, y una URL fuera de esa lista responde 400 sin que el
    /// servicio se conecte a ella.
    /// </remarks>
    /// <param name="coleccion">Coleccion destino (p. ej. el nombre del repositorio); se crea en la primera ingesta.</param>
    /// <param name="request">URL del repositorio y opciones.</param>
    /// <response code="202">Ingesta admitida, en estado <c>pendiente</c>.</response>
    /// <response code="400">URL ausente o fuera de los origenes permitidos, o perfil no registrado.</response>
    /// <response code="409">El perfil de ingesta choca con el de la coleccion existente.</response>
    /// <response code="429">Demasiadas ingestas en curso; reintentar tras Retry-After.</response>
    [HttpPost("{coleccion}/ingestas/git")]
    [Authorize]
    [ProducesResponseType(typeof(IngestaResponse), StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<IngestaResponse>> PostIngestaProyectoGit(
        string coleccion, [FromBody] IngerirProyectoGitRequest? request, CancellationToken cancellationToken)
    {
        var respuesta = await Mediator.Send(
            new IngerirProyectoGitCommand(coleccion, request?.Url, request?.CarpetaRaiz, request?.Perfil, request?.Resumen,
                request?.PerfilIngesta, request?.RepositorioLogico, request?.PerfilLlm), cancellationToken);
        return Accepted($"/api/colecciones/{coleccion}/ingestas/{respuesta.IngestaId}", respuesta);
    }

    /// <summary>Estado de una ingesta de proyecto (.zip o Git).</summary>
    /// <remarks>
    /// Consultar hasta que <c>estado</c> sea <c>completada</c> (conteos definitivos) o <c>error</c> (ver
    /// <c>mensajeError</c>).
    /// </remarks>
    /// <param name="coleccion">Coleccion a la que se envio la ingesta.</param>
    /// <param name="ingestaId">Identificador que devolvio el 202.</param>
    /// <response code="200">Estado actual de la ingesta.</response>
    /// <response code="404">No existe en esta instancia, o es de otra coleccion.</response>
    [HttpGet("{coleccion}/ingestas/{ingestaId:guid}")]
    [Authorize]
    [ProducesResponseType(typeof(IngestaResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IngestaResponse>> GetEstadoIngesta(
        string coleccion, Guid ingestaId, CancellationToken cancellationToken) =>
        Ok(await Mediator.Send(new GetEstadoIngestaQuery(coleccion, ingestaId), cancellationToken));

    /// <summary>Calcula los vectores de textos sueltos con el perfil de la coleccion. No escribe nada.</summary>
    /// <remarks>Devuelve los mismos vectores que se guardarian al indexar esos textos.</remarks>
    /// <param name="coleccion">Coleccion cuyo perfil de embeddings se usa (no necesita existir).</param>
    /// <param name="request">Textos y perfil opcional.</param>
    /// <response code="200">Un vector por texto, en el mismo orden.</response>
    /// <response code="400">Cuerpo invalido o perfil no registrado.</response>
    /// <response code="409">El perfil pedido no es el de la coleccion.</response>
    [HttpPost("{coleccion}/embeddings")]
    [Authorize]
    [ProducesResponseType(typeof(EmbeddingsResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status501NotImplemented)]
    public async Task<ActionResult<EmbeddingsResponse>> PostEmbeddings(
        string coleccion, [FromBody] CalcularEmbeddingsRequest? request, CancellationToken cancellationToken) =>
        Ok(await Mediator.Send(new CalcularEmbeddingsCommand(coleccion, request?.Textos, request?.Perfil), cancellationToken));

    /// <summary>Lista los documentos indexados en una coleccion.</summary>
    /// <remarks>
    /// Ruta, huella del contenido y cantidad de fragmentos (chunks) de cada documento. Catalogo de consulta, sin
    /// paginar.
    /// </remarks>
    /// <param name="coleccion">Coleccion a listar.</param>
    /// <response code="200">Documentos ordenados por ruta; vacia si la coleccion no existe (nunca 404).</response>
    /// <response code="503">El vector store no responde.</response>
    [HttpGet("{coleccion}/documentos")]
    [Authorize]
    [ProducesResponseType(typeof(IReadOnlyList<DocumentoIndexadoResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<IReadOnlyList<DocumentoIndexadoResponse>>> GetDocumentos(
        string coleccion, CancellationToken cancellationToken) =>
        Ok(await Mediator.Send(new GetDocumentosQuery(coleccion), cancellationToken));

    /// <summary>Busqueda semantica: los fragmentos de la coleccion mas parecidos a la consulta.</summary>
    /// <remarks>
    /// <c>espacio</c> elige contra que se compara: el codigo (por defecto) o el resumen de negocio (solo en colecciones
    /// creadas con <c>resumen=true</c>). <c>modulos</c>, <c>simbolosDefinidos</c>, <c>lenguaje</c> y <c>namespace</c>
    /// acotan los resultados y se combinan con AND.
    /// </remarks>
    /// <param name="coleccion">Coleccion en la que buscar.</param>
    /// <param name="request">Consulta y filtros opcionales.</param>
    /// <response code="200">Resultados ordenados de mayor a menor puntuacion.</response>
    /// <response code="400">Cuerpo invalido o perfil no registrado.</response>
    /// <response code="409">Perfil distinto al de la coleccion, espacio <c>resumen</c> sin resumen, o coleccion no preparada para <c>modulos</c>.</response>
    /// <response code="503">El vector store o el servidor de embeddings no responde.</response>
    [HttpPost("{coleccion}/busquedas")]
    [Authorize]
    [ProducesResponseType(typeof(BusquedaResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status501NotImplemented)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<BusquedaResponse>> PostBusqueda(
        string coleccion, [FromBody] BuscarRequest? request, CancellationToken cancellationToken) =>
        Ok(await Mediator.Send(
            new BuscarEnColeccionQuery(coleccion, request?.Consulta, request?.TopK, request?.Perfil, request?.SimbolosDefinidos, request?.Espacio,
                request?.Modulos, request?.Lenguaje, request?.Namespace, request?.PrefetchPorRama),
            cancellationToken));
}
