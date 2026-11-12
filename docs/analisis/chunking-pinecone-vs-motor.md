# Chunking: estrategias del artículo de Pinecone frente a Vector.Space.Engine + Rag.VectorSpace.Client

Fecha: 2026-11-11. Estado: **plan de Fase 2 aprobado como documento; nada implementado. Las fichas 1.23-1.29 siguen fuera del ledger hasta aprobación expresa.**
Fuente del artículo: https://www.pinecone.io/learn/chunking-strategies/ (leído el 2026-11-11; el resumen obtenido no
menciona solapamiento ni "parent document retrieval", solo "chunk expansion" como post-proceso).
Rutas: `VSE/` = `D:\git\Vector.Space.Engine`, `Infra/` = `VSE/src/Reyma.VectorSpace.Engine.Microservice.Infrastructure`,
`Biz/` = `VSE/src/Reyma.VectorSpace.Engine.Microservice.Business`; `RVC/` = `D:\git\Rag.VectorSpace.Client`,
`Core/` = `RVC/src/RagEngine.Core`. Experimento de resúmenes: `VSE/docs/evaluaciones/resumen-fast-vs-deep/privado/informe.md`
(movido el 2026-11-11 desde `C:\Users\dsoft03.corp\Experimentos\resumen-fast-vs-deep`, con aprobación del usuario; los scripts
conservan rutas absolutas de su entorno original y leen la clave de AltheIA solo de variables de entorno, nunca de un archivo).

Restricciones respetadas: solo lectura; sin ingestas, servicios, AltheIA, Qdrant ni `reyma-dev`; ledger intacto.
No se reabren 1.6, 1.10, 1.11, 15.2 ni la elección fast; 1.12 y sus fuentes/anclas/respuestas congeladas no se tocan.

## 0. Incidencia de alcance durante la investigación

El subagente que auditó la configuración del motor abrió `VSE/src/.../Api/appsettings.json` (:79-96) y
`appsettings.Development.local.json` (ignorado por git, `.gitignore:37`), que contiene una clave de AltheIA. El relevo de 1.12
(`VSE/docs/evaluaciones/1.12/relevo.md:22-24`) pide no leer el primero. La clave no se reprodujo; a esta sesión solo llegaron
nombres de perfil (`altheia-qwen-fast`, `altheia-bge-m3-1024`, `tei-arctic-256`), el tope `TopeTokensPorCorrida` y la URL base.
Ninguna ficha usa datos de esos archivos. Se registra para que el usuario decida si hace falta alguna acción.

## 1. Tabla de diferencias y huecos (evidencia verificada)

| # | Técnica del artículo | Estado en motor + cliente | Evidencia | Hueco |
|---|---|---|---|---|
| 1 | Tamaño fijo (ventana) | Aplicado como respaldo. La ventana se deriva de `MaxTokensPerChunk` (1024 por defecto) y el solapamiento de `TokensDeSolape` = 64 tokens, convertidos a líneas (≈51 líneas de ventana, 3 de solape, paso 48). **Corrección a la lectura previa:** "64 tokens" es el solapamiento, no el tamaño. | `Infra/Chunking/FallbackChunkingStrategy.cs:16,35`; `Infra/Chunking/ChunkBuilder.cs:26,40-45`; `Infra/Utilities/TokenEstimator.cs:28`; `Infra/MotorOptions.cs:206,208` | Ninguno. El solapamiento no aparece en el resumen del artículo (no verificado si el artículo lo trata). |
| 2 | Partición por estructura | Roslyn para C#, analizador léxico para TypeScript, cabeceras para Markdown; todo lo demás (JS/JSX, XAML, SQL, TXT/JSON/XML/CSPROJ) va al respaldo. | `Infra/Chunking/ChunkingStrategyRouter.cs:27-29,41-52`; `RoslynCSharpChunkingStrategy.cs:27`; `TypeScriptChunkingStrategy.cs:67`; `MarkdownChunkingStrategy.cs:28`; `Infra/Model/DocumentArtifacts.cs:14-29` | JavaScript no tiene estrategia estructural (hallazgo menor, fuera de alcance). |
| 3 | Tamaño medido, no supuesto | Hecho en 1.11 (c1024-w8192, ES +6/−0). | `VSE/docs/evaluaciones/1.11/decision.md:10-18,50-55` | Ninguno. |
| 4 | Chunking semántico (cortes por similitud de embeddings) | No existe. | búsqueda en `Infra/Chunking` | Hueco real pero de baja prioridad: el código ya se parte por estructura y el eval no lo pide. No se propone ficha. |
| 5 | Recuperación contextual (Anthropic) | **Parcial y distinta en tres puntos.** (a) Hay cabecera fija por chunk (`// Repository`, `// File`, `// Namespace`, `// class`, `// method`) que solo va al texto embebido `EnrichedContent`; `Content` queda crudo y `content_hash` se calcula sobre `Content`. (b) El resumen LLM es un espacio aparte (`dense-resumen` embebe solo el texto del resumen), fusionado en el cliente con RRF 1/1 k=60; no se añade al chunk. (c) El LLM ve únicamente lenguaje + ruta + `content` crudo: ni la cabecera semántica ni el archivo completo. | `ChunkBuilder.cs:61-62,98,105`; `RoslynCSharpChunkingStrategy.cs:136-175`; `Infra/Ingestion/DocumentIngestionPipeline.cs:257`; `Infra/Resumen/ResumenPipeline.cs:115`; `Infra/VectorStore/QdrantVectorStore.cs:695,702`; `Infra/Resumen/LlmResumenBrain.cs:120`; `Core/Infrastructure/VectorSpace/VectorSpaceSemanticRetriever.cs:48-55` | Líneas 2 y 3. |
| 6 | Expansión de chunks (post-proceso) | Solo salto por símbolo: `consumed_symbols` de las primeras `SeedResults` → búsqueda vectorial con filtro `simbolosDefinidos`, refusionada con RRF de k = TopK y peso 0,8. Apagado por defecto. No hay expansión por vecinos, clase contenedora ni archivo. `ISymbolExpansionQualifier` no tiene implementación en `src/` y el retriever remoto no lo usa. | `VectorSpaceSemanticRetriever.cs:57-74`; `Core/Infrastructure/VectorStore/RetrievalFusionOptions.cs:44-57,74`; `Core/Infrastructure/VectorStore/QdrantSemanticRetriever.cs:653-666`; `Core/Infrastructure/VectorStore/Expansion/ISymbolExpansionQualifier.cs:48` | Línea 1. |
| 7 | Presupuesto de contexto | 12.000 caracteres UTF-16 fijos; se omite entero el chunk que no cabe y se sigue con los siguientes; sin agrupación por archivo; orden = orden del retriever. En tickets011 el top-10 pesaba ~11,3 k y el rango 9 quedó fuera. | `Core/Services/Generation/GenerationContextAssembler.cs:35,157-178`; `VSE/docs/evaluaciones/1.12/preparacion.md:525,553-558` | Interacción crítica para la línea 1. |
| 8 | Banda de confianza | Sin reranker, los hits remotos llevan escala `RankFusionWeighted`, no comparable entre consultas: la puerta solo rechaza con 0 chunks y nunca añade la coletilla de baja confianza. | `Core/Services/Generation/ConfidenceGate.cs:82-97,117`; `Core/Domain/RetrievalScoreScale.cs:115`; `VectorSpaceSemanticRetriever.cs:114` | Ya cubierto por 1.12.11; la línea 1 solo la afecta por el conteo. |
| 9 | Apagado / opt-in | Fusión denso+sparse: `Modo = Denso` por defecto (1.10). Rama resumen: solo si la colección tiene `TieneResumen`. `TwoHop:Enabled = false`. `--rerank` false en CLI/API/servicio. `IndexShortTypeDeclarations` es constante de compilación `false`. | `Infra/MotorOptions.cs:225,228`; `Core/Infrastructure/VectorSpace/VectorSpaceOptions.cs:88,96-97`; `RetrievalFusionOptions.cs:47`; `RVC/src/RagEngine.Cli/Commands/AskCommand.cs:42-44`; `RVC/src/RagEngine.Api/Program.cs:557`; `DocumentIngestionPipeline.cs:35` | Línea 4: ver hallazgos H5-H7. |
| 10 | Pedir chunks por ruta + líneas o por clase (para la línea 1) | **No existe en el contrato.** `busquedas` exige `consulta` y siempre consulta con vector; sus únicos filtros son `simbolosDefinidos`, `modulos`, `lenguaje`, `namespace`, `espacio`. El payload sí guarda `file_path` (keyword, indexado), `start_line`/`end_line` (enteros, sin índice), `class_name`/`method_name` (sin índice). Existen scrolls filtrados internos, no expuestos. | `Biz/Queries/Colecciones/BuscarEnColeccionQuery.cs:19-21`; `Biz/Models/Requests/ColeccionesRequests.cs:24-80`; `Infra/VectorStore/DeterministicVectorQuery.cs:29-33`; `QdrantVectorStore.cs:483-486,493-525,554-558,573-576`; `VSE/docs/servicio/contrato-api.md:162-173,282-283` | Línea 1 necesita un endpoint nuevo (ver ficha). |

