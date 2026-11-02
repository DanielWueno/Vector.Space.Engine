# 1.21 - Modulos en busquedas: decision pendiente del responsable

Fecha: 2026-11-02. Estado: **bloqueado, no aprobado ni implementado**.
Base Vector.Space.Engine: `7c32bfe43251379f33e7678d2b7950e0de9e0d3f` (1.22 hecho).
Base Rag.Context.Engine, solo lectura: `aae1de8`.

## Resultado y limite de la decision

El codigo permite describir la capacidad y comparar mecanismos, pero no demostrar
si los usuarios emplean `module` o `FilterByModule`. Esa confirmacion es condicion
expresa de reentrada de la ficha. Se solicito al responsable durante esta sesion;
no estuvo disponible. **Las opciones y valores sugeridos de la consulta no son
respuestas ni autorizaciones.**

El usuario si fijo el objetivo posterior: Rag.Context.Engine sera cliente completo
de VectorSpace para ingesta y busqueda, conservando orquestacion y generacion RAG.
Eso no decide por si solo la politica de autorizacion ni permite eliminar filtros.
1.12 queda bloqueada por 1.21, no ejecutada. No se cambia 1.14.

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
El uso real de filtros sigue **no confirmado**, no "inexistente".

## Alternativas tecnicas, no seleccionadas

### (a) Prefijos de frontera calculados en la ingesta - recomendacion condicionada

Si se decide conservar modulo como filtro de busqueda, la alternativa recomendada
es derivar un array keyword de los prefijos de namespace/ruta, incluyendo cada
valor completo. Preservaria la comparacion ordinal, los separadores respectivos y
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
El nombre del campo y el contrato HTTP no se fijan sin elegir esta alternativa.

### (b) Match.Text mas comprobacion posterior

Reutiliza namespace/ruta y no obliga a rellenar payload. Conserva el mecanismo
aproximado del origen, pero hereda resultados incompletos tras el corte y depende
del servidor. La evidencia **historica** de 1.2, no una medicion nueva, encontro
subcadena sensible a mayusculas en Qdrant 1.14.1 frente a tokens sin distinguir
mayusculas en 1.19.2 (`V\docs\evaluaciones\1.2\decision.md:75-87`).
La copia de `tools\vectorstore-eval\Harness\Model.cs:224-234` y
`QdrantStore.cs:117-134` es un banco sintetico, no el endpoint productivo actual.
No recomendar este mecanismo como nuevo contrato exacto ni como autorizacion.

### (c) Etiqueta explicita de ingesta

Sirve si el dominio necesita modulos independientes de namespace/ruta, pero
**cambia** la semantica: exige asignar etiquetas, definir su responsable y ampliar
todos los origenes de ingesta afectados. No hay evidencia para inventar ese mapa.
La etiqueta aportada por el consumidor tampoco es prueba de permiso de acceso.

### No incluir modulo en la primera integracion

Solo es una alternativa si el responsable acepta expresamente ese alcance.
En tal caso, el adaptador debe rechazar el filtro o contexto restringido no
soportado, nunca omitirlo, convertirlo en una busqueda sin restriccion ni caer
silenciosamente al pipeline local. Conservar la capacidad futura con condicion de
entrada; no eliminarla por no haber demostrado uso actual.

## Puntos ya indexados y rollback

En (a) o (c), modificar solamente el upsert no completa colecciones existentes:
`V\src\Reyma.VectorSpace.Engine.Microservice.Infrastructure\Ingestion\DocumentIngestionPipeline.cs:215-244`
omite documentos con la misma huella. Su cabecera incluye contrato, opciones,
modelo y dimension (`:283-300`). Cambiar `ChunkingContract.Version` o esa huella
fuerza revectorizacion al reingestar; **no es una migracion automatica** y no se
justifica fingir un cambio de chunking solo para agregar un dato derivado.

