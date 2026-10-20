# Instrucciones para IA - Reyma.VectorSpace.Engine.Microservice

Constitucion del servicio para asistentes de IA (Copilot, Claude, etc.). Las guias
detalladas por capa estan en [docs/constitucion/README.md](../docs/constitucion/README.md);
leerlas antes de crear codigo nuevo.

## Arquitectura

Microservicio **.NET 10** con **Clean Architecture**. Capas vigentes hoy:

1. **Domain** (`Reyma.VectorSpace.Engine.Microservice.Domain`) - entidades.
2. **Business** (`Reyma.VectorSpace.Engine.Microservice.Business`) - CQRS con MediatR (queries, commands, responses, validadores).
3. **Api** (`Reyma.VectorSpace.Engine.Microservice.Api`) - controllers REST y composicion.

El proyecto `Reyma.VectorSpace.Engine.Microservice.Persistence` (PostgreSQL/`ApplicationDbContext`,
EF Core) que traia la plantilla fue retirado por completo en el item `1.9-decidir-si-se-conserva-postgres-de-la-plantilla`
del ledger (`docs/plan/ejecucion-plan.estado.json`): no tenia un solo `DbSet` en uso, nada fuera de su
propio registro lo referenciaba, y la tabla de acceso del servicio vive en configuracion, no en base de
datos. Este servicio guarda datos en Qdrant (vector store), no en PostgreSQL. Ver
`docs/constitucion/PERSISTENCE_LAYER.md` para la condicion bajo la cual se recrearia esa capa.

Flujo hoy: `Request -> Controller -> Mediator -> Handler -> Response`.
Dependencias permitidas: Api -> Business; Business -> Domain.
No invertirlas.

## Reglas criticas

- La composicion comun vive en `AddReymaDefaults` / `UseReymaDefaults`; `Startup.cs` solo delega. No duplicar esa configuracion.
- Controllers heredan de `BaseController` (`Reyma.Mvc.Base.Controllers`) y usan `Mediator.Send(...)`; sin logica de negocio.
- Las acciones de negocio llevan la politica acordada con `[Authorize(Policy = ApiSecurityConstants.Policies.X)]`.
  Permisos con formato `PREFIJO.API.RECURSO.ACCION` (prefijo actual: `VECTORSPACE.ENGINE`, ver `ServicioInfo.PrefijoPermisos`).
  Excepcion de arranque: los tres GET de `EjemploController` usan `[Authorize]` sin politica.
  No extender esa excepcion a recursos de negocio sin una decision explicita. Nunca `[AllowAnonymous]` salvo health checks.
- Cada query/command es un `record` que implementa `IRequest<T>` con su handler anidado; se agrupa en `Queries/<Recurso>/` o `Commands/<Recurso>/`.
- Respuestas como `record` en `Business/Models/Responses/`; nunca exponer entidades.
- Fechas via `IDateTime` (UTC), no `DateTime.Now`.
- Propagar siempre `CancellationToken`.
- Nunca poner secretos, cadenas de conexion ni credenciales en archivos versionados: van por variables de entorno, user-secrets o Doppler.
- Los defaults sensibles de desarrollo que ya vienen generados son una limitacion existente, no permiso
  para agregar otros o reproducirlos en respuestas, logs y documentacion. Revisar su externalizacion antes de distribuir.
- Cambios de esquema solo con migraciones de EF Core.

## Forma de trabajo

- Antes de editar, leer el indice de constitucion, la guia de la capa y los ejemplos/tests reales.
- Las guias de ampliacion no implican que todos los tipos, paquetes o recursos ilustrados ya existan.
- Revisar el diff y probar el comportamiento afectado; indicar los controles que no se pudieron ejecutar.
- Si una regla contradice el codigo o falta definir autorizacion, explicarlo y resolverlo con el responsable;
  no eliminar validaciones ni ampliar accesos para hacer que un ejemplo funcione.
- Actualizar la guia correspondiente al introducir un patron nuevo. No duplicar reglas en `CLAUDE.md` y `AGENTS.md`.
- Estas instrucciones no autorizan despliegues, acceso a datos reales ni aplicacion de migraciones.

## Estructura al agregar un recurso

```
Domain/<Recurso>.cs
Business/Models/Responses/<Recurso>Response.cs
Business/Queries/<Recurso>/Get<Recurso>Query.cs
Api/Controllers/<Recurso>Controller.cs
Api/ApiSecurityConstants.cs            (nueva politica)
tests/.../<Recurso>Tests.cs
```

Si el recurso necesitara persistencia relacional real (ver condicion en
`docs/constitucion/PERSISTENCE_LAYER.md`), se agrega tambien
`Persistence/Configurations/<Recurso>Configuration.cs`, lo que implica recrear esa capa.

## Estilo

- C# con nullable reference types, `ImplicitUsings` y namespaces por capa.
- Nombres de dominio en espanol; codigo de infraestructura en ingles.
- Documentacion XML en APIs publicas.
- Commits en ingles.

## Comandos

```bash
dotnet restore --interactive   # feed privado Gr-Api (Reyma.*)
dotnet build
dotnet test
dotnet run --project src/Reyma.VectorSpace.Engine.Microservice.Api
```

Ruta base: `/reyma/vectorspace/engine` (`ASPNETCORE_BASEPATH`). Health: `/health/self` y `/health/ready`.