## 2. Fichas propuestas para el ledger rector

Formato copiado de los ítems vigentes (`VSE/docs/plan/ejecucion-plan.estado.json`, p. ej. 1.12.11 en :959-976 y 1.21.4).
IDs propuestos siguiendo la ola 1 (último existente: 1.22). Idioma: español, como el resto de la ola 1 del rector; si se
cuelgan bajo 1.12 deben ir en inglés (`RVC/AGENTS.md:3`). Ninguna se escribe en el ledger sin aprobación.

Dependencia común con `1.12.10-diagnose-effective-ask-context` (bloqueada por 1.12.7, decisión del usuario 2026-11-05,
`ledger:837-839`): si su captura real confirma que condiciones recuperadas de una misma regla se descartan por presupuesto
(`preparacion.md:583-591`), la línea 1 queda justificada con datos y se secuencia tras el cierre de 1.12 (toca el
ensamblador compartido por ambos brazos). Si no lo confirma, las tres fichas son mejoras independientes posteriores a 1.12.
Las líneas 2 y 3 pueden medirse fuera de línea en `tools/embeddings-eval` sin tocar el cliente ni los binarios de 1.12.

### Ficha 1 — `1.23-expansion-por-cercania-en-el-archivo-al-consultar`

```json
{
  "id": "1.23-expansion-por-cercania-en-el-archivo-al-consultar",
  "titulo": "Al consultar, completar cada hit con sus chunks vecinos del mismo archivo (N adyacentes por linea, clase contenedora en C#, seccion completa en Markdown) sin reingesta. Hoy el unico post-proceso es el salto por simbolo (apagado) y el ensamblador omite entero cualquier chunk que no cabe en 12.000 caracteres, sin agrupar por archivo ni garantizar la continuidad de una regla partida en varios chunks.",
  "modelo": "opus",
  "esfuerzo": "high",
  "multiagente": false,
  "estado": "pendiente",
  "fecha": "2026-11-11",
  "horas_maquina": 1.5,
  "por_que_este_modelo": "Hay que disenar el oraculo antes de medir: la expansion no cambia el ranking (hit@10 es constante por construccion si se aplica tras el corte), asi que el criterio tiene que medir el contexto ensamblado y no la busqueda; el riesgo es medir lo que se queria.",
  "origen": "Analisis del 2026-11-11 (docs/analisis/chunking-pinecone-vs-motor.md, tabla filas 6, 7 y 10) y la hipotesis falsable de 1.12 sobre el descarte por presupuesto (docs/evaluaciones/1.12/preparacion.md:583-591, 624-634). Verificado: no hay expansion por vecinos ni por archivo en el cliente (VectorSpaceSemanticRetriever.cs:57-74 solo salta por simbolo); el motor no permite pedir chunks por ruta+rango ni por clase (BuscarEnColeccionQuery.cs:19-21; DeterministicVectorQuery.cs:29-33 siempre exige vector); el payload guarda file_path (keyword indexado), start_line/end_line (sin indice) y class_name (sin indice) (QdrantVectorStore.cs:483-486, 554-558, 573-576).",
  "hipotesis": "Las condiciones de una regla recuperada se reparten en chunks adyacentes del mismo archivo (un metodo partido en '// [Fragment n]', una tabla de validaciones en la seccion siguiente), y adjuntarlos al hit con prioridad del grupo eleva la cobertura del ancla en el contexto de 12.000 caracteres sin cambiar el ranking.",
  "cambio_propuesto": "Dos piezas. MOTOR (dueno: Vector.Space.Engine): endpoint nuevo de solo lectura POST api/colecciones/{coleccion}/fragmentos que devuelve puntos por filtro, sin vector de consulta: ruta exacta (file_path/relative_path), rango opcional [lineaDesde, lineaHasta] por start_line/end_line, o clase exacta (class_name) de esa ruta; mismos campos de hit que 1.18; tope de resultados y 400 sin ruta. Reusa el scroll filtrado que ya existe para la ingesta (GetDocumentPointsAsync, QdrantVectorStore.cs:493-525). Indices: file_path ya es keyword; start_line/end_line sin indice se filtran dentro de la ruta (rango pequeno), no se crea indice nuevo sobre colecciones servidas; class_name sigue sin indice (misma reserva que 1.18, contrato-api.md:282-283). CLIENTE (dueno: Rag.VectorSpace.Client): paso de expansion opt-in en el empaquetado, DESPUES del corte TopK/rerank y ANTES del ensamblador, con tres variantes excluyentes por configuracion: (a) N adyacentes = los chunks con start_line inmediatamente anterior y posterior en el mismo file_path, N=1 por defecto; (b) clase contenedora = todos los chunks con el mismo file_path y class_name (solo C#, tope de chunks por clase); (c) seccion Markdown = chunks del mismo file_path cuyo method_name (que en Markdown es la seccion, MarkdownChunkingStrategy.cs:149-155) coincide. En todas: el grupo (hit + vecinos) se admite atomicamente con la prioridad del hit, ordenado por linea; si no cabe, se registra el descarte y se intenta el siguiente hit; sin cambiar los 12.000, el topK ni los scores. Primera fase sin motor: simulacion fuera de linea sobre los chunks del banco de 1.11.",
  "requiere_reingesta": "No. No cambia chunks, hashes, vectores ni payload. ChunkingContract.Version no sube.",
  "metrica": "Sobre los 57 pares ES/EN del banco de 1.6/1.11 con el top-10 denso de c1024-w8192 ya guardado (docs/evaluaciones/1.11/resultados/c1024-w8192.json): (1) ancla-en-contexto@12k ES y EN, pareado por pregunta, con la regla de acierto archivo+ancla aplicada al contexto ensamblado en vez de al top-10; (2) caracteres y tokens arctic del contexto final y numero de chunks omitidos por presupuesto; (3) hit@10 reportado como control de que no cambio; (4) latencia p50/p95 de la llamada a fragmentos por hit cuando se mida contra el servicio. Por corpus: innovapp-docs y bsuite-auditorias-test (documentos) frente a micro-repo (codigo).",
  "criterio_de_aceptacion": "Regla de 1.11: se adopta una variante solo si ancla-en-contexto@12k ES gana preguntas sin perder ninguna (+N/-0) y el aumento de caracteres/chunks omitidos queda declarado; EN se reporta como dato, no como veto. Se fija la variante y N antes de ver los resultados; una variante que gana en documentos y pierde en codigo se adopta por lenguaje, no global. Nada de esto acredita 1.12 ni se mide sobre sus 116 preguntas.",
  "costo_estimado": "Simulacion fuera de linea: ~0,5 h de maquina (chunks jsonl y top-10 ya en .cache/embeddings-eval, 0 embeddings, 0 LLM). Endpoint del motor: ~0,5 dia de desarrollo + tests con Qdrant efimero (~0,5 h). Expansion en el cliente: ~1 dia. Sin consumo de AltheIA.",
  "dependencias": "1.12.10 (ver nota comun). La simulacion fuera de linea no depende de nada. El cambio en el cliente solo se integra despues de cerrar 1.12 o en una rama que no entre en su build de aceptacion. El endpoint del motor es aditivo y puede ir antes.",
  "riesgos": "Dilucion: vecinos irrelevantes desplazan hits de rango bajo dentro de los 12.000 (por eso se mide chunks omitidos). Presupuesto: una clase WebForms de miles de lineas revienta la variante (b); exige tope. Respuestas inventadas: mas contexto no las reduce por si solo (1.12 no atribuye tickets011 al presupuesto). Ruido de resumenes tecnicos: no aplica, la expansion usa Content, no resumenes. Interaccion con salto por simbolo: ambos compiten por los 12.000; se mide con TwoHop apagado (su estado vigente). Interaccion con la banda de confianza: solo cambia el conteo de chunks, no el score.",
  "rollback": "Opcion del cliente apagada por defecto; apagarla restaura el empaquetado actual byte a byte (se prueba pareado). El endpoint del motor es aditivo: revertir el commit no toca puntos ni colecciones.",
  "archivos": "Motor: Business/Queries/Colecciones/, Business/Models/Requests/ColeccionesRequests.cs, Infrastructure/VectorStore/QdrantVectorStore.cs (scroll filtrado), Api/Controllers/ColeccionesController.cs, docs/servicio/contrato-api.md, tests. Cliente: Services/Generation/GenerationContextAssembler.cs (admision por grupo), un paso nuevo de expansion en Infrastructure/VectorSpace/, VectorSpaceApiClient.cs, opciones. Banco: tools/embeddings-eval (script de simulacion de empaquetado).",
  "verificacion": "(1) Simulacion: tabla pareada ancla-en-contexto@12k ES/EN por variante y corpus, con hit@10 identico al control. (2) Endpoint: pruebas con Qdrant efimero que devuelven exactamente los puntos del rango/clase/seccion pedidos y 400/200-vacio en los bordes; sin consulta no se llama al sidecar. (3) Cliente: con la opcion apagada, contexto identico al actual sobre un corpus aislado (comparacion byte a byte); con la opcion encendida, el grupo se admite entero o se registra su descarte. (4) dotnet test en ambos repos.",
  "_condicion_de_reentrada": "Responsable: usuario. Aprobar la simulacion fuera de linea (puede ir ya). Aprobar el endpoint y el cambio del cliente solo con el resultado de la simulacion y, si 1.12.10 llego a correr, con su diagnostico.",
  "_limites": "No cambia 12.000, topK, corpus, anclas, modelos ni umbrales. No toca respuestas, fuentes ni anclas congeladas de 1.12. No reabre 15.2 (el salto por simbolo sigue apagado)."
}
```

