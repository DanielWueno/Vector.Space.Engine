# 1.12 - Scope alignment and client acceptance

Authoritative plan: `docs\plan\ejecucion-plan.estado.json`, parent
`1.12-decidir-si-rag-context-engine-pasa-a-cliente`.
**Quality remains blocked. H3 search passed; primary answer acceptance did not.**

## CURRENT: README-aligned ownership and remaining work

This documentation-only correction follows the user's scope question and
English-language request on 2026-11-04. Entry commits were rector `41b0833` and
client `9722d66`; permitted paths were clean. Effective model: `gpt-6-astra`;
requested effort: `xhigh`, effective effort not observable. Estimated phase
cost: **0.75 machine-hours**, not measured consumption. No subagents, product
changes, new acquisition, services or deployment.

Client documentation and its coordination note are committed together in
`a63d752fd53315618f072c5883979bc2c1ce9cf7`. This is a documentation-only commit,
not a new measured product version. DWeno author/committer and the trailer-free
message were verified; no amend or push.

### Scope correction

The prior breakdown incorrectly treated a VectorSpace service client as a
presentation-only application. The original README/client contract retains RAG
orchestration in the client. **Items1.12.1 through1.12.6 are now discarded,
unexecuted expansion proposals**, not completed migration work. Their contents
and estimates remain historical; the parent no longer depends on1.12.6.
Future expansion is not prohibited forever, but requires its own explicit
architecture decision. This is not a decision to abandon the existing remote
ingestion/search client.

| Owner | Responsibilities under the current contract |
|---|---|
| VectorSpace | Ingestion, chunking, embeddings, Qdrant/index lifecycle, indexed business summaries and native code/summary search with filters. |
| Rag.VectorSpace.Client | Final fusion, symbol-hop orchestration, reranking/calibration, meta-intent, confidence gate, context/prompts, answer generation/history/streaming/sanitization, API/CLI and presentation. |

Native dense/sparse fusion is not the client's final fusion. Summary generation
during ingestion is not conversational answer generation. Both READMEs now
make these distinctions. No new engine migration is required merely because
the client still implements its intended RAG responsibilities.

### Client-owned pending tasks, tracked only in the rector ledger

| ID | Deliverable | State and dependency |
|---|---|---|
| `1.12.7-complete-source-oracles` | Complete116+29 source-grounded oracles, including every material condition and positive/negative/incomplete/contradictory scenarios. | Pending; no dependency on the discarded migration. |
| `1.12.8-implement-acceptance-risk-evidence` | Implement and substantiate the46 real risk cases and compatible evidence producer. | Pending; parser tests do not satisfy these cases. |
| `1.12.9-implement-supplementary-evaluation` | Implement the separate resumable29-question/two-round producer. | Pending; actual232-response supplementary acquisition belongs to acceptance. |
| `1.12.10-diagnose-effective-ask-context` | Compatible representation/screen/resumption and observed-ask diagnosis,360 HTTP responses. | Blocked by1.12.7 and its real runtime/provenance gates. |

Each task has a literal criterion, risks and rollback in the ledger. Task7
requires substantive source review, not just a passing citation parser.
Task8 must exercise actual composition, not replace the retriever or certify
test names. Task9 must demonstrate the actual producer and interrupted/resumed
behavior; synthetic checks are not supplementary quality evidence.

Tasks7/8/9 can be prepared concurrently in separate owned files and isolated
resources. Task10 can follow7 on a pinned immutable diagnostic build while
independent development continues elsewhere. Serialize shared Fixture entry
points, checkpoint/verifier changes and ledger integration; no concurrent index
or WAL writers, automatic fan-out, or whole-worktree copies of excluded
configuration. One eligible task per new conversation. The client's inherited
162 items, including19.6, remain outside this work.

The parent has a literal dependency on10, but closure also requires the other
deliverables and **actual integral acceptance evidence**. Arnes does not enforce
multiple dependencies through underscore notes. Keep the parent blocked until
all requirements pass; no automatic closure after a diagnostic or producer.

### Engine-side and cross-project pending work

The existing engine capabilities required by the Local client are implemented,
not globally quality-certified by this statement. Integration tests may expose
an engine defect; record a scoped, evidence-backed fix if that happens rather
than assuming another migration is necessary.

The other existing pending items were read but not changed:

- **1.7.2:** Azure DevOps ingestion automation, conditional on its separately
  authorized deployment/pipeline access. Its old `ControlDeAcceso` entry
  condition predates removal in1.14 and needs reconciliation before execution.
- **1.21.5:** future tenant semantics, conditional on an explicit need and
  document-assignment source.
- **1.21.6:** future restricted-access design, conditional on explicit consumers,
  identity and protection requirements.

These do not block explicit Local1.12. Current HTTP authentication and rejection
of unsupported tenant/Authorized/Empresarial requests remain unchanged.

### Acceptance, provenance and limits

1.12 still needs complete source oracles, compatible final binaries and full
representation, search screening, real resumption,928 **new** primary HTTP
responses/464 answer judgments,29 supplementary questions/232 separate
responses, and all46 genuine risk receipts. After integration, old producer or
diagnostic build receipts are not automatically evidence for the final build.
Do not combine748 new responses with180 rejected-version responses.

The rejection remains45 pairs/180 responses, zero retrieval losses and two
adjudicated pairs. Tickets011 invented a Systems exception; both answers were
incorrect, not a control-correct regression. Docs011 favored the candidate.
No causal attribution to H3 is established. Preserve all known material source
conditions, judgments and labels; no new-blind-review claim or mechanical
verifier presented as a second semantic reviewer.

Existing time/AltheIA authorizations remain valid. The new preparation-task
estimates sum to18h, excluding final acquisition, not observed consumption or
parallel wall time. Discarded16h and historical42h figures are not new budgets.
Keep the original repository read-only, exclude its Properties, and absolutely
exclude the protected rector API configuration from enumeration/read/hash/copy/
staging/build. No build of the rector API project. Preserve Local rollback,
indexes, volume, frozen inputs/protocols, binaries and `quality-rejection.json`.

New active text is English. Required ledger keys/status values, stable IDs and
historical records retain their original form. Client README/instructions/note
are committed together; rector README/preparation/handoff/ledger together with
the actual client commit reference. No coauthor, amend or push.

## HISTORICAL: superseded full-RAG migration breakdown

Peticion del 2026-11-04T14:59:59-06:00: explicar que motor sigue en el cliente y
dejar el trabajo desglosado. **Solo planificacion; no se ejecuta la migracion.**
Bases comprobadas: rector `2da4c5d`, cliente `9722d66`, fuentes consultadas y
documentos permitidos sin cambios ajenos. Modelo efectivo `gpt-6-astra`, esfuerzo
efectivo no observable (`xhigh` solicitado); fase documental estimada **0.5 h**,
no consumo medido. Sin subagentes. Contrato del padre `opus/high` intacto.

**Ya en VectorSpace:** fragmentacion, embeddings, ingesta, indice, resumenes
indexados y busqueda nativa, incluida fusion denso/sparse. **Todavia en el
cliente:** fusion final codigo/resumen, salto por simbolos, reranking/calibracion,
meta-intent, confidence gate, seleccion/packing del contexto, prompts, llamada
al LLM, streaming/resiliencia y sanitizacion de la respuesta. No confundir
fusion nativa con fusion final ni embeddings remotos con decisiones remotas.
La tabla de fuentes del apartado de frontera mas abajo sigue siendo evidencia;
se releyeron las implementaciones actuales, no solo las notas.

Se registran **seis subfichas reales del ledger rector**, no una lista privada ni
notas que el arnes deba interpretar como trabajo ya realizado:

| ID corto | Entregable y propietario | Dependencias |
|---|---|---|
| 1.12.1 | Contrato versionado, puertos/DTO comunes, oraculo de porte y propiedad exacta de archivos. Un responsable. | Preparacion pendiente; concretar/aprobar alcance antes de producto. |
| 1.12.2 | Recuperacion compuesta en VectorSpace: fusion final, saltos, rerank y calibracion. | 1.12.1. |
| 1.12.3 | Grounding/contexto en VectorSpace: meta-intent, gate, resumenes/contexto y prompts. | 1.12.1. |
| 1.12.4 | Generacion en VectorSpace: LLM, historial, stream, resiliencia y sanitizacion. | 1.12.1. |
| 1.12.5 | Adaptador del cliente: HTTP/eventos/fuentes, sin volver a ejecutar motor; UI/CLI permanece. | 1.12.1. |
| 1.12.6 | Integracion de composicion real, observacion y contratos en ambos repositorios. Un integrador. | **Todos** 1.12.2, 1.12.3, 1.12.4 y 1.12.5. |

**Paralelismo viable:** terminado el contrato, 1.12.2/3/4/5 se desarrollan contra
sus puertos y fixtures congelados, sin esperar la implementacion de otro frente.
Cada ficha contiene su criterio literal de positivos, negativos y riesgos,
rollback y limites; no inventar el oraculo despues de ver resultados. El porte
mantiene comportamiento incluso donde la version actual es insuficiente:
**migrar no equivale a corregir la fabricacion ni a mejorar recall**.
Una correccion semantica o un cambio de referencia necesita su propuesta propia.

Arnes **1.17.4**, comprobado en el lector instalado, admite un ID literal en
`bloqueado_por`, no una lista ni un DAG. Por eso 1.12.2..5 quedan `bloqueado`
hasta cerrar el contrato; 1.12.6 tambien queda `bloqueado`. Su arista a 1.12.5
es solo una de cuatro: las otras constan en `_dependencias_adicionales` y
**requieren comprobacion manual de todos los estados/evidencias antes de
desbloquear**. Las notas no instalan esa puerta. 1.12.1 es la unica nueva ficha
`pendiente`; no se autoejecuta. El padre queda despues, `bloqueado` por 1.12.6
y por su rechazo de calidad. Ninguna dependencia va hacia delante.

La peticion no lanza agentes ni cambia `multiagente:false`: paralelismo entre
sesiones independientes, una subficha por conversacion. Antes de editar en
paralelo, preparar ramas/areas aisladas **sin copiar la configuracion excluida**;
no crear clones/worktrees completos del rector que la reproduzcan. Si no hay
aislamiento revisado, serializar. Los frentes no editan simultaneamente contratos,
DI/endpoints, Fixture/PairedEvaluation ni ledger: contrato pertenece a 1.12.1,
composicion/instrumento a 1.12.6 y avance del ledger a un coordinador. Integrar
commits por ficha, sin mezclar cambios ajenos; ninguna escritura concurrente en
indices/cache/WAL. No se crean ramas, worktrees o servicios en esta preparacion.

Estimacion inicial de contrato/porte/integracion: **16 h sumadas**, no tiempo de
pared al paralelizar ni presupuesto de aceptacion. No repedir autorizaciones de
tiempo/AltheIA existentes; concretar solo alcance nuevo de API/composicion.
Mantener exclusion absoluta del appsettings protegido y prohibicion de compilar
el proyecto API rector. El futuro camino de comprobacion debe usar composicion
real permitida y aislada; si no puede hacerlo, registrar bloqueo, no omitirla.

**Cierre de 1.12:** integrar los seis frentes no satisface la puerta funcional.
Siguen pendientes los oraculos116+29, screen/resume/diagnostico compatibles,
productores y evidencia de46 riesgos/29 suplementarias,928 respuestas nuevas y
464 juicios sustentados. Version migrada no completa748 con180 de la rechazada.
Conservar control Local, indices, volumen, artefactos, protocolos, juicios y
`quality-rejection.json`. No se reescribe ningun resultado historico ni otra
ficha existente; se agregan solo las subfichas de este trabajo.

## HISTORICO: instrumentacion autorizada y puerta integral offline

Continuacion autorizada el 2026-11-04T12:41:47-06:00, sobre rector `abc9bb9`
y cliente `1b57bf2`. El usuario confirma **destino cliente; motor en VectorSpace**,
autoriza captura y mecanismo de comprobacion, y pide continuar el verificador
integral. No se interpreta como permiso para retuning, otra hipotesis, corregir
respuestas a medida, cambiar el control o migrar silenciosamente mas producto.
`opus/high` sigue intacto; efectivo `gpt-6-astra`, esfuerzo efectivo no observable
(`xhigh` solicitado). Sin subagentes. Estimacion de fase **6 h**, no consumo medido;
tiempo/AltheIA previamente autorizados. No se reutiliza la estimacion historica42h.

Instrumentos guardados en cliente
`9722d6662f4e4f22811d5d34f83fdbf55522ace6`:17 archivos propios, incluida la nota
del ledger cliente; autor/committer DWeno, mensaje sin trailers y allowlist limpia
verificados. Estos dos documentos y el ledger rector se guardan juntos en su
commit coordinado. Sin amend ni push.

Esta fase implementa instrumentos y produce representacion offline nueva,
**no adquiere nuevas respuestas ni resuelve el rechazo semantico**. La ficha
vuelve a **bloqueado por calidad**, no a hecho. El destino cliente ya esta
confirmado: conservar Local es rollback transitorio, no decision de abandonar.
Otra decision documental de conservar pipeline requeriria que el usuario
revise expresamente ese destino; no la toma el agente.

### Instrumentos implementados en el cliente

Rutas relativas a `D:\Git\Rag.VectorSpace.Client`.

| Pieza | Comprobacion y limite |
|---|---|
| `IGenerationContextObserver` / `GenerationContextAssembler` | Puerto opt-in, ausente de la composicion normal. Observa entradas reales, resolucion de resumen y cada admision/descarte real, entrada con cabecera, conteos UTF16, contexto final y limite12000. No recompone contexto desde search ni cambia packing. |
| `Fixture\GenerationCapture.cs` | Decora servicios reales de retrieval/generacion solo en hosts de evaluacion. Correlacion por intento; scope async por avance del stream. Guarda retrievalask no redactado, decision de grounding, contexto, cuerpo JSON permitido efectivamente enviado al SDK, SSE consumido, terminacion y respuesta final. Trace incompleta/error/cancelacion no se acredita. |
| `OllamaHttpClientFactory` | Sobrecarga observadora que envuelve el transporte/TLS existente; conserva la firma anterior y la seleccion de credencial. No imprime headers, credenciales, CA, URLs ni configuracion completa. Resumenes conservan su propio Kernel. |
| `PairedEvaluation` | `--ask-diagnose` acota45 preguntas/dos rondas/360HTTP; exige representacion, screen y resume actuales antes de inferir. Guarda enlace/hash de cada trace dentro del par atomico. Primaria exige oraculo precongelado y sella codigo de verificadores/protocolos. Marcador de rechazo se comprueba primero. |
| `verify_ask_observation.py` | Recalcula packing, enlaza contexto real con mensajes enviados y respuesta HTTP, verifica SSE/budgets, compara brazos/repeticiones. `--oracle` permite comprobar citas literales en retrieval/resuelto/packing/mensajes. Una cita ausente NO demuestra ausencia de una parafrasis; declara preguntas sin oraculo. No es adjudicador semantico. |
| `verify_acceptance.py` | Puerta offline de representacion4cohortes, fuentes/indices/resumenes, binarios/proveedores/configuracion, screen, resume real,232pares/928HTTP, trazas, cobertura de condiciones en juicios,29extras y46 casos de riesgo desglosados de los padres. Ausencia, cambio, duplicados, skips/cero tests o rechazo fallan. No registra aceptacion por existir JSON. |

Prerregistro nuevo `protocol-ask-observation.json`; los padres permanecen
intactos. La especificacion detallada se completo durante desarrollo, **antes
de nueva medicion**, no se presenta como un nuevo ensayo cegado. Los bytes
capturados se limitan a corpus evaluativo y mensajes permitidos; no a trafico
general. La captura observa bytes consumidos por el SDK, no transformaciones
internas del proveedor ni paquetes que el SDK no llegara a consumir.
En prueba sintetica el SDK serializa el presupuesto2048 como
`max_completion_tokens`; se admite tambien `max_tokens`, nunca ambos a la vez.
Esto no cambia el valor ni reconstruye el payload historico.

### Resultado mecanico y limites comprobados

Comprobaciones manuales, separadas de edicion; no se atribuyen a hooks:

* Build del **Fixture permitido**, con `VectorSpaceRoot`, sin compilar el
  proyecto API rector:0 errores/advertencias.13 tests focalizados pasan,
  cero skips;25 tests Python pasan. Selftests de checkpoint29 y recovery8 pasan.
* `--observation-self-test` usa transporte sintetico en memoria: SDK real,
  igualdad de bytes/payload/respuesta, exclusiones de campos/headers,
  SSE fragmentado/cancelado, scope entre yields y captura completa/interrumpida.
  Compara packing observado con el **DLL historico preservado**: igualdad exacta.
  No acredita conectividad ni calidad de AltheIA.
* El selector inicial mas amplio tuvo18 fallos: goldens LF frente a CRLF de
  Windows, hashes de plantillas y teardown SQLite con archivo abierto. Se
  inspeccionaron diferencias concretas; no se cambiaron goldens/prompts,
  ni se presentan como una suite global verde. Comparacion con DLL medido
  y pruebas nuevas de nointerferencia pasan. Warnings de `CiFixtures/WarningFixture`
  siguen siendo los sinteticos preexistentes.
* Los lectores nuevos verifican la representacion y el resume historicos,
  **sin convertirlos en puertas de los DLL nuevos**. El verificador historico
  sigue devolviendo exit2:45pares/180HTTP,0perdidas,2adjudicaciones,
  fabricacion nueva tickets011, `regression=false`. La puerta integral devuelve
  exit1 ante el mismo marcador; no modifica ni reabre esa corrida.
