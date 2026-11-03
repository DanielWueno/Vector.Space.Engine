# Prompt de relevo - solo VectorSpace/1.12

Copia el primer bloque VIGENTE en una conversacion nueva. Los bloques anteriores
se conservan despues como historia, no como instrucciones actuales.

```text
Retoma unicamente 1.12-decidir-si-rag-context-engine-pasa-a-cliente de
Vector.Space.Engine. Sin subagentes; una conversacion, un item. Ninguna otra ficha.

REPOSITORIOS Y FUENTE DE VERDAD
- D:\Git\Vector.Space.Engine: rector docs\plan\ejecucion-plan.estado.json.
- D:\git\Rag.Context.Engine: original SOLO LECTURA, base
  3c3972b90b74ee3d9cd7b86945e07d4775a9c965; ledger
  docs\analisis-futuro\ejecucion-plan.estado.json.
- D:\Git\Rag.VectorSpace.Client: Git independiente, rama
  feature/vectorspace-client, sin remote/push. Su ledger tiene solo la nota
  _bifurcacion_1_12 del trabajo actual; 162 fichas heredadas intactas.
  Implementacion y resultado parcial persistidos en
  3bf19259a91756f14c27dbe500be2be98d71e0c6 (51 archivos del item).
  El commit de VectorSpace que contiene este relevo coordina la ficha/evidencia.
Leer instrucciones/ledgers completos, ficha 1.12 completa, sus fuentes,
git status/diff incluidos staged y commits actuales. Conservar cambios ajenos.
Leer _implementacion_y_medicion_2026_11_03, resultado y reentrada del rector,
preparacion.md y protocol.json del cliente. El rechazo no desaparece por
compilar ni porque las notas historicas dijeran que no habia producto.

PROHIBICIONES
NO leer/buscar/hashear/modificar/copiar al build ni incluir en diff/commit
src\Reyma.VectorSpace.Engine.Microservice.Api\appsettings.json de VectorSpace.
Tiene cambio ajeno. No git add -A. Preservar Properties/ sin seguimiento
del original Rag; no copiarlo ni incorporarlo. No modificar original/19.6.
No colecciones servidas, cambios de credenciales/permisos, despliegues,
tuneles nuevos/modificados, acceso corporativo adicional ni push.
No ejecutar compose/configuracion heredada.

AUTORIZACIONES Y DECISIONES VIGENTES
Cliente COMPLETO Local, no solo brain: VectorSpace posee fragmentacion,
embeddings, lectura/escritura del indice y resumen indexado. Rag conserva
API/CLI, fusion, simbolos, reranking, contexto, generacion y fuentes.
Original/backend local conservados; rollback explicito, no fallback.
Ya estan aprobados implementar en el clon, ingestas/pruebas aisladas,
ranking diferente con nueva referencia pareada estricta, tiempo y consumo
AltheIA SIN LIMITE exclusivamente para 1.12, y corpus completo si necesario.
No repetir aprobaciones ni tratar el 1 M/dia del ejemplo como cuota verificada.
Mantener estimacion numerica (40 h provisional), contar consumo y respetar
limites reales/429, nunca eludirlos.

PROVEEDORES E IDENTIDAD
Ambos brazos usan Snowflake/snowflake-arctic-embed-m-v2.0 por el tunel
existente 8081, 256d Matryoshka/L2, query prefix "query: ", TEI1.8.3,
ventana8192, lote16384, servicioSHA78502d8e61223d2c73d4bb7aeaea46787e90d596.
Ese SHA NO es una revision de pesos; el servicio no la expone.
Resumen y generacion: AltheIA qwen3.8-27b-fast con TLS/CA existentes.
ALTHEIA_KEY/ALTHEIA_CERT se consumen en proceso, sin imprimir/persistir valores
ni contenido de certificados. No ejecutar el ejemplo preguntar.ps1.
El control adapta el pipeline local a esos proveedores; NO es el runtime
original intacto ni el baseline5.e. No montar modelos locales ni sustituir bge-m3.
No usar el Qdrant del tunel6333.

RESULTADO QUE OBLIGA A PARAR Y REPREPARAR
Cliente opt-in implementado y funcional, pero 1.12 esta BLOQUEADO por calidad.
Protocolo v3 congelado antes de medir: cohortes completas docs, tickets,
bsuite-repo y auditorias, orden inverso ronda2. Rechazo temprano permite
detener consumo ante un contraejemplo, nunca aprobar trabajo no medido.
En .artifacts\1.12-paired-r1 se obtuvieron 124 respuestas HTTP200, 31 pares
completos search/ask: 26docs y 5tickets, solo ronda1.
Contraejemplo tickets-microservice-005 (adjuntar documento/evidencia):
control hit_any@10=true, hit_full@10=true; candidato true/false,
tanto vista historica como corregida.
RegistroTicketDocumentoValidationMessages aparece en control rango7,
validator lineas14-21; candidato rango10 trae otro chunk, lineas25-29,
sin esa ancla. Ambos tienen TicketDocumentoRequest request en rango1.
Recomputacion independiente con normalizacion de separadores/basename,
comparacion ordinal y hashes de TODOS los bytes fuente confirma la perdida.
No fallo de infraestructura ni prueba de que sparse sea la unica causa:
tambien cambian chunking/pools. No afinar parametros despues del resultado
ni repetir la corrida para borrar el rechazo.

REENTRADA DE ESTA MISMA FICHA
Preparar una hipotesis revisada y protocolo antes de otra medicion, manteniendo
criterio/corpus/anclas y procedencia comparable de ambos brazos. Si requiere
ampliar contrato/alcance del servidor, concretar y obtener esa autorizacion;
no ejecutar otra ficha para hacerlo. Resolver reanudacion/reuso de indices y
cache compatibles antes de trabajos largos: el runner actual exige salida nueva.
La decision TOTAL no se reabre ni se reduce silenciosamente a solo brain.
No falta presupuesto; falta una alternativa que supere el criterio rechazado.
No activar como sustituto aceptado, no marcar hecho.

EVIDENCIA Y LIMITES
- 110 tests enfocados pasan, 0fallos/omitidos; guardia contra cero tests.
  CLI/API/harness compilan con 0errores/0warnings nuevos.
- Fixture real r3/r4 pasa: cuatro fuentes, ingesta/idempotencia, JWT401,
  restricciones observadas en HTTP de codigo/resumen/simbolos, search/ask,
  429/Retry-After, borrado de documento de prueba, 404 desconocido.
- 18 fallos Windows al ampliar tests de contexto se reprodujeron EXACTAMENTE
  en copia aislada de la revision original (CRLF/goldens y poolsSQLite).
  No arreglarlos aqui ni modificar 19.6/goldens/harness ajenos.
- Corpus completos docs/tickets:15/553 archivos,743/1855 chunks control,
  629/1707 candidato. Resumenes false desde el protocolo inicial.
- Corrida primaria888.696s; control2598 textos/712096 tokens de embeddings
  de documentos, sin truncamiento. Fixtures r3/r4:8 resumenes por brazo total;
  candidato2798 tokens de resumen reportados. No total facturado disponible.
- No se gastaron los31880 misses restantes tras el rechazo.
  Restan otras cohortes/preguntas, ronda2, suplemento29, adjudicacion semantica
  completa y matriz total de riesgos. No paridad ni certificacion ask.
- evaluation.json SHA256:
   9acd079abed96cd43d151d31ae6586aac1f94d9ff746cd951cdd1ce21ff4b511
  protocolo MEDIDO SHA256:
   9a682d542c74096914caff1bc1ad62b5bc7f328a6dbac08258c612c889180232
  rejection-verification.json es comprobacion parcial del rechazo.
  No presentarlo ni Fixture --paired como verificacion_comando completa.
- Datos crudos/corpus/caches/TRX son artefactos locales ignorados, conservados;
  no commitearlos ni publicar fuentes/secretos.

BANCO CONGELADO Y AISLAMIENTO
.artifacts\1.12-20261103-r3:116 preguntas=55bsuite+16auditorias+26docs+19tickets,
104 ancladas y12negativas. Erratas previas solo en derivado docs-020 y tickets-008;
ambas vistas conservadas, instrumentos/5.e originales intactos;29 extras aparte.
SHA instrumento19b9526fcb467b6dfe0470dddfe4a818e3deccd27932346c99cbd984145cf815.
SHA fuentes32a535d48b489795b01b4810de7255c2159642315c492647b8b6371dc0863fd8.
SnapshotsGit:
BusinessSuite.Xaf6a7b52187a076da1202fdc3c9d787b368578648a,
docs-bsute-innovapp-planbb7f1038b5087dfcabdac4bf8cb66b09948506b7,
Reyma.TI.Tickets.Microservicecaa9e5f19eb7ce52a2a432eddfbe6b7adb4bcaba.
Resumen en ambos bsuite: misses18612/13268, versiones0879c221/d6778228,
cero hits compatibles. DiferenciaCRLF/LF no se normaliza para forzar hits.
BackupSQLite consistente r2, hash654bd7d7b724bc4b52fb8a1fc21a557072927580a53386fb692dbae956810c79.
Nunca dos escritores sobre el mismo WAL; original siempre solo lectura.
Cache del candidato es nativo EN MEMORIA, no SQLite; puntos completos persisten.
Qdrant propio v112-qdrant-20261103 DETENIDO, volumen
v112-qdrant-20261103-data conservado, puertosloopback26333/26334,
imagen1.19.2 sha256:b7b0444c4c351c970b98e90a6f89c2ee4287c65b44e52b4cb503fa5b2aa927ad.
HTTP hosts detenidos. No auxiliares de pruebas pendientes. No borrar volumen.

DEPENDENCIAS Y CODIGO
Verificar por lectura directa los ocho adicionales y1.4/1.19; estaban hechos.
1.21.3 literal bloqueado_por no es bloqueo vivo. Rag19.6 sigueen_curso
heredado, no ejecutar/cerrar ni modificar CI/TestCategories/TracingGenerationTests/
GenerationHttpHarnessTests. No1.21.5/1.21.6 sin solicitud/condiciones nuevas.
Releer contratos/filtros/compatibilidad Requests/Responses deVectorSpace y
evidencia1.18/1.20/1.21.1/1.21.4/1.21.3. Cliente: VectorSpace/,DI,API/SourceDto,
CLI,testsVectorSpace,docs/eval/README y5.e.
El componente3c3972b9 sigue sin DI EN EL ORIGINAL; EN EL CLON ya hay cliente
completo/composicion real. No repetir el estado historico como actual.
Denso+resumen RRF1/1k60,pool100,topK1..100,escalas explicitas; no tres ramas
independientes. Tenantactivo/TODOAuthorized y Empresarial+remoto fallan antes
de I/O/LLM/rerank, sin quitar claims/JWT. L/N textuales y modulo por rama/salto.
Resumen remoto incluso null autoritativo, metricas/metadatos desconocidos
ausentes, estado parcial/404/429 explicito. No clienteQdrant/SQLite propio
en camino remoto (auditoria local si). No tocar anclas/umbrales tras medir.
Fixture referencia Infrastructure y SOLO4archivos API enlazados, NUNCA el
proyecto API ni appsettings protegido. UtcDateTime Business evita SQL.

PERSISTENCIA Y SALIDA
Anunciar ID/alcance/modelo efectivo y horas; mantener opus/high del plugin;
gpt-6-astra efectivo hasta ahora, esfuerzo no expuesto. Sin subagentes.
Comandos de aceptacion separados de edicion; arnes valida ficha, no aceptacion.
Commits coordinados solo1.12+ledgers, autor/committerDWeno, sin coautoria ni
firmas de herramientas, amend o push. Revisar gitlog y archivos de cada commit.
Resultado parcial y cambios deben quedar persistidos; no llamar cerrado al
item bloqueado. Entregar evidencia, limites, commits y relevo completo.
No hay otra ficha autorizada y ejecutable para continuar en su lugar.
```