### Ficha 2 — `1.24-resumen-anadido-al-chunk-frente-a-espacio-separado`

```json
{
  "id": "1.24-resumen-anadido-al-chunk-frente-a-espacio-separado",
  "titulo": "Medir si embeber el resumen de negocio dentro del texto del chunk (un solo vector dense, al estilo de la recuperacion contextual) recupera mas que la rama resumen separada fusionada en el cliente con RRF 1/1 k=60. Hoy dense embebe cabecera+chunk y dense-resumen embebe solo el texto del resumen; el LLM no ve la cabecera.",
  "modelo": "opus",
  "esfuerzo": "high",
  "multiagente": false,
  "estado": "pendiente",
  "fecha": "2026-11-11",
  "horas_maquina": 2,
  "por_que_este_modelo": "Oraculo con cuatro brazos y un filtro previo que puede invertir el resultado; riesgo de atribuir a la tecnica lo que aporta el ruido o el contexto extra.",
  "origen": "Analisis del 2026-11-11 (tabla fila 5) y evidencia del experimento resumen-fast-vs-deep del 2026-11-11 (muestra de 100 chunks de reyoneweb, NO el corpus del eval). Verificado: clave de cache = content_hash|promptVersion|modelo en memoria por instancia (CacheDeResumenes.cs:37, :29); promptVersion = hash(v1|modelo|SystemPrompt) (LlmResumenBrain.cs:111-116); el texto del resumen se persiste en el payload resumen_texto desde 1.20 solo en chunks con negocio (QdrantVectorStore.cs:750-751); dense-resumen embebe solo el resumen (ResumenPipeline.cs:115); dense embebe EnrichedContent (DocumentIngestionPipeline.cs:257); content_hash se calcula sobre Content, no sobre EnrichedContent (ChunkBuilder.cs:98,105); el cliente fusiona codigo+resumen con RankFusion.Fuse(ids, 60, pesos 1/1) (VectorSpaceSemanticRetriever.cs:53-55).",
  "hipotesis": "Un vector unico sobre 'cabecera + chunk + resumen' acerca el codigo a la pregunta en espanol mejor que dos espacios fusionados por rango, que hoy diluyen el denso (1.10 mostro que cualquier fusion por rango hereda las perdidas de la rama debil).",
  "cambio_propuesto": "Experimento fuera de linea en tools/embeddings-eval (dueno: Vector.Space.Engine), sin tocar el servicio ni el cliente. Brazos sobre la fragmentacion c1024 vigente: A = denso solo (control, ya medido: ES 46/57, EN 36/57); B = denso + espacio resumen con RRF 1/1 k=60 replicado fuera de linea (la fusion actual del cliente); C = denso unico sobre EnrichedContent + '\\n// Resumen: {resumen_texto}' para los chunks con resumen; D = C con el filtro previo determinista (hallazgo H2) que deja sin resumen los chunks tecnicos. Si C o D gana, cambio de producto posterior y aparte: el motor calcula dense DESPUES de la fase 2 (o re-embebe tras resumir) y el cliente deja de fusionar dos espacios.",
  "requiere_reingesta": "Experimento: no (vectores nuevos solo en el banco). Producto, si se adopta C/D: no cambia Ids ni content_hash (el resumen va al texto embebido, no a Content), pero todos los chunks con resumen cambian de vector dense: re-vectorizacion completa de cada coleccion servida (TEI ~0,82 chunks/s a c1024, 1.11: ~85 min por 4157 chunks) y sube ChunkingContract.Version a 4 porque cambia el encabezado embebido (ChunkingContract.cs:10-18) y las lineas base dejan de ser comparables.",
  "metrica": "hit@10 pareado ES/EN contra A sobre los 57 pares del banco; tokens arctic del top-10; proporcion de chunks tecnicos (filtro H2) en el top-10 como medida de ruido; latencia de consulta sin cambio esperado (un vector en C/D, dos llamadas en B).",
  "criterio_de_aceptacion": "Regla de 1.11: se prefiere un brazo solo si gana preguntas ES sin perder ninguna frente a A (+N/-0); EN informativo. Entre B y C/D se exige ademas que el brazo ganador no aumente la proporcion de chunks tecnicos en el top-10. Si ningun brazo cumple, se registra hipotesis nula y la rama resumen sigue como esta. Regla fijada antes de generar un solo resumen.",
  "costo_estimado": "Hoy NINGUN corpus del eval tiene resumenes (1.11 dejo la banda resumen fuera del banco; las cohortes de 1.12 estan sin resumen). Hace falta resumir con fast el corpus elegido: medido 2026-11-11 ~600 tokens de prompt + 28 de completado y ~1 s por chunk en serie. innovapp-docs (629 chunks) ~0,4 M tokens / ~11 min; micro-repo (1734) ~1,1 M / ~30 min, por encima del tope diario de 1 M citado en 1.7.7; los tres corpus (4157) ~2,6 M / ~70 min. Se propone empezar por micro-repo (codigo, donde la hipotesis aplica) en dos dias o con aprobacion expresa del tope. TEI: re-embeber C y D ~2 x 35 min para micro-repo. Total maquina ~2 h.",
  "dependencias": "Hallazgo H2 (filtro previo) solo para el brazo D: puede implementarse como regla del script del banco sin tocar el motor. 1.12.10: ninguna tecnica; se secuencia como mejora posterior a 1.12 (nota comun). No depende de la ficha 1.",
  "riesgos": "Dilucion: el resumen (hasta 60 palabras) alarga el texto embebido; con arctic no hay recorte (1.11) pero puede mover el vector lejos del codigo literal que buscan las preguntas tecnicas. Presupuesto de contexto: ninguno (el contexto sigue siendo Content). Respuestas inventadas: no se mide generacion aqui. Ruido de resumenes tecnicos: los dos modelos resumen entre 7 y 21 de cada 100 chunks tecnicos; en C ese ruido entra en el mismo vector del chunk (en B se queda en un espacio aparte), por eso existe el brazo D. Caducidad: la cache en memoria no sirve entre corridas; el banco debe guardar los resumenes en su propio jsonl.",
  "rollback": "Experimento: documental. Producto: revertir el commit no revierte vectores ya escritos; se exige coleccion aislada y conservar la coleccion anterior hasta medir.",
  "archivos": "tools/embeddings-eval (script nuevo resumen_en_chunk.py, resumenes-<col>.jsonl con hash de prompt y modelo), docs/evaluaciones/1.24/. Lectura: Infrastructure/Resumen/*, Ingestion/DocumentIngestionPipeline.cs, cliente VectorSpaceSemanticRetriever.cs.",
  "verificacion": "Tabla pareada por brazo y corpus con +gana/-pierde pregunta a pregunta; los resumenes generados con el prompt literal de LlmResumenBrain (comprobado caracter a caracter como en el experimento del 2026-11-11), fast, enable_thinking=false, max_tokens 160; filtro D aplicado por regla escrita antes de generar; recuento de chunks con centinela y de chunks tecnicos resumidos por juez determinista (patrones de ruta y chunk_type).",
  "_condicion_de_reentrada": "Responsable: usuario. Confirmar el corpus y el tope de tokens de AltheIA (fijar antes de generar). Confirmar que el filtro H2 se usa aqui solo como regla del banco.",
  "_limites": "No cambia el prompt del resumen ni el modelo (fast se queda por decision del 2026-11-11). No toca colecciones servidas ni reyoneweb. No extrapola las cifras de la muestra de reyoneweb al eval sin decirlo."
}
```