Para (a), se recomienda preparar un relleno explicito con `SetPayload`, paginado,
idempotente y reanudable, sin TEI/LLM ni cambios en IDs, vectores, hashes, resumenes
o huellas. Coordinar con escritores: un upsert antiguo puede quitar el campo;
un relleno sobre datos leidos antes de otra ingesta puede dejarlo desactualizado.
Definir version/completitud verificable y comportamiento explicito mientras
falten campos; no presentar una migracion incompleta como "cero coincidencias".
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

Lenguaje y namespace entran aqui como **brechas identificadas**, no como cambios
aprobados. `R\src\RagEngine.Core\Infrastructure\VectorStore\QdrantSemanticRetriever.cs:685-705`
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
| 1. Semantica conservada o cambiada, con motivo | Origen contrastado; eleccion del responsable pendiente. |
| 2. Mecanismo (a), (b) o (c) | Alternativas y recomendacion documentadas, ninguna aprobada. |
| 3. Tratamiento de documentos existentes | Coste, riesgo y alternativas definidos; depende de 2. |
| 4. Frontera de autorizacion | Estado de 1.14 comprobado; necesidad de restriccion real pendiente. |
| 5. Lenguaje y namespace dentro/fuera | Brechas documentadas; alcance final pendiente, no suprimido. |

La aceptacion completa **no se satisface**; 1.21 no se marca hecho. La validacion
del JSON por `arnes` no sustituye esas decisiones ni acredita paridad funcional.

Antes de desbloquear, el responsable debe confirmar (sin aportar secretos):
uso real de module/FilterByModule, comportamiento deseado del modulo en el cliente
completo, frontera de autorizacion y alcance de lenguaje/namespace/tenant.
Si desconoce el uso, puede declarar expresamente el contrato que exige conservar
y aceptar esa incertidumbre; el agente no puede asumir esa respuesta.

Despues de elegir, **crear en el ledger, antes de cerrar 1.21**, las fichas de
implementacion/migracion que esa eleccion requiera, con dependencias, presupuesto,
criterio literal y rollback. No se crean ahora fichas ejecutables de un mecanismo
no aprobado. Como minimo, si se elige (a), su criterio debe cubrir:
fronteras positivas/negativas anteriores y casos adversariales que llenaban el
top-K con falsos candidatos; filtros combinados; denso/fusion/resumen/simbolos;
paridad de IDs/orden/scores sin filtro sobre el mismo dataset; migracion completa
sin revectorizar y sin alteracion de los demas datos; error explicito si falta
infraestructura o cobertura en la comprobacion. Lenguaje/namespace/tenant y una
eventual reapertura de 1.14 necesitan fichas propias si quedan trabajo adicional.

1.18, 1.20 y 1.22 ya estan hechos; no se reescriben sus resultados. No bastan para
dar por lista la integracion: 1.12 queda `bloqueado_por` 1.21 y debera incorporar
las dependencias nuevas, no solo notas `_...`, cuando se aprueben. El objetivo
cliente completo no autoriza una sustitucion de solo embeddings ni ejecutar 1.12
en esta conversacion. Al retomarla, tambien debe respetar el trabajo `19.6`
en_curso de Rag, su baseline vigente, presupuesto real y colecciones aisladas.

No hay otro item automaticamente ejecutable en el ledger actual: 1.7.2 conserva
condiciones externas sin satisfacer y 1.12 depende de esta decision. El relevo
inmediato es **resolver y retomar 1.21**, no iniciar otro item de producto.

### Comprobaciones ejecutadas

En invocacion separada de la edicion, `arnes.cmd validar` valido los 32 items
(28 hechos, 3 bloqueados, 1 descartado). `arnes.cmd validar --al-cerrar
1.21-decidir-filtro-de-modulo-en-busquedas` devolvio 0 pero informo expresamente
que el item esta bloqueado y **no cerro**. `arnes.cmd --solo-anunciar` devolvio 1:
no hay siguiente pendiente. Se uso arnes 1.17.4 (017e7d9); su wrapper local emite
errores de cmd sobre comentarios antes de ejecutar el arnes. No se corrigio ese
problema ajeno ni se confundio su codigo de salida con aceptacion de la decision.
El SHA256 del appsettings.json local de VectorSpace permanecio identico al inicial.
