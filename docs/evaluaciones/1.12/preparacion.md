# 1.12 - Preparacion del cliente completo y bifurcacion local

Fecha: 2026-11-03, continuacion posterior a `122755f`.
**Resultado parcial. Producto y aceptacion funcional pendientes.**
Fuente rectora: `docs\plan\ejecucion-plan.estado.json`, ficha
`1.12-decidir-si-rag-context-engine-pasa-a-cliente`. No se ejecuta otra ficha.

## Autorizaciones y repositorios

La decision TOTAL de `122755f` no se reabre: VectorSpace sera dueno de
fragmentacion, embeddings, indice y resumen indexado; el cliente conserva API/CLI,
orquestacion, fusion de espacios, salto por simbolos, reranking, contexto y generacion.
No se reduce a sustituir el brain ni se retira el pipeline local.

El usuario aprobo posteriormente **"te apruebo uso de tiempo sin limite"**, solo
para 1.12. Ya no hay bloqueo presupuestario de 4 h. La consulta historica sin
respuesta permanece intacta. La nueva consulta sobre IMPLEMENTAR y ACTIVAR pruebas
aisladas tampoco obtuvo respuesta; no se interpreta como consentimiento.

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
soportadas y criterio de calidad acordado. Esta preparacion no elige una variante
en ausencia de autorizacion/oraculo, ni ejecuta otra ficha para resolverla.

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
El de bsuite-repo NO: 5.e declara `5c0b6532ac78` y 55 preguntas (47+8);
el actual agrega 20 de salto y 9 salto-negativo. Las 55 preguntas de 5.e
siguen presentes por texto, pero eso no demuestra identidad de sus anclas.
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
| P0, autorizacion | Consentimiento de composicion/activacion en el clon, recursos aislados concretos y rollback. Estado bloqueado hasta entonces; tiempo ya aprobado. |
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
Antes de producto, acordar referencia reconstruible (conservar 5.e como historia,
resolver sus 55 preguntas frente a 84 actuales), procedimiento de control aislado
y criterio de paridad por pregunta/total/categoria. No importar automaticamente
el +/-1 de olas 5/6 ni el comparador de calibracion 12.10.

Como propuesta a ratificar, no criterio ya aprobado: reportar hit_any/hit_full
@1/3/5/10 y perdidas individuales; requerir cobertura completa, ninguna fabricacion
nueva por pregunta y correctas de ask no inferiores al control, con adjudicacion
de respuestas, no solo score. Fijar margen de recall y replicas antes de medir.
Los 29 casos adicionales y el labeled-set de abstencion pueden ser suplementarios,
pero nunca reemplazar silenciosamente los casos y referencia exigidos.

Un futuro runner debe fallar con evidencia/corpus/infra ausentes, cero tests,
skips o casos requeridos omitidos, identidad de instrumento distinta y parejas
incompletas. Registrar `verificacion_comando` solo cuando exista y cubra TODO
el criterio. No se crea un comando ficticio ni se modifica el instrumento en
esta fase documental.

## Corpus, misses y horas de maquina

Inventario completado: artefactos de evaluacion anteriores. **No inventario
completo del corpus**: faltan snapshots/rutas autorizadas y huellas de las cuatro
fuentes a reconstruir; nombres de colecciones y rutas historicas no son evidencia
de disponibilidad actual. No se inspeccionan otros repositorios corporativos,
ni colecciones servidas, para llenar esa ausencia sin alcance.

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

Reentrada: confirmar implementacion/activacion en el clon; cerrar elecciones
de ranking/contrato y oraculo pareado; obtener corpus/snapshots y contar misses;
congelar ambos brazos y recursos aislados antes de marcar en_curso. Publicacion
GitHub requiere propietario/destino confirmados y autorizacion separada; sin push.
`1.21.5-definir-tenencia-del-cliente` y
`1.21.6-definir-autorizacion-del-cliente` siguen bloqueadas, sin peticion expresa.
No hay otra ficha autorizada y ejecutable para sustituir esta preparacion.

Comprobaciones documentales y commits se registran en `resultado` del ledger.
`arnes validar` comprueba fichas, no ejecuta la aceptacion de arriba.