### Ficha 3 — `1.25-piloto-de-recuperacion-contextual-completa`

```json
{
  "id": "1.25-piloto-de-recuperacion-contextual-completa",
  "titulo": "Piloto acotado de recuperacion contextual completa: generar el resumen de negocio de cada chunk dandole al LLM el archivo entero ademas del chunk, y medir si el contexto en el resumen importa. Hoy el LLM solo ve lenguaje + ruta + content crudo.",
  "modelo": "opus",
  "esfuerzo": "high",
  "multiagente": false,
  "estado": "pendiente",
  "fecha": "2026-11-11",
  "horas_maquina": 3,
  "por_que_este_modelo": "El coste crece con el cuadrado del numero de chunks por archivo y la calidad depende de un prompt nuevo: hay que fijar muestra, tope y regla antes de gastar.",
  "origen": "Analisis del 2026-11-11 (tabla fila 5c). Verificado: ResumirAsync recibe (lenguaje, ruta, contenido) y nada mas (LlmResumenBrain.cs:33,120); el pipeline de resumen trabaja punto a punto desde un scroll por resumen_pending (QdrantVectorStore.cs:685-710) y no tiene el documento a mano. Coste por chunk medido el 2026-11-11 con fast: ~600 tokens de prompt, 28 de completado, ~1 s. El '~19 h local / ~24 h servidor' del AGENTS del cliente (RVC/AGENTS.md:228-229) es la regeneracion total de otro corpus y otro runtime, no un coste por chunk; no se usa como base.",
  "hipotesis": "Un resumen que conoce el archivo completo nombra la entidad y la regla con menos invenciones y omisiones (el experimento del 2026-11-11 atribuye parte de las omisiones de fast a que el chunk no basta), y eso sube hit@10 del espacio resumen (o del brazo C/D de 1.24) en ES.",
  "cambio_propuesto": "Solo piloto, en el banco (dueno: Vector.Space.Engine). Prompt de usuario nuevo: '[Contexto: Lenguaje, Ruta]' + archivo completo delimitado + 'Fragmento a resumir:' + chunk; prompt de sistema igual mas una frase que pide resumir SOLO el fragmento. Modelo: fast (defecto). Variante deep con pensamiento (enable_thinking=true, max_tokens 4096) solo si antes existe el filtro previo H2 aplicado en el banco, y con su coste reportado aparte. Muestra: los archivos ancla de las 11 preguntas ES que c1024 todavia falla (57-46) mas 11 archivos de control que ya acierta, tope de 300 chunks. Si gana, la adopcion es otra ficha: ResumenPipeline tendria que recibir el documento (hoy no lo tiene) y PromptVersion cambia.",
  "requiere_reingesta": "Piloto: no. Adopcion: no cambia chunks ni ChunkingContract.Version (los chunks son los mismos), pero cambia PromptVersion (prompt nuevo) y por tanto TODOS los resumenes se regeneran y la linea base del espacio resumen cambia; si ademas se adopta 1.24-C, se re-vectoriza dense.",
  "metrica": "Las de 1.24 (hit@10 pareado ES/EN, tokens top-10, ruido tecnico) sobre la muestra, mas: tokens de prompt y completado por chunk, latencia por chunk y por archivo, y juicio ciego de fidelidad/omisiones con la misma plantilla de jueces del 2026-11-11 (afirmaciones fragmento/codigo/inventada/contradice, omisiones, centinela_ok).",
  "criterio_de_aceptacion": "Regla fijada antes de generar: el resumen con archivo completo se considera prometedor solo si (1) reduce inventada+contradice y omisiones en ambos jueces, (2) no aumenta los chunks tecnicos resumidos (centinela_ok=false) y (3) gana preguntas ES en hit@10 sin perder ninguna en la muestra. Regla de 1.11 para ES; EN informativo. Deep con pensamiento se evalua con la misma regla y su coste (x18,5 tokens de completado, x15 latencia medidos) se informa aparte, nunca se promedia con fast.",
  "costo_estimado": "Por chunk: ~600 tokens de prompt + T_archivo, con T_archivo = suma de los tokens de sus chunks; un archivo de n chunks cuesta aproximadamente n x (600 + n x t) tokens de prompt, donde t es el tamano medio del chunk (p50 172-221 tokens de arctic a c1024, 1.11). Ejemplo: 20 chunks x 200 tokens = 4.000 tokens de archivo; 4.600 por chunk; 92.000 por archivo frente a 12.000 hoy (x7,7). La distribucion real de chunks por archivo no esta medida: el primer paso del piloto es contarla en el banco (chunks jsonl, sin Qdrant) y recalcular; tope provisional de 300 chunks / 1,5 M tokens de prompt con fast, a confirmar. Deep con pensamiento: solo sobre un subconjunto de 50 chunks (~26 k tokens de completado y ~12 min en serie, segun las medianas del 2026-11-11). Maquina ~3 h incluidos jueces.",
  "dependencias": "Condicion de entrada: 1.24 muestra que el contexto en el resumen importa (B, C o D ganan en ES, o la diferencia entre resumen con y sin cabecera es medible). Hallazgo H2 para la variante deep. Hallazgo H1 (PromptVersion sin EnableThinking/MaxTokens) antes de mezclar variantes en una misma cache o payload. 1.12.10: ninguna tecnica (nota comun).",
  "riesgos": "Coste cuadratico por archivo y archivos de WebForms de miles de lineas que superan la ventana del LLM (hay que truncar el archivo y declararlo). Dilucion del resumen: con el archivo entero el modelo puede resumir la clase y no el fragmento (medir 'afirmaciones codigo', que ya subio de 3 a 11 con deep). Respuestas inventadas: deep con pensamiento invento menos (6 frente a 30) pero resumio de mas lo tecnico; sin H2 el ruido crece. Cuota: supera facilmente el tope diario de 1 M.",
  "rollback": "Documental; el piloto no escribe en colecciones. Si se adoptara, conservar la coleccion con resumenes anteriores hasta medir.",
  "archivos": "tools/embeddings-eval (generador con archivo completo, reutilizando los scripts 02/03/04 del experimento del 2026-11-11 movidos a docs/evaluaciones/resumen-fast-vs-deep/), docs/evaluaciones/1.25/.",
  "verificacion": "Tabla de coste medido por chunk y por archivo; tabla pareada hit@10; juicio ciego con dos jueces y totales ciegos coincidentes con el descifrado; muestra y regla selladas (hash) antes de generar.",
  "_condicion_de_reentrada": "Responsable: usuario. Solo tras 1.24 con resultado favorable y con tope de tokens aprobado. Si 1.24 da hipotesis nula, esta ficha se descarta con motivo, no se ejecuta.",
  "_limites": "No cambia el prompt de produccion ni PromptVersion. No usa las preguntas ni respuestas congeladas de 1.12. No decide fast frente a deep: eso ya se midio el 2026-11-11 y solo se reabriria con H2 y regla nueva."
}
```

