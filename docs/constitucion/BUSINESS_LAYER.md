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
