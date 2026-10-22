# Capa Api

Proyecto `Reyma.VectorSpace.Engine.Microservice.Api`. Expone REST y compone el servicio.

## Controllers

- Heredan de `BaseController` (`Reyma.Mvc.Base.Controllers`); usan `Mediator.Send(...)` y devuelven `Ok(...)`.
- Ruta con `[Route("[controller]")]` (o agrupada, p. ej. `catalogos/[controller]`); la ruta base del servicio
  (`ASPNETCORE_BASEPATH`) la aplica el host.
- Una accion HTTP por endpoint, sin logica de negocio, con `CancellationToken`.
- Listados: un `Get` con `SieveModel` que devuelve `PagedResponse<TResponse>`.
- Documentar con XML y atributos de Swagger (`ProducesResponseType`) cuando aporten.

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