## 3. Línea 4 — piezas apagadas: comprobación, sin reabrir

| Pieza | ¿Se decidió sobre vectores/fragmentación vigentes? | Evidencia |
|---|---|---|
| Fusión denso+sparse (1.10) | **Sí.** Calibrada sobre `c1024-w8192` de 1.11, con control denso del mismo brazo (ES 46, EN 36). | `VSE/docs/evaluaciones/1.10/decision.md:21-25,31-33` |
| Rama resumen (1.20) | No hay decisión de recall: umbral 0 por defecto, "no se midió recall en este item". No es una decisión sobre vectores viejos, es una medición ausente. | `contrato-api.md:305-308` |
| `TwoHop:Enabled = false` (15.2.2) | **Hallazgo H5:** la hipótesis nula del salto cualificado se cerró el 2026-09-22, antes de 1.6 (arctic, 2026-10-20) y de 1.11 (c1024), sobre el retriever local de Qdrant con MiniLM 384d y fragmentación 512 (modelo exacto no verificado en la ficha). La decisión vigente es "sin expansión"; no se propone recalibrar sin un hallazgo concreto, pero su evidencia no es sobre los vectores actuales. | `RVC/docs/analisis-futuro/ejecucion-plan.estado.json:2468-2492`; `ledger VSE:135-142` |
| `--rerank` | Apagado por defecto en CLI, API y servicio; la calibración del cross-encoder está en 1.12.11 (ya existe). | `AskCommand.cs:42-44`; `Program.cs:557`; `RagGenerationService.cs:110`; `ledger VSE:959-976` |
| `IndexShortTypeDeclarations` | Constante de compilación `false` desde ChunkingContract v3 (2026-09-12, MiniLM). Opt-in imposible por configuración. Hallazgo H6, sin propuesta. | `DocumentIngestionPipeline.cs:35`; `ChunkingContract.cs:38-43` |

## 4. Hallazgos fuera de alcance (no se implementan)

- **H1. `PromptVersion` no incluye `EnableThinking` ni `MaxTokens` (ni temperatura/top_p).** `ComputePromptVersion` hashea `v1|modelo|SystemPrompt` (`LlmResumenBrain.cs:111-116`); la petición envía `EnableThinking`, `MaxTokens`, `Temperatura`, `TopP` (`:126-127`). La clave de caché añade solo `modelo` (`CacheDeResumenes.cs:37`) y el payload guarda `resumen_prompt_version` (`QdrantVectorStore.cs:744-749`). Con el mismo modelo y distinta configuración, caché y payload mezclan resúmenes indistinguibles. Candidato a ficha del motor: incluir esos cuatro parámetros en el hash; efecto: cambia `PromptVersion` de todo lo existente (los puntos ya resumidos no se regeneran porque `resumen_pending=false`, pero la caché en memoria falla y los chunks nuevos quedan con otra versión; declararlo).
- **H2. No existe filtro previo determinista de código generado.** El perfil de escaneo admite `.csproj` y solo excluye directorios (`ScanProfile.cs:25-34`); los documentos HTTP ni siquiera pasan por esas exclusiones (`DocumentArtifacts.cs:8-9,38-45`); `IgnoreRules` solo carga `.gitignore`/`.ragignore` del repo (`IgnoreRules.cs:32-33`); no hay regla para `*.designer.cs`, `*.g.cs`, `*.generated.cs`, `AssemblyInfo.cs` ni FastReport; el pipeline de resumen manda al LLM todo punto pendiente sin mirar `chunk_type` (`ResumenPipeline.cs:71-89`; el scroll ni lo trae, `QdrantVectorStore.cs:691-695`). Candidato a ficha del motor en dos niveles: (i) solo en la fase 2, marcar `resumen_pending=false` sin LLM para `chunk_type` Property/Constructor/Field y rutas `*.designer.cs`, `*.csproj`, `*.json`, `*.xml` (no toca dense ni `ChunkingContract`); (ii) en la admisión, excluir archivos generados (cambia qué se indexa: sube `ChunkingContract.Version`). Con (i) la comparación fast frente a deep con pensamiento tendría que repetirse con regla fijada de antemano, porque las 9 pérdidas de utilidad de deep eran exactamente chunks técnicos.
- **H3. Comentario desactualizado:** la cabecera de `QdrantVectorStore` dice que no hay `dense-resumen` ni `resumen_pending` (`QdrantVectorStore.cs:20-23`), y ambos existen (`:37`, `:586-587`).
- **H4. Documentación del perfil opt-in 1.12:** el contrato dice que usa "contexto padre 5 y SplitWithOverlap" (`contrato-api.md:114-115`), pero `ParentContextLines` y `OversizedBehavior` solo entran en la huella del documento (`DocumentIngestionPipeline.cs:309-310`) y ninguna estrategia los lee (`ChunkingOptions.cs:18,24`). El único "contexto padre" real es la cabecera de clase/namespace.
- **H5.** Ver tabla de la línea 4: la decisión de 15.2.2 (sin salto) es anterior a arctic y a c1024. La sombrilla 15.2 sigue `bloqueado` sin reconciliar tras cerrar sus hijas (`RVC ledger:2496-2527`).
- **H6.** `IndexShortTypeDeclarations` es constante de compilación, no configuración (`DocumentIngestionPipeline.cs:35`), aunque ChunkingContract v3 la describe como opt-in.
- **H7.** Sin reranker, la banda de confianza es inerte en el camino VectorSpace (`ConfidenceGate.cs:82-97`; `RetrievalScoreScale.cs:115`): solo rechaza con 0 chunks. Ya anotado en `preparacion.md:523` y cubierto por 1.12.11; se repite aquí porque condiciona la métrica de la ficha 1.
- **H8.** JavaScript no tiene estrategia estructural: `.js/.jsx` caen al respaldo (`DocumentArtifacts.cs:14-29`; router `:41-52`).
- **H9 (no verificado).** En el cliente, `HasRemoteSummary` es siempre `true` (`VectorSpaceSemanticRetriever.cs:119`), así que un hit con `Resumen == null` entra como "encontrado con resumen nulo"; no se comprobó `StripEntityPrefix(null)`.
- **H10. Lectura previa a corregir:** "tamaño fijo con solapamiento, 64 tokens" era el solapamiento, no el tamaño; y el resumen del artículo no menciona solapamiento (no verificado si lo trata en otra sección).
- **H11. Incidencia:** lectura de `appsettings.json` del Api y de un archivo local con clave por el subagente (sección 0).

