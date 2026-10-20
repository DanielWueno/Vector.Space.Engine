# Capa Persistence

> **Retirada en el item `1.9-decidir-si-se-conserva-postgres-de-la-plantilla` del ledger**
> (`docs/plan/ejecucion-plan.estado.json`, 2026-10-20). El proyecto
> `Reyma.VectorSpace.Engine.Microservice.Persistence` y su `ApplicationDbContext` **no existen hoy**
> en el codigo: se eliminaron por completo (csproj, entrada en la `.sln`, referencias desde Api y
> Business, tests de wiring). Este servicio guarda vectores en Qdrant, no en PostgreSQL, y su tabla
> de control de acceso vive en configuracion (`ControlDeAcceso:Concesiones`), sin uso real de base
> relacional. Esta guia queda como referencia de ampliacion futura, no como descripcion del estado
> actual.
>
> **Condicion para recrearla:** si el servicio llega a necesitar persistencia relacional real -por
> ejemplo el registro de ingestas de `1.7-definir-modelo-de-consumo-de-ingesta`, hoy bloqueado y
> diferido a proposito- se recrea el proyecto siguiendo las reglas de abajo, en vez de improvisar
> una base de datos sin este patron. Antes de recrearla, releer `docs/plan/ejecucion-plan.estado.json`
> (items 1.7 y 1.9) para confirmar que la necesidad es real y no un atajo para un caso que cabria en
> configuracion.

Si se recrea, el proyecto se llama `Reyma.VectorSpace.Engine.Microservice.Persistence`. `ApplicationDbContext` hereda de `BaseDbContext`
(`Reyma.Persistence.Base.Postgres`) y recibe `ICurrentUserAccessor` para la auditoria de usuario.

## Reglas

- Una clase de configuracion por entidad en `Configurations/`, replicando la estructura de Domain.
- Heredar de `GrBaseSequenceEntityConfig<TEntity, TStatus>` o `BaseEntityConfig<TEntity>` segun la entidad.
- Nombrar tabla de forma explicita: `builder.ToTable("xx_ct_normas", "public")`.
- Llaves foraneas: `fk_{tabla_origen}_{tabla_destino}`; indices `ix_{tabla}_{columna}`; unicos `uk_{tabla}_{columna}`.
- Registrar configuraciones por escaneo de ensamblado en `ApplicationDbContext`; no agregar `DbSet` innecesarios.
- Fuera de Development, la cadena sale de `CONNECTION_STRING`. En Development se usa
  `ConnectionStrings:Default` si no hay partes principales de `DatabaseConnection`;
  si se indica una parte, se requieren Host, Database, UserId y Password.
- No agregar cadenas reales versionadas. Los defaults sensibles existentes de desarrollo
  no son una autorizacion para replicarlos; usar configuracion externa.

## Migraciones

```bash
dotnet ef migrations add NombreMigracion --project src/Reyma.VectorSpace.Engine.Microservice.Persistence --startup-project src/Reyma.VectorSpace.Engine.Microservice.Api
dotnet ef database update --project src/Reyma.VectorSpace.Engine.Microservice.Persistence --startup-project src/Reyma.VectorSpace.Engine.Microservice.Api
```

Los comandos requieren las herramientas/dependencias de EF disponibles. Revisar el SQL
generado y contar con autorizacion del entorno antes de aplicar; no ejecutar `database update`
como paso automatico al generar o probar una API. Nunca editar migraciones ya desplegadas.

## Ejemplo

```csharp
public sealed class NormaConfiguration : GrBaseSequenceEntityConfig<Norma, TipoEstatus>
{
    public override void Configure(EntityTypeBuilder<Norma> builder)
    {
        base.Configure(builder);
        builder.ToTable("xx_ct_normas", "public");
        builder.Property(x => x.Nombre).HasMaxLength(200).IsRequired();
        builder.HasIndex(x => x.Nombre).IsUnique().HasDatabaseName("uk_xx_ct_normas_nombre");
    }
}
```
