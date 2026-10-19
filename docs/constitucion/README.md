# Constitucion del servicio - Reyma.VectorSpace.Engine.Microservice

Guias por capa con patrones, convenciones y buenas practicas. Sirven como referencia
para el equipo y para asistentes de IA. Las reglas comunes estan en
[.github/copilot-instructions.md](../../.github/copilot-instructions.md).

## Alcance y puntos de entrada

Esta constitucion corresponde a **este servicio**. Se incluyo al generarlo; la
opcion de omitirla no cambia la arquitectura, los tests ni la autenticacion.
No se actualiza automaticamente cuando se instala otra version del generador.

| Archivo | Uso |
|---|---|
| [.github/copilot-instructions.md](../../.github/copilot-instructions.md) | Reglas comunes y entrada para Copilot |
| [CLAUDE.md](../../CLAUDE.md) | Entrada de Claude Code que importa las reglas comunes |
| [AGENTS.md](../../AGENTS.md) | Enlaces para agentes que soporten este formato |

La carga depende del asistente y su configuracion. Si no lee esos archivos
automaticamente, referenciarlos en la tarea. Su presencia no garantiza cumplimiento,
no reemplaza revision humana/tests y no concede permisos de infraestructura.

## Guias

| Guia | Cuando consultarla |
|---|---|
| [DOMAIN_LAYER.md](./DOMAIN_LAYER.md) | Antes de crear entidades |
| [PERSISTENCE_LAYER.md](./PERSISTENCE_LAYER.md) | Antes de crear configuraciones de EF Core o migraciones |
| [BUSINESS_LAYER.md](./BUSINESS_LAYER.md) | Antes de crear queries, commands o responses |
| [API_LAYER.md](./API_LAYER.md) | Antes de crear controllers o permisos |

## Como trabajar

1. Leer las reglas comunes y las guias de las capas que se van a modificar.
2. Revisar una implementacion y sus tests existentes antes de copiar un patron.
3. Definir respuesta, autorizacion y limites del cambio; mantener la separacion de capas.
4. Revisar el diff y ejecutar las pruebas pertinentes. No tomar el login manual como sustituto de tests.
5. Si hay contradicciones, acordar la solucion y actualizar regla, codigo y pruebas juntos.

Los ejemplos de entidades, migraciones y paginacion ilustran ampliaciones; no
significan que esos recursos o todas sus dependencias existan en el servicio inicial.

## Consideraciones de la base inicial

- Los tres GET de `EjemploController` requieren usuario autenticado pero no politica
  de permiso. Los recursos de negocio necesitan su autorizacion acordada.
- Una audiencia compartida de Development es una facilidad de prueba, no aislamiento
  entre APIs. No desactivar validaciones JWT para resolver problemas de login.
- Los defaults sensibles de desarrollo existentes no autorizan agregar secretos
  versionados. No copiarlos a prompts, respuestas, logs, capturas o documentacion.
- Aplicar migraciones o desplegar requiere autorizacion del entorno por separado.

## Estructura de capas

```
src/
  Reyma.VectorSpace.Engine.Microservice.Domain/       Entidades
  Reyma.VectorSpace.Engine.Microservice.Persistence/  ApplicationDbContext y configuraciones
  Reyma.VectorSpace.Engine.Microservice.Business/     Queries, commands, responses, validadores
  Reyma.VectorSpace.Engine.Microservice.Api/          Controllers y composicion
tests/
  Reyma.VectorSpace.Engine.Microservice.Tests/        Pruebas de integracion y contrato
```

```
Request -> Controller -> Mediator -> Handler -> DbContext -> Base de datos
Response <- Controller <- Response (record) <- Mapeo <- Entidad
```

## Paquetes Reyma

`Reyma.Mvc.Base.Controllers` (BaseController), `Reyma.Mediator.Base.Postgres.*` (CQRS),
`Reyma.Persistence.Base.Postgres.*` (BaseDbContext y bases de EF), `Reyma.AutoMapper.*`,
`Reyma.FluentValidations.*`, `Reyma.IdentityService.Tools.*`, `Reyma.Permissions.Validator.*`,
`Reyma.IntegrityService.Tools.*`, `Reyma.Dates.*`, `Reyma.RateLimit.*`, `Reyma.Swagger.Extensions`.

## Mantenimiento

Actualizar estas guias cuando se introduzca un patron nuevo, cambien paquetes Reyma con
cambios incompatibles o se agreguen funcionalidades transversales.

Mantener las reglas comunes en un solo lugar y las especificas en su guia por capa.
CLAUDE y AGENTS enlazan a esas fuentes; no mantener tres versiones de la misma regla.
Documentar las excepciones locales del servicio y revisarlas al incorporar cambios
del generador, sin sobrescribir decisiones de negocio.