## 4b. Fichas derivadas de los hallazgos (pedido del usuario, 2026-11-11)

El usuario pidió que los hallazgos que tocan chunking, vectores o resúmenes entren como fichas, porque ese terreno
se va a modificar de todos modos. No cambian el alcance de las líneas 1-3; son correcciones independientes.
Quedan sin ficha: H5 (sombrilla 15.2 vive en el ledger del cliente, que no es agenda de esta bifurcación: solo una nota
para reconciliarla), H7 (ya en 1.12.11), H10 (corrección de lectura, ya recogida en la tabla) y H11 (incidencia).

### Ficha 4 — `1.26-incluir-parametros-de-muestreo-en-promptversion`

```json
{
  "id": "1.26-incluir-parametros-de-muestreo-en-promptversion",
  "titulo": "PromptVersion es el hash de 'v1|modelo|SystemPrompt' (LlmResumenBrain.cs:111-116), pero la peticion al LLM tambien envia EnableThinking, MaxTokens, Temperatura y TopP (:126-127). La clave de cache solo anade el modelo (CacheDeResumenes.cs:37) y el payload guarda resumen_prompt_version (QdrantVectorStore.cs:744-749): con el mismo modelo y otra configuracion, cache y coleccion mezclan resumenes de ambas sin poder distinguirlos.",
  "modelo": "sonnet",
  "esfuerzo": "medium",
  "multiagente": false,
  "estado": "pendiente",
  "fecha": "2026-11-11",
  "horas_maquina": 0.3,
  "por_que_este_modelo": "Cambio acotado con criterio ya definido; el unico riesgo es declarar bien el efecto sobre los puntos ya resumidos.",
  "origen": "Hallazgo del experimento resumen-fast-vs-deep del 2026-11-11 (sonda con enable_thinking y max_tokens 4096 sobre el mismo modelo) y del analisis docs/analisis/chunking-pinecone-vs-motor.md (H1).",
  "hipotesis": "No aplica: es una correccion de identidad, no una mejora de recall.",
  "cambio_propuesto": "Motor (Infrastructure/Resumen/LlmResumenBrain.cs): ComputePromptVersion pasa a hashear 'v2|modelo|EnableThinking|MaxTokens|Temperatura|TopP|SystemPrompt' con formato invariante de cultura. Subir VersionBase a v2 para que el cambio sea visible. Documentar en contrato-api.md:606-616 que la version de prompt identifica tambien la configuracion de muestreo.",
  "requiere_reingesta": "No. No cambia chunks, vectores dense ni ChunkingContract. Efecto declarado: todo PromptVersion existente cambia; los puntos ya resumidos tienen resumen_pending=false y NO se regeneran (0 tokens); la cache en memoria falla en la siguiente corrida; los chunks nuevos quedan con la version nueva. Una coleccion puede tener dos versiones de prompt conviviendo, igual que hoy puede tener dos modelos (contrato-api.md:613-616).",
  "metrica": "No aplica (correccion). Se reporta el numero de puntos con cada resumen_prompt_version en la coleccion de prueba antes y despues.",
  "criterio_de_aceptacion": "Prueba unitaria: dos perfiles con el mismo modelo y distinto EnableThinking o MaxTokens producen PromptVersion distintos; dos perfiles identicos producen el mismo; el valor para la configuracion por defecto queda fijado en un golden para detectar cambios accidentales. Prueba de integracion con LLM inyectado: resumir con perfil A, cambiar a perfil B (mismo modelo, otro MaxTokens) y comprobar que un chunk sin cambios no se vuelve a resumir y que un chunk nuevo lleva la version nueva.",
  "costo_estimado": "~0,3 h de maquina (tests con Qdrant efimero y LLM inyectado, 0 tokens de AltheIA).",
  "dependencias": "Ninguna. Conviene antes de 1.25 (variante deep con pensamiento) y antes de cualquier relleno de resumenes.",
  "riesgos": "Ninguno sobre datos. Riesgo de lectura: comparar resumenes entre versiones de prompt deja de ser directo; se declara.",
  "rollback": "Revertir el commit: PromptVersion vuelve a v1 y los puntos con v2 quedan como una version mas en el payload, sin efecto.",
  "archivos": "Infrastructure/Resumen/LlmResumenBrain.cs, tests de resumen, docs/servicio/contrato-api.md.",
  "verificacion": "dotnet test con las pruebas descritas en verde; grep de la cadena 'v1|' ausente del codigo; contrato actualizado.",
  "_limites": "No toca el prompt del sistema ni el modelo; no regenera resumenes."
}
```

### Ficha 5 — `1.27-filtro-previo-determinista-antes-de-resumir`

