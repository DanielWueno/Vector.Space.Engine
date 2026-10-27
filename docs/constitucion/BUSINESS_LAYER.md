# Capa Business

Proyecto `Reyma.VectorSpace.Engine.Microservice.Business`. CQRS con MediatR; el ensamblado se registra con
`BusinessAssembly` (AutoMapper, mediador y FluentValidation).

## Queries y commands

- `record` que implementa `IRequest<TResponse>` con el handler como clase anidada (ver `Queries/Ejemplo/GetEjemploQuery.cs`).
- Ubicacion: `Queries/<Recurso>/Get...Query.cs` y `Commands/<Recurso>/<Accion>...Command.cs`.
- Inyeccion por constructor primario: `IApplicationDbContext<T>`, `IMapper`, `IIdentityUserService<int>`, `IDateTime`.
- Fechas con `IDateTime` (UTC). Propagar `CancellationToken` a toda llamada asincrona.
- Listados paginados: heredar de `GetPagedInfoQuery<TResponse>` y su handler base, con Sieve para filtro/orden;
  sobrescribir siempre `DefaultSortPropertyName` (p. ej. `nameof(Norma.Nombre)`).
- Los commands se validan con FluentValidation (`AbstractValidator<TCommand>`) junto al command.
- Errores de negocio con las excepciones de Reyma; el filtro `ReymaExceptionFilterAttribute` las traduce a HTTP.
- Admision de operaciones caras (1.7): el handler pide un permiso a un limitador antes del motor
  (`ILimiteDeIngestas` en `IngerirCommand`) y lo libera con `using`. Asi el limite corre despues de los behaviors de
  autorizacion y validacion. Rebasarlo lanza `IngestasSaturadasException` (429 con `Retry-After`). Los valores viven en
  opciones validadas al arrancar (`LimiteDeIngestasOptions`), no en constantes.
- Comando con trabajo en segundo plano (1.7.5, `IngerirProyectoZipCommand`): cuando el trabajo real tarda demasiado
  para una respuesta HTTP sincrona (minutos u horas, ver 1.7.3), el handler hace la validacion y la admision de forma
  sincrona (para que un 400/429 no dependa de haber hecho trabajo de mas) y lanza el resto con `_ = Task.Run(...)`,
  usando `CancellationToken.None` dentro de esa tarea: cerrar la conexion HTTP no debe abortar un trabajo ya admitido.
  El permiso del limitador (`ILimiteDeIngestas`) se libera en el `finally` de la tarea, no en el handler. El progreso
  se expone con un registro de estado en memoria (`IRegistroDeIngestasProyecto`, singleton, un diccionario
  concurrente por `ingestaId`) que dos queries de solo lectura consultan (`GetEstadoIngestaQuery` por `ingestaId`,
  `GetIngestasQuery` desde el item 1.15 con un filtro `Coleccion` opcional desde el 1.16 -- null trae las de
  cualquier coleccion --, Operacion `Consultar` ambas); como el limitador, no persiste entre reinicios ni se
  comparte entre replicas. Los puertos de
  Business que necesita este patron van en `Business/Motor/IMotor.cs` junto a los demas `IMotorDe*`: el comando
  orquesta la asincronia, pero la lectura/validacion del contenido (p. ej. el .zip) y el trabajo pesado viven en
  Infrastructure, igual que el resto del motor.
- Mismo patron, segundo origen (1.7.6, `IngerirProyectoGitCommand`): origen `proyecto` desde una URL Git en vez de
  un `.zip`. Unica diferencia con 1.7.5: lo que el handler hace de forma SINCRONA antes de admitir (para que un 400
  no dependa de trabajo de mas) es solo validar la URL contra la lista blanca (`IMotorDeIngestaProyectoGit.ValidarUrlGit`,
  instantaneo, sin red) — no hay equivalente a "leer el .zip" porque el contenido todavia no existe del lado del
  servicio. Clonar, escanear el checkout y vectorizar (incluido el caso "el repo no trae documentos admitidos") pasa
  a ser parte del trabajo en segundo plano, con el mismo `IRegistroDeIngestasProyecto` y el mismo
  `GetEstadoIngestaQuery` que el `.zip`.
- Opt-in de una fase 2 opcional (1.7.7, resumen de negocio): un `bool? Resumen` en el command (`IngerirCommand`,
  `IngerirProyectoZipCommand`, `IngerirProyectoGitCommand`) viaja tal cual hasta el puerto de Infrastructure
  (`resumen: bool`, nunca `bool?`: el handler resuelve `request.Resumen == true` antes de llamar al motor). El
  motor decide que hacer con el (crear un vector opcional, rechazar activarlo sobre una coleccion existente); el
  command no valida nada especifico de ese campo mas alla de los limites ya existentes del comando. Mismo patron
  para cualquier fase 2 opcional futura: el command solo transporta el opt-in, Infrastructure resuelve el permiso
  de habilitarlo y Business no conoce Qdrant ni el LLM.

## Responses

- `record` inmutables en `Models/Responses/`; nunca devolver entidades.
- Mapeo simple con `IMapFrom<TEntity>`; mapeos complejos con `IHaveCustomMappings`.
- Documentacion XML en tipos y propiedades publicas; `[DataContract]`/`[DataMember]` solo si se necesita control de serializacion.
- Tipos nullable acordes con la base de datos.

## Ejemplo

```csharp
public sealed record GetEjemploQuery : IRequest<EjemploResponse>
{
    public sealed class GetEjemploQueryHandler(IDateTime dateTime)
        : IRequestHandler<GetEjemploQuery, EjemploResponse>
    {
        public Task<EjemploResponse> Handle(GetEjemploQuery request, CancellationToken cancellationToken) =>
            Task.FromResult(new EjemploResponse(ServicioInfo.Nombre, dateTime.Now));
    }
}
```
