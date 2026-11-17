# Capa Api

Proyecto `Reyma.VectorSpace.Engine.Microservice.Api`. Expone REST y compone el servicio.

## Controllers

- Heredan de `BaseController` (`Reyma.Mvc.Base.Controllers`); usan `Mediator.Send(...)` y devuelven `Ok(...)`.
- Ruta con `[Route("[controller]")]` (o agrupada, p. ej. `catalogos/[controller]`); la ruta base del servicio
  (`ASPNETCORE_BASEPATH`) la aplica el host.
- Una accion HTTP por endpoint, sin logica de negocio, con `CancellationToken`.
- Listados: un `Get` con `SieveModel` que devuelve `PagedResponse<TResponse>`.
- Documentar con XML y atributos de Swagger (`ProducesResponseType`) cuando aporten.

## Documentacion para Swagger

Api y Business generan su XML (`GenerateDocumentationFile`); `AddReymaSwagger` lo carga. Lo que se escribe ahi es
lo que lee quien abre Swagger sin conocer el servicio, asi que:

- Cada accion lleva `<summary>` (que hace), `<remarks>` si hace falta contexto, `<param>` de la ruta/query y un
  `<response code="...">` por cada codigo que puede devolver.
- Cada propiedad de un request lleva `<summary>` que se explique sola: que es, si es opcional, que pasa si se omite y
  **de donde salen los valores validos** (p. ej. `GET api/catalogo/perfiles-embeddings`). Sin remitir a otra clase
  ("Ver X.Perfil") ni a numeros de item del plan: eso no se ve en Swagger.
- Respuestas como `record` posicional: documentar cada campo con `<param name="Campo">` en el record.
- Un formulario multipart va como clase con `[FromForm]` (ver `Models/IngestaProyectoZipForm.cs`), no como
  parametros sueltos: asi Swagger muestra la descripcion de cada campo.
- Los valores aceptados que cambian por instancia (perfiles, origenes) no se enumeran a mano en la documentacion: se
  exponen en `CatalogoController`, leidos de la misma configuracion que valida las solicitudes.
- `CatalogoTests` comprueba que los campos de ingesta tienen descripcion; extenderlo al agregar campos.

## Seguridad y permisos

- Las acciones de negocio llevan `[Authorize(Policy = ApiSecurityConstants.Policies.X)]` con la politica acordada.
- Excepcion inicial: los tres GET de `EjemploController` usan `[Authorize]` sin politica
  para probar el login. No copiar esa excepcion a recursos de negocio sin definir su autorizacion.
- Formato de permiso: `PREFIJO.API.RECURSO.ACCION` (prefijo en `ServicioInfo.PrefijoPermisos`), p. ej. `VECTORSPACE.ENGINE.API.EJEMPLO.CONSULTAR`.
- Agregar cada politica nueva en `ApiSecurityConstants` y cubrirla en pruebas (ver `EjemploTests`).
- Sin `[AllowAnonymous]` fuera de los health checks.

## Composicion

- `Startup.cs` delega en `AddReymaDefaults` / `UseReymaDefaults`; las extensiones especificas estan en
  `Microsoft/Extensions/DependencyInjection/`. Agregar ahi nuevos registros, no en `Startup`.
- Configuracion nueva por entorno: `.env` local (item 1.5, ver `.env.example`) o user-secrets en desarrollo; en el
  entorno de despliegue real, el gestor de secretos que se decida (no hay ninguno implementado hoy). Nunca secretos
  en `appsettings*.json`. Los defaults sensibles de desarrollo existentes requieren revision/externalizacion y no
  deben reproducirse.

## Pruebas

Cada endpoint nuevo requiere pruebas en `tests/`: 401 sin token, autorizacion acordada
(politica cuando corresponda) y respuestas de exito/error. Inspeccionar atributos no
sustituye comprobar el comportamiento HTTP con una identidad de prueba.