## Relevo anterior de preparacion (historia, no ejecutar)

```text
Retoma unicamente 1.12-decidir-si-rag-context-engine-pasa-a-cliente de
Vector.Space.Engine. Sin subagentes; una conversacion/un item; ninguna otra ficha.

REPOSITORIOS Y LECTURAS
- D:\Git\Vector.Space.Engine: rector docs\plan\ejecucion-plan.estado.json.
- D:\git\Rag.Context.Engine: original SOLO LECTURA; ledger
  docs\analisis-futuro\ejecucion-plan.estado.json.
- D:\Git\Rag.VectorSpace.Client: clon Git independiente, rama
  feature/vectorspace-client, sin remote/push. Base original 3c3972b90b74ee3d9cd7b86945e07d4775a9c965.
  Su ledger heredado conserva 162 fichas intactas; solo _bifurcacion_1_12
  pertenece a este trabajo. No elegir fichas heredadas.
Lee instrucciones y ledgers completos, ficha 1.12 completa, fuentes,
git status/diff incluidos staged; preservar cambios ajenos.
Leer _servicios_remotos_2026_11_03, _autorizacion_consumo_altheia_2026_11_03,
resultado/reentrada y docs\evaluaciones\1.12\preparacion.md.
Verificar HEAD/commits actuales; no tomar los commits antiguos del relevo
historico como si fueran el estado final.

PROHIBICIONES
NO leer/buscar/hashear/modificar/copiar al build ni incluir en diff/commit
src\Reyma.VectorSpace.Engine.Microservice.Api\appsettings.json de VectorSpace.
Tiene un cambio ajeno. No usar git add -A. Preservar el Properties/ sin
seguimiento del original Rag; no copiarlo ni commitearlo.
No colecciones servidas, credenciales nuevas/cambiadas, permisos, despliegues,
SSH nuevo, cambios del tunel o push. No correr compose/configuracion heredada.

DECISIONES YA APROBADAS
Cliente COMPLETO Local, no solo brain: VectorSpace es dueno de fragmentacion,
embeddings, lectura/escritura del indice y resumen indexado. Rag conserva
API/CLI, fusion, simbolos, reranking, contexto, generacion y fuentes.
Original y control local se conservan; rollback explicito, nunca fallback.
Tiempo ilimitado aprobado SOLO para 1.12. Implementacion en el clon e
ingestas/pruebas aisladas aprobadas expresamente. "ok, realizalo" aprobo
ranking diferente, referencia pareada nueva y criterio estricto, conservando
5.e historico. NO repetir estas aprobaciones.

CORRECCION POSTERIOR DEL USUARIO: SERVICIOS EXISTENTES
Usar Snowflake del tunel existente y AltheIA ya configurada; solo Qdrant de
pruebas local aislado. Mismos modelos en ambos brazos. El control sera una
adaptacion de pruebas del pipeline local, NO runtime original ni 5.e.
No montar Ollama/TEI locales ni sustituir Snowflake por bge-m3 silenciosamente.
Puertos indicados y comprobados escuchando en loopback: 6333 y 8081.
NO se consulto ni se debe usar el Qdrant compartido de 6333 para esta prueba.
GET /info en 8081 confirmo Snowflake/snowflake-arctic-embed-m-v2.0,
float32, max_input_length=8192, max_batch_tokens=16384, TEI 1.8.3,
commit de SERVICIO 78502d8e61223d2c73d4bb7aeaea46787e90d596.
No se expone revision de pesos: no confundirla con ese SHA.
AltheIA /v1/models responde con bge-m3, qwen3.8-27b-deep y
qwen3.8-27b-fast; usar fast como el ejemplo del usuario.
ALTHEIA_KEY/ALTHEIA_CERT existentes se consumen en proceso con TLS validado,
sin revelar valores ni contenido de certificados, ni cambiar credenciales.
El ejemplo PowerShell se inspecciono solo por AST; NO ejecutarlo:
puede modificar credenciales/archivos de uso. No leer appsettings protegido.
El probe de metadatos reutiliza HttpClient/TLS de Infrastructure, no API.
Todavia cero requests de embeddings/LLM/Qdrant y cero servicios iniciados.

CONSUMO YA AUTORIZADO SIN LIMITE
Despues de la consulta de cuota sin respuesta, el usuario respondio:
"altheia no tiene presupuesto, tu usalo sin un limite
Pero igual no creo haga falta consumir todo un corpus para estas medidas,
si es necesario entonces realizalo".
No volver a solicitar presupuesto de horas/tokens. Primero fixtures pequenos
para fallar barato; despues corpus congelado completo para el criterio pareado,
porque quitar distractores puede facilitar artificialmente la busqueda.
Reusar cache/trabajo compatible; respetar limites reales/429, nunca eludirlos.
El ejemplo declara 1 M tokens/dia: no es cuota disponible verificada ni
reserva exclusiva para esta ficha. Hay 31880 misses unicos de resumen.
Referencia historica de 1.7.3, ~670 tokens/chunk: ~21.36 M solo resumen,
unas 22 ventanas de 1 M si fueran completas, mas ask/reintentos.
Es estimacion, NO medida; la ausencia de respuesta fue superada por la
autorizacion posterior. Sellar banco/configuracion/procedencia antes
de ingestas masivas. La reserva numerica de 40 h maquina es provisional,
no limite ni calendario; tiempo ilimitado sigue aprobado.
Instrumentar consumo real por fase y reanudacion. No reutilizar el tope de
otra ficha, omitir resumen/control ni reducir corpus para simular aceptacion.
El rector esta en_curso antes de producto, no hecho.

PREPARACION YA REALIZADA
Protocolo ACTIVO del clon: docs\eval\quality\1.12\protocol.json.
El protocolo local anterior queda en protocol-local.pre-altheia.json
como historia sustituida ANTES de producto/medicion.
Snapshots/modelo de preparacion en .artifacts\1.12-20261103-r3; r2 conserva
backup SQLite consistente y remote-preflight.json. Son ignorados, no commits.
prepare.py fija fuentes Git, no working trees:
BusinessSuite.Xaf 6a7b52187a076da1202fdc3c9d787b368578648a;
docs-bsute-innovapp-plan bb7f1038b5087dfcabdac4bf8cb66b09948506b7;
Reyma.TI.Tickets.Microservice caa9e5f19eb7ce52a2a432eddfbe6b7adb4bcaba.
Fuentes por extensiones; sin configuraciones, symlinks ni archivos >512 KiB.
Instrumento SHA256 19b9526fcb467b6dfe0470dddfe4a818e3deccd27932346c99cbd984145cf815.
Fuentes SHA256 32a535d48b489795b01b4810de7255c2159642315c492647b8b6371dc0863fd8.
116 preguntas = 55 bsuite + 16 auditorias + 26 docs + 19 tickets.
104 ancladas cubiertas en chunks de AMBOS motores, 12 negativas.
Erratas previas verificadas SOLO en derivado: elipsis docs-020 y mayuscula
tickets-008. Conservar ambas vistas, originales y 5.e; medir ambos brazos
con igual instrumento. Las 29 preguntas bsuite posteriores son suplemento.
Chunks control/candidato: 23221/17628, 2081/1792, 743/629, 1855/1707.
Resumen habilitado en ambos corpus bsuite de ambos brazos; no docs/tickets.
Con qwen3.8-27b-fast: prompt_version control 0879c221, candidato d6778228;
misses 18612/13268, cero hits compatibles. Diferencia de prompts CRLF/LF
verificada: NO normalizar claves ni inventar hits. Backup SHA256
654bd7d7b724bc4b52fb8a1fc21a557072927580a53386fb692dbae956810c79.
Nunca dos escritores sobre WAL. No modificar cache original.

DEPENDENCIAS Y LIMITES
Comprobar nuevamente estados por lectura directa. Estaban hechos:
1.18-exponer-metadatos-de-chunk-en-busquedas
1.20-busqueda-explicita-sobre-banda-resumen
1.21-decidir-filtro-de-modulo-en-busquedas
1.21.1-implementar-filtro-exacto-de-modulo
1.21.2-preparar-compatibilidad-de-filtros-del-cliente
1.21.4-implementar-filtros-textuales-de-lenguaje-y-namespace
1.21.3-implementar-guardias-y-traduccion-de-filtros-del-cliente
1.22-reparar-dos-pruebas-rojas-preexistentes
Tambien 1.4-portar-codigo-del-motor y
1.19-listar-documentos-indexados-de-una-coleccion, este ultimo fuera de las
ocho adicionales. bloqueado_por apunta a 1.21.3 hecha, no es bloqueo actual.
Rag/19.6-ci-que-distingue-fallos-reales-de-falta-de-infraestructura sigue
en_curso; no ejecutar/cerrar/arreglar su CI/categorias ni sus harness
TracingGenerationTests y GenerationHttpHarnessTests. Pruebas propias de
1.12; parar si no es posible separar archivos.

CODIGO, CRITERIO Y ROLLBACK
Releer contratos/filtros/compatibilidad de VectorSpace, Requests/Responses
de Colecciones y evidencia 1.18/1.20/1.21.1/1.21.4/1.21.3.
Releer SourceDto/Program API, ISemanticRetriever, QdrantSemanticRetriever,
ServiceCollectionExtensions, GenerationContextAssembler, VectorSpace/
y tests/VectorSpace/ del clon, docs/eval/README y 5.e.
Componente 3c3972b9 sigue sin DI; no es cliente completo ni guardia de host.
No hay cambios de producto: implementarlos solo tras preparar el banco.
Protocol.json fija pools/escalas: codigo Denso + resumen, RRF1/1 k60,
pool100 por rama/primario, topK1..100; no equivale a tres ramas locales.
MinScore bajo piso servidor se rechaza; no comparar RRF con coseno.
L/N textuales y modulo opcional en codigo/resumen/cada salto ANTES de consumo.
TODO Authorized y tenant activo se rechazan. Empresarial+remoto falla desde
composicion REAL API/CLI antes de catalogo/embeddings/rerank/LLM, incluyendo
meta-intent, sin quitar claims/JWT ni caer a Local.
Resolver perfiles/manifiestos sin Qdrant propio; resumen remoto incluso
null autoritativo en SourceDto y contexto Simple, sin cache local sustitutiva.
Metadatos ausentes no se inventan; ingesta/estado remoto muestra omitidos,
pendientes, 429/Retry-After, fallos y 404/desconocido tras reinicio.
Dos rondas /api/search y /api/ask, mismo instrumento, 928 respuestas primarias;
sin perdida por pregunta de hit_any@10/hit_full@10 ni correcta->incorrecta/
abstencion, ninguna fabricacion nueva. Adjudicacion ask sustentada con
fuentes, negativos/riesgos y ambas vistas del instrumento.
Fallo de infraestructura/evidencia, cero tests/skips requeridos o faltantes
de pares/resumen/adjudicacion deben fallar. No verificacion_comando parcial.
No anclas/umbrales/corpus cambiados tras medir, ni paridad sin medida.
Rollback explicito a control retenido, no fallback; codigo no revierte indices.
Qdrant1.19.2 y caches/estado separados por brazo, sin indices full-text nuevos.

CIERRE
Anunciar ID/alcance/modelo efectivo y horas; mantener opus/high del plugin,
registrar _ejecucion, no inventar esfuerzo. Ultima sesion gpt-6-astra,
esfuerzo no expuesto. Sin subagentes. Aceptacion separada de edicion;
arnes validar solo valida ficha. No marcar hecho resultado parcial.
Commits coordinados solo del item/ledgers en VectorSpace y clon, original
intacto, autor/committer DWeno, sin Co-authored-by ni firma, amend o push.
Comprobar git log -1 --format=%B y archivos incluidos tras cada commit.
Entregar resultado/evidencia/limites/commits y relevo completo.
No hay siguiente item autorizado: 1.21.5-definir-tenencia-del-cliente y
1.21.6-definir-autorizacion-del-cliente requieren peticion expresa propia.
```