* La entrada nueva `--ask-diagnose`, con representacion actual y screen/resume
  antiguos, rechaza `Diagnostic representation drift` antes de proveedores/
  adquisicion. No crea directorio de corrida; no se invoca la corrida rechazada.
* Nueva representacion **sin red/inferencia**:
  `.artifacts\1.12-ask-representation-r1`. Cuatro cohortes completas:
  bsuite2113/23221, auditorias311/2081, docs15/743, tickets553/1855
  documentos/chunks; igualdad de texto/enriquecido/lineas/IDs/nombre fisico.
  Relector independiente verifica fuentes, inventario completo y126 binarios
  actuales. Esto prueba representacion, **no recall ni aceptacion**.

Sellos de esta representacion y build:

```text
representation.json 402a3bb04a93c14a61d5e82131ed2710ced4f1dbc3fda1e30cd73bc42028479b
Fixture.dll         2e599c2537126149354644c6fd7ad0eb6db754cf00f9ee507711b33a24562e46
RagEngine.Core.dll  1e99c011ab0d1fa28674ce281d8e71139053b23c3e8dd5de034d37df71758616
RagEngine.Api.dll   d374f80fb649b13f4a51165af32f4caa38d07ac4ec316ade4a9b294659c9b9a7
```

No nuevas llamadas de embedding/LLM, ingestas, servicios, consultas a indices
vivos o cambios de tuneles/credenciales. El estado exited del Qdrant es la ultima
observacion historica, no una nueva comprobacion. Se conservan indices, volumen,
respuestas, binarios medidos y protocolos anteriores. El original y la ruta
protegida del API rector quedan fuera de lecturas/copias/build/staging.

Comparacion documental contra las bases:37 otras fichas/root rector y162
heredadas cliente intactas; resultado anterior conservado como prefijo y
secciones anteriores conservadas salvo rotulo VIGENTE->HISTORICO. Cuatro hashes
del rechazo permanecen iguales. `arnes validar` con rutas explicitas pasa
38/162 fichas; `--al-cerrar` informa bloqueado/no cerrado, no ejecuta aceptacion.
Avisos heredados del wrapper Windows/55 campos del clon quedan documentados.
Una coma JSON ausente fue detectada, corregida y revalidada antes del commit;
la consulta `--help` del wrapper no encontro ledger y no ejecuto un item.

### Evidencia que aun falta; no simular un verificador ya satisfecho

El agregador es ejecutable, pero **el expediente integral aun no existe**.
No se agrega `verificacion_comando` de cierre parcial al ledger.

1. Completar y revisar el oraculo estructurado de116+29 preguntas, contra
   fuentes completas, no solo anclas. Formato en `verify_acceptance.oracle`:
   pregunta/Note exactas, condiciones materiales con path/hash/lineas/cita,
   escenarios positivo/negativo/incompleto/contradictorio y politica explicita.
   `oracle` verifica estructura/citas; NO escribe ni certifica semanticamente
   que todas las condiciones hayan sido identificadas.
2. Obtener screenHTTP45 y **resume real nuevos** para el build instrumentado.
   El ejemplo viejo de7respuestas no basta por tener el mismo nombre.
   Despues diagnostico360HTTP separado, preservando todas sus respuestas y
   fallos. El rechazo historico no se reanuda ni se borran etiquetas conocidas.
3. Argumentar la causa con captura: condicion no recuperada, resuelta pero
   descartada, enviada pero omitida/contradicha, y diferencias entre mensajes.
   Mismos bytes/diferente salida solo muestran variabilidad operacional
   condicionada; no prueban RNG exclusivo. Headers distintos impiden asumir
   prompts iguales. **No se implemento agrupacion, H4 ni correccion generativa.**
4. Antes de intentar aceptar una version, ejecutar928HTTP nuevas,464 juicios
   de respuesta con todas las condiciones y enlaces ciegos/revelados. Si ambos
   brazos fabrican, una comparacion de afirmaciones previa a etiquetas debe
   distinguir fabricacion compartida de otra nueva; no basta dos booleanos.
   El verificador no es segundo revisor ni prueba criptografica de cegamiento.
5. Construir/adaptar productores de evidencia para los46 casos nombrados
   `V112AcceptanceRiskTests.*` del agregador: **esos casos integrales no se
   afirman implementados ni ejecutados**. Necesitan revision sustantiva de
   su oraculo/codigo, composicion real sin sustituir el retriever, TRX real
   no vacio/sin skips, binarios compatibles y evidencia cruda sellada.
   Un manifest con `passed:true`, nombres de tests o ejemplos sinteticos
   NO demuestra el riesgo; los tests del parser no son esos46 tests.
6. Productor separado para29 suplementarias congeladas (posiciones56..84
   del evalset bsuite, SHA fijado), two-hop activado en ambos brazos,
   dos rondas/232HTTP aparte. El agregador exige enlace al checkpoint primario,
   mismos indices/build y juicios completos. **No se ejecuta ni se acredita
   ese productor en esta fase.** Ninguna respuesta suplementaria cuenta en928.

Entradas reales implementadas, no comandos de cierre ya satisfechos
(desde el cliente, sustituyendo argumentos por evidencia compatible):

```text
Fixture.dll --ask-diagnose PREPARED REPRESENTATION SCREEN RESUME NEW-DIAGNOSTIC
verify_ask_observation.py RUN PREPARED [--oracle SOURCE-ORACLE]
verify_acceptance.py oracle PREPARED FULL-ORACLE
Fixture.dll --paired-h3 PREPARED REPRESENTATION SCREEN RESUME FULL-ORACLE NEW-PRIMARY
verify_acceptance.py accept RUN PREPARED REPRESENTATION SCREEN RESUME --oracle FULL-ORACLE --risks RISK-EVIDENCE --supplement SUPPLEMENT-RUN
```

Los argumentos son rutas, no evidencia existente por escribirlos. La forma
primaria antigua sin oraculo ahora falla antes de inferir; el marcador rechazado
sigue teniendo precedencia. Reentrada de un par exige su trace original.
No trasladar748 pendientes a una version nueva ni mezclar180 respuestas viejas.
Rollback: observador desregistrado por defecto, Local explicito, revertir solo
commits propios mediante commits nuevos; no revierte ni borra puntos indexados.

### Frontera cliente/motor: deuda de migracion registrada, no ejecutada

El protocolo historico llamaba "cliente completo" a delegar almacenamiento/
ingesta/embeddings pero **dejaba motor RAG en Rag**. La aclaracion del usuario
exige distinguir ese estado implementado del destino final, sin reescribir
la medicion ni prometer que ya todo corre en VectorSpace.

| Responsabilidad | Estado comprobado y migracion pendiente |
|---|---|
| Fragmentacion, embeddings, ingesta, indice/resumen indexado y busqueda nativa | Ya remotos: rector `Infrastructure\Ingestion\DocumentIngestionPipeline.cs:100,110,262`, `Vectorization\TeiVectorizationBrain.cs:20`, `VectorStore\QdrantSemanticRetriever.cs:71`. H3 presupuesto nativo esta alli. |
| Fusion final codigo/resumen, saltos por simbolos, reranking y escalas | Siguen ejecutandose en cliente `Infrastructure\VectorSpace\VectorSpaceSemanticRetriever.cs:48-85`; trasladar comportamiento/contrato/perfiles a VectorSpace con filtros/autorizacion por rama y sin doble fusion accidental. No declararlos migrados por usar HTTP debajo. |
| Meta-intent y confidence gate | Cliente `Services\Generation\SemanticMetaIntentDetector.cs:79-92` y `RagGenerationService.cs:126-176`; embeddings remotos no trasladan la decision. Migrar decision/calibracion/procedencia, sin retocar umbrales ahora. |
| Resolucion de contexto, presupuesto/packing, prompts y generacion | Cliente `GenerationContextAssembler.cs`, `RagGenerationService.cs:217-282`, `Infrastructure\Generation\ChatAnswerStreamer.cs`. Candidatos claros a motor VectorSpace, incluidos mensajes/terminacion/uso y observacion opt-in. La captura nueva no hace esa migracion. |
| Sanitizacion semantica de formato, resultado de grounding y fuentes | Hoy coordinados en Rag; separar politica de respuesta del renderer. Motor debe entregar resultado/fuentes/stream suficientes, cliente presenta/redacta segun contrato y no vuelve a recuperar para inventar fuentes. |
| UI/CLI, transporte, seleccion explicita de perfil/coleccion, estado/presentacion | Frontera propia del cliente. Mantener rechazos de capacidades no soportadas y autenticacion, no usar migracion para retirar guardas. Pipeline Local permanece solo como control/rollback durante transicion. |

La futura migracion necesita contrato de turno RAG, streaming/cancelacion/error,
compatibilidad de fuentes y perfiles, configuracion de proveedores en el dueno
correcto y medicion nueva sobre ambos endpoints. Cambia producto y potencialmente
API rector: **esta fase solo la anota dentro de1.12**. No toca otras fichas,
el archivo protegido, identidad corporativa ni despliegues. Exigir propuesta
de alcance/rollback concreta antes de implementarla; no pedir otra vez tiempo/
AltheIA ni confundir esta deuda con una aceptacion funcional.

## HISTORICO: diagnostico offline y rutas de cierre, sin nueva adquisicion

Preparacion del 2026-11-04, posterior a `fcfead0cebb69290eb64f50e2c929b3fc900c64c`
(rector) y `1b57bf2c2eb3906df903d284d6d0948d90bf92b9` (cliente).
Ambos eran HEAD al entrar; ramas `feature/1.12-h2-ingestion-compatibility` y
`feature/vectorspace-client`. Original HEAD `3c3972b90b74ee3d9cd7b86945e07d4775a9c965`,
consultado solo como metadato Git, sin leer su `Properties`.
Status/diff de entrada vacios en las rutas documentales permitidas; fuentes
leidas del cliente sin diferencias contra su base. No se inspecciona el cambio
ajeno del API rector: exclusion previa absoluta, incluso de enumeracion,
hashes, status/diff, builds y staging. No se afirma limpieza de todo el rector.

Solo esta ficha y estos documentos cambian. Modelo efectivo `gpt-6-astra`;
solicitado `gpt-6-astra/xhigh`, esfuerzo efectivo no observable; `opus/high`
del plugin intactos, sin subagentes. Estimacion de esta fase: **0.75 h maquina**,
no consumo medido. Las **42 h son historia**, no estimacion de esta fase.
Estado **bloqueado**, sin nueva corrida, inferencia, ingesta, servicio, cambio
de producto, H4 o retuning. Permisos anteriores de tiempo/AltheIA vigentes.

### Criterio literal y dos cierres que no son equivalentes

La `verificacion` literal de la ficha dice:

> Decision registrada (pasa a cliente o conserva pipeline propio, y con que alcance:
> total o solo el brain de embeddings) con motivo. Si pasa a cliente con paridad
> completa: /api/search y /api/ask de Rag.Context.Engine responden contra el servicio
> remoto con el mismo eval-set y la baseline vigente de ese repo
> (docs/eval/quality/5.e/), pareado, sin reingestar ni tocar colecciones servidas, y
> sobre el contrato ya ampliado por 1.18 (y 1.20 si aplica). Si el alcance es parcial,
> la paridad se mide solo sobre lo que efectivamente cambia de dueno (p. ej. los
> vectores densos).

La decision posterior de destino fue **cliente completo Local explicito** en la
bifurcacion, conservando orquestacion/generacion. La referencia nueva pareada con
modelos iguales fue autorizada; `protocol.json` conserva 5.e como historia, no
como baseline ask ni runtime comparable. `protocol-compatibility.json` cambia
representacion, `protocol-h3-search.json` cambia presupuesto nativo y
`protocol-h3-acceptance.json` coordina su medicion: ninguno elimina el criterio
semantico, los riesgos o la cobertura. Los parametros locales del protocolo
`protocol-local.pre-altheia.json` quedaron sustituidos antes de medir; sus
riesgos y reglas semanticas siguen heredados. No mezclar defaults Denso/1024 o
MiniLM historicos con H3 FusionNativa/512 y Snowflake/AltheIA.

| Ruta | Condicion concreta de cierre; ninguna se cumple en esta fase |
|---|---|
| Aceptar cliente completo | Version identificada y autorizada; todas las puertas y aceptacion integral de abajo. Registrar alcance Local y limites, evidencia, resultado y ledger juntos. No equivale a desplegar ni borrar el original. |
| Decidir conservar pipeline propio | **Decision expresa del usuario**, con motivo y alcance, que revise la direccion de migracion ya elegida. Registrar que se cierra una decision, no que el cliente paso search/ask. Conservar rechazos, original y opcion futura sin inventar aceptacion. Preparar control documental especifico para esa ruta, sin disfrazarlo de verificador funcional. |

Un cambio a solo embeddings seria otra decision expresa y requeriria medir esa
parte; no es una salida automatica. "Conservar Local" en el rechazo significa
**estado operativo/rollback mientras no hay aceptacion**, no abandono de la
migracion decidido por el asistente. Las dependencias 1.4, 1.18, 1.19, 1.20,
1.21/1.21.1/.2/.3/.4 y 1.22 estan hechas por lectura actual. El bloqueo es
calidad/decision pendiente, no esas dependencias ni un nuevo permiso de coste.

### Evidencia comprobada: caso completo y contraejemplo favorable

Rutas de esta seccion relativas a `D:\Git\Rag.VectorSpace.Client`.
Se leyeron los cuerpos search/ask de ambos brazos y ambos casos, los juicios
y revelaciones originales y las fuentes congeladas; **etiquetas ya conocidas**.
Esto es diagnostico posterior, no otra revision cegada ni segundo revisor.

El verificador existente se ejecuto manualmente, offline, en invocacion separada:

```powershell
python -B docs\eval\quality\1.12\verify_h3_primary_rejection.py .artifacts\1.12-h3-primary-r1 .artifacts\1.12-20261103-r3 .artifacts\1.12-h3-representation-r6 .artifacts\1.12-h3-http-r2 .artifacts\1.12-h3-resume-r1
```

Resultado **exit 2 / NOT ACCEPTED**: 45 pares, 180 respuestas, cero perdidas
retrieval, dos pares adjudicados, una fabricacion nueva y ninguna transicion
control-correcto -> candidato-incorrecto en esos dos juicios. Comprueba
integridad, mascaras y correspondencias, no semantica independiente ni causalidad.
No se ejecuta adquisicion, ni se borra `quality-rejection.json`.

**tickets011:** pregunta "Que condiciones deben cumplirse para poder reasignar un
ticket?"; la Note pide las reglas ricas de `OnValidate`, no solo la unica ancla
retrieval. Fuentes congeladas bajo `.artifacts\1.12-20261103-r3\corpus\tickets`:

| Fuente | Hecho relevante |
|---|---|
| `src\Reyma.TI.Tickets.Microservice.Business\Commands\ServiciosTI\Tickets\ReasignarTicketCommandHandler.cs:134-162` | Todos los guards enumerados en el oraculo de abajo; SHA256 `fada19ed7a01cd22422c626d45c12638111fc981cb0dbc84fdacddcb469505a0`. |
| Mismo handler:161-162 | Fuera de **estatus general MesaDeAyuda**, actor = asignado actual **o** departamento del actor = departamento del asignado actual. Pertenecer a Sistemas NO es una tercera alternativa. |
| `docs\Guia-pruebas-envio-correos.md:62-85` | Prueba de reasignacion y negativos; "no es de Sistemas" describe indisponibilidad de la accion. No convierte ser de Sistemas en condicion suficiente para saltar los otros guards. |
| `src\Reyma.TI.Tickets.Microservice.Api\Controllers\ServiciosTI\TicketsController.cs:214-219` | Accion protegida por politica de reasignacion. La fuente del controller no demuestra por si sola un override por grupo Sistemas. |
| Handler:115-122,175-177 | El correo se intenta despues del commit; su fallo se registra. Efecto: nuevo responsable, estatus general Proceso e interno Enviado. No exigir entrega efectiva del correo como prerrequisito. |

La respuesta candidata primero enuncia bien asignado/departamento, pero luego
agrega literalmente:

> Users from other departments (or those without the ticket assigned) generally
> cannot perform this action unless they are part of the IT Systems team (as
> implied by the negative test case where non-Systems users don't see the option).

La contradiccion interna no cancela la excepcion inventada. El control no afirma
esa excepcion. Ambos omiten area/departamento/centro de trabajo no dados de baja;
el control omite ademas AutorizacionInvalida. La lectura completa observa tambien
que ninguno explicita el GUID no vacio del nuevo asignado: **observacion
diagnostica adicional**, no reescritura del juicio sellado. No basta eliminar
"unless ... Systems" para obtener una respuesta completa. La candidata responde
en ingles a una pregunta en espanol, pese a la regla de idioma: observacion de
seguimiento de instrucciones, no nuevo umbral de rechazo inventado a posteriori.

**docs011 favorable:** el control omitio que debe existir encuesta vigente;
el candidato si lo incluyo. Fuente congelada
`corpus\docs\Encuesta de Satisfacción al Cierre de Ticket.md`,
RN-3.1:317 y negativo CA-3.1.7:363-366; SHA256
`a6ae1d3da41c348d4577c2c284c04bfb501e08a80435269082c28000ae855ce4`.
Se mantienen juicio y par: no compensan el contraejemplo universal de tickets011,
pero impiden describir un deterioro generativo uniforme del candidato.

### Traza del codigo medido: search no es el prompt de ask

DLL preservadas en `.artifacts\1.12-h3-http-r2\measured-binaries`:
`RagEngine.Core.dll` SHA256
`92333ac199aedb813a5b2878aff1a58cf30bfde9fd649453a55dd9f67c13ab91`;
`RagEngine.Api.dll`
`f6bd883d952fe7d8c0cec42d11aa28888e6121bb0ba36f9229d926c3e026cdc5`.
Ambas coinciden byte a byte con las dependencias actuales del Fixture. Lectura
offline de PE/PDB: identificadores coinciden; SHA256 de los documentos PDB
coincide con los **11 archivos** de la traza: RagGenerationService,
GenerationContextAssembler, SystemPromptComposer, SimpleSystemPrompt,
ConfidenceGate, ChatAnswerStreamer, ambos SemanticRetriever, RetrievedSummary,
Program de Rag API y Contracts. No se recompilo ni cargo la API del rector.
Esto liga codigo a binarios, **no recupera entradas runtime no registradas**.
Fixture medido `022abe94...` y final `b26292ee...` siguen distintos.

| Paso | Codigo y evidencia; alcance exacto |
|---|---|
| Adquisicion | `Fixture\PairedEvaluation.cs:245-263`: search y luego ask por brazo son requests separados; topK10, minScore.1, rerankfalse, Simple, historia vacia. H3 prefetch40/transporte100, sin resumen ni two-hop en docs/tickets. |
| Retrieval | Local `QdrantSemanticRetriever.cs:140-187`: fusion nativa sin vector resumen. Remoto `VectorSpaceSemanticRetriever.cs:41-73`: fusion nativa recibida y RRF cliente de una rama `1/(60+rango)`, que conserva ese orden y cambia puntuaciones. |
| Observacion real | En tickets011 y docs011 los diez SourceDto de search son identicos por contenido, archivo, seccion, lineas, resumen y orden, salvo score. No es solo igualdad del hit del ancla. |
| Entrada de ask | `RagEngine.Api\Program.cs:794-887` y `RagGenerationService.cs:126-176`: valida, aplica meta-intent, recupera otra vez, evalua gate y emite ContextReady **antes** de ensamblar. No reusa el array search. |
| Gate | `ConfidenceGate.cs:85-137`: RRF no se compara con bandas absolutas calibradas para rerank. Que el score candidato sea ~.016 no prueba baja confianza ni causa un fallback por .05. |
| Resolucion de contexto | `GenerationContextAssembler.cs:56-133`, `RetrievedSummary.cs:8-15`: Simple usa resumen cuando existe; cobertura >=.5 y al menos uno cubierto excluye chunks sin resumen; cobertura cero conserva raw. Null remoto es autoritativo, no toma cache local. |
| Orden y presupuesto | `GenerationContextAssembler.cs:145-212`: orden de entrada, cabeceras con score F2 y repositorio; limite **12.000 caracteres UTF-16**, no tokens. Omite entero un chunk que no cabe y sigue con menores; no corta su texto ni garantiza continuidad de una regla. |
| Prompt | `SystemPromptComposer.cs:51-79`: Simple gana sobre familia codigo/docs. `SimpleSystemPrompt` exige fundamentacion, sintesis de todos los fragmentos e idioma de la pregunta; no contiene una excepcion para Sistemas. |
| Generacion | `ChatAnswerStreamer.cs:50-80`: sistema + historia + pregunta, temperatura.1, top_p.95, max_tokens2048, sin seed. Mismo generador/proveedor declarado en ambos brazos; no es determinista por tener temperatura baja. |
| Salida | `RagGenerationService.cs:247-282` aplica sanitizador de formato Simple. `SimpleAnswerSanitizer` transforma codigo/identificadores, no valida reglas de negocio. API publica fuentes de ContextReady, no lista de chunks realmente admitidos en el prompt. |

En tickets011 search contiene, en ambos brazos:

| Rango | Fuente abreviada / rango declarado del chunk | Significado |
|---|---|---|
| 1 | Guia:62-85 | Caso de correo, negativos y mencion de Sistemas. |
| 2 | Reasignar handler:154-173, OnValidate | Guard asignado/departamento; raw incluye contexto previo. |
| 3 | Handler:50-54 | Constructor. |
| 4 | Guia:136-144 | Tabla y resumen de validaciones. |
| 5 | ValidarReasignar handler:92-107 | Otra operacion, no reemplaza OnValidate de reasignar. |
| 6 | TicketsController:149-151 | Otra accion. |
| 7 | Handler:123-131, OnSave | Cola del envio de correo, no OnValidate. |
| 8 | CustomContainer:18-36 | Composicion, no requisitos de reasignacion. |
| 9 | Handler:132-157, OnValidate | Bajas, GUID vacio, estatus general e inicio del guard interno. |
| 10 | TicketsController:214-220 | Accion y permiso de reasignar. |

Los rangos del chunk no equivalen a recortar esas lineas del archivo: raw contiene
solape/contexto padre. El oraculo cita el **archivo congelado**, no deduce reglas
solo del rango anunciado. Raw del rango9 contiene las condiciones ausentes.

**Simulacion condicional, NO captura de ask:** aplicar el algoritmo leido a los
search preservados, con metadatos de sus snapshots, CRLF de cabeceras Windows,
resumenes nulos y sin cambiar orden, da:

| Caso/brazo | Longitud final UTF-16 | Chunks omitidos |
|---|---:|---|
| tickets011 control | 11337 | rango9: antes=10516, entrada=2104, separador=7; no cabe |
| tickets011 candidato | 11274 | rango9: antes=10460, entrada=2097, separador=7; no cabe |
| docs011 control | 11098 | rangos9 y10 |
| docs011 candidato | 10906 | rangos9 y10 |

En tickets011 el guard161-162 del rango2 SI cabe en esa simulacion: la omision
del rango9 no explica por si sola la excepcion inventada para Sistemas. La
cabecera tambien cambia: scores control .83/.50/... frente a candidato .02/...;
repositorio `my-repo` frente a vacio, porque el mapper remoto no lo inventa.
En docs011 cambia analogamente el nombre de repositorio.
Por tanto **igual contenido/orden search no significa prompt byte-identico**.

En los ask de tickets011 hay diez posiciones; solo las dos fuentes de prosa
conservan contenido, las ocho de codigo tienen file/content/lineas nulos por
`SourceDto.Redacted`, no por prueba de descarte del contexto. En docs011, todo
prosa, contenido/archivo/orden de las diez fuentes ask coincide con search en
ambos brazos; aun asi son fuentes anteriores al empaquetado. Resumenes visibles
nulos, cohortes sin resumen e indices completos apoyan la simulacion, pero no
sustituyen una captura del resultado de ResolveContextChunks de ese request.

**No registrado en la evidencia revisada:** lista completa no redactada del
retrieval de cada ask de tickets011, decisiones efectivas de admision, contexto
final, mensajes/payload realmente enviados al proveedor, respuesta previa al
sanitizador, finish_reason, usage, seed/fingerprint/revision de pesos y posibles
transformaciones/truncamiento internos del proveedor. max_tokens2048 limita
salida, no demuestra truncamiento efectivo; el limite local de contexto no es
la ventana declarada del modelo. No reconstruir esos datos como certeza.

### Hipotesis falsable y propuesta previa a cualquier cambio

**Hipotesis de composicion a contrastar, no nueva hipotesis implementada:** el
empaquetado compartido por chunk puede separar condiciones de una misma regla
que SI fueron recuperadas. Prediccion: una futura captura de ask con estos
fragmentos muestra el guard161-162 admitido y las bajas/GUID del rango9
descartadas por presupuesto en ambos brazos. Se refuta para ese turno si todas
esas condiciones llegan al mensaje efectivo, o si el retrieval de ask nunca
las obtuvo (otro fallo de recuperacion, no de empaquetado).

La fabricacion tiene una pregunta causal separada: transformar un negativo de
la guia en excepcion suficiente puede ser un fallo de sintesis del generador
compartido, una influencia de cabeceras diferentes, o variacion del muestreo/
servicio. **No se ha aislado ninguna.** Si entradas efectivas identicas generan
veredictos distintos, se demuestra variabilidad operacional condicionada a esa
entrada, no automaticamente su causa estocastica exclusiva. Dos repeticiones
no estiman una tasa fiable. Si cambian mensajes, scores, repositorio o revision
del proveedor, no llamar a esa diferencia "solo azar". El resultado favorable
docs011 debe entrar en el contraste, sin seleccionarlo o descartarlo segun convenga.

**Primer cambio propuesto: instrumentacion opt-in de evaluacion, no solucion
semantica.** En el cliente/harness, registrar por request/attempt:
identidad de run/brazo/pregunta/ronda; retrieval no redactado (IDs, raw hashes,
orden, escala y metadatos); resumen resuelto o null/origen; cada admision/descarte
y presupuesto; contexto final; plantilla, opciones efectivas y mensajes finales
del conector; respuesta cruda y final, terminacion/usage si el proveedor los
expone. Sellar bytes, no solo nombres. Una lista cerrada de campos y almacenamiento
local de evaluacion; **nunca headers auth, tokens, certificados, configuraciones
completas o corpus enviado a otro servicio**. Capturar al borde del transporte
permitido para distinguir lo armado por Semantic Kernel de lo realmente enviado;
lo que haga internamente el proveedor seguira no observable.

Sin captura nueva, no afirmar reproducido el turno historico. Probar primero
offline que el observador no altera contenido/orden/payload y que missing trace
falla; observar ambos brazos con el mismo instrumento. Propuesta de diagnostico
acotado futuro: 45 preguntas completas docs/tickets, cuatro negativas, dos rondas
y ambos endpoints/brazos = **360 respuestas HTTP nuevas**, no aceptacion primaria.
Conservar todos los resultados y el orden del protocolo, sin reintentar un
veredicto desfavorable. No ejecutar esta propuesta bajo la autorizacion de
diagnostico **offline** de esta conversacion.

**Unica intervencion de producto candidata si se confirma el descarte:** agrupar
para el empaquetado los fragmentos ya recuperados de la misma unidad semantica
(identidad por fuente/ruta, clase y metodo; seccion documental cuando exista),
priorizar el grupo por su mejor rango original y ordenar sus fragmentos por
linea con desempate estable. Admitir el grupo completo o registrar su descarte,
sin cambiar 12.000, topK, corpus, anclas, modelos, prompts, scores ni retrieval.
Sin identidad fiable, conservar comportamiento y marcar cobertura desconocida;
no unir metodos homonimos de archivos/clases distintos ni asumir que el nombre
distingue sobrecargas: la identidad de unidad debe ser verificable antes de
implementar, no inventarse a partir del caso observado. "Grupo completo" solo
significa todos sus fragmentos recuperados, NO todo el metodo original.
No recuperar o insertar la fuente correcta por ID tickets011, nombre de handler
o contenido esperado. Riesgo: un grupo grande desplaza otras evidencias o no
cabe; debe medirse y poder refutar la propuesta, no esconderse con un presupuesto
mayor. Esta propuesta NO garantiza impedir la fabricacion de Sistemas.

El contraste causal seria 2x2: Local/remoto H3 x empaquetado vigente/agrupado;
instrumentacion comun, mismo retrieval capturado por celda, mismos prompts y
generador. Primero comparar cobertura offline sobre entradas capturadas; solo
con alcance de producto autorizado medir respuestas. Otra variante completa de
45x2x4 supondria **otras 360 HTTP**, no repetir hasta obtener verde. Informar
efecto de empaquetado dentro de cada backend, no atribuirlo a H3. Para la
**aceptacion de migracion**, el control sigue sin ese cambio de comportamiento
y el candidato lleva su version declarada: no mejorar ambos silenciosamente.
Si se optara por arreglar generacion comun en ambos brazos, se necesita decision
explicita de nueva referencia y protocolo distinto, no contabilizarla como
aislamiento del efecto de migrar. Ninguna de estas variantes se implementa aqui.

### Oraculo previo del eventual cambio, incluidos negativos

Se conserva el criterio de veredictos de los protocolos. Una respuesta
"correcta" a tickets011 debe cubrir **todas** las condiciones materiales de
OnValidate, no solo el ancla:

| Condicion obligatoria | Positivo / negativo aislado, con las demas satisfechas |
|---|---|
| Area, departamento y centro del ticket no Baja (134-141) | Tres activos / cada uno Baja por separado: rechazar. No sustituir por estado del departamento destino. |
| Nuevo PersonalAsignadoId no vacio (143-144) | GUID valido / GUID.Empty: rechazar. |
| Estatus general no Cancelado, Atendido ni AutorizacionInvalida (146-147) | General permitido / cada excluido: rechazar. |
| Estatus interno Recibido o MesaAyuda (149-150) | Cada permitido / Enviado o Activo: rechazar. No confundir interno con general. |
| Asignado nuevo distinto del actual (152-153) | Distinto / mismo: rechazar. |
| Empleado destino existe y pertenece al departamento elegido (155-157) | Coincide / inexistente o otro departamento: rechazar. |
| Fuera de general MesaDeAyuda, actor asignado actual O de su departamento (159-162) | Cada alternativa valida; actor distinto y departamento distinto: rechazar **aunque sea de Sistemas**. |
| Excepcion exacta MesaDeAyuda | Exime solo el ultimo guard; no exime bajas, GUID, estados, distinto asignado ni pertenencia del destino. |
| Permiso de la accion (controller214-219) | Permiso presente / ausente: no afirmar autorizacion por cumplir reglas de datos. No inventar reglas de la clase base no examinada. |

Para docs011: positivos Ticket Atendido y Solicitud Proyecto/Equipo Entregado
con encuesta vigente; negativos terminal sin encuesta vigente y no terminal con
encuesta vigente. Debe conservar esa condicion y no compensar tickets011.
En todas las preguntas, aplicar Question/Note y fuentes completas requeridas:
un hit de ancla o respuesta fluida no acredita las condiciones restantes.

Riesgos previos del instrumento/intervencion: evidencia de reglas partida entre
chunks; fragmento obligatorio fuera de presupuesto; grupo demasiado grande;
solapes o huecos que impiden declarar exhaustividad; guia parcial junto a guard
explicito; fuentes realmente contradictorias sin prioridad documentada; resumen
que omite una condicion; null remoto y cache local contradictoria; ausencia de
contexto. No inferir el reciproco de un negativo ni resolver una contradiccion
inventando una excepcion. Si falta evidencia, declarar limite/abstencion, no
afirmar regla completa; **abstenerse no cuenta como correcto** en una pregunta
contestable. Una mera eliminacion de la frase Sistemas, dejando bajas/GUID u
otras omisiones, no demuestra que la intervencion soluciono el defecto propuesto.
Los fixtures de estos riesgos son suplemento diagnostico, nunca sustituyen o
modifican las 116 preguntas, anclas, 29 suplementos o umbrales congelados.

### Aceptacion integral preparada, NO ejecutada

1. Fijar objetivo de la siguiente sesion y alcance nuevo; conservar esta corrida
   rechazada con su marcador. Nueva version/nueva identidad y directorio, sin
   migrar sellos. Guardar binarios/deps/runtimeconfig y hashes de codigo, protocolos,
   instrumentos, corpus, configuracion permitida, prompts, modelos y proveedores.
   Revisiones de pesos no expuestas siguen desconocidas, no equivalen al commit TEI.
2. Repetir representacion **completa en cuatro cohortes** con los binarios que se
   mediran: documentos/chunks 2113/23221, 311/2081, 15/743, 553/1855 en la evidencia
   vigente; auditorias solapa bsuite. Exigir todos los bytes/path/raw/enriched/
   lineas/ID y nombres logicos/fisicos, no aceptar solo esos conteos.
3. Antes de inferir, comprobar procedencia de indices y finalizacion de ingesta/
   resumen, payload/vectores/esquema completos. Reusar indices compatibles no
   autoriza reutilizar respuestas. Docs/tickets sin resumen; bsuite/auditorias
   con resumen en ambos brazos. Contar misses reales por brazo antes de consumir,
   caches aisladas; memoria candidata no durable, un escritor por WAL.
4. Diagnostico HTTP H3 vigente: 45 preguntas/4 negativas/90 search nuevas, ambas
   vistas, cortes1/3/5/10 y verificador; cualquier perdida any/full@10 rechaza.
   Nueva DLL, incluso la de guarda final, exige nueva representacion/screen.
   Probar reanudacion **real** con binarios actuales: ingesta resumida1/2,
   interrupcion3/4, reacquisicion atomica4/4 y posterior reuso sin HTTP;
   lock exclusivo, identidades independientes, negativos de sello/ausencia/
   corrupcion/404/parcial. No usar la corrida rechazada para esa prueba.
5. Primaria nueva: **116 preguntas (104 ancladas+12 negativas), dos rondas,
   232 pares de cuatro respuestas = 928 HTTP**; 55 bsuite,16 auditorias,26 docs,
   19 tickets. Ronda1 docs/tickets/bsuite/auditorias, control-candidato;
   ronda2 inversa, candidato-control; search-ask por brazo. Preservar cada
   intento/latencia/error. Cero perdidas any/full@10 por pregunta/ronda/vista;
   reportar tambien1/3/5/10/categorias. Nunca compensar una perdida con promedios.
6. Adjudicar las **464 respuestas ask / 232 pares** con afirmaciones, omisiones,
   contradicciones, abstencion, fabricacion y citas completas antes de revelar
   etiquetas nuevas. Una revision de estas etiquetas reveladas no se vuelve
   cegada al ocultarlas otra vez: declarar exposicion y, para afirmar nueva
   ceguera, separar un revisor no expuesto a esos mapeos. No prometer segundo
   revisor semantico si solo se corre un verificador. Toda regresion o nueva
   fabricacion rechaza; faltantes/indeterminados impiden aceptar.
7. Las **29 suplementarias**, two-hop y negativas, se congelan/revisan por ID,
   peticion, expectativa y evidencia por brazo, separadas del denominador116.
   Si el ejecutor no tiene un manifiesto/verificador de esa cobertura, debe
   construirlo antes de adquirir; no inventar que 928 ya las incluye.
8. Exigir evidencia compatible para **cada** riesgo obligatorio de los padres:

| Riesgo heredado | Evidencia necesaria para cierre |
|---|---|
| Composicion real API y CLI/core | Empresarial+remoto rechazado antes de catalogo/query/embeddings/rerank/LLM, sin reemplazar el retriever probado por fake; contadores cero y no fallback. |
| Contextos no soportados | Todos los Authorized, incluso admin/claims vacios, y tenant activo rechazados antes de consumo. |
| Filtros en toda rama/salto | L/N textuales y modulo exacto; conjuncion imposible, namespace ausente, ruta sola, mayusculas/tokens/fronteras, candidatos ajenos de mayor score fuera de semillas, reranker, LLM y fuentes. |
| Autenticacion HTTP | VectorSpace autenticado; no quitar JWT ni usar credenciales corporativas adicionales. |
| Resumen y presentacion | Remoto autoritativo incluso null/cache vacia o contradictoria; modos Simple/Technical/prosa y streaming/fuentes. |
| Ranking y limites | TopK1/100, pools/MinScore incompatibles, desempates, escalas, rerank y two-hop; no convertir RRF en coseno. |
| Ingesta/estado | Idempotencia, cambio/borrado de documento de prueba, omitidos, pendientes, fallo, cancelacion,429/Retry-After y reinicio/404, sin parciales exitosos. |
| Errores de transporte | Red y400/401/403/409/503 visibles, no exito vacio ni fallback. |
| Rollback real | Seleccion explicita del backend Local retenido, sin afirmar que revierte puntos indexados. |
| Suplemento | Los29 casos presentes y separados, sin sustituir primarios ni riesgos. |

**748 no es el trabajo reutilizable de una version corregida.** Es solamente la
diferencia aritmetica928-180 del run rechazado. Otra version debe adquirir sus
**928 respuestas primarias completas nuevas**, incluidos los45 pares anteriores.
Tampoco contar el screen, resume o diagnostico360 como primaria. Solo una corrida
nueva no rechazada puede reanudar pares completos con identidad/binarios/
protocolo exactamente compatibles; las interrupciones parciales conservan
intentos pero no suman cobertura.

**Falta verificador integral.** `PairedEvaluation` se niega a cerrar incluso al
llegar a928; `verify_h3_search.py` solo screen y
`verify_h3_primary_rejection.py` solo rechazo. No hay `verificacion_comando`
integral en la ficha y no se agrega uno ficticio. Construir un agregador que:
valide procedencia/cobertura exacta, ingestas/resumenes, representacion, screen
y resume; recalcule mascaras desde HTTP; enlace cada juicio/cita/mapeo con sus
respuestas; exija manifiestos completos de29 y de **todos** los riesgos y sus
resultados ejecutados. No puede decidir semantica por existencia de un JSON.
Debe fallar ante duplicados, mezcla de builds, tamper, missing, cero tests,
skips requeridos, estado ambiguo, pares/adjudicaciones incompletos y cualquier
rechazo. Probar esos negativos antes de registrarlo como comando real. Su salida
debe distinguir evidencia parcial/rechazo/aceptacion, nunca tratar exit0 de un
subverificador o de `arnes validar` como aceptacion del conjunto.

### Coste, autorizacion, rollback y entrega de esta fase

Estimacion propuesta, no consumo: instrumentacion/fixtures offline **1-2 h**;
diagnostico nuevo acotado **1-2 h por variante de360HTTP**, mas preflights/screen/
resume que correspondan. Una eventual intervencion general de empaquetado y sus
tests: **2-4 h**, condicionada al hallazgo y autorizacion de alcance. No son
limites ni reservas ejecutadas. Aceptacion integral: reestimar con misses y
throughput reales de ambos brazos; las40/42h y18612misses previos no son coste
actual probado. Registrar consumo real disponible y desconocidos por separado.

Permiso genuinamente nuevo: captura local detallada opt-in de contexto/payload
permitido y eventual cambio de empaquetado del cliente, con finalidad, datos,
ambos brazos y rollback descritos; esta conversacion autoriza **prepararlos,
no ejecutarlos**. No requiere ampliar servidor ni modelo; cualquier ampliacion
distinta se decide aparte. No pedir otra vez tiempo, AltheIA, H3 o las ingestas
aisladas ya autorizadas. Una decision de conservar pipeline requiere decision
del usuario, no consentimiento presupuestario.

Rollback de la preparacion: nuevo commit que revierta solo sus adiciones
documentales si se solicita; no borrar historia. Eventual instrumentacion/
empaquetado: opt-in desactivado y reversion por nuevo commit, Local explicito,
sin fallback. Preservar indices, volumen, caches, artefactos, protocolos y
`quality-rejection.json`; codigo no revierte puntos. No hubo conexiones nuevas
ni comprobacion viva de Qdrant en esta fase: su estado `exited` es el ultimo
recibo historico, no observacion renovada. Consumo de proveedor de esta fase:
**cero nuevas llamadas**; consumo/horas del asistente no observables.

La entrega es esta preparacion y ficha bloqueada juntas en commit documental,
DWeno autor/committer, sin coautoria, amend o push. No es cierre funcional ni
decision de abandonar la migracion.

Control documental manual separado: comparacion JSON contra `fcfead0` conserva
las otras37 fichas, campos de raiz e historia completa de1.12; resultado anterior
solo recibe un apendice. Los documentos historicos se conservan salvo rotulo
VIGENTE->HISTORICO. Marcador de rechazo, checkpoint, evaluation y resultado H3
mantienen sus SHA256; documentos/protocolos/ledger del cliente sin cambios.
`git diff --check` acotado pasa. `arnes 1.17.4 validar` pasa38fichas y
`--al-cerrar` informa **bloqueado, no cerro**; no ejecuta aceptacion. Una coma
JSON ausente se corrigio antes de repetir satisfactoriamente estos controles;
avisos preexistentes del wrapper Windows no se alteraron.

## HISTORICO: proveedor recuperado; H3 primaria rechazada por fabricacion nueva

Continuacion 2026-11-04 tras el reintento solicitado por el usuario. Bases:
rector `0407d270`, clon `de578473`. Solo1.12, sin subagentes. Modelo efectivo
gpt-6-astra, xhigh solicitado y esfuerzo no observable; opus/high intactos.
El GET8081/info respondio200 en0.016s con los cinco metadatos congelados.
No se modificaron SSH, tuneles, credenciales ni modelos.

El diagnostico H3 paso **45 preguntas/4 negativas/90 respuestas nuevas**,
sin perdidas pareadas any/full@10, en ambas vistas y todos los cortes.
Despues de prerregistrar `protocol-h3-acceptance.json`, se adapto el coordinador:
gate H3, adopcion de identidades independientes de control/candidato y pruebas
originales de ingesta/indices completos. El cambio de binarios obligo a nueva
representacion completa r6 y nuevo screen HTTP r2, tambien45/90 sin perdidas.
No se migraron respuestas ni sellos. La reanudacion real H3 resumida paso:
ingesta1/2, interrupcion3/4, adquisicion atomica4/4 y reuso sin HTTP.

La primaria produjo **45 pares/180 respuestas HTTP**, ronda1:
26docs y19tickets, sin perdidas retrieval. No obstante, la revision cegada de
**tickets-microservice-011** detecto una fabricacion nueva del candidato:
afirma que pertenecer a Sistemas permite saltar el requisito de estar asignado
al ticket o pertenecer al departamento del asignado. El guard literal de
`ReasignarTicketCommandHandler.cs:161-162` no tiene esa excepcion fuera de
MesaDeAyuda. El control no la inventa. **Ambas respuestas son incorrectas**
por otras omisiones: el rechazo es por **fabricacion nueva**, NO por
control-correcto -> candidato-incorrecto. Los juicios y citas se guardaron
antes de revelar etiquetas; hashes/mapeos/cuerpos reales fueron comprobados.
El otro par adjudicado, docs011, favorece al candidato y tambien se conserva.
Un unico revisor sustentado; no revision semantica independiente ni causalidad
atribuida al ranking H3.

Se solicito detener el proceso tras el rechazo y se paro Qdrant propio.
El primer recibo capturo scan1667/started; el runner posteriormente persistio
scan2113/failed y finalizo a17:41:41.776268Z. **La herramienta de parada no
demuestra terminacion inmediata del proceso hijo.** No hubo nuevos pares.
BSuite candidato quedo404, sin coleccion creada;18612 misses se contaron antes
del scan. No se acredita ingesta completa, regeneracion masiva ni consumo
facturado total. Los cuatro indices docs/tickets se compararon enteros al
final y permanecen intactos. Qdrant quedo `exited`, volumen conservado.

Resultado versionado: clon `docs\eval\quality\1.12\h3-primary-result.json`.
Commit coordinado del clon `1b57bf2c2eb3906df903d284d6d0948d90bf92b9`,
10archivos de1.12 con instrumentos y ledger, autor/committerDWeno sintrailers.
SHA256 del resultado localCRLF
`6e0c6f06fab4fd97cecb699ad60b56bf33080e4da4328b325a194f273f67a397`;
blobGitLF `6086d490fe0177680e302d8b5362825c187008ad99a71c67d5432f126bd46e59`.
Comprobados identicos salvo normalizacion de finales de linea de Git.
El commit rector que contiene este bloque preserva el rechazo, no acepta1.12.
Artefactos completos: `.artifacts\1.12-h3-primary-r1`, incluidos revisiones,
revelaciones, marcador de rechazo y reconciliacion de parada. Verificador:

```powershell
python docs\eval\quality\1.12\verify_h3_primary_rejection.py .artifacts\1.12-h3-primary-r1 .artifacts\1.12-20261103-r3 .artifacts\1.12-h3-representation-r6 .artifacts\1.12-h3-http-r2 .artifacts\1.12-h3-resume-r1
```

Invocacion real separada: exit2, rechazo explicito de aceptacion.
`--evidence-only` verifica solo la evidencia del rechazo y sale0; ninguno de
esos modos acredita928HTTP,29extras, adjudicacion completa o todos los riesgos.
Build aislado,88tests de cliente sin skips,29+8 aserciones,15tests Python,
23casos HTTP mas401 y negativos de entrada pasaron; no sustituyen ese criterio.
Avisos heredados de tests conservados.

La guarda final `quality-rejection.json` impide reentrar antes de red o cambios
de salida; probada contra la corrida real sin alterar checkpoint.
DLL medida `022abe940f64dee2078728c89f1531bc93df5cf5004c13dd13f808427e899c56`,
126binarios preservados en HTTP-r2. DLL final de guarda
`b26292ee07233510272fc7cd7ce295fbcf6a414cf53fed798a27d086f1454169`;
125dependencias iguales. No se afirma medicion nueva con la DLL final.

No reintentar para borrar el fallo, retocar criterio/corpus/anclas/modelos ni
iniciar H4. Eventual investigacion causal requiere alcance e hipotesis/oraculo
previos; conservar permisos existentes sin pedirlos otra vez y obtener solo
los que exija alcance genuinamente nuevo. Original, otras fichas y archivo API
protegido intactos; sin push/amend. Historial e incidencias anteriores debajo.

## HISTORICO: reentrada de metadatos sin respuesta; bloqueo conservado

Continuacion 2026-11-04, bases rector `1875aa21` y clon `4cd0916e`.
Solo1.12, sin subagentes ni cambios de producto. Una consulta acotada a
`GET http://127.0.0.1:8081/info`, iniciada a las `16:30:07.746603Z`,
termino con `TimeoutError` tras **15.016s**, exit1, sin respuesta HTTP.
Sin reintentos, redirecciones, reparacion de tunel ni sustitucion de modelo.
No se repitio `--h3-search-diagnose`: el prerrequisito sigue ausente.

Coinciden siete SHA256 del relevo: propuesta, protocolo ejecutable, manifiesto
de representacion, DLL actual y preservada, recibo de indices y bloqueo anterior.
Las diez dependencias/insumos siguen hechos. Qdrant propio se inspecciono solo
por identidad, imagen, volumen y puertos: coinciden y sigue `exited`.
No se arranco ni consulto; no se repitieron comparaciones de indices en vivo,
builds, representacion o tests de producto. **0 nuevas respuestas search/ask,
embeddings, llamadas LLM o AltheIA. H3 sigue sin medicion ni veredicto de calidad.**

Recibo nuevo, sin alterar los resultados anteriores:
`D:\Git\Rag.VectorSpace.Client\docs\eval\quality\1.12\h3-search-result.json`,
campo `metadata_reentry_20261104`. Ambos ledgers conservan **bloqueado**.
Modelo efectivo gpt-6-astra, xhigh solicitado pero esfuerzo no observable;
opus/high intactos.42h restantes estimadas ya autorizadas, no consumo ni limite.
Reentrada solo tras restauracion independiente del proveedor existente por via
autorizada; no sondeos en bucle. Las puertas y comandos completos siguen debajo.

Incidencia registrada: el primer status/diff--stat se lanzo en paralelo a la
lectura inicial del relevo e incluyo la ruta protegida y avisoLF/CRLF antes de
conocer la exclusion. No se mostro su contenido ni hash explicito; no se copio,
compilo la API, preparo ni incluyo ese archivo en un commit. Operaciones
posteriores con allowlist y exclusion explicita. No afirmar exclusion total
de aquella enumeracion. Original y cambios ajenos preservados.

Clon persistido en `de578473591c97bd99850df91845e18f53de7b92`: solo el
resultado H3 con recibo nuevo y la nota/resultado del ledger, DWeno autor y
committer, sin coautoria ni push. El commit rector contenedor coordina
preparacion, relevo y ficha; no es cierre funcional. `arnes validar` con rutas
explicitas paso en ambos ledgers; avisos heredados Windows/esquema intactos.
Comparaciones JSON y diff acotado conservan historia, otras fichas y original.

## HISTORICO: H3 autorizada e implementada; diagnostico real bloqueado por proveedor

Continuacion 2026-11-04, bases rector `c6d5b87` y clon `b7944b3`.
Commit coordinado del clon: `4cd0916e19eb91db6dfd705d554059719b32aecd`.
La autorizacion expresa POSTERIOR del usuario resuelve exclusivamente el
alcance de `protocol-h3-search.proposed.json`, SHA256
`61967d0ee7a4fd8f121cf524fdc7fa68ca3f6a64a99642fba8614663e7ad29af`.
Se conserva la consulta historica sin respuesta. Antes de producto se registro
`protocol-h3-search.json`, SHA256
`512a6d89ce63e98e785e081ca42ab80bfbfa9b44d543c2f994c78db2c518304b`,
y solo1.12 paso a `en_curso`. Ahora vuelve a **bloqueado**, no hecho.

Producto: `prefetchPorRama` opcional entero JSON estricto40..1200, solo
codigo/FusionNativa; DTO/query/puerto/controller/motor. Omitido/null conserva
`max(4*topK,40)`. Opt-in cliente `VectorSpace:UseRetrievalBudget=false`,
solo primaria: `max(4*B,40)`, B=TopK o3*TopK con reranking. Resumen y saltos
no envian override. Pool/transporte100 y TopK publico1..100 no garantizan100:
dos prefijos40 permiten como maximo80 distintos. Sin relleno, cambio de escala,
reduccion silenciosa de TopK ni modificacion del control.

Comprobaciones manuales, no hooks: build aislado0warnings/errors,88tests de
VectorSpace del clon,23casos HTTP mas401 con0consumo de dependencias, fixture
autenticado HTTP/Qdrant sintetico con f32/UUID/40/1200/defaults/retorno/filtros
antes de candidatos y cada salto,540contrastes offline del fusionador real,
autopruebas23/8/10 y3tests del verificador HTTP. El fixture sintetico no usa
Snowflake/AltheIA ni acredita calidad; dos fallos iniciales de su propio harness
se corrigieron sin modificar producto, corpus, ranking ni umbrales.
Los dos dobles de tests del servidor solo recibieron el parametro de puerto:
no se compilo el proyecto API protegido ni se ejecuto su suite dependiente.

Representacion completa actual antes de embeddings: bsuite2113/23221,
auditorias311/2081 (solapan),docs15/743,tickets553/1855. Manifiesto
`.artifacts\1.12-h3-representation-r4\representation.json`,
SHA `a7bed7d4bff27402809e66bd360ce77bc324f0bfc80ed8f01358e2d9a9e54eae`.
Los cuatro indices docs/tickets se compararon enteros con los snapshots
historicos: esquema,payload,vectores,modelo,nombres e ingesta/resumen completos,
no solo poblacion. Sin reingesta;0misses de resumen en ese diagnostico sin resumen.

El comando REAL `--h3-search-diagnose` se ejecuto en otra invocacion:
el puerto8081 acepta TCP, pero `GET /info` no responde y agoto120.390704s.
Se detuvo antes de consultas/embeddings. **0/45 preguntas y0/90 respuestas
search nuevas: H3 no medida, ni aprobada ni rechazada por perdida pareada.**
No procede ejecutar la aceptacion116x2/928HTTP,29extras,adjudicacion/riesgos.
El verificador independiente falla con exit1 por ausencia del sello HTTP.
No se sustituyo modelo, modifico tunel ni solicito de nuevo permisos vigentes.

Resultado reproducible completo del clon:
`docs\eval\quality\1.12\h3-search-result.json`.
Recibos: bloqueo `b5cee2b5c74097f41bb8192fb63f0518e079d568b536ef28f29ea309ebfecf46`;
indices `66296b95105b5e89ec092c3bcb05fc0482f8e0b9bd7bbaddfc21711361bcf11a`;
DLL `105b41753dc1ce47c66eda768ebb0fa7ad1d75ecbc3450a24f4a52bc09cdadee`.
Binarios medidos preservados bajo `.artifacts\1.12-h3-http-r1\measured-binaries`.
Evidencia local no versionada: conservarla; el JSON versionado no sustituye los
snapshots, cuerpos HTTP ni DLL. Qdrant propio se verifico antes de arrancar y se
detuvo despues (`exited`), conservando volumen, indices y fixtures sinteticos.

Consumo nuevo conocido:0embeddings de proveedor,0LLM,0AltheIA. Total maquina
real y total historico facturado desconocidos. Restante provisional42h:
2h instrumentos/HTTP/runner mas40h aceptacion condicional, no limite ni consumo.
Modelo efectivo gpt-6-astra; xhigh solicitado, esfuerzo efectivo no observable;
opus/high del plugin intactos, sin subagentes.
`arnes validar D:\Git\Vector.Space.Engine\docs\plan\ejecucion-plan.estado.json`
paso con exit0:38items,4bloqueados,1descartado,33hechos. Version instalada1.17.4;
el wrapper Windows emite avisos de parseo preexistentes y no se modifico.
Esta validacion del ledger no ejecuta ni sustituye aceptacion funcional.

Reentrada: disponibilidad del proveedor existente con metadatos congelados,
sin arreglar tuneles dentro de este alcance. Verificar binarios/recibos y
repetir representacion si cambian; completar45HTTP. Solo si pasan, adaptar la
puerta del coordinador primario a H3 (sigue especifica de H2), demostrar resume
real de cuatro respuestas con binarios actuales y ejecutar TODA la aceptacion.
No activar cliente como aceptado ni H4. Rollback: opt-infalse,Local explicito,
auxiliares propios detenidos, indices/controles/volumen/evidencias preservados.
Volver a H2 no la acepta; revertir codigo no revierte puntos.

## HISTORICO: H3 causal offline y permiso entonces pendiente

**H3-C sostiene el mecanismo de presupuesto en la cohorte congelada; H3-P no
esta implementada ni aceptada.** La consulta de permiso especifico de busqueda
no obtuvo respuesta. 1.12 queda bloqueado por ese alcance nuevo y por calidad
pendiente. H2 sigue RECHAZADA; las secciones posteriores conservan la historia.

Antes del instrumento/medicion se registro en el clon
`docs\eval\quality\1.12\protocol-h3-budget-diagnosis.json`, SHA256
`bfe40fda47953e9386ce064faa7866e0bf138f89b096d744edc67f200bb3768e`.
Se congelaron hipotesis, metodo2x2 (40/400 en ambos brazos), predicciones y
refutaciones, criterio, representacion H2, corpus/anclas/modelos y rollback.
No se busco un parametro ganador ni se retoco tickets014.

El presupuesto por rama NO equivale a union fusionada, resultados devueltos,
pool cliente o TopK publico. En H2, transporte100 deriva prefetch400 por rama;
controlK10 deriva40. Aumentar solo el corte de una lista ya ordenada no cambia
su top10; ampliar las ramas SI puede anadir contribuciones a miembros presentes.

Tras comprobar2873recibos, cuatro representaciones completas y snapshots con
schema/modelo/nombres/estado de ingesta/resumen, el contraste45preguntas,
4negativas,ambasvistas,@1/3/5/10 reproduce H2 exactamente. Las cuatro
predicciones pasan: a igual presupuesto coinciden los45top10 y todas las
mascaras. El cambio40->400 altera el orden top10 en31preguntas de cada brazo.
Con400, ambos brazos pierden014 any/full@10 y013 any/full@3; con40 el
candidato no difiere en ninguna mascara del control40 original. No se
sustituye el control de aceptacion por control400.

### Explicacion de014, igual en ambos brazos

Valores de score redondeados solo en esta tabla; el derivado conserva los
bits y contribuciones exactas f32. Son todos los predecesores del ancla
en cualquiera de los dos presupuestos, no una seleccion favorable.

| Chunk | Rango denso/sparse | Rango40->400 | Score40->400 |
|---|---|---|---|
| Guia-pruebas-envio-correos.md7-17 | 3/1 | 1->1 | 0.750000000 |
| Asignacion de permisos para usuarios APP Reyma TI.md96-112 | 1/40 | 2->2 | 0.524390221 |
| Guia-pruebas-envio-correos.md111-118 | 2/10 | 3->3 | 0.424242437 |
| MesaAyudaTicketCommandHandler.cs60-85 | 20/2 | 4->4 | 0.380952388 |
| MesaAyudaTicketCommandHandler.cs82-107 | 10/3 | 5->5 | 0.340909094 |
| Guia-pruebas-envio-correos.md62-85 | 8/5 | 6->6 | 0.277777791 |
| TicketsController.cs112-114 | 4/16 | 7->7 | 0.258823544 |
| TicketsController.cs121-123 | 5/17 | 8->8 | 0.222222224 |
| Guia-pruebas-envio-correos.md136-144 | 16/6 | 9->10 | 0.201680675 |
| TicketAsignacionExtensions.cs81-100 | 270/4 | 11->9 | 0.200000003->0.203690037 |
| **Ancla ValidarMesaAyudaTicketCommandHandler.cs73-77** | **9/9** | **10->11** | **0.200000003 sin cambio** |

El competidor `f36653e7-4135-5365-bdbb-79b925f36e8a` ya pertenece a la
union40 por sparse4: NO es un nuevo miembro. En40 empata con el ancla
`2c679ab0-cfd8-5ffb-acff-ff045f9a8a79`; UUID ordinal pone2c...antesf3....
En400 suma denso270, `f32(1/271)=0.0036900369450449944`, y adelanta
a ambos chunks en puestos9/10. El ancla conserva sus dos contribuciones
`0.10000000149011612` y score `0.20000000298023224`.

Los vectores de documentos dense/sparse son iguales en los2598puntos
docs/tickets. Aun asi, las ramas sparse guardadas difieren en score/orden
en21preguntas, orden en8 y prefijo40 en3; no cambian top10fusionado a
igual presupuesto. La causa numerica/de ejecucion residual NO se aislo.
No generalizar a todas las ramas, filtros, resumen, reranking, ask o corpus.

### Instrumento, evidencia y limites

`h3-budget-result.json` versiona resultado, hashes, comandos y consumo.
`.artifacts\1.12-h3-budget-r1`, `1.12-h3-budget-guard-r1` y
`1.12-h3-budget-r2` quedan conservados. r2 fortalece P4 de conjuntos de
preguntas a cada vista/metrica/corte, sin cambiar el protocolo: filas,
resumen y recibos de r1/r2 son identicos. Fuentes medidas conservadas.
Derivado r2 SHA `9d13b18c01fa8f84cb5b4216d43704f713779a10413610a72808ec54bace516f`;
contraste nativo `e3c9db793319b96aac69d9789ca4d81111c33dc90ae41a334663cdab5f9b8949`;
DLL `5454879735da3994a97d285a244cb027e36efd540663724773381cfbcccc9ad8`.
126binarios DLL/deps/runtimeconfig preservados sin configuraciones.

Clon persistido en `b7944b3ee2db1021159c430c2a9f8afd1d6a1ff3`:
8archivos de instrumento/protocolos/propuesta/resultado y nota de ledger
juntos; autor/committerDWeno, mensaje sin coautoria/firmas, archivos y arbol
limpio comprobados tras commit. Sin remoto/push. El commit rector contenedor
coordina solo ficha/preparacion/relevo, no aceptacion funcional.

Build aislado sin errores/advertencias;8tests focalizados incluidos
ausencia/manipulacion/cobertura incompleta/atribucion exacta;10aserciones
previas;540comparaciones con el metodo REAL de fusion servidor, IDs y bits
f32 a limites10/100/800. Invocacion completa sin `--evidence-only` sale2.
Nada de ello es aceptacion HTTP de producto ni reutilizacion de respuestas
historicas con binarios nuevos. El original y los indices no se tocaron.

Duraciones parciales r1/r2:30.7654754/31.1885262s,guarda26.9112065s.
0servicios iniciados,0red experimental,0embeddings/ingestas/LLM.
Estimaciones2h offline+4h implementacion condicional+40h aceptacion; no
limites ni consumo total. Horas globales/tokens facturados desconocidos.
Modelo efectivo gpt-6-astra; xhigh solicitado, esfuerzo no expuesto.
Sin subagentes; opus/high del plugin se conserva.

### Propuesta congelada y reentrada

`protocol-h3-search.proposed.json`, SHA
`61967d0ee7a4fd8f121cf524fdc7fa68ca3f6a64a99642fba8614663e7ad29af`:
campo opcional `prefetchPorRama`40..1200 solo codigo/FusionNativa;
ausente conserva default. Cliente opt-in calcula max(4*B,40), B=TopK
o3*TopK con rerank: presupuesto por demanda logica, no constante40.
PublicTopK1..100 y pool configurado100 permanecen; prefijos40 permiten
MAXIMO80unicos, no prometer100disponibles ni rellenar/bajarTopK.
Resumen/saltos mantienen comportamiento anterior; perfiles de ingesta
intactos. Sin framework generico, datos, despliegues ni credenciales.

Consulta especifica con impacto y rollback realizada: **usuario no disponible,
sin respuesta afirmativa; NO autorizado por silencio**. No implementar esa
ampliacion antes del permiso. No pedir de nuevo los permisos ya vigentes.
Si se autoriza: protocolo ejecutable/guardas antes de medir, cambio minimo,
pruebas negativas/filtros/escalas/riesgo, representacion/indices completos
compatibles antes de embeddings y nueva procedencia. Primero45HTTP estricto;
cualquier perdida@10 rechaza, sin retune/H4. Solo si pasa, aceptacion
original116x2/928HTTP,29extras,adjudicacion cegada y todos los riesgos.
Rollback explicito Local y desactivar opt-in, conservando indices/evidencia.

Incidencia de exclusion registrada: la primera enumeracion
`git diff --name-only` del rector incluyo la ruta protegida y avisoLF/CRLF;
se excluyo antes de diff de contenido. No contenido mostrado, hash explicito
ni build del proyecto API. Operaciones siguientes por allowlist explicita;
no afirmar exclusion total de aquella enumeracion. Cambios ajenos preservados.

## Continuacion H2 autorizada posteriormente (2026-11-03,16:35)

**Resultado: H2 implementada y RECHAZADA, ficha bloqueada por calidad.** El permiso
especifico ya no bloquea. Perfil/ranking no se retocaron tras medir y no se ejecuto
la aceptacion completa ni otra ficha.

La representacion coincide exhaustivamente por documento/chunk, raw/enriched,
lineas e ID en cuatro cohortes:2113/23221,311/2081,15/743,553/1855
documentos/chunks. Las auditorias solapan fuentes de bsuite; no son totales
de documentos unicos. El nombre fisico alternativo no modifica H2.
La ingesta real sigue ocurriendo en el servidor, no se copiaron chunks al indice.

Runner principal: identidad persistente, lock, envelopes atomicos, estado/job/
errores/misses y pares de cuatro respuestas. Probe real r2 interrumpe tras un
documento resumido de dos, conserva ese punto al reingestar, luego interrumpe
tras3/4respuestas; reentrada valida indices/cache, adquiere4/4 y confirma un
par atomico. Una tercera reentrada restaura ese par sin HTTP. No equivale a
reanudar la corrida historica r2 ni a completar la aceptacion masiva.

Diagnostico H2 completo:45preguntas,4negativas,ambas vistas,@1/3/5/10,
native400 candidato/native40 control. **tickets-microservice-014 pierde
any@10 y full@10 true->false en ambas vistas.** El mismo chunk
`2c679ab0-cfd8-5ffb-acff-ff045f9a8a79`, rawhash
`3a35bfc63a57bc47ef3dff079af25e30209605817ad66789dc08ae79c7182265`,
`ValidarMesaAyudaTicketCommandHandler.cs:73-77`, queda nativo10/control y
nativo11/candidato; denso9 y sparse9 en ambos. No seleccionar otro pool
despues de observarlo ni atribuir toda discrepancia historica a fragmentacion.

Evidencia en el clon: `docs\eval\quality\1.12\h2-result.json` y artefactos
`.artifacts\1.12-h2-representation-r2`, `1.12-h2-resume-probe-r2`,
`1.12-h2-fixture-r1`, `1.12-h2-diagnosis-r1`. Resultado diagnostico SHA256
`5a425137cb1911493aef938c6f3216c36af0ce6a7c9003d91010b7a62e9a8596`;
verificacion independiente
`7049a89910a867d151c3f60396660b14bb5a4be8213d05186387eb8628f1e751`.
Recompone45rankings/mascaras con cosenos, producto sparse y RRFf32 desde
vectores guardados; verifica todos los bytes/sellos y los cuatro indices
historicos mas los dos H2 completos e intactos. DLL medida
`f0004d65c48bc47e93bb60fc0516c86f9ed285da26f7d67906d820f2ca16c220`,
con todas sus DLL/deps/runtimeconfig conservadas, sin configuraciones.

Build aislado0warnings/0errores;80tests cliente sin skips;23aserciones checkpoint,
8inventario,10diagnostico; fixture HTTP real con12aserciones H2 mas filtros,
resumen/saltos, idempotencia/borrado,401/404/429. No llamar esto paridad.
Diagnostico156.1912939s,45embeddingsconsulta,2598puntos candidatos,0LLM;
tokens/factura totales desconocidos. Fixtures/probes pequenos:7resumenes por
brazo,2542tokens resumen candidatos reportados,7llamadas resumen control
reportadas y8respuestas ask. No se consumieron resumenes masivos.

Revision final:3negativos del verificador rechazan ausencia/manipulacion/
cobertura incompleta; modo offline verifica45y sale2 sinflag de solo evidencia.
Se cerro la entrada invalida CodeScoreFloor distinto0.1 en FusionNativa,
sin cambiar parametros medidos. Build/80tests/23+8aserciones pasan;2warnings
heredados del proyecto tests no corregidos. Binario recompilado rechaza
el sello anterior antes de red/salida; no se migraron observaciones.
Arnes validar pasa en ambos y --al-cerrar informa bloqueado/no cerro.
Clon persistido en `2d8865aead9443aa1e0a7f77b6856da31a97c613`,22archivos
solo1.12 junto con su ledger; autor/committerDWeno,mensaje sin coautoria,
arbol limpio y sin push comprobados. El commit rector contenedor coordina
servidor/contrato/dobles dependientes y resultado/ledger/relevo, no aceptacion.

Reentrada: nueva hipotesis y protocolo comparativo ANTES de cambios/medicion,
manteniendo rechazos Denso005/H1-010/H2-014 y el oraculo estricto.
No repetir H2 sin cambios ni ajustar ranking/perfil para pasar. Las autorizaciones
vigentes no se piden otra vez; solo un alcance realmente nuevo requiere permiso.
Quedan116x2/928HTTP,29extras,adjudicacion cegada y todos los riesgos para una
eventual aceptacion. La siguiente seccion conserva el prerregistro previo.

El usuario autoriza ahora el perfil opt-in servidor y nombre logico separado,
DTO/jobs/procedencia y rechazo de mezclas, mas los cambios dependientes del clon.
La consulta sin respuesta de abajo se conserva: esta aprobacion es posterior,
no retroactiva. Solo se retira el bloqueo de permiso H2; la calidad sigue sin
aceptar y las puertas tecnicas permanecen. Unicamente1.12, sin subagentes.

Contrato/oraculo registrado ANTES de producto en el clon:
`docs\eval\quality\1.12\protocol-compatibility.json`.
Campos `perfilIngesta="rag-local-1.12"` y `repositorioLogico`, juntos y con
procedencia inmutable; nativo sin cambios. Primero runner reanudable con
identidad, estado de ingesta/resumen y cuatro respuestas atomicas; luego
igualdad completa raw/enriched/lineas/IDs de todos los corpus sin embeddings,
diagnostico45 native400/controlnative40 y, solo si pasa, aceptacion completa.
No retocar ranking, corpus, anclas ni umbrales segun el resultado.

Modelo efectivo gpt-6-astra, esfuerzo no expuesto; alias opus/high conservados.
8h implementacion+40h aceptacion estimadas, no limites ni consumo medido.
Rollback Local explicito, indices/volumen/rechazos conservados. Sin API protegida,
original, otras fichas, despliegues, credenciales/permisos/tuneles o push.

## Diagnostico posterior a 355613f: H1 refutada (historia preservada)

Solo1.12, sin subagentes. Se preservan literal los resultados anteriores.
No se cambio producto del servidor, cliente ni original: solo instrumento
aislado y documentacion. 1.12 sigue **bloqueado**, no aceptado.

H1 se fijo antes de implementar/medir en `protocol-diagnosis.json`:
candidato FusionNativa, pool100/prefetch400, contra control native40.
Sobre los cuatro indices completos conservados se midieron las45 preguntas
docs/tickets, incluidas4 negativas, ambas vistas y @1/3/5/10. Un vector real
Snowflake por pregunta se compartio entre brazos. Rankings exactos completos,
RRF f32 k2, desempate UUID ordinal y umbrales originales. La recomposicion
independiente coincide con el metodo real de fusion del servidor; esto NO
certifica la composicion de su endpoint HTTP ni activa FusionNativa en cliente.

| Caso/ancla | Control | Candidato | Conclusion acotada |
|---|---|---|---|
| tickets005, validator14-21 | dense18,sparse3,native7 | dense16,sparse4,native8 | Fusion recupera el ancla sobre el mismo indice; pool40/400 no cambia ese resultado |
| tickets010, validacion de estatus | raw70-95,dense7,sparse7,native8 | raw70-93,dense8,sparse8,native11 | H1 pierde full@10 en ambas vistas; pool40 tampoco lo resuelve |

El ancla005 conserva el mismo hash crudo en ambos indices; sus hashes
enriquecidos difieren. El ancla010 sobrevive, pero el chunk crudo cambia.
No atribuir toda la perdida a sparse ni afirmar causalidad exclusiva de
fragmentacion/enriquecimiento. Denso candidato tambien pierde tickets012.
Los contrafactuales explican mecanismos, no autorizan elegir parametros
despues de observar resultados. H1 queda refutada aunque mejore005.
Los62 top10 antiguos disponibles se reprodujeron EXACTAMENTE por
ruta,lineas,contenido y orden. El rechazo original permanece intacto.

### Correccion del inventario, previa a cualquier embedding diagnostico

La primera invocacion fallo porque9/1707 hashes candidatos de tickets no
coincidian con `chunks.json`; los otros tres multisets si coincidieron.
Se prerregistro `protocol-diagnosis-inventory.json`, sin alterar H1 ni su
oraculo, para exigir doble replay sobre los mismos bytes: nombre preparado
`v112-<dataset>` contra TODO el inventario preparado y nombre UUID real
contra TODOS los puntos. Ambas igualdades pasan: cambiar el nombre reproduce
exactamente los9 hashes distintos; docs tiene0/629 diferencias. Sin reingesta.
El header afecta al presupuesto de fragmentacion. El runner anterior solo
comprobaba conteos candidatos; no retrocertificar su procedencia completa.

### Evidencia, consumo y reanudacion

Salida `.artifacts\1.12-diagnosis-r2`; resumen versionado
`docs\eval\quality\1.12\diagnosis-result.json` en el clon.
Resultado `result-ecbcc49e4c034f1a8d7cfe02020a15f4.json` SHA256
`e3f8032d45664c7a90c667ec7eebc30ec9c5c83139ad6faef7bf5fd0f3d1b035`.
Verificacion independientev2 SHA256
`fb82c14b05aa34c432156be163fc4c82b160a38ca59326de61d2f8903aa7050d`.
Comprueba45 preguntas completas, recibos/sello, mascaras, RRF y cosenos
recalculados desde vectores guardados; evidencia vacia/incompleta falla.

Corrida9.2250393s,45 embeddings de consulta,0 embeddings documentos,0 LLM.
Tokens de consulta, factura y tiempo total de sesion desconocidos. Las4h de
investigacion y40h de aceptacion siguen estimaciones, no limites.
Los31880 misses restantes no se consumieron.

El instrumento incorpora lock exclusivo, checkpoints con recibos inmutables
y sello de fuentes/inventario/protocolo/binarios/servicio/indices. El intento
real de reanudar r2 fallo ANTES de inferencia por diferencias de escape JSON
en la canonizacion. Se corrigio y se cubrio con10 aserciones, incluido roundtrip
escapado; build0warnings/0errores. La DLL medida se conserva en
`measured-binaries\Fixture.dll`, SHA256
`a34c12a71f0b4ad52b6ca3b606120016494c08ec06f74171c7b25551ec7f284c`.
El binario corregido rechaza correctamente el sello antiguo antes de inferir.
**No hubo reanudacion real r2 completa exitosa.** No sobrescribir/migrar sus
sellos ni regenerar H1 rechazada para conseguir un verde. El runner primario
largo sigue sin reanudacion: antes de usarlo faltan identidad persistente de
cohortes/colecciones, ingesta/resumen/procedencia y commits atomicos de pares
de cuatro respuestas. La cache candidata en memoria no es durable.

Comandos ejecutados manualmente en invocaciones separadas de edicion:

```powershell
# Desde D:\Git\Rag.VectorSpace.Client; diagnostico, NUNCA cierre completo.
dotnet build docs\eval\quality\1.12\Fixture\Fixture.csproj -p:VectorSpaceRoot=D:\Git\Vector.Space.Engine --no-restore --verbosity quiet
dotnet docs\eval\quality\1.12\Fixture\bin\Debug\net10.0\Fixture.dll --diagnose-self-test
# Procedencia historica del binario medido: NO repetir con el binario corregido.
dotnet docs\eval\quality\1.12\Fixture\bin\Debug\net10.0\Fixture.dll --diagnose .artifacts\1.12-20261103-r3 .artifacts\1.12-paired-r1 .artifacts\1.12-diagnosis-r2
# Salida existente inmutable: para recomprobar elegir OTRO nombre de reporte.
python docs\eval\quality\1.12\verify-diagnosis.py .artifacts\1.12-20261103-r3 .artifacts\1.12-paired-r1 .artifacts\1.12-diagnosis-r2 --report .artifacts\1.12-diagnosis-r2\independent-verification-v2.json
```

El diagnostico devuelve error por H1 refutada; aun sin perdidas devolveria
error por aceptacion completa ausente. El verificador independiente valida
la evidencia del rechazo, NO la calidad para cerrar1.12. Los110 tests y18
fallos Windows anteriores no se reejecutaron ni se modificaron.

Los cuatro indices mantuvieron hashes completos de payload/vectores/esquema
antes/despues. Qdrant propio se verifico por ID/imagen/volumen/puertos antes
de arrancar y quedo detenido, `exited`, volumen conservado. Sin escrituras de
indice, SQLite/WAL, hosts HTTP nuevos, AltheIA, credenciales o tuneles.

### H2 propuesta: permiso especifico del servidor pendiente

`protocol-compatibility.proposed.json` mantiene ranking H1 fijo y propone
perfil de ingesta servidor opt-in compatible con representacion del control,
con nombre logico independiente de coleccion. Antes de embeddings exige
igualdad completa raw/enriched/lineas/IDs; despues45 preguntas y, solo si no
pierde, toda la aceptacion original. No es garantia de mejora ni unica solucion.
Se pide aprobacion de contrato/perfil/DTOs/jobs/procedencia y pruebas aisladas,
sin despliegues ni indices servidos; defaults nativos intactos, controles
conservados y rollback Local explicito. Estimacion8h implementacion mas40h
provisionales de aceptacion. **Consulta especifica para futura reentrada:
usuario no disponible; no autorizada, implementada ni medida.**
Las aprobaciones anteriores del clon/tiempo/AltheIA siguen vigentes.

Persistencia del diagnostico en el clon:
`b14ebdbcb2a96d08ebdb680f8db6bbfe0ccbc399`,8 archivos solo1.12, incluyendo
instrumento/protocolos/propuesta/resumen y nota del ledger. Autor/committerDWeno,
mensaje y archivos comprobados, sin coautoria/firmas, arbol limpio, sin push.
El commit rector que contiene esta seccion coordina ficha/preparacion/relevo;
no representa cierre funcional. `arnes validar` sale0 en ambos y `--al-cerrar`
informa bloqueado/no cerro; solo validacion documental, con avisos heredados.

## Implementacion y fixture real anteriores (historia preservada)

Se implemento solo en `D:\Git\Rag.VectorSpace.Client`: transporte/API tipada,
retrieval remoto, perfiles/catalogo, brain para meta-intent, pipeline ZIP/estado,
resumen remoto autoritativo y composicion real API/CLI. El backend local sigue
seleccionable y predeterminado; un fallo remoto no activa fallback. No se cambio
producto del original ni de VectorSpace.

La combinacion Empresarial+VectorSpace falla desde la composicion real, antes
del consumo; se rechazan Authorized y tenant activo. L/N textuales y modulo
opcional se conservan en codigo, resumen y cada salto. Los metadatos/metricas
que el servicio no expone permanecen ausentes. El cliente no abre cache SQLite
de resumen ni estado de ingesta; el servidor usa su cache nativo en memoria.

Comprobaciones manuales, separadas de edicion:

- 110 pruebas enfocadas pasan, sin fallos ni omitidas, con comprobacion del TRX
  para rechazar cero tests. Composicion API/CLI, transporte, contexto y pipeline.
- Fixture sintetico real `1.12-live-fixture-r4`: ingesta de cuatro documentos,
  idempotencia, JWT/401, filtros en requests REALES de codigo/resumen/simbolos,
  respuestas search/ask de ambos brazos, 429/Retry-After, borrado de un documento
  de prueba, 404 de operacion desconocida y control local explicito.
- r3 y r4 completos: 18.49/18.50 segundos; 4 resumenes por brazo/corrida.
  Tokens de resumen reportados por candidato: 1391/1407. El contrato no entrega
  el total de tokens de resumen del control ni de ask: no inventar total facturado.
- Las dos ejecuciones anteriores del fixture fallaron antes de ingesta por
  registros faltantes del host de pruebas: accessor de identidad y reloj UTC.
  Se usan registros existentes y reloj Business sin SQL, identidad corporativa
  ni lectura/compilacion de la API/configuracion protegida.
- Una ampliacion de pruebas de contexto encontro 18 fallos Windows. El mismo
  conjunto de 18 se reprodujo en copia aislada de la revision original:
  CRLF en goldens y archivos SQLite abiertos. No se modificaron goldens ni 19.6.

Qdrant propio: `v112-qdrant-20261103`, volumen
`v112-qdrant-20261103-data`, imagen Qdrant 1.19.2
`sha256:b7b0444c4c351c970b98e90a6f89c2ee4287c65b44e52b4cb503fa5b2aa927ad`,
loopback 26333/26334. El Qdrant del tunel 6333 queda intacto.
Los hosts HTTP son efimeros, con JWT sintetico local validado; no cambian
credenciales reales. Conservar volumen/indices para evidencia y rollback.

Antes de la primera medicion primaria se registro protocolo v3: cohortes
completas docs, tickets, bsuite-repo y auditorias, orden inverso en ronda 2.
Si un par completo demuestra una perdida contra el criterio universal ya
congelado, detener con error y conservar el rechazo; no consumir los 31880 misses
restantes para demostrar de nuevo el incumplimiento. No cambiar documentos,
anclas, umbrales, modelos ni flags de resumen; no aprobar trabajo no medido.
`Fixture --paired` adquiere evidencia, pero NO certifica por si mismo
adjudicacion semantica, cobertura de riesgos ni aceptacion completa.

## Resultado pareado: rechazo temprano verificado

La corrida `.artifacts\1.12-paired-r1` termino con error tras **124 respuestas
HTTP 200, 31 pares completos**, todos de ronda 1: 26 docs y 5 tickets.
Se ingirieron los corpus completos de esas dos cohortes, no un subconjunto
favorable: 15/553 archivos, 743/1855 chunks del control y 629/1707 puntos
del candidato. Los resumenes estaban deshabilitados en estas cohortes desde
el protocolo original, no se omitieron para facilitar esta prueba.

Contraejemplo: **`tickets-microservice-005`**, adjuntar documento/evidencia a ticket.

| Evidencia @10 | Control | Candidato |
|---|---|---|
| Ancla `TicketDocumentoRequest request` | Rango 1 | Rango 1 |
| Ancla `RegistroTicketDocumentoValidationMessages` | Rango 7, validator lineas 14-21 | Ausente; rango 10 trae otro chunk del validator, lineas 25-29 |
| hit_any | true | true |
| hit_full | **true** | **false** |

Ocurre en ambas vistas, historica y corregida. La recomputacion independiente
normaliza los dos separadores de ruta, usa basename y comparacion ordinal
de anclas, confirma las cuatro respuestas HTTP del par y vuelve a comprobar
los hashes de **todos** los bytes del corpus congelado. No es un fallo de
infraestructura ni del nombre de archivo. No aisla causalmente sparse,
chunking o pools: no atribuirlo a uno solo sin otro experimento.

- SHA256 `evaluation.json`:
  `9acd079abed96cd43d151d31ae6586aac1f94d9ff746cd951cdd1ce21ff4b511`.
- SHA256 del protocolo medido, copiado antes de ejecutar:
  `9a682d542c74096914caff1bc1ad62b5bc7f328a6dbac08258c612c889180232`.
- `rejection-verification.json` conserva el contraejemplo independiente.
  Configuracion, esquemas, hashes binarios, respuestas, tiempos y archivos
  cegados permanecen bajo la salida de corrida, ignorada por Git.
- Duracion primaria: **888.696 s**. Ingesta candidato docs/tickets:
  44.651/101.938 s; control: 73.532/206.487 s.
  Control: 2598 textos y **712096 tokens de embeddings de documentos**,
  sin truncamiento. No es el total de tokens de consultas/generacion.
- No se generaron los **31880 misses** de resumen restantes: el criterio ya
  esta refutado. No equivale a declarar que esos brazos/cohortes pasaron.
- Quedan sin completar resto de tickets, bsuite/auditorias, ronda 2, suplemento29,
  adjudicacion semantica de ask y cobertura total de riesgos. Una lectura
  parcial de respuestas cegadas no certifica ask.

Comandos reales ejecutados en llamadas separadas de edicion:

```powershell
# Desde D:\Git\Rag.VectorSpace.Client. Evidencia parcial, NO cierre de 1.12.
dotnet test tests\RagEngine.Core.Tests\RagEngine.Core.Tests.csproj --no-restore --filter "FullyQualifiedName~VectorSpace|FullyQualifiedName~TransportProfileTests|FullyQualifiedName~CollectionAuthorizationServiceTests" --logger "trx;LogFileName=remote-real-composition-r4.trx" --results-directory .artifacts\1.12-tests --verbosity minimal
dotnet build src\RagEngine.Cli\RagEngine.Cli.csproj --no-restore --verbosity quiet
dotnet build docs\eval\quality\1.12\Fixture\Fixture.csproj -p:VectorSpaceRoot=D:\Git\Vector.Space.Engine --no-restore --verbosity quiet
dotnet run --project docs\eval\quality\1.12\Fixture\Fixture.csproj -p:VectorSpaceRoot=D:\Git\Vector.Space.Engine --no-restore -- .artifacts\1.12-live-fixture-r4
dotnet run --project docs\eval\quality\1.12\Fixture\Fixture.csproj -p:VectorSpaceRoot=D:\Git\Vector.Space.Engine --no-restore -- --paired .artifacts\1.12-20261103-r3 .artifacts\1.12-paired-r1
```

Las salidas existentes no se sobreescriben: los dos ultimos comandos son la
procedencia de lo ejecutado, no una invitacion a regenerarlo. El ultimo devolvio
error por el criterio; no se cambio para hacerlo verde. Los comandos no son
`verificacion_comando` de una aceptacion completa.

Qdrant propio **detenido y comprobado `exited`**, volumen conservado; hosts HTTP
detenidos en `finally`. Sin auxiliares de prueba que deban seguir ejecutandose.
Rollback: backend Local explicito y eventual revert del commit de composicion,
sin borrar indices ni el original. Otra medicion necesita hipotesis/protocolo
previos, conservar el rechazo y reutilizar trabajo compatible. El runner actual
exige salida nueva y no ofrece reanudacion ni certificacion de adjudicaciones:
resolver esas limitaciones antes de una futura corrida larga. No hay otra ficha
autorizada; no abrir 1.21.5/1.21.6.

## Persistencia coordinada

Cliente: `3bf19259a91756f14c27dbe500be2be98d71e0c6`, rama
`feature/vectorspace-client`: 51 archivos del item, producto/pruebas/harness
y nota del ledger juntos. Autor/committer DWeno, mensaje comprobado sin
coautoria ni firma de herramienta; arbol limpio, sin remote/push.
La medicion precede al commit y conserva sus hashes binarios/configuracion.
El commit de VectorSpace que contiene este documento persiste unicamente
la ficha 1.12, esta evidencia y el relevo; no es cierre funcional.

## Historia del preflight: servicios remotos, no inferencia local

La respuesta **"ok, realizalo"** de las 12:44 aprobo ranking distinto y nueva
referencia pareada estricta. Implementacion y tiempo ilimitado tambien estaban
aprobados. No volver a presentar esas consultas historicas como pendientes.

Antes de producto o medicion, el usuario corrigio el banco: Snowflake por el
tunel ya existente y AltheIA configurada, dejando Qdrant de pruebas aislado local.
Ambos brazos usaran los mismos modelos remotos. El control sera una adaptacion
aislada del pipeline original, NO su runtime intacto ni el baseline 5.e.
El original no se modifica. No se toca el Qdrant del tunel ni se cambia el tunel.

Comprobaciones reales, solo metadatos:

- Los puertos indicados 6333/8081 estan escuchando en loopback. Cero requests a
  Qdrant, ninguna coleccion listada, creada o modificada.
- `/info` en 8081: `Snowflake/snowflake-arctic-embed-m-v2.0`, float32,
  ventana 8192, lote 16384, TEI 1.8.3 y commit del servicio
  `78502d8e61223d2c73d4bb7aeaea46787e90d596`.
  No expone revision de pesos: ese commit NO es un hash del modelo.
- AltheIA `/v1/models`: `bge-m3`, `qwen3.8-27b-deep`, `qwen3.8-27b-fast`.
  Se selecciona `fast`, como en el ejemplo proporcionado. Esto prueba acceso al
  catalogo, no inferencia ni cuota disponible. No se selecciona bge-m3 en lugar
  del Snowflake indicado por el usuario.
- El ejemplo PowerShell se inspecciono mediante AST, sin ejecutar sus funciones
  que cambian credenciales/archivos de uso, ni mostrar valores secretos.
  El probe reutiliza el cliente HTTP/TLS del motor con las variables existentes
  `ALTHEIA_KEY`/`ALTHEIA_CERT`, sin desactivar validacion ni seguir redirecciones.
  No lee el appsettings protegido ni imprime certificados o credenciales.

Protocolo activo en el clon: `docs\eval\quality\1.12\protocol.json`.
La propuesta de inferencia local se conserva, **sustituida antes de medir**, en
`protocol-local.pre-altheia.json`. Dos rondas, mismo corpus y criterios estrictos;
no se relajan anclas/umbrales por cambiar servicios.

Preparacion real reproducible (`.artifacts\1.12-20261103-r2` y `-r3`, ignorados):

| Cohorte | Archivos admitidos | Chunks control / candidato | Preguntas con anclas ausentes |
|---|---:|---:|---:|
| bsuite-repo | 2113 | 23221 / 17628 | 0 / 0 |
| auditorias | 311 | 2081 / 1792 | 0 / 0 |
| docs | 15 | 743 / 629 | 0 / 0 |
| tickets | 553 | 1855 / 1707 | 0 / 0 |

Son 116 preguntas: 104 ancladas y 12 negativas, con cobertura verificada tras
los chunkers/reglas de admision REALES, no recall medido. Dos erratas verificadas
antes de retrieval se conservan en copia derivada: elipsis no literal de docs-020
y mayuscula inicial de tickets-008. Se deben reportar ambas vistas sobre ambos
brazos; datasets originales y cinco JSON 5.e intactos. Las 29 preguntas posteriores
de bsuite son suplemento, no sustitucion.

- SHA256 instrumento: `19b9526fcb467b6dfe0470dddfe4a818e3deccd27932346c99cbd984145cf815`.
- SHA256 manifiesto de fuentes: `32a535d48b489795b01b4810de7255c2159642315c492647b8b6371dc0863fd8`.
- Cache original abierta solo lectura; backup SQLite consistente y separado,
  hash `654bd7d7b724bc4b52fb8a1fc21a557072927580a53386fb692dbae956810c79`.
  Ningun segundo escritor sobre su WAL.
- Para `qwen3.8-27b-fast`: versiones de prompt `0879c221` / `d6778228`,
  misses **18612 / 13268**, cero hits compatibles. Los prompts solo difieren
  por CRLF/LF; no normalizar claves ni fingir reutilizacion de la cache historica.

**Actualizacion posterior:** el usuario autoriza AltheIA **sin limite** y corpus
completo si es necesario. La consulta de cuota sin respuesta queda como historia,
NO como bloqueo. Primero fixtures pequenos; solo despues aceptacion completa.
Registrar consumo y respetar limites reales, sin eludirlos. El ejemplo declara 1 M tokens diarios,
no una reserva para esta ficha ni cuota disponible verificada. La referencia de
1.7.3 (~670 tokens/chunk) extrapola **21.359.600 tokens solo para resumenes**:
unas 22 ventanas de 1 M si estuvieran completamente disponibles, mas ask,
reintentos y otras comprobaciones. Es estimacion, no consumo medido. No omitir
resumenes/control, acaparar cuota o reutilizar presupuestos de otras fichas.
Las **40 h de maquina** son provisionales y distintas del calendario por cuota;
la aprobacion de tiempo ilimitado permanece, no hay que volver a pedirla.

Herramientas de preparacion compiladas/ejecutadas manualmente en llamadas
separadas de la edicion. No hay producto implementado, servicios iniciados,
ingestas, embeddings/resumenes/generaciones ni aceptacion funcional.
El rector volvio temporalmente a `bloqueado` antes de modificar producto y
despues a `en_curso` con la autorizacion expresa de consumo sin limite.

Comandos de preparacion (NO son `verificacion_comando` de 1.12):

```powershell
# Desde D:\Git\Rag.VectorSpace.Client. Cada salida debe ser nueva.
python docs\eval\quality\1.12\prepare.py --source-parent D:\Git --output .artifacts\1.12-20261103-r3
dotnet run --project docs\eval\quality\1.12\Preparation\Preparation.csproj -p:VectorSpaceRoot=D:\Git\Vector.Space.Engine --no-restore -- .artifacts\1.12-20261103-r3 .artifacts\1.12-20261103-r2\summary-seed.sqlite3 qwen3.8-27b-fast
dotnet run --project docs\eval\quality\1.12\Preparation\Preparation.csproj -p:VectorSpaceRoot=D:\Git\Vector.Space.Engine --no-restore -- --remote-preflight .artifacts\1.12-20261103-r2\remote-preflight.json
```

`--remote-preflight` solo consulta metadatos autorizados; no ejecuta el script
del usuario ni pruebas viejas que apuntan a otra instancia. El proyecto referencia
Core/Infrastructure, NO compila la API de VectorSpace ni copia su appsettings.
Corregir la configuracion del control y sellar su procedencia antes de medir;
los hashes locales historicos no se transfieren al nuevo proveedor remoto.

## Historial hasta 832b31a: autorizaciones y repositorios

Lo que sigue conserva la preparacion anterior. Sus bloqueos/ausencias historicos
quedan actualizados por la seccion vigente y el ledger, no se reabren.

La decision TOTAL de `122755f` no se reabre: VectorSpace sera dueno de
fragmentacion, embeddings, indice y resumen indexado; el cliente conserva API/CLI,
orquestacion, fusion de espacios, salto por simbolos, reranking, contexto y generacion.
No se reduce a sustituir el brain ni se retira el pipeline local.

El usuario aprobo posteriormente **"te apruebo uso de tiempo sin limite"**, solo
para 1.12. Ya no hay bloqueo presupuestario de 4 h. La consulta historica sin
respuesta permanece intacta. La consulta inicial sobre IMPLEMENTAR y ACTIVAR
pruebas aisladas tampoco obtuvo respuesta en esa etapa.

**Actualizacion posterior a `b50eb6b`, 12:21:** el usuario respondio expresamente:

> Autorizo implementar 1.12 en D:\Git\Rag.VectorSpace.Client y ejecutar ingestas y pruebas aisladas, conservando el original y sin tocar colecciones servidas, credenciales, despliegues ni hacer push.

La autorizacion de implementacion y pruebas aisladas **ya existe**: no volver
a pedirla ni presentar la consulta historica sin respuesta como estado actual.
Sigue faltando el protocolo/oraculo previo. Se consulto la propuesta concreta
de ranking y referencia pareada de abajo; el usuario no estuvo disponible.
No se interpreta esa ausencia como aprobacion metodologica ni como revocacion
de la autorizacion de implementacion. No se modifico producto.

Durante esta conversacion el usuario pidio una bifurcacion con Git aparte para
conservar el original y luego autorizo generarla. Se creo:

| Repositorio | Funcion y estado |
|---|---|
| `D:\Git\Vector.Space.Engine` | Ledger rector; base `122755f2c9dd1e45fbac94102a92daba7d7ad07a`. Solo documentacion de 1.12. |
| `D:\git\Rag.Context.Engine` | Original conservado; base `3c3972b90b74ee3d9cd7b86945e07d4775a9c965`. Ninguna edicion ni commit. |
| `D:\Git\Rag.VectorSpace.Client` | Clon local desde esa base, rama `feature/vectorspace-client`, `.git` propio, sin hardlinks/alternates/remotes. Solo instrucciones, README y nota de procedencia en ledger. |

El nombre de carpeta es el propuesto localmente, no un nombre remoto confirmado.
Commit de preparacion del clon: `4e328bc7765033ea2f04a96d163dd1b534a5e017`;
solo AGENTS.md, README.md y nota en el ledger, autor/committer DWeno,
sin coautoria ni firma de herramienta.
Actualizacion de autorizacion y bloqueo metodologico en el clon:
`e7b1b7375a51322d99b7cc8087634cfb7a968603`, los mismos tres archivos;
autor/committer DWeno, mensaje comprobado sin coautoria/firma y arbol limpio.
La cuenta GitHub autenticada es `DanielWueno`; el mensaje escribio `DanielWeno`.
La aclaracion de propietario/destino no obtuvo respuesta. **No se creo un
repositorio remoto ni se hizo push**. No se agregaron colaboradores ni se afirma
que los permisos heredados del filesystem garanticen un unico lector/propietario.
La titularidad personal solicitada se conserva como requisito de una publicacion
futura, no como un hecho remoto ya implementado.

El clon contiene solo el estado commiteado: no los cambios ajenos ni caches/modelos
no versionados. Sus configuraciones heredadas NO autorizan iniciarlo o conectarlo
a servicios. El cambio ajeno de `appsettings.json` de VectorSpace no fue leido
(tampoco para hash), buscado, incluido en diffs ni preparado para commit.
`src\RagEngine.Api\Properties\` del original sigue fuera del trabajo.

## Dependencias y separacion de 19.6

Ambos JSON completos se cargaron/deserializaron y se reviso el inventario de
estados: 38 fichas en VectorSpace y 162 en Rag. Comprobacion directa del ledger,
no inferencia del relevo:

| ID completo | Estado | Relacion |
|---|---|---|
| `1.18-exponer-metadatos-de-chunk-en-busquedas` | hecho | adicional |
| `1.20-busqueda-explicita-sobre-banda-resumen` | hecho | adicional |
| `1.21-decidir-filtro-de-modulo-en-busquedas` | hecho | adicional |
| `1.21.1-implementar-filtro-exacto-de-modulo` | hecho | adicional |
| `1.21.2-preparar-compatibilidad-de-filtros-del-cliente` | hecho | adicional |
| `1.21.4-implementar-filtros-textuales-de-lenguaje-y-namespace` | hecho | adicional |
| `1.21.3-implementar-guardias-y-traduccion-de-filtros-del-cliente` | hecho | adicional y `bloqueado_por` literal |
| `1.22-reparar-dos-pruebas-rojas-preexistentes` | hecho | adicional |
| `1.4-portar-codigo-del-motor` | hecho | prerrequisito de motor |
| `1.19-listar-documentos-indexados-de-una-coleccion` | hecho | insumo, NO pertenece a las ocho adicionales |

El unico `en_curso` de Rag es
`19.6-ci-que-distingue-fallos-reales-de-falta-de-infraestructura`.
Su alcance es CI/categorias xUnit, aislamiento de TracingGenerationTests,
GenerationHttpHarnessTests y documentacion de CI. Sus puertas pendientes necesitan
Actions/push; no se ejecutan aqui. No se reescriben sus fallos/resultados historicos.

1.12 necesitaria composicion/puertos/cliente/CLI y pruebas propias. Se puede separar
de 19.6 en el clon: no modificar `ci.yml`, `TestCategories.cs`, los harness citados
ni su ficha. Las nuevas pruebas tendrian archivos propios, sin reemplazar el
retriever bajo prueba por un fake al probar composicion. Si posteriormente hace
falta cambiar un archivo de 19.6, parar y registrar el conflicto; no resolverlo aqui.
Todas las fichas heredadas del clon quedan intactas como historia, no como agenda
nueva. El original y su ledger permanecen sin cambios.

Las fichas y resultados completos de 1.18/1.20/1.21.1/1.21.4/1.21.3 se
contrastaron con contratos/codigo. Son evidencia historica, no nuevas corridas:
1.18 expuso metadatos y ANY de simbolos; 1.20 persistio texto/espacio resumen;
1.21.1 registro 44 casos y rollback del lector; 1.21.4 registro 87 casos mas
sonda independiente; 1.21.3 registro 48 casos simulados. Sus conteos no son
constantes de aceptacion de 1.12. No se reejecutaron esos items.

## Contrato actual y decisiones que aun faltan

`R\` denota original/clon en la base indicada; `V\`, VectorSpace.

| Componente y evidencia | Tratamiento requerido antes de implementar |
|---|---|
| `R\src\RagEngine.Core\Extensions\ServiceCollectionExtensions.cs:149,174-177,276-277` | Registra brain ONNX, admin/writer Qdrant, retriever Qdrant y pipeline local. La rama remota debe seleccionar explicitamente servicios distintos, sin resolver Qdrant/brain/resumidor local. Conservar rama local de control. |
| `R\src\RagEngine.Core\Infrastructure\VectorSpace\` y tests homonimos | Componente de `3c3972b9` sigue sin registro DI: solo estos archivos y sus tests lo referencian. Devuelve HttpResponseMessage, no implementa ISemanticRetriever ni ingesta/catalogo. Su capacidad textual es declarativa; no prueba un servidor real. |
| `R\src\RagEngine.Api\Program.cs:406-460,693-1025` | Empresarial construye Authorized; actualmente lee manifiesto antes del retrieval. Validar backend+modo desde composicion real ANTES de resolver admin, hacer HTTP, embeddings, reranker o LLM. Tambien CLI, que no dispara ValidateOnStart por construir el contenedor. |
| `R\src\RagEngine.Core\Infrastructure\VectorStore\RetrievalProfileResolver.cs` e `Abstractions\IVectorStoreAdmin.cs` | El perfil de retrieval depende del manifiesto en Qdrant. El catalogo remoto solo entrega perfil de embeddings/modelo/dimension: no es ACL ni manifiesto Rag. Hace falta definir perfil local explicito por coleccion remota y sus capacidades verificadas; no inventar hashes ONNX, fecha, salud o esquema ausentes. |
| `V\...\Models\Responses\ColeccionesResponses.cs` | El hit no trae repository_name ni last_modified exigidos por metadatos locales. Definir representacion de procedencia remota/ausencia sin fabricar valores. Campos historicos null no acreditan cobertura de citas. |
| `R\src\RagEngine.Api\Contracts.cs:54-112` y `Program.cs:561-573` | SourceDto recibe hoy resumen de ISummaryCache. Transportar texto remoto en el resultado hasta search/ask/stream, sin consulta/generacion obligatoria de cache local. Un null remoto sigue null, no se rellena con un resumen de otra procedencia. Preservar redaccion Simple/Technical/prosa. |
| `R\src\RagEngine.Cli\Commands\IngestCommand.cs`, `IngestStatusCommand.cs`, `DoctorCommand.cs` | Orquestar envio de documentos/proyecto, id y estado remoto; catalogo/documentos para descubrimiento. No conservar chunking/embeddings/escritura local en el camino remoto, ni fingir salud ONNX/Qdrant propia. |
| `V\docs\servicio\contrato-api.md` | Documentos sincronos o zip asincrono, estados pendiente/procesando/completada/error y conteos finales. Registro en memoria por instancia: 404 tras reinicio significa estado desconocido, no exito. ResumenesPendientes > 0 impide declarar aceptacion completa aunque estado sea completada. Omitidos y fallos se exponen. No activar origen Git/acceso corporativo por comodidad. |

### Rankings, escalas, pools y umbrales

El retriever local (`R\...\QdrantSemanticRetriever.cs:111-176,303-362,414-551`)
usa dos ramas independientes con RRF nativa, o tres independientes con RRF ponderada
si hay resumen. VectorSpace (`V\...\QdrantSemanticRetriever.cs:112-176`) entrega
**un ranking de codigo** (Denso o FusionNativa, configuracion de instancia) y
**otro denso de resumen**. No expone sparse independiente, modo por solicitud,
umbral por solicitud ni cursor de busqueda.

Por tanto `RRF(RRF(denso,sparse),resumen)` NO reproduce
`RRF_ponderada(denso,sparse,resumen)`. No aplicar automaticamente pesos MiniLM
1/1.3/2.5 a arctic ni etiquetar el resultado como equivalencia algoritmica.
Tambien el salto de simbolos usa codigo: si la instancia esta en FusionNativa,
no es el salto solo denso del origen. Elegir Denso preservaria esa caracteristica
del salto, pero tampoco preserva las tres ramas primarias.

Limites comprobados, no una configuracion candidata aprobada:

| Caso local | Pool calculado | Limite remoto |
|---|---|---|
| Sin rerank | base K; prefetch `max(4K,40)` | 100 resultados por peticion/espacio |
| Con rerank | base 3K; prefetch `max(12K,40)` | Con K=10 ya se requieren 120 por rama local |
| Two-hop sin rerank | final K + MaxExpansionResults; prefetch no se ensancha | K=100 + 20 excede 100; no recortar en silencio |
| Simbolos | hasta 50 nombres, ANY; AND con filtros | 1..50 por peticion; tampoco hay cursor para completar un ranking |

Pedir 100 varias veces no pagina ni recupera el resto. FusionNativa remota
ademas calcula su propio prefetch `max(4*topK,40)`: pedir un pool mayor puede
reordenar el ranking. Esto debe constar como intervencion, no como transporte neutro.

`MinimumSimilarityScore`/MinScore local NO viaja en el componente actual.
VectorSpace fija UmbralSimilitud del codigo en instancia (default 0.10) y
UmbralSimilitudResumen (default 0). No comparar un score RRF contra un coseno,
ni filtrar el ranking fusionado para simular un umbral de su rama densa.
La API local admite TopK 1..100: reducirlo, limitar pools o rechazar MinScore
variable es cambio visible que debe decidirse, no introducirse silenciosamente.

**Decision pendiente concreta:** prerregistrar modo/ramas y fusion del candidato,
pesos y escalas, manejo de umbrales, pools y contrato de entradas no representables.
O bien ampliar explicitamente el contrato necesario dentro del alcance autorizado,
o aceptar una estrategia remota distinta con rechazo visible de entradas no
soportadas y criterio de calidad acordado. La implementacion ya esta autorizada,
pero esta preparacion no elige una variante sin resolver ese oraculo,
ni ejecuta otra ficha para resolverla.

### Restricciones ya decididas

Local explicito; lenguaje/namespace por Match.Text, modulo opcional exacto.
Transmitir restricciones intactas en codigo, resumen y cada salto, antes del
corte/consumo de candidatos. Namespace no se sustituye por modulo o ruta.
Conservar blancos/ausentes segun el traductor y no normalizar valores activos.

Tenant explicito no blanco y TODO Authorized se rechazan antes del transporte.
Empresarial nunca se convierte a Local. 400/401/403/409/503, cancelacion y
fallos de red no permiten quitar filtros, devolver exito vacio ni fallback.
Se conserva JWT HTTP de VectorSpace; pruebas con identidad sintetica no acreditan
login real ni autorizan acceso corporativo.

## Inventario documental del instrumento

Los cinco JSON de `R\docs\eval\quality\5.e\` se cargaron completos. Son salidas
de recall: **no contienen respuestas de /api/ask**. Procedencia comun declarada:
`9a6e005`, `git_dirty=true`, `model_qint8_arm64.onnx`, 384d, ventana 256,
chunking contract 3, prompt `210d3e35`, K=10, sin rerank, min_score=0.1,
pesos 1/1.3/2.5 y k=60. No acreditan modelos/binarios/corpus presentes.

| Archivo 5.e | Filas | SHA256 de bytes actuales |
|---|---:|---|
| bsuite-repo.rebaseline.json | 55 | `bd40cc65d20b7f25f2d5817c94fb70db0e4d3c74871cf6f5f62a51dda89043b4` |
| bsuite-auditorias.rebaseline.json | 16 | `02c0b99f9f1d177384b5c9fe44c4f8504e373bd0ac61b81f617545ff74f116d8` |
| bsuite-auditorias.rebaseline-repro2.json | 16 | `014939ce3a49c41e2844a50afcc9977655a067f420fb39acf215d5532ea4240c` |
| innovapp-docs.rebaseline.json | 26 | `e53aa1a654cff66b9a21f63e7f711a527b02e4f1ebe516be7412d805ac950f41` |
| tickets-microservice.rebaseline.json | 19 | `506bf3885fffd84e40f3c352072c4cf86ded3bb9e28e288f1c41da9227effd0f` |

Eval-sets actuales: 145 filas en cuatro datasets; 133 con anclas textuales y
12 sin ellas. Eso es inventario del archivo, **no n efectivo con anclas validadas
contra fuente e indice**, que sigue pendiente.

| Dataset | Actual con ancla / sin ancla | Filas de 5.e | SHA256 bytes checkout |
|---|---:|---:|---|
| bsuite-repo | 76 / 8 | 55 | `c8626766ffadc8dc5f2f7816921d25c6a7d28510477b58615002318939ea4c11` |
| bsuite-auditorias | 16 / 0 | 16 | `8a322c20b68e76875aa5121ead7c6537d4483ff290be58dbc88689a2ae7b7efa` |
| innovapp-docs | 22 / 4 | 26 | `5642fe0d8a6e3785eecfaba95013015d898f3adb8540a4e8a227135dfc27e5a0` |
| tickets-microservice | 19 / 0 | 19 | `d961a14a3dc08704001ff6db2febdc34f8166e9bd8443c393c8957120b0b8d43` |

La conversion **solo en memoria** CRLF -> LF da respectivamente:

| Dataset | SHA256 LF |
|---|---|
| bsuite-repo | `cdb39feef4ee1a20a4e9fe11912bbe4caf82e1d29a12f09e93198164fb68f6d3` |
| bsuite-auditorias | `ec6ff169d1a63b65cddcd799eacea0ab17770edf42fb2ed8e58ead621cd563ba` |
| innovapp-docs | `1f9571b91552adfa1aa4eca9af92cafe2476a5c0d4d4fd0ea78719d54d85574d` |
| tickets-microservice | `2ae380bb02df6c0fb28aaa3cb3101973a126ba3c1c8b970d0802ad104709bf0c` |

Los ultimos tres prefijos coinciden con `provenance.eval_set_hash` de 5.e.
El del archivo completo bsuite-repo NO: 5.e declara `5c0b6532ac78` y
55 preguntas (47+8); el actual agrega 20 de salto y 9 salto-negativo.
La primera preparacion solo habia comprobado presencia de las preguntas
por texto, no identidad de sus anclas.

**Hallazgo posterior, comprobado sin escribir archivos:** seleccionar los
objetos completos actuales por `Question`, en el orden de `results[].question`
del baseline, recupera exactamente los primeros 55 objetos del eval-set de 84.
Serializar con `json.dumps(cohort, ensure_ascii=False, indent=2) + "\n"`,
codificar UTF-8 y calcular SHA256 produce:

`5c0b6532ac7826f4afd7cbdeefd590a94e44b0cf284614b298b017ce9bc3c08a`.

Coincide con el prefijo historico `5c0b6532ac78`; el baseline no guarda un
hash historico completo. Es evidencia de reconstruccion del instrumento de
55 objetos/anclas, no recuperacion de corpus, indice, modelos o respuestas ask.
No se sustituyeron las 55 preguntas por las 84 ni se editaron anclas/archivos.
La diferencia 55-vs-84 ya no justifica afirmar que la cohorte sea irrecuperable.

`git show 9a6e005:docs/eval/bsuite-auditorias.eval-set.json` fallo:
objeto `9a6e005` inexistente en este checkout, que NO es shallow. No se hizo fetch
ni se asumio que otro commit reproduce ese arbol sucio.

El inventory del gate nocturno ya usa 6.c para bsuite-repo; **no sustituye** la
referencia 5.e exigida por 1.12. `quality-baseline.py` usa otro bundle de preguntas,
solo llama ask y continua ante errores antes de salir 0: tampoco es la aceptacion.
`rag eval --baseline` informa regresiones/incomparabilidad sin fallar por ellas.
Estos hechos impiden registrar cualquiera de esos comandos como cierre completo.

## Protocolo previo: puertas y oraculos

Este es un protocolo de preparacion, **no un experimento congelado ejecutable**.
Faltan decisiones enumeradas abajo; no hay resultados candidatos observados
ni se han elegido umbrales despues de medir.

| Puerta | Oraculo obligatorio y evidencia que debe fallar si falta |
|---|---|
| P0, autorizacion | Implementacion e ingestas/pruebas aisladas en el clon YA aprobadas; tiempo tambien. Concretar recursos aislados y rollback antes de ejecutarlos. El bloqueo actual es metodologico, no falta de esos consentimientos. |
| P1, procedencia | Manifestar ambos commits/builds, archivos sucios permitidos, hashes completos del corpus y de cada documento, anclas/IDs de preguntas, n efectivo, contratos de chunking, imagen/version/esquema de Qdrant, embeddings codigo/resumen, prompts/modelos LLM, reranker/binario/tokenizer, pesos/pools/umbrales/gates y configuracion efectiva sin secretos. Identificar toda diferencia intencional; no exigir igualdad falsa entre MiniLM y arctic. |
| P2, composicion real | Arrancar API mediante su composicion real con Empresarial+remoto: rechazo antes de HTTP/catalogo/consulta, embeddings, reranker y LLM; contadores cero y ningun fallback. Ejercitar tambien CLI/puerto y cada Authorized, incluso admin/sin claims; tenant activo en Local se rechaza. Los tests aislados del traductor no sustituyen esto. |
| P3, filtros/consumo | Positivos L/N/M y ANY de simbolos; negativos de otros lenguajes, namespace ausente/ruta coincidente, fronteras alpha/alpha-private/Alpha, conjuncion imposible; falsos candidatos de mayor score. Mismos filtros en todas las peticiones/saltos; ningun candidato ajeno llega a semillas, reranker, LLM o fuentes. Version/esquema iguales al oraculo textual. |
| P4, contratos | Citacion tecnica/prosa/simple y resumen remoto con cache local vacia e incluso con entrada local contradictoria; no inventar metadatos ausentes. Ranking/desempate/escalas y limites alrededor de cada pool; fallo visible de 400/401/403/409/503/red/cancelacion, sin ampliacion ni resultado parcial exitoso. |
| P5, ingesta | Nueva coleccion aislada con resumen desde creacion; idempotencia, cambio/eliminacion de documento, omitidos, 429/Retry-After, pendiente/procesando/error/completada, reinicio/404, resumen pendiente/sentinel y reanudacion. Verificar conteos y perfil antes de aceptar; ninguna cache local sustituye la fase remota. |
| P6, parejas search/ask | Mismas preguntas, anclas y configuracion no intervenida en ambos brazos; llamar ambos endpoints reales, no solo CLI. Conservar respuestas/citas/rankings/latencias y adjudicacion sustentada por pregunta. Incluir negativos y riesgos, sin descontarlos del conteo de cobertura. Ninguna ausencia o excepcion cuenta como acierto. |
| P7, repetibilidad/rollback | Repetir ambos brazos con orden prerregistrado y control conservado. Cambio de instrumento justificado exige nueva procedencia y ambos brazos, no solo candidato. Probar seleccion explicita del backend local; conservar puntos experimentales separados, no borrarlos como supuesto rollback de codigo. |

**Bloqueo de oraculo, responsable usuario + ejecutor de 1.12:** el criterio literal
de la ficha pide paridad con eval-set/baseline 5.e, pero no fija un margen numerico
para el cambio de algoritmo ni una adjudicacion de ask; 5.e no tiene esas respuestas.
Antes de producto, acordar referencia reconstruible (conservar 5.e como historia
y las 55 preguntas recuperadas), procedimiento de control aislado
y criterio de paridad por pregunta/total/categoria. No importar automaticamente
el +/-1 de olas 5/6 ni el comparador de calibracion 12.10.

### Propuesta concreta consultada tras la autorizacion

**Sin respuesta; no aprobada ni ejecutada.** La consulta no volvio a pedir
implementacion ni tiempo: pidio resolver el cambio algoritmico y la referencia.

- Conservar los cinco JSON 5.e intactos. Cohorte principal de 116 preguntas:
  55 bsuite-repo, 16 auditorias, 26 docs y 19 tickets. Son 104 con anclas textuales
  y 12 negativos, aun no n efectivo validado; repro2 es una replica, no otra cohorte.
  Los 29 casos posteriores serian suplementarios, nunca sustitutos.
- Construir control local aislado desde el original y candidato remoto sobre
  el mismo snapshot. Registrar referencia pareada nueva de search/ask con
  procedencia completa. No presentar 5.e como comparable automaticamente
  ni reemplazarlo sin decision expresa.
- Dos corridas pareadas completas; cero perdidas por pregunta de hit_any@10
  e hit_full@10 frente al control. Ninguna fabricacion nueva ni transicion
  de respuesta correcta a incorrecta/abstencion en ask, con adjudicacion sustentada.
  Reportar tambien @1/3/5/10, categorias y perdidas individuales. Negativos,
  filtros, composicion, errores parciales y rollback obligatorios.
- Candidato inicial propuesto: codigo Denso y resumen separado donde corresponda;
  RRF de esos dos rankings en Rag, pesos 1/1 y k=60 como hipotesis no calibrada;
  pool remoto de 100 por espacio y pool primario de 100. TopK publico 1..100;
  salto por simbolos y reranking en Rag. No equivale a tres ramas y cambia pools.
  MinScore incompatible con el piso configurado del servidor se rechaza,
  no se ignora. Si no supera el criterio, no se activa ni se bajan umbrales.

La alternativa consultada fue preparar explicitamente la ampliacion contractual
si se exige equivalencia algoritmica. Antes de producto siguen pendientes la
resolucion de esta eleccion, escalas/desempates/presupuesto exacto de saltos,
orden de las corridas, rubricado verificable de ask, configuracion y congelacion
de corpus/anclas. Esta propuesta concreta **no es todavia un oraculo completo**.
El labeled-set de abstencion podria ser suplemento, nunca reemplazo silencioso.

Un futuro runner debe fallar con evidencia/corpus/infra ausentes, cero tests,
skips o casos requeridos omitidos, identidad de instrumento distinta y parejas
incompletas. Registrar `verificacion_comando` solo cuando exista y cubra TODO
el criterio. No se crea un comando ficticio ni se modifica el instrumento en
esta fase documental.

## Corpus, misses y horas de maquina

Inventario completado: artefactos de evaluacion anteriores. Tras la autorizacion
se comprobo solo existencia de las tres carpetas locales siguientes, sin leer
sus fuentes, crear snapshots ni consultar red/indices:

| Corpus local | Uso historico, no ingesta ejecutada |
|---|---|
| `D:\Git\BusinessSuite.Xaf` | bsuite-repo completo y tres subcarpetas para auditorias |
| `D:\Git\docs-bsute-innovapp-plan` | innovapp-docs |
| `D:\Git\Reyma.TI.Tickets.Microservice` | micro-repo, eval-set tickets-microservice |

`replicate-env\scripts\04-ingest-collections.sh` del original identifica para
auditorias las subcarpetas `src\Modules\REYMA.XAFR1PV.Compras\Auditorias`,
`src\Modules\REYMA.XAFR1PV.Compras\Compras\Utils` y
`src\Modules\REYMA.XAFR1PV.Base`. Se leyo el procedimiento historico,
**no se ejecuto** ni se comprobaron aun esas subcarpetas. Historial con resumen
en ambos corpus bsuite y sin resumen en docs/tickets: no autoriza omitirlo
del control/candidato donde corresponda.

**No hay inventario completo del corpus:** faltan revisiones/snapshots,
documentos elegibles, hashes, anclas comprobadas y conteos de ambos contratos.
La presencia de carpetas no prueba identidad con 5.e ni cobertura del indice.
No se accedio a otros repositorios corporativos por red ni a colecciones servidas.

Se encontro por nombre la cache heredada y archivos WAL/SHM en
`R\replicate-env\data\`; no se abrio, copio ni conto su contenido. Su presencia
no prueba que haya un escritor activo ni permite una copia consistente por
copiar solo el .sqlite3.

Antes de ingerir: inventariar documentos elegibles y chunks/hash por cada
contrato; fijar prompt_version/modelo; contar claves unicas y misses
`(content_hash,prompt_version)` contra cache aislada consistente. El sentinel
es acierto de cache, el error no. La cache del servidor es en memoria y su clave
incluye modelo: no asumir que la SQLite de Rag evita sus llamadas LLM.
No existe un conteo de misses realizado en esta conversacion.

| Partida | Estimacion/conteo actual |
|---|---|
| Preparacion documental y separacion Git | 0.25 h estimadas, no cronometraje de maquina |
| Continuacion documental tras aprobacion de implementacion | 0.1 h adicionales estimadas, no cronometraje ni consumo de tokens medido |
| Reserva tecnica historica | 4 h numericas provisionales: 1 pruebas + 1.42 embedding/upsert candidato + 1 eval + 0.58 margen; incompleta |
| Reconstruccion y embeddings del control | Pendiente de corpus/modelos/configuracion; NO cero |
| Embeddings codigo/resumen del candidato | Recontar por contrato y ambas bandas; 1.42 h es referencia historica, no medida vigente |
| Resumenes control/candidato | Misses, tokens y throughput separados pendientes; 19 h es referencia historica de regeneracion total, no presupuesto universal |
| Consumo ejecutado aqui | 0 ingestas/consultas a indices/embeddings/resumenes/generaciones LLM; 0 servicios iniciados |

Sumar tiempos de reconstruccion, embeddings, resumenes, evaluacion y rollback
para AMBOS brazos; registrar conteos/tokens/tiempos reales. No omitir resumenes o
control para encajar en 4 h ni pedir mas aprobacion solo por duracion.

## Aislamiento, rollback y reentrada

Colecciones exclusivamente de prueba, identificadas por corrida, con caches
SQLite/auditoria/estado separadas por brazo y escritor. No iniciar el compose
heredado contra volumenes/caches compartidos. Nunca dos escritores host/contenedor
en el mismo WAL; si un procedimiento necesita parar un consumidor compartido,
su impacto necesita autorizacion especifica, no se lo detiene automaticamente.

Rollback actual: revertir solo los commits documentales de 1.12 en sus
repositorios respectivos. Original Rag sin cambios; no borrar el clon.
Rollback futuro: seleccionar backend local conservado, revertir commits de
composicion y restaurar configuracion/perfiles del control como unidad.
Un error remoto nunca selecciona local automaticamente. Revertir codigo no
revierte puntos ya escritos; no modificar/eliminar colecciones servidas.

Reentrada: NO volver a pedir implementacion/activacion ni tiempo; resolver
la propuesta metodologica sin respuesta, cerrar ranking/contrato y oraculo
pareado; inventariar/congelar los corpus locales y contar misses;
congelar ambos brazos y recursos aislados antes de marcar en_curso. Publicacion
GitHub requiere propietario/destino confirmados y autorizacion separada; sin push.
`1.21.5-definir-tenencia-del-cliente` y
`1.21.6-definir-autorizacion-del-cliente` siguen bloqueadas, sin peticion expresa.
No hay otra ficha autorizada y ejecutable para sustituir esta preparacion.

Comprobaciones documentales y commits se registran en `resultado` del ledger.
`arnes validar` comprueba fichas, no ejecuta la aceptacion de arriba.
