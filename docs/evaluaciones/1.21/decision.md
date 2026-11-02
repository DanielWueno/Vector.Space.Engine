# 1.21 - Conservar modulo como filtro opcional de busqueda

Fecha: 2026-11-02. Estado: **decision documental resuelta; implementacion pendiente**.
Base Vector.Space.Engine: `7c32bfe43251379f33e7678d2b7950e0de9e0d3f` (1.22 hecho).
Base Rag.Context.Engine, solo lectura: `aae1de8`.

## Decision y motivo

El responsable aclaro: **"no uso module"** y, despues de conocer su funcion,
**"esta bien que se quede por lo mismo pruebas"**. Se conserva como capacidad
opcional para acotar busquedas y hacer pruebas; no se afirma que haya uso actual
ni que solo pueda usarse en tests automatizados.

Se conserva el predicado ordinal del origen: namespace exacto/descendiente por `.`
**o** ruta relativa exacta/descendiente por `/`. Se elige **(a), prefijos derivados
con indice keyword**, para filtrar antes del corte sin depender de `Match.Text`.
Los puntos existentes se completaran mediante **SetPayload**, sin revectorizar
ni cambiar el contrato de chunking. No se elige (b), por su dependencia de version
y perdida de candidatos, ni (c), porque no se pidio un mapa de etiquetas nuevo.

El filtro es un dato opcional de busqueda, **no una frontera de autorizacion**.
Se conserva 1.14: no se agregan permisos por modulo/coleccion a VectorSpace.
La autorizacion que corresponda a Rag sigue siendo responsabilidad de Rag; este
cierre no habilita un modo empresarial ni garantiza aislamiento en acceso directo.

Lenguaje, namespace y tenant quedan **fuera de la implementacion de modulo**.
No se eliminan ni se presume su uso o falta de uso. Su compatibilidad se preparara
en `1.21.2-preparar-compatibilidad-de-filtros-del-cliente`, antes de 1.12.
Esa separacion satisface el punto 5 de esta decision sin inventar otra decision
del responsable. El objetivo sigue siendo cliente completo para ingesta/busqueda,
con orquestacion y generacion RAG en Rag.

Se crean dos fichas, sin ejecutarlas:

- `1.21.1-implementar-filtro-exacto-de-modulo`: contrato opcional, prefijos/indexado,
  relleno de puntos existentes, pruebas aisladas y rollback.
- `1.21.2-preparar-compatibilidad-de-filtros-del-cliente`: revisar el contrato
  resultante de 1.21.1 y resolver/preparar los filtros restantes y los contextos
  autorizados antes de integrar. Si hacen falta mas implementaciones, registrarlas
  y enlazarlas como prerrequisitos efectivos de 1.12.

La cadena de entrada es 1.21 -> 1.21.1 -> 1.21.2 -> 1.12. El siguiente trabajo
sera 1.21.1; **1.12 sigue bloqueada**. Cerrar esta decision no significa que el
filtro ni su migracion esten implementados, ni autoriza tocar colecciones servidas.

## Antecedente: bloqueo registrado en e8cf0bb

El codigo permite describir la capacidad y comparar mecanismos, pero no demostrar
si los usuarios emplean `module` o `FilterByModule`. Esa confirmacion es condicion
expresa de reentrada de la ficha. Se solicito al responsable durante esta sesion;
no estuvo disponible. **Las opciones y valores sugeridos de la consulta no son
respuestas ni autorizaciones.**

El usuario si fijo el objetivo posterior: Rag.Context.Engine sera cliente completo
de VectorSpace para ingesta y busqueda, conservando orquestacion y generacion RAG.
En ese momento no habia respuesta sobre modulo; 1.12 quedo bloqueada por 1.21,
sin ejecutarse. La respuesta posterior resuelve ese bloqueo como se indica arriba.
El analisis parcial y sus comprobaciones se conservan como antecedente.

## Evidencia de codigo, configuracion y pruebas

Las rutas `R\...` son relativas a `D:\Git\Rag.Context.Engine`; `V\...`, a
`D:\Git\Vector.Space.Engine`. Las lineas corresponden a las bases anteriores.