## Relevo anterior hasta 832b31a (historia, no ejecutar)

Copia el texto siguiente en una conversacion nueva:

```text
Retoma unicamente 1.12-decidir-si-rag-context-engine-pasa-a-cliente de
Vector.Space.Engine. Sin subagentes, una conversacion/un item.
No ejecutes ninguna otra ficha, tampoco las heredadas en la bifurcacion.

REPOSITORIOS
- D:\Git\Vector.Space.Engine
  Ledger rector: docs\plan\ejecucion-plan.estado.json.
- D:\git\Rag.Context.Engine
  Original conservado como motor local; solo lectura.
  Ledger: docs\analisis-futuro\ejecucion-plan.estado.json.
- D:\Git\Rag.VectorSpace.Client
  Repositorio Git independiente creado por peticion posterior del usuario,
  desde 3c3972b90b74ee3d9cd7b86945e07d4775a9c965.
  Rama feature/vectorspace-client; no worktree, no alternates, sin remote/push.
  Preparacion documental commiteada en
  4e328bc7765033ea2f04a96d163dd1b534a5e017 (sin cambios de producto).
  Autorizacion expresa y bloqueo metodologico registrados despues en
  e7b1b7375a51322d99b7cc8087634cfb7a968603, tambien solo documentacion.
  Consultar status/log actual y el resultado del ledger rector.
  Ledger heredado: docs\analisis-futuro\ejecucion-plan.estado.json.
  La nota _bifurcacion_1_12 conserva procedencia; las 162 fichas heredadas no
  son una agenda nueva. La unica fuente de avance de 1.12 sigue en VectorSpace.

Lee las instrucciones de los tres repositorios, ambos ledgers de origen
completos y la nota del clon. Lee completa 1.12 y sus fuentes, incluidas
_decision_alcance_2026_11_03, _decision_compatibilidad_2026_11_03,
_solicitud_aprobacion_2026_11_03 y las actualizaciones posteriores a 122755f.
En particular, leer _autorizacion_implementacion_posterior_b50eb6b,
_propuesta_oraculo_posterior_b50eb6b y
_instrumento_recuperado_posterior_b50eb6b. Las solicitudes anteriores sin
respuesta son historia; NO describen la autorizacion actual.
Lee docs\evaluaciones\1.12\preparacion.md de VectorSpace; sus propuestas no
son un protocolo experimental completamente congelado ni aceptacion funcional.
Comprueba status/diff y staged de los tres repositorios, preservando cambios ajenos.

PROHIBICIONES
En VectorSpace NO leas, busques, hashees, modifiques ni incluyas en diff/commit:
src\Reyma.VectorSpace.Engine.Microservice.Api\appsettings.json.
Tiene un cambio ajeno. Excluyelo de las operaciones; no uses git add -A.
Preserva src\RagEngine.Api\Properties\ sin seguimiento en el original Rag:
no pertenece a 1.12 y no se copio al clon.
Sin colecciones servidas, despliegues, cambios de credenciales/permisos,
acceso corporativo adicional ni push. No inicies servicios/configuracion
heredados del clon: podrian apuntar a recursos compartidos.

DECISION Y AUTORIZACIONES
122755f decidio alcance TOTAL, no solo brain. VectorSpace sera dueno de
fragmentacion, embeddings, indice y resumen indexado. El cliente conserva
API/CLI, fusion/orquestacion, salto por simbolos, reranking, contexto,
generacion y fuentes. La bifurcacion conserva el proyecto original; no cambia
esa decision ni demuestra que el cliente este implementado.

Tiempo: el usuario aprobo el 2026-11-03 "te apruebo uso de tiempo sin limite",
solo para 1.12. Ya registrado como actualizacion posterior a 122755f.
No volver a pedir aprobacion solo por duracion. Mantener estimacion numerica,
inventario, misses, horas/tokens reales de ambas ramas. Las 4 h del ledger
son una estimacion historica incompleta, no limite ni bloqueo.

Alcance YA APROBADO, 2026-11-03 a las 12:21, posterior a b50eb6b:
"Autorizo implementar 1.12 en D:\Git\Rag.VectorSpace.Client y ejecutar ingestas
y pruebas aisladas, conservando el original y sin tocar colecciones servidas,
credenciales, despliegues ni hacer push."
No volver a pedir esta autorizacion. No se implemento todavia: se consulto
una propuesta concreta para resolver el ranking diferente y la referencia
pareada nueva, y el usuario no estuvo disponible. El bloqueo restante es
metodologico, NO presupuestario ni ausencia de aprobacion de implementacion.
Resolver ese oraculo y la preparacion antes de marcar en_curso/tocar producto.

GitHub: no se creo repositorio remoto. El usuario pidio titularidad personal
unica y escribio DanielWeno; gh api user mostro DanielWueno. La aclaracion
no obtuvo respuesta. Confirmar propietario/destino antes de crear remoto;
no interpretar Git local como garantia de permisos exclusivos. Sin push.

DEPENDENCIAS: verificar IDs completos y estado por lectura actual.
Las ocho adicionales estaban hechas:
1.18-exponer-metadatos-de-chunk-en-busquedas
1.20-busqueda-explicita-sobre-banda-resumen
1.21-decidir-filtro-de-modulo-en-busquedas
1.21.1-implementar-filtro-exacto-de-modulo
1.21.2-preparar-compatibilidad-de-filtros-del-cliente
1.21.4-implementar-filtros-textuales-de-lenguaje-y-namespace
1.21.3-implementar-guardias-y-traduccion-de-filtros-del-cliente
1.22-reparar-dos-pruebas-rojas-preexistentes
Tambien 1.4-portar-codigo-del-motor y
1.19-listar-documentos-indexados-de-una-coleccion estaban hechos.
1.19 NO pertenece a las ocho adicionales. bloqueado_por sigue apuntando a
1.21.3 hecha: no es el bloqueo actual.

Rag solo tenia en_curso
19.6-ci-que-distingue-fallos-reales-de-falta-de-infraestructura.
No ejecutarlo/cerrarlo ni arreglar su CI/categorias/TracingGenerationTests/
GenerationHttpHarnessTests. En el clon conservar su historia intacta y usar
pruebas propias de 1.12; si es imposible separar archivos, parar/documentar.
El componente de filtros de 3c3972b9 sigue sin DI y no implementa retrieval
completo: comprobar que no haya cambiado, sin confundir sus tests con
composicion real o aceptacion de 1.12.

LECTURAS TECNICAS
VectorSpace: docs\evaluaciones\1.21\compatibilidad-cliente.md,
docs\servicio\filtros.md, docs\servicio\contrato-api.md,
Business\Models\Requests\ColeccionesRequests.cs y
Business\Models\Responses\ColeccionesResponses.cs bajo src\
Reyma.VectorSpace.Engine.Microservice.Business;
fichas/evidencia de 1.18, 1.20, 1.21.1, 1.21.4 y 1.21.3.
Rag/clon: Api\Contracts.cs (SourceDto), Api\Program.cs, Core\
Abstractions\ISemanticRetriever.cs e IVectorStoreAdmin.cs,
Core\Infrastructure\VectorStore\QdrantSemanticRetriever.cs y
RetrievalProfileResolver.cs, Core\Infrastructure\VectorSpace\,
Core\Extensions\ServiceCollectionExtensions.cs, tests\
RagEngine.Core.Tests\VectorSpace\, docs\eval\README.md y
docs\eval\quality\5.e\. Las rutas de producto comienzan en src\RagEngine.*.

PREPARACION PENDIENTE, ANTES DE PRODUCTO
1. Fijar rankings/escalas/umbrales/pools. VectorSpace ofrece codigo Denso O
   FusionNativa segun instancia y resumen separado, no los tres rankings
   independientes de RRF ponderada local. El salto de simbolos tambien
   sigue el modo de codigo. topK remoto <=100 sin cursor; K=10 con rerank
   requiere prefetch local 120. MinScore no viaja hoy en el cliente.
   No recortar pools, cambiar escalas, quitar sparse/resumen ni prometer
   equivalencia sin decision explicita y criterio previo.
2. Resolver perfiles/manifiestos sin Qdrant propio, metadatos remotos
   ausentes, ingesta/estado (incluido estado desconocido tras reinicio),
   resumenes pendientes y traslado del texto remoto a SourceDto sin cache
   local. No inventar salud, hashes o metadatos ni esconder parciales.
3. Mantener lenguaje/namespace textuales y modulo opcional con restricciones
   en codigo/resumen/cada salto antes de semillas/reranker/LLM/fuentes.
   Rechazar tenant explicito activo y TODO Authorized. No convertir
   Empresarial a Local ni quitar filtros/claims; mantener JWT de VectorSpace.
4. Probar composicion REAL API y CLI: Empresarial+remoto incompatible
   falla antes de catalogo/consulta/embeddings/reranker/LLM, contadores cero,
   sin fallback. Los tests existentes del traductor no acreditan esto.
5. Resolver oraculo y baseline: 5.e contiene recall, NO respuestas ask.
   bsuite-repo 5.e tiene 55 preguntas (47+8); actual 84 (76+8).
   Las 55 se reconstruyeron EN MEMORIA por Question, en el orden de
   results[].question del baseline, con objetos/anclas completos:
   son los primeros 55 del eval-set actual. json.dumps(cohort,
   ensure_ascii=False, indent=2) + "\n", UTF-8, da SHA256
   5c0b6532ac7826f4afd7cbdeefd590a94e44b0cf284614b298b017ce9bc3c08a.
   Coincide con el prefijo historico 5c0b6532ac78; no se dispone del
   SHA256 historico completo. No afirmar que la cohorte es irrecuperable.
   No se editaron datasets/baselines ni se recupero corpus/indice/modelos.
   9a6e005 no existe como objeto local y el repo no es shallow.
   En los otros tres datasets el hash LF coincide con el prefijo historico;
   los bytes Windows CRLF difieren, registrado sin editar los archivos.
   No sustituir por 6.c/nightly ni importar +/-1 de otras olas.
   Acordar criterio de paridad, referencia reconstruible y adjudicacion ask;
   si no es inequivoco, seguir preparando sin implementar.
   PROPUESTA CONSULTADA SIN RESPUESTA, no decision aprobada:
   - 116 preguntas principales de 5.e: 55/16/26/19, 104 con anclas
     textuales y 12 negativos; repro2 no duplica casos. Las 29 posteriores
     son suplemento, no sustitucion.
   - Control local aislado del original y candidato sobre mismo snapshot;
     nueva referencia pareada search/ask, conservando 5.e historico sin
     afirmar comparabilidad automatica ni sustituirlo silenciosamente.
   - Dos corridas completas; cero perdidas POR PREGUNTA de hit_any@10 y
     hit_full@10; ninguna fabricacion nueva ni correcta -> incorrecta/
     abstencion en ask. Adjudicacion sustentada, no solo scores; negativos,
     riesgos, filtros, composicion, parciales y rollback obligatorios.
   - Denso codigo y resumen separado, RRF de dos rankings pesos 1/1,
     k=60, pool remoto 100 por espacio y primario 100; TopK publico
     1..100, saltos y reranking Rag. Hipotesis no calibrada y NO equivalente
     a tres ramas. Rechazo visible de MinScore incompatible con piso
     servidor. Si falla, no bajar umbrales ni activar.
   Alternativa consultada: preparar ampliacion contractual si se exige
   equivalencia. Faltan resolver eleccion, escalas/desempates/presupuesto
   de saltos, orden de corridas, rubricado ask y congelacion de entradas.
   No presentar esta propuesta como oraculo completo o ya aceptado.
6. Congelar corpus/hash por documento, anclas/n efectivo, modelos/binarios,
   prompts, versiones/esquemas y configuracion de ambos brazos. Datos
   ausentes no son evidencia. El inventario documental tiene 145 preguntas,
   133 con anclas/12 sin ellas; NO es n efectivo verificado contra indices.
7. Inventariar corpus autorizado y contar misses (content_hash,prompt_version)
   antes de ingerir, con caches consistentes separadas. No se conto la cache
   heredada ni se abrio su WAL. Cache remota es en memoria e incluye modelo;
   no asumir reutilizacion de SQLite. Presupuestar embeddings, resumenes,
   reconstruccion del control y evaluacion de ambos brazos por separado.
   Existen D:\Git\BusinessSuite.Xaf, D:\Git\docs-bsute-innovapp-plan y
   D:\Git\Reyma.TI.Tickets.Microservice (solo Test-Path, no lectura de fuentes).
   El script historico replicate-env\scripts\04-ingest-collections.sh
   identifica las tres subcarpetas de auditorias; NO ejecutarlo contra
   nombres servidos. Faltan snapshot/hash/anclas/chunks/misses: existencia
   no prueba disponibilidad de las anclas ni identidad con 5.e.

ACEPTACION Y ROLLBACK
Parear /api/search y /api/ask reales con mismo eval-set/baseline acordados,
negativos y riesgos. Conservar perdidas individuales, respuestas y procedencia.
No cambiar corpus/anclas/umbrales tras medir. Si cambia justificadamente el
instrumento, registrar y medir AMBOS brazos. No omitir resumenes o control.
Solo colecciones/caches de prueba por brazo; nunca dos escritores sobre WAL.
Conservar backend e indices locales: rollback por seleccion explicita y
reversion de commits de composicion, nunca fallback automatico. Revertir
codigo/bandera no revierte datos indexados.

No existe aun verificacion_comando de aceptacion completa: no inventarlo.
quality-baseline.py usa otro bundle y continua ante errores; rag eval
--baseline informa regresiones sin fallar. No usarlos solos como cierre.
El futuro runner debe fallar por falta de infraestructura/evidencia,
cero tests, omisiones, cobertura incompleta y parejas no comparables.
Ejecutar aceptacion en llamada separada de edicion y registrar salida/limites.
arnes validar solo comprueba ficha, no aceptacion funcional.

COMMIT Y CIERRE
Anunciar ID/alcance/modelo y esfuerzo efectivos (no inventar esfuerzo) y
horas_maquina. Registrar procedencia en _ejecucion sin cambiar opus/high
del plugin. Un item; preservar notas/resultados historicos.
Commits coordinados solo de 1.12 en VectorSpace y el clon, con sus ledgers
y resultados; original Rag sin cambios. Autor y committer DWeno,
sin Co-authored-by ni firmas de herramientas, sin amend ni push.
Tras cada commit comprobar git log -1 --format=%B y archivos incluidos.
No marcar hecho resultado parcial. Entregar evidencia, limites, commits
y prompt completo para la siguiente conversacion.

No hay siguiente item autorizado y ejecutable de esta tarea:
1.21.5-definir-tenencia-del-cliente y
1.21.6-definir-autorizacion-del-cliente siguen condicionadas a peticion
expresa y sus entradas, no autorizadas por tiempo ilimitado de 1.12.
El siguiente trabajo es desbloqueo/preparacion de ESTA ficha.
```
