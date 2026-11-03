# Filtros de busqueda: usos, ejemplos y limites

Un filtro acota **que chunks pueden participar** en una busqueda; la consulta
describe lo que se busca dentro de ese conjunto. No asigna permisos ni garantiza
que existan resultados por encima del umbral. Es util para explorar un area del
codigo y para comparar pruebas con y sin una restriccion.

Esta guia distingue capacidades **disponibles** de decisiones **pendientes de
implementar**. Los nombres, consultas y documentos de ejemplo son sinteticos:
no acreditan uso real ni se ejecutaron contra servicios para escribir esta guia.
Contrato normativo: [contrato-api.md](contrato-api.md).

## Que filtro usar y donde existe

| Necesidad | Entrada | Semantica | Disponibilidad |
|---|---|---|---|
| Buscar dentro de un modulo o directorio raiz | VectorSpace: `modulos`; CLI de Rag: `--module` | Namespace exacto/descendiente por `.` **O** ruta relativa exacta/descendiente por `/`. Comparacion ordinal, sensible a mayusculas. Varios modulos: **AND**. | Implementado en ambos, pero Rag aun usa su motor local. VectorSpace filtra antes del corte; Rag usa prefiltro textual y comprobacion posterior. |
| Encontrar definiciones de uno o varios simbolos | VectorSpace: `simbolosDefinidos` | Coincidencia keyword exacta con **cualquiera** de los nombres guardados. Se combina con los otros filtros por AND. | Implementado en VectorSpace. No hace por si mismo expansion por simbolos ni reranking. |
| Acotar por lenguaje, por ejemplo CSharp | CLI de Rag: `--language`; puerto: `FilterByLanguage` | `Match.Text` sobre `language` con el nombre del enum. No se convierte en igualdad keyword. | Existe en Rag local. Conservacion decidida el 2026-11-03; falta implementarlo en VectorSpace (1.21.4). |
| Acotar por texto del namespace | CLI de Rag: `--namespace`; puerto: `FilterByNamespace` | `Match.Text` SOLO sobre `namespace`. No es un prefijo exacto ni incluye rutas. | Existe en Rag local. Conservacion decidida el 2026-11-03; falta implementarlo en VectorSpace (1.21.4). |
| Separar documentos por tenant | CLI/puerto local de Rag: `--tenant`/`FilterByTenant` | En Rag actual, keyword exacto; se combina con tenant autorizado por AND. | **Diferido en el futuro cliente remoto**. VectorSpace no guarda ni filtra tenant. Una solicitud que lo exija debera rechazarse, no ignorarse. |
| Restringir por permisos de una identidad | Rag: contexto `Authorized`, derivado de modo `Empresarial` | En el motor actual de Rag, ACL de coleccion mas filtros de contexto. No es un parametro de filtro libre de VectorSpace. | **Diferido en el futuro cliente remoto**. Primera integracion en Local explicito, sin derivar permisos; solicitar Authorized debera producir rechazo, no conversion a Local. |

`lenguaje` y `namespace` en un resultado de VectorSpace son metadatos de salida:
**no implican parametros de entrada**. No agregues propiedades inventadas al JSON
actual esperando que filtren; su mera presencia no demuestra que se aplicaron.
Los nombres HTTP de lenguaje/namespace se fijaran y documentaran en 1.21.4.
El cuerpo de `/api/search` y `/api/ask` de Rag tampoco expone estos filtros:
los ejemplos de lenguaje/namespace siguientes son de su CLI, no de esas rutas.

## Ejemplos HTTP disponibles en VectorSpace

Son cuerpos para `POST api/colecciones/{coleccion}/busquedas`, relativos a la
base configurada del servicio. Usa una coleccion de prueba ya preparada y una
sesion autenticada por los mecanismos existentes; aqui no se incluyen tokens.

### Comparar sin filtro y con modulo

Control sin restriccion:

```json
{
  "consulta": "como se confirma un pedido",
  "topK": 5
}
```

Misma consulta, solo dentro del modulo `Ventas`:

```json
{
  "consulta": "como se confirma un pedido",
  "topK": 5,
  "modulos": ["Ventas"]
}
```

Para `Ventas`, el predicado de modulo admite estos ejemplos de chunks:

| Namespace | Ruta relativa | Elegible por modulo |
|---|---|---|
| `Ventas` | `otro/Pedido.cs` | Si: namespace exacto. |
| `Ventas.Pedidos` | `otro/Pedido.cs` | Si: descendiente por punto. |
| `Otro` | `Ventas/Pedido.cs` | Si: descendiente de ruta por barra. |
| `VentasExtra` | `otro/Pedido.cs` | No: no hay frontera despues de Ventas. |
| `ventas` | `otro/Pedido.cs` | No: distinta mayuscula. |
| `Empresa.Ventas` | `src/Ventas/Pedido.cs` | No: Ventas no es la raiz de ninguno. |
| ausente | `readme.md` | No: la ausencia de namespace no concede coincidencia. |

Elegible no significa devuelto necesariamente: siguen aplicando umbral, top-K,
espacio de busqueda y demas filtros.

### Interseccion de modulos, no union

```json
{
  "consulta": "pruebas de confirmacion",
  "modulos": ["Ventas", "pruebas"]
}
```

Un chunk con namespace `Ventas.Pedidos` y ruta `pruebas/unitarias/Pedido.cs`
cumple ambos, cada uno por un campo diferente. Un chunk de `Ventas.Pedidos` en
`src/Pedido.cs` no cumple `pruebas`. La lista **no** significa "Ventas o pruebas".
Para exigir una rama mas concreta se puede usar `["Ventas", "Ventas.Pedidos"]`.

### Combinar modulo con simbolos

```json
{
  "consulta": "confirmar o cancelar un pedido",
  "modulos": ["Ventas"],
  "simbolosDefinidos": ["ConfirmarPedido", "CancelarPedido"]
}
```

Exige el modulo Ventas **y** que `simbolosDefinidos` del chunk contenga al menos
uno de esos nombres exactos. Copia los nombres de los metadatos reales del corpus:
no asumas que una palabra de la consulta o un nombre de clase es un simbolo
definido por el chunk. No es una busqueda por subcadena del nombre del metodo.

La misma combinacion se puede enviar con `"espacio": "resumen"` si la coleccion
tiene el vector de resumen y el servicio tiene configurado su perfil. No agregues
`perfil` junto con ese espacio: responde 400. En resumen solo participan chunks
que ya tienen ese vector; el filtro no genera resumenes pendientes.

## Ejemplos de lenguaje y namespace: CLI actual de Rag

Estos ejemplos se ejecutarian desde `D:\Git\Rag.Context.Engine`, contra una
coleccion local de prueba existente. **No invocan el futuro adaptador remoto**.
No se ejecutaron como parte de esta preparacion documental.

```powershell
dotnet run --project .\src\RagEngine.Cli -- search "confirmar pedido" --collection filtros-prueba --language CSharp
dotnet run --project .\src\RagEngine.Cli -- search "confirmar pedido" --collection filtros-prueba --namespace "Empresa.Ventas"
dotnet run --project .\src\RagEngine.Cli -- search "confirmar pedido" --collection filtros-prueba --language CSharp --namespace "Empresa.Ventas"
```

El primer caso envia `Match.Text(language, "CSharp")`; el segundo,
`Match.Text(namespace, "Empresa.Ventas")`; juntos exigen **ambas** condiciones.
Lenguajes del enum actual: `CSharp`, `TypeScript`, `JavaScript`, `Xaml`, `Sql`,
`Markdown`, `PlainText`, `Unknown`. El nombre del filtro no cambia la consulta
semantica ni transforma el documento a ese lenguaje.

**Namespace textual no equivale a modulo.** No incluye el OR con ruta, no aplica
la comprobacion de frontera del modulo y no se transforma en `modulos`. Para un
alcance exacto namespace/ruta usa modulo; para mantener la busqueda textual por
namespace usa namespace, con la semantica de la version/esquema de Qdrant.

`Match.Text` depende de tokenizacion, indices y version. El banco historico de
1.2 observo, sin indice full-text:

| Consulta sobre namespace | Qdrant 1.14.1 | Qdrant 1.19.2 |
|---|---|---|
| `Reyma.Ventas` | Coincidia con `Reyma.Ventas.Api`, `Reyma.VentasExtra.Api` y `xReyma.Ventas`. | Coincidia solo con `Reyma.Ventas.Api` entre esos tres. |
| `reyma.ventas` | No coincidia con esos valores con mayusculas. | Coincidia con `Reyma.Ventas.Api`. |