| Evidencia | Hecho comprobado |
|---|---|
| `R\src\RagEngine.Core\Infrastructure\VectorStore\QdrantSemanticRetriever.cs:735-779` | Cada modulo coincide ordinalmente con namespace exacto o descendiente separado por `.`, **o** ruta relativa exacta o descendiente separado por `/`. No es subcadena, etiqueta ni segmento interior. `FilterByModule` y el modulo del contexto `Authorized` se combinan con **AND**. |
| Mismo archivo, `:105-206` | `Match.Text` prefiltra cada rama; despues se comprueba la frontera exacta sobre candidatos ya limitados. Los rechazados no siembran two-hop; sus resultados tambien se comprueban. Puede quedar menos de K sin rellenado. |
| `R\src\RagEngine.Core\Domain\RetrievalResult.cs:69-86` | El contrato declara expresamente ese limite del top-K; null/vacio no restringe. El helper rechaza blancos y el llamador no activa filtros blancos. |
| `R\src\RagEngine.Api\Program.cs:369-409` | Solo se leen claims de una identidad autenticada unica; mas de un modulo distinto invalida esa identidad para este adaptador. `Empresarial` produce contexto `Authorized`; `Local` no impone el modulo de identidad. |
| `R\src\RagEngine.Core\Infrastructure\Authorization\CollectionActorResolver.cs:10-23` y `CollectionAuthorizationOptions.cs:3-14` | `Local` es el default y resuelve un administrador local sin consultar identidad. `Authorized` es un contexto de retrieval, no el nombre de un modo de configuracion: el modo se llama `Empresarial`. |
| `R\src\RagEngine.Cli\Commands\SearchCommand.cs:37-48,84-95` | La CLI real expone `--language`, `--namespace`, `--tenant` y `--module`; construye contexto Local y pasa los filtros explicitos. Capacidad disponible no equivale a uso observado. |
| `R\src\RagEngine.Api\Contracts.cs:26-33` | El cuerpo compartido por search/ask no expone esos filtros; el modulo autorizado llega por identidad. No confundir paridad HTTP con paridad completa de la CLI/puerto. |
| `R\tests\RagEngine.Core.Tests\ModuleBoundaryTests.cs:10-42` | Casos sinteticos de frontera: incluye `alpha`, `alpha.Foo` y `alpha/Foo.cs`; excluye `alpha-private`, `Alpha`, `Company.alpha` y `src/alpha/Foo.cs` al pedir `alpha`. |
| `R\tests\RagEngine.Core.Tests\RetrievalAuthorizationContextHttpHarnessTests.cs:562-625` | Un middleware **de prueba** crea identidades a partir de cabeceras X-Test y claims inventados. No prueba login, despliegue ni usuarios reales. |
| `V\src\Reyma.VectorSpace.Engine.Microservice.Infrastructure\VectorStore\QdrantSemanticRetriever.cs:78-153` | 1.18 ya filtra por `defined_symbols` en denso, sparse y resumen. No hay filtro de modulo. La afirmacion antigua de la ficha de que todo usa `filter: null` quedo obsoleta. |
| `V\src\Reyma.VectorSpace.Engine.Microservice.Infrastructure\VectorStore\QdrantVectorStore.cs:342-343,410-433` | Guarda namespace y ruta relativa; crea indices keyword de `defined_symbols` y `file_path`, no de modulo/namespace/ruta relativa. |
| `V\src\Reyma.VectorSpace.Engine.Microservice.Api\Controllers\ColeccionesController.cs:12-25` y `V\docs\servicio\contrato-api.md:478-497` | Tras 1.14 basta autenticacion; no hay autorizacion por coleccion/modulo ni politicas corporativas aplicadas a estas acciones. Una seccion antigua de configuracion no restablece el behavior retirado. |

### Configuracion inspeccionada, no configuracion efectiva de un proceso

Se proyecto exclusivamente `Authorization:Mode`, sin mostrar otros valores:

| Archivo de Rag.Context.Engine | Valor encontrado |
|---|---|
| `src\RagEngine.Api\appsettings.json` (versionado) | `Local` |
| `config\shared.appsettings.json` (versionado) | Clave ausente |
| `src\RagEngine.Api\appsettings.Servidor.json` (versionado) | Clave ausente |
| `src\RagEngine.Api\appsettings.Development.local.json` (local no versionado) | Clave ausente |
| `src\RagEngine.Cli\appsettings.json` y `appsettings.Servidor.json` (versionados) | Clave ausente |