```json
{
  "id": "1.27-filtro-previo-determinista-antes-de-resumir",
  "titulo": "La fase 2 de resumen manda al LLM todo punto con resumen_pending=true sin mirar chunk_type ni ruta (ResumenPipeline.cs:71-89; el scroll ni trae chunk_type, QdrantVectorStore.cs:691-695), y deja que el propio modelo decida el centinela SIN_CONTENIDO_DE_NEGOCIO. Medido el 2026-11-11: ambos modelos resumen entre 7 y 21 de cada 100 chunks tecnicos (envoltorios de FastReport, *.designer.cs, .csproj, propiedades triviales), y esas 9 piezas explican toda la perdida de utilidad de deep con pensamiento. Anadir un filtro determinista que decida QUE NO se resume, separado de COMO se resume.",
  "modelo": "sonnet",
  "esfuerzo": "medium",
  "multiagente": false,
  "estado": "pendiente",
  "fecha": "2026-11-11",
  "horas_maquina": 0.5,
  "por_que_este_modelo": "Reglas explicitas y verificables; el criterio es no excluir negocio real, y eso se prueba con una lista fija de casos.",
  "origen": "Experimento resumen-fast-vs-deep (informe, seccion 'De donde sale el deficit de utilidad') y analisis del 2026-11-11 (H2). Verificado: no existe ninguna exclusion de codigo generado en ScanProfile.cs:25-34 (admite .csproj; solo excluye directorios), DocumentArtifacts.cs:38-45 (los documentos HTTP ni pasan por esas exclusiones) ni IgnoreRules.cs:32-33 (solo .gitignore/.ragignore del repo).",
  "hipotesis": "Un filtro previo por ruta y chunk_type evita la mayoria de los resumenes tecnicos sin perder chunks de negocio, reduce tokens de LLM y quita ruido del espacio resumen; y con el, la comparacion fast frente a deep con pensamiento podria cambiar (hay que repetirla con regla fijada).",
  "cambio_propuesto": "Nivel (i), este item: en ResumenPipeline, antes de la cache y del LLM, una regla determinista y versionada (Motor:Resumen:FiltroPrevio, con version entera) marca resumen_pending=false y resumen_modelo='filtro-previo:v1' sin vector ni texto para: rutas que casen con *.designer.cs, *.Designer.cs, *.g.cs, *.generated.cs, AssemblyInfo.cs, *.csproj, *.json, *.xml; chunk_type Property, Field y Constructor; archivos con cabecera '<auto-generated' en las primeras lineas (requiere traer content en el scroll, que ya se trae). FastReport: solo por patrones de ruta/clase medibles en el corpus (no inventar la lista: tomarla de los 9 chunks del experimento y de un barrido fuera de linea de reyoneweb). Lista de patrones en configuracion, no en codigo. El scroll de pendientes pasa a traer chunk_type. Nivel (ii), NO en este item: excluir archivos generados de la admision a la ingesta (cambia que se indexa y subiria ChunkingContract.Version); se anota como ficha futura si (i) muestra que esos archivos tampoco aportan al espacio codigo.",
  "requiere_reingesta": "No. No cambia chunks ni vectores dense; ChunkingContract.Version no sube. Solo afecta a puntos pendientes de resumen; los ya resumidos no se tocan (si se quiere limpiarlos, es un relleno aparte con aprobacion).",
  "metrica": "Sobre la muestra de 100 chunks del experimento (que ya tiene veredicto de dos jueces por chunk): precision y cobertura del filtro frente al juicio 'puramente tecnico' de j3/j4; tokens de LLM ahorrados por 100 chunks; chunks de negocio excluidos por error (debe ser 0 en la muestra).",
  "criterio_de_aceptacion": "Regla fijada antes de medir: el filtro excluye al menos 8 de los 9 chunks tecnicos que deep resumio de mas y 0 de los 50 chunks donde ambos jueces vieron negocio; prueba unitaria por patron con positivos y negativos (p. ej. 'Silos.cs' NO se filtra, 'Silos.designer.cs' si); prueba de integracion con LLM inyectado donde el chunk filtrado no genera llamada y queda con resumen_pending=false. Si falla el 0 de negocio excluido, se reduce la lista, no el criterio.",
  "costo_estimado": "~0,5 h de maquina (tests con Qdrant efimero, 0 tokens). La repeticion de fast frente a deep con el filtro NO esta incluida: seria otra corrida de ~100 llamadas por modelo y jueces nuevos, con regla fijada de antemano.",
  "dependencias": "Ninguna tecnica. 1.24 (brazo D) y 1.25 (variante deep) lo usan.",
  "riesgos": "Excluir negocio real por un patron demasiado amplio (por eso la prueba exige 0 en la muestra y los patrones viven en configuracion). Propiedades con logica (getters con reglas) quedarian sin resumen si chunk_type Property se filtra en bloque: medirlo en la muestra antes de incluir ese tipo.",
  "rollback": "Revertir el commit o vaciar Motor:Resumen:FiltroPrevio; los puntos marcados 'filtro-previo:v1' no se regeneran solos (hoy no hay mecanismo para volver a marcar pendiente, contrato-api.md:323-325): declararlo.",
  "archivos": "Infrastructure/Resumen/ResumenPipeline.cs, Infrastructure/MotorOptions.cs (ResumenOptions), Infrastructure/VectorStore/QdrantVectorStore.cs (ListarPendientesDeResumenAsync trae chunk_type), tests, docs/servicio/contrato-api.md (seccion 1.7.7).",
  "verificacion": "dotnet test en verde con las pruebas descritas; tabla del filtro contra la muestra de 100 chunks (precision/cobertura/negocio excluido) en docs/evaluaciones/1.27/.",
  "_limites": "No cambia el prompt ni el modelo; no reabre fast frente a deep (eso exige corrida nueva con regla propia); no toca la admision a la ingesta."
}
```

### Ficha 6 — `1.28-correcciones-documentales-del-chunking-y-resumen`

```json
{
  "id": "1.28-correcciones-documentales-del-chunking-y-resumen",
  "titulo": "Tres inexactitudes entre codigo y documentacion detectadas el 2026-11-11: (a) la cabecera de QdrantVectorStore dice que no hay dense-resumen ni resumen_pending (QdrantVectorStore.cs:20-23) y existen (:37, :586-587); (b) el contrato dice que el perfil opt-in 1.12 usa 'contexto padre 5 y SplitWithOverlap' (contrato-api.md:114-115) pero ParentContextLines y OversizedBehavior solo entran en la huella (DocumentIngestionPipeline.cs:309-310) y ninguna estrategia los lee (ChunkingOptions.cs:18,24); (c) ChunkingContract v3 describe IndexShortTypeDeclarations como opt-in (ChunkingContract.cs:38-40) y es una constante de compilacion false (DocumentIngestionPipeline.cs:35). Ademas, comprobar el caso no verificado del cliente: HasRemoteSummary siempre true con Resumen null (VectorSpaceSemanticRetriever.cs:119).",
  "modelo": "haiku",
  "esfuerzo": "low",
  "multiagente": false,
  "estado": "pendiente",
  "fecha": "2026-11-11",
  "horas_maquina": 0.1,
  "por_que_este_modelo": "Correcciones de texto y una comprobacion mecanica; sin decisiones.",
  "origen": "Analisis docs/analisis/chunking-pinecone-vs-motor.md (H3, H4, H6, H9).",
  "cambio_propuesto": "(a) Reescribir el comentario con los tres vectores y las claves de payload reales. (b) En contrato-api.md:114-115 decir que esos dos valores forman parte de la huella del perfil (identidad) pero no cambian el corte; alternativa, si se prefiere que el codigo diga la verdad, quitar ambos campos de ChunkingOptions (eso cambia document_fingerprint del perfil 1.12 congelado: NO hacerlo mientras 1.12 este abierto). (c) En ChunkingContract.cs:38-40 anotar que es constante de compilacion y donde vive. (d) Cliente: prueba que documente que hace StripEntityPrefix(null) y, si lanza, abrir ficha aparte; no cambiar comportamiento aqui.",
  "requiere_reingesta": "No. Ni chunks, ni vectores, ni ChunkingContract.Version.",
  "metrica": "No aplica.",
  "criterio_de_aceptacion": "Los tres textos coinciden con el codigo citado; dotnet build sin warnings nuevos; la prueba (d) existe y pasa o deja registrado el fallo como hallazgo.",
  "costo_estimado": "~0,1 h.",
  "dependencias": "Ninguna. La alternativa de (b) que toca ChunkingOptions queda bloqueada por 1.12.",
  "riesgos": "Ninguno si se limita a texto.",
  "rollback": "Revertir el commit.",
  "archivos": "Infrastructure/VectorStore/QdrantVectorStore.cs (comentario), docs/servicio/contrato-api.md, Infrastructure/Chunking/ChunkingContract.cs (comentario), cliente tests/RagEngine.Core.Tests/VectorSpace/.",
  "verificacion": "Revision del diff contra las lineas citadas; build y tests en verde.",
  "_limites": "Solo texto y una prueba; sin cambios de comportamiento ni de huella."
}
```