Es evidencia historica, no una comprobacion de tu instancia. Conservar
`Match.Text` no garantiza resultados invariantes al cambiar de version. 1.21.4
debera probar el operador de origen y el nuevo filtro sobre el MISMO corpus,
Qdrant 1.19.2 y esquema sin indice full-text (referencia del servicio actual),
sin declarar paridad con un despliegue antiguo no comprobado.

## Ausentes, blancos y errores

| Entrada | Comportamiento |
|---|---|
| VectorSpace: `modulos` o `simbolosDefinidos` ausente/null | No activa ese filtro. |
| VectorSpace: lista presente vacia, mas de 50 elementos o cadena blanca | 400, no una busqueda ampliada. `modulos` exige ademas arreglo JSON de cadenas, sin elementos null. |
| Modulo `" Ventas"` o `"Ventas "` | Se conserva literal, sin trim ni lowercase; no equivale a `"Ventas"`. |
| Rag local: lenguaje null; namespace/tenant/modulo null, vacio o blanco | No activa ese filtro explicito. No interpretar esto como permiso de acceso. |
| VectorSpace: modulo sobre coleccion existente sin preparacion completa | 409 antes de vectorizar; no reintentar quitando el filtro. |
| VectorSpace: coleccion inexistente | 200 con resultados vacios; no demuestra que otra coleccion este preparada. |
| Futuro cliente: tenant explicito no blanco, o contexto Authorized incluso sin claims | Rechazo explicito antes de HTTP y de consumir resultados. Pendiente de implementar en 1.21.3. |

Para una coleccion anterior al filtro de modulo, ver
[procedimiento de relleno](../../tools/relleno-modulo/README.md).
Una reingesta sin cambios no repara sus prefijos. Esta guia no autoriza ejecutar
el relleno ni alterar una coleccion servida.

## Como aprovecharlos en pruebas

Con un corpus sintetico aislado, fija consulta, vectores, umbral y top-K, y compara
control sin filtro con el caso filtrado. Para comprobar filtrado antes del corte,
incluye candidatos ajenos de mayor puntuacion y K candidatos elegibles: los
ajenos no deben ocupar puestos ni dejar el resultado incompleto por post-filtrado.
En codigo, fusion y resumen deben cumplirse las mismas restricciones.

Para lenguaje/namespace, fija tambien version e indices: usa valores exactos,
otros lenguajes, mayusculas, tokens, subcadenas, namespace ausente y una ruta
coincidente con namespace distinto. Un resultado por ruta en ese ultimo caso
puede ser correcto para modulo, pero no demuestra que se haya filtrado namespace.
Compara contra `Match.Text` de referencia, no contra un predicado de modulo.

Para el futuro adaptador, prueba ademas que un tenant activo o Authorized
rechazado no provoque peticiones remotas, uso de resultados, ni fallback local.
No uses los ejemplos de esta guia como prueba de recall o de utilizacion real.

## Alcance de uso libre y capacidades diferidas

La decision del 2026-11-03 es que la primera integracion use **Local explicito**:
sin derivar permisos por usuario, tenant o modulo. Los filtros opcionales siguen
sirviendo para acotar consultas/pruebas, no para autorizar. No se modifica ahora
el codigo local de Rag; su modo Empresarial existente no se convierte
automaticamente a Local y no queda habilitado en el futuro adaptador.

**Autenticacion no es autorizacion por recurso**: VectorSpace sigue requiriendo
token valido; 1.14 ya retiro permisos por coleccion. Esta decision no elimina
el login ni vuelve publico el servicio. No promete aislamiento frente a otro
cliente autenticado que omita un filtro.

Tenant se retomara cuando se definan asignacion por documento, datos historicos,
ausencias y convivencia de tenants (1.21.5). Authorized se retomara con fuente
de identidad/reglas, alcance de acceso, claims ausentes y pruebas de denegacion
(1.21.6). No son prerrequisitos del modo Local; si una solicitud los exige,
se rechaza expresamente hasta que esten implementados y verificados.

Decision y evidencia tecnica: [compatibilidad del cliente](../evaluaciones/1.21/compatibilidad-cliente.md).
Estado y dependencias: [ledger](../plan/ejecucion-plan.estado.json).