La API carga `WebApplication.CreateBuilder` e inserta configuracion compartida
(`R\src\RagEngine.Api\Program.cs:60-67`). No se comprobo que un proceso estuviera
ejecutando esa configuracion, sus overrides, ni que cargase el archivo local
anterior. No se consultaron logs, identidades, tokens, user-secrets ni servicios.
Los ejemplos del roadmap y los tests no completan esa falta de evidencia.
En la inspeccion inicial el uso real no estaba confirmado. La fuente posterior
para modulo es la declaracion del responsable, no estos archivos. No se extiende
"no uso module" a lenguaje, namespace, tenant ni a otros usuarios no consultados.

## Mecanismo elegido y alternativas descartadas para este alcance

### (a) Prefijos de frontera calculados en la ingesta - elegido

Se derivara un array keyword de los prefijos de namespace/ruta, incluyendo cada
valor completo. Preserva la comparacion ordinal, los separadores respectivos y
el OR entre campos. No aplicar lowercase, trim o normalizacion nueva que altere
las fronteras. Un namespace ausente no inventa modulo; la ruta aun puede coincidir.

Cada restriccion activa debe ser una condicion obligatoria: coincidencia de
cualquiera de sus prefijos **dentro** de esa restriccion, AND entre restricciones
y con `simbolosDefinidos`. Filtrar en el servidor todas las ramas (codigo, sparse,
resumen y consultas por simbolo), antes del corte, evita perder puestos por un
post-filtro de modulo. No promete eliminar los demas limites existentes de umbral,
empates o pools finitos de fusion, ni demuestra una mejora de recall.

Ventaja: contrato de frontera independiente de la tokenizacion full-text.
Coste: campo/indice nuevos, escrituras futuras y migracion de puntos existentes.
Preparacion para 1.21.1: campo de payload `module_prefixes`; parametro HTTP
opcional `modulos`, lista de 1 a 50 cadenas no blancas, con AND entre elementos.
Un solo modulo satisface el uso pedido; la lista permite expresar tambien la
interseccion de restricciones que el puerto de Rag ya admite, sin inferir permisos.
Ausente/null conserva la busqueda actual; lista vacia o elemento blanco responde
400, no una busqueda ampliada silenciosamente. No normalizar mayusculas ni recortar
valores no blancos. El limite de lista sigue el patron de `simbolosDefinidos`.
La ingesta deriva el campo; el consumidor no envia ni mantiene etiquetas.

### (b) Match.Text mas comprobacion posterior - no elegido

Reutiliza namespace/ruta y no obliga a rellenar payload. Conserva el mecanismo
aproximado del origen, pero hereda resultados incompletos tras el corte y depende
del servidor. La evidencia **historica** de 1.2, no una medicion nueva, encontro
subcadena sensible a mayusculas en Qdrant 1.14.1 frente a tokens sin distinguir
mayusculas en 1.19.2 (`V\docs\evaluaciones\1.2\decision.md:75-87`).
La copia de `tools\vectorstore-eval\Harness\Model.cs:224-234` y
`QdrantStore.cs:117-134` es un banco sintetico, no el endpoint productivo actual.
No recomendar este mecanismo como nuevo contrato exacto ni como autorizacion.

### (c) Etiqueta explicita de ingesta - no elegida

Sirve si el dominio necesita modulos independientes de namespace/ruta, pero
**cambia** la semantica: exige asignar etiquetas, definir su responsable y ampliar
todos los origenes de ingesta afectados. No hay evidencia para inventar ese mapa.
La etiqueta aportada por el consumidor tampoco es prueba de permiso de acceso.

### No incluir modulo en la primera integracion - no elegido

El responsable pidio conservarlo para pruebas. Por tanto, no se elimina de la
primera integracion por falta de uso actual. El principio de no omitir restricciones
sigue vigente para los filtros/contextos aun no soportados: rechazarlos
explicitamente, nunca ampliar la busqueda ni caer silenciosamente al pipeline local.

## Puntos ya indexados y rollback

