# Capa Domain

Proyecto `Reyma.VectorSpace.Engine.Microservice.Domain`. Contiene solo entidades; no depende de otras capas.

## Reglas

- Una entidad por archivo, agrupadas por carpeta funcional (`Catalogos/`, `Transaccionales/`).
- Entidades **con estatus** heredan de `GrBaseSequenceEntity<TipoEstatus>`; **sin estatus**, de `BaseEntity`
  (`Reyma.Entities.Base.Postgres`). Agregar el paquete cuando se cree la primera entidad.
- Propiedades de navegacion `virtual` y anulables; llaves foraneas explicitas.
- Nombres de dominio en espanol y en singular.
- Sin logica de infraestructura, sin atributos de EF (el mapeo va en Persistence), sin referencias a Business o Api.

## Tablas (PostgreSQL)

Prefijo corto del servicio y tipo: catalogos `xx_ct_`, transaccionales `xx_tr_`, en minusculas y plural
(`xx_ct_normas`). Definir el prefijo `xx` al crear la primera entidad y documentarlo aqui.

## Ejemplo

```csharp
namespace Reyma.VectorSpace.Engine.Microservice.Domain.Catalogos;

public class Norma : GrBaseSequenceEntity<TipoEstatus>
{
    public string Nombre { get; set; } = null!;
    public string? Descripcion { get; set; }
    public virtual ICollection<Criterio>? Criterios { get; set; }
}
```