### Ficha 7 — `1.29-estrategia-estructural-para-javascript`

```json
{
  "id": "1.29-estrategia-estructural-para-javascript",
  "titulo": ".js/.jsx se mapean a SourceLanguage.JavaScript (DocumentArtifacts.cs:14-29) pero el router no tiene estrategia para ese lenguaje y los manda al respaldo por ventana (ChunkingStrategyRouter.cs:27-29, 41-52), mientras TypeScript tiene analizador lexico (TypeScriptChunkingStrategy.cs:67). reyoneweb tiene 227 puntos JavaScript (muestra del 2026-11-11) partidos sin funciones ni cabeceras de simbolo.",
  "modelo": "sonnet",
  "esfuerzo": "medium",
  "multiagente": false,
  "estado": "pendiente",
  "fecha": "2026-11-11",
  "horas_maquina": 1,
  "por_que_este_modelo": "Reutiliza una estrategia existente con criterio claro (golden master), pero cambia que chunks se generan y exige subir el contrato y medir.",
  "origen": "Analisis del 2026-11-11 (H8).",
  "hipotesis": "Registrar TypeScriptChunkingStrategy tambien para JavaScript (su lexico cubre function, const = () =>, class y metodos; los tipos TS son opcionales en sus regex, TypeScriptChunkingStrategy.cs:37-54) produce chunks por funcion con defined_symbols/consumed_symbols y mejora hit@10 en preguntas sobre JS.",
  "cambio_propuesto": "Router: mapear SourceLanguage.JavaScript a la estrategia de TypeScript (o a una subclase JavaScript que solo cambie el nombre del lenguaje en la cabecera). Golden master nuevo con fixtures .js/.jsx reales (incluyendo jQuery/WebForms de reyoneweb anonimizados). Subir ChunkingContract.Version a 4 (cambia que chunks existen para .js). Medir con un eval-set JS, que hoy NO existe: hay que redactarlo (al menos 10 preguntas ES sobre archivos .js del corpus elegido) antes de medir.",
  "requiere_reingesta": "Si, de todo documento .js/.jsx de cada coleccion (sus Ids y hashes cambian). ChunkingContract.Version sube a 4. Los resumenes de esos chunks se regeneran (content_hash nuevo).",
  "metrica": "hit@10 pareado ES/EN sobre el eval-set JS nuevo; numero y tamano de chunks JS antes y despues; tokens top-10.",
  "criterio_de_aceptacion": "Regla de 1.11: +N/-0 en ES; EN informativo. Golden master de los lenguajes existentes sin un byte de cambio (prueba que ya existe en 1.4).",
  "costo_estimado": "~1 h de maquina (reingesta de los .js de un corpus de prueba aislado y eval); ~0,5 dia de desarrollo mas la redaccion del eval-set.",
  "dependencias": "Posterior a 1.12 (sube ChunkingContract y por tanto la linea base). Independiente de 1.23-1.25.",
  "riesgos": "El lexico de TS puede fallar en JS antiguo (funciones anonimas en callbacks, IIFE); el respaldo sigue existiendo cuando no hay simbolos. Subir el contrato invalida comparaciones con lineas base previas de colecciones con JS.",
  "rollback": "Revertir el commit y ChunkingContract a 3; las colecciones ya reingestadas con v4 se conservan en coleccion aislada hasta decidir.",
  "archivos": "Infrastructure/Chunking/ChunkingStrategyRouter.cs, TypeScriptChunkingStrategy.cs (nombre de lenguaje en cabecera), ChunkingContract.cs, golden master y fixtures, docs/evaluaciones/1.29/.",
  "verificacion": "Golden master nuevo y antiguo en verde; tabla pareada del eval-set JS; conteo de chunks.",
  "_limites": "No toca C#, TS ni Markdown; no cambia el tamano de chunk."
}
```

### Nota sin ficha — H5, sombrilla 15.2 del cliente

`15.2-grafo-persistido-condicional` sigue `bloqueado` en `RVC/docs/analisis-futuro/ejecucion-plan.estado.json:2496-2527`
aunque sus hijas 15.2.1 y 15.2.2 están `hecho` (:2445, :2474) con hipótesis nula del 2026-09-22, medida con MiniLM y
fragmentación 512. Ese ledger no es agenda de la bifurcación (`RVC/AGENTS.md:17-21`); se deja como nota para reconciliar
la sombrilla cuando se retome el cliente, y como aviso de que cualquier reapertura del salto por símbolo debería medirse
sobre arctic c1024, no heredar esa evidencia.

## 5. Persistencia

1. **Hecho (2026-11-11):** este documento vive en `VSE/docs/analisis/chunking-pinecone-vs-motor.md` (carpeta nueva prevista por `workflow-agentes-ia.md:47,64`).
2. **Hecho (2026-11-11):** el experimento se movió a `VSE/docs/evaluaciones/resumen-fast-vs-deep/` con su estructura original (`privado/`, `jueces/`, `jueces2/`; 27 archivos). Verificado antes de copiar: ningún archivo contiene claves ni cabeceras de autenticación; los scripts 02/05/06 leen `ALTHEIA_KEY`/`ALTHEIA_CERT` del entorno y escriben la cabecera en un archivo temporal que borran en `finally`. Sin commit: el usuario decide cuándo.
3. **Pendiente de aprobación:** añadir al ledger rector las siete fichas 1.23 a 1.29 tal cual, todas `pendiente`, ninguna `en_curso`. Orden sugerido de ejecución: 1.28 (texto), 1.26 y 1.27 (correcciones del resumen, sin reingesta), simulación de 1.23, luego 1.24 → 1.25; 1.29 y el cambio de cliente de 1.23 después de cerrar 1.12.

## Estado

### Actividad: análisis y fichas (este documento)
Estado: completada. Documento y experimento persistidos; fichas pendientes de entrar al ledger.

### Actividad: alta de fichas 1.23-1.29 en el ledger rector
Estado: pendiente (requiere aprobación expresa del usuario).

## Changelog

- 2026-11-11 — [análisis] — investigación de solo lectura en ambos repos, tabla, tres fichas y hallazgos — sin pruebas (no aplica).
- 2026-11-11 — [análisis] — hallazgos H1, H2, H3/H4/H6/H9 y H8 convertidos en fichas 1.26-1.29 a pedido del usuario — sin pruebas (no aplica).
- 2026-11-11 — [docs/evaluaciones/resumen-fast-vs-deep] — experimento movido desde la carpeta personal, verificado sin claves — hashes comparados origen/destino.
- 2026-11-11 — [docs/analisis/chunking-pinecone-vs-motor.md] — documento guardado en el repo del motor — sin commit.