En (a) o (c), modificar solamente el upsert no completa colecciones existentes:
`V\src\Reyma.VectorSpace.Engine.Microservice.Infrastructure\Ingestion\DocumentIngestionPipeline.cs:215-244`
omite documentos con la misma huella. Su cabecera incluye contrato, opciones,
modelo y dimension (`:283-300`). Cambiar `ChunkingContract.Version` o esa huella
fuerza revectorizacion al reingestar; **no es una migracion automatica** y no se
justifica fingir un cambio de chunking solo para agregar un dato derivado.

Para (a), se preparara un relleno explicito con `SetPayload`, paginado,
idempotente y reanudable, sin TEI/LLM ni cambios en IDs, vectores, hashes, resumenes
o huellas. Coordinar con escritores: un upsert antiguo puede quitar el campo;
un relleno sobre datos leidos antes de otra ingesta puede dejarlo desactualizado.
Definir version/completitud verificable y comportamiento explicito mientras
falten campos; no presentar una migracion incompleta como "cero coincidencias".
1.21.1 debe responder 409 a busquedas con modulo en una coleccion existente sin
esa preparacion completa, antes de vectorizar. Una coleccion inexistente sigue
respondiendo 200 sin resultados; sin filtro se conserva el comportamiento actual.
La comprobacion de completitud debe cubrir el indice y todos los puntos, y solo
publicarse tras finalizar el relleno bajo exclusion efectiva de escritores.
Para (c), primero hace falta el mapa autorizado de etiquetas historicas.

Cualquier futura ficha debe probar en coleccion aislada puntos nuevos, antiguos,
documentos sin cambios, interrupcion/reanudacion y escrituras concurrentes o su
exclusion efectiva. La aplicacion a colecciones servidas requiere aprobacion
separada, respaldo recuperable y rollback de datos, no solo de codigo.
Un campo/indice aditivo puede quedar inerte tras revertir el lector/escritor;
si se exige eliminarlo, acotar la limpieza a ese campo/indice o restaurar una
copia verificada. No borrar colecciones como rollback automatico.

Esta ficha es exclusivamente documental: revertir su commit deshace la nota y
las consecuencias registradas en el ledger; no altera contratos ni colecciones.

## Autorizacion y los otros filtros

Mantener 1.14 significa que el servicio no infiere que modulo pertenece a un
usuario. Si Rag debe restringirlo, Rag tiene que resolver identidad/autorizacion
y aplicar el contexto **en todas las ramas y antes de consumir resultados**
(semillas, reranking, contexto del LLM, fuentes y respuestas).
Un filtro opcional de VectorSpace nunca protege frente a otro cliente con token
valido que lo omita: conservar autorizacion en Rag protege su interfaz, no convierte
el acceso directo a VectorSpace en multi-tenant. Si ese acceso debe restringirse,
hay que reabrir 1.14 y preparar su propia decision/implementacion; no basta un campo.

Lenguaje y namespace quedan **fuera de 1.21.1**, como brechas de compatibilidad
para 1.21.2, no como capacidades eliminadas.
`R\src\RagEngine.Core\Infrastructure\VectorStore\QdrantSemanticRetriever.cs:685-705`
usa `Match.Text` para ambos. La descripcion de namespace dice "prefix", pero el
codigo no hace la comprobacion de frontera del modulo: no equipararlos sin decision.
Tenant, acoplado a autorizacion, usa keyword exacto y AND entre filtro/contexto
(`:707-743`); el contrato de VectorSpace no lo porta. La CLI los expone, aunque
no se conozca su uso. Para 1.12 se debe decidir por filtro si se conserva, cambia
con consentimiento o difiere con rechazo explicito. No declarar paridad total
solo porque search/ask no tengan esos parametros en el cuerpo HTTP.

## Aceptacion literal y consecuencias para 1.12

| Punto de `verificacion` | Estado |
|---|---|
| 1. Semantica conservada o cambiada, con motivo | Se conserva namespace/ruta ordinal con fronteras, para mantener la capacidad solicitada de acotar pruebas. |
| 2. Mecanismo (a), (b) o (c) | Elegido (a): prefijos keyword, por exactitud previa al corte; (b) y (c) no elegidos con motivos arriba. |
| 3. Tratamiento de documentos existentes | SetPayload paginado/idempotente, sin TEI/LLM ni cambio de huella; completitud/exclusion y rollback exigidos en 1.21.1. |
| 4. Frontera de autorizacion | Filtro opcional, no permiso; 1.14 se conserva. Rag sigue siendo responsable de sus restricciones; acceso directo no queda aislado por modulo. |
| 5. Lenguaje y namespace dentro/fuera | Fuera de la implementacion de modulo; compatibilidad, junto con tenant, preparada en 1.21.2 antes de 1.12. |

La aceptacion es **documental**, no una medicion de recall ni prueba del filtro
implementado. La respuesta del responsable y los cinco puntos anteriores permiten
cerrar 1.21 una vez verificados documento, fichas nuevas y ledger. La validacion
del JSON por `arnes` no sustituye esa revision ni acredita paridad funcional.

Las fichas de implementacion/migracion estan registradas antes del cierre.
1.21.1 exige:
fronteras positivas/negativas anteriores y casos adversariales que llenaban el
top-K con falsos candidatos; filtros combinados; denso/fusion/resumen/simbolos;
paridad de IDs/orden/scores sin filtro sobre el mismo dataset; migracion completa
sin revectorizar y sin alteracion de los demas datos; error explicito si falta
infraestructura o cobertura en la comprobacion. Lenguaje/namespace/tenant y una
eventual reapertura de 1.14 necesitan fichas propias si 1.21.2 determina trabajo
adicional; este cierre no las autoriza de antemano.

1.18, 1.20 y 1.22 ya estan hechos; no se reescriben sus resultados. No bastan para
dar por lista la integracion: 1.12 queda `bloqueado_por` 1.21.2, que requiere
1.21.1 hecho y verificara su contrato real antes de preparar la compatibilidad.
Hay aristas literales, no solo notas `_...`. El objetivo
cliente completo no autoriza una sustitucion de solo embeddings ni ejecutar 1.12
en esta conversacion. Al retomarla, tambien debe respetar el trabajo `19.6`
en_curso de Rag, su baseline vigente, presupuesto real y colecciones aisladas.

El relevo inmediato es **1.21.1-implementar-filtro-exacto-de-modulo** en una
conversacion nueva, despues de cerrar 1.21. 1.7.2 conserva sus condiciones externas;
1.12 no se ejecuta hasta satisfacer todas sus condiciones.

### Comprobaciones del bloqueo inicial (e8cf0bb; historicas)

En invocacion separada de la edicion, `arnes.cmd validar` valido los 32 items
(28 hechos, 3 bloqueados, 1 descartado). `arnes.cmd validar --al-cerrar
1.21-decidir-filtro-de-modulo-en-busquedas` devolvio 0 pero informo expresamente
que el item esta bloqueado y **no cerro**. `arnes.cmd --solo-anunciar` devolvio 1:
no hay siguiente pendiente. Se uso arnes 1.17.4 (017e7d9); su wrapper local emite
errores de cmd sobre comentarios antes de ejecutar el arnes. No se corrigio ese
problema ajeno ni se confundio su codigo de salida con aceptacion de la decision.
El SHA256 del appsettings.json local de VectorSpace permanecio identico al inicial.

### Comprobaciones tras resolver la decision

Revision manual de los cinco puntos de la matriz, las fichas nuevas y sus
dependencias, en invocacion separada de la edicion. Se verifico que el criterio y
rollback originales no cambiaron y que el resultado parcial se conserva literalmente
en `_resultado_parcial_e8cf0bb`.

`arnes.cmd validar` valido 34 items (29 hechos, 2 pendientes, 2 bloqueados,
1 descartado). `arnes.cmd validar --al-cerrar
1.21-decidir-filtro-de-modulo-en-busquedas` confirmo cierre con resultado.
`arnes.cmd validar --item 1.21.1-implementar-filtro-exacto-de-modulo` confirmo que
la ficha esta preparada; se anuncio su ID sin ejecutarla y se comprobo que es
el primer pendiente, con su prerrequisito hecho. 1.12 conserva su bloqueo.

`git diff --check` de los archivos documentales sin errores; appsettings.json
de VectorSpace conserva su SHA256 inicial y Rag mantiene sus cambios ajenos
intactos. No se ejecutaron pruebas de producto, servicios ni ingestas: se verifica
una decision documental, no la implementacion futura del filtro.
