# 1.6 — Estrategia de modelos de embeddings

Fecha: 2026-10-19/20. Ficha: `1.6-decidir-estrategia-de-modelos-de-embeddings`.
Entorno: host Windows (28 CPU, torch 2.14.1 CPU) y Docker Desktop (TEI `cpu-1.8`), **solo CPU**.
Banco reproducible: [`tools/embeddings-eval/`](../../../tools/embeddings-eval/README.md). Evidencia cruda:
`resultados/*.json`, `fuentes-primarias.json`, `preguntas-en.json`.

## Decisión

1. **Un solo modelo para documentación y código: `Snowflake/snowflake-arctic-embed-m-v2.0`, guardado a
   256 dimensiones (Matryoshka: truncar + L2).** Empata en calidad con bge-m3 y Qwen3-Embedding-0.6B
   (diferencias dentro del ruido de 57 preguntas, comparación pareada abajo) con el menor coste: frente
   a bge-m3, 2,5× más rápido en el host y 1,3× en TEI con menos RAM; frente a Qwen3, ~18× en el host;
   y vectores 4× más chicos que bge-m3/Qwen3 (y más chicos que los 384d de hoy).
2. **Sidecar: TEI 1.8.3** (`text-embeddings-inference:cpu-1.8`). Sirve arctic con implementación
   nativa (sin código remoto), paridad coseno ≥ 0,9986 con la referencia. Infinity 0.0.78 **no arranca**
   con arctic (exige xformers). Cierra lo que 1.8 dejó abierto.
3. **MiniLM se retira**: en este banco recupera 4/57 con el denso (16/57 sólo gracias al sparse).
4. **No se adopta modelo por tipo de proyecto**: el mismo modelo lidera en docs y en código, y el
   modelo de código evaluado (jina-v2-base-code, solo inglés) recupera 1/19 con preguntas en español.
5. El **sparse léxico propio se conserva** (independiente del denso), pero la fusión RRF nativa con el
   modelo nuevo **resta** recall frente al denso solo: se registra como ítem 1.10, no se decide aquí.

## Protocolo (fijado antes de medir, con el usuario)

- Corpus: `bsuite-auditorias-test` (2084 chunks), `innovapp-docs` (743), `micro-repo` (1903), con los
  eval-sets de Rag.Context.Engine (57 preguntas respondibles, 4 fuera de dominio). `bsuite-repo`
  (~22,6k chunks) fuera por coste.
- El baseline 5.e **no es comparable** (MiniLM qint8 arm64, tres bandas con resumen, colecciones no
  indexadas aquí, eval-set de tickets re-etiquetado): referencia 22/57 hit@10. El control es MiniLM
  re-medido aquí sobre los **mismos chunks**; entre arms solo cambia el denso.
- Chunks: ingesta real del CLI (`ingestar-control.ps1`) en un Qdrant efímero, sin `--con-resumen`.
  Sparse de la consulta: `SparseTokenizer` real (`QueryVectors`). Retrieval: reproducción exacta de
  `QdrantSemanticRetriever` sin banda resumen (denso-solo y fusión nativa). **Fidelidad: el control
  reproduce el `rag eval` real 61/61 en hit@k y en top-10 exacto.**

## Criterio 0 — fuente primaria (Hugging Face, 2026-10-19; `fuentes-primarias.json`)

| Candidato (tabla externa) | Verificado | Destino |
|---|---|---|
| nomic-embed-text-v1.5 | 137M, 768d, idioma declarado `en` | Descartado: mono-idioma (1.8 ya vio que no separa ES/EN) |
| nomic-embed-text-v2-moe (sucesor) | 475M MoE, multilingüe, Apache-2.0 | **No medido por coste** (decisión de presupuesto) |
| multilingual-e5-small | 118M, 384d, 94 idiomas, MIT | Medido |
| snowflake-arctic-embed-s | 33M, sin idiomas (v1 inglés) | Descartado: superado por arctic-embed-m-v2.0 (multilingüe) |
| snowflake-arctic-embed-m-v2.0 | 305M, 768d (MRL 256), 74 idiomas, Apache-2.0, 8k | **Medido — elegido** |
| EmbeddingGemma-300m | Licencia Gemma, acceso restringido | Descartado: superado por `embeddinggemma-2` (2026-09, Apache-2.0) |
| embeddinggemma-2 | 744M, arquitectura nueva | **No medido por coste** (más pesado que Qwen3, que ya rinde 0,3 chunks/s) |
| Qwen3-Embedding-0.6B | 596M, 1024d (MRL), 32k, Apache-2.0 | Medido |
| jina-embeddings-v2-base-code | 161M, `en` + código | Medido solo en micro-repo como prueba del "modelo por tipo" |
| jina-code-embeddings-0.5b, jina-embeddings-v3 | CC-BY-NC-4.0 | Descartados: no comerciales |
| granite-embedding-97m/311m-multilingual-r2 | 2026-04, Apache-2.0, ModernBERT | Descartados por coste medido: 3,3 textos/s (sdpa) o 0,64 (eager) frente a 21 de e5-small con los mismos 64 textos |
| bge-m3 (histórico) | 568M, 1024d, MIT | Medido |
| nomic-embed-code | 7B | Descartado: inviable en CPU |

## Criterios 1 y 2 — recall@10 (hit-any, denso solo / fusión denso+sparse)

| Modelo | ES denso | ES fusión | EN denso | auditorías / innovapp / micro (ES denso) |
|---|---|---|---|---|
| MiniLM, ONNX de producción (control) | 4/57 | 16/57 | 5/57 | 1/16 · 3/22 · 0/19 |
| MiniLM ventana 128 | 9/57 | 18/57 | — | 1 · 7 · 1 |
| multilingual-e5-small | 31/57 | 30/57 | **17/57** | 10 · 11 · 10 |
| bge-m3 | 39/57 | 38/57 | 38/57 | 12 · 13 · 14 |
| arctic-embed-m-v2.0 (768d) | 38/57 | 36/57 | 37/57 | 11 · 13 · 14 |
| **arctic-embed-m-v2.0 @256d** | **40/57** | 36/57 | 36/57 | 13 · 14 · 13 |
| Qwen3-Embedding-0.6B | 41/57 | 39/57 | 36/57 | 12 · 16 · 13 |
| jina-v2-base-code (solo micro-repo) | 1/19 | 5/19 | — | — |

- **Criterio 1 (ES/EN simétrico):** las 61 preguntas traducidas al inglés (`preguntas-en.json`,
  mismas anclas). e5-small cae de 31 a 17: **no es simétrico, descartado**. bge-m3, arctic y Qwen3
  aguantan (pierden 1–5).
- **Pareado frente al control:** ningún candidato pierde una sola pregunta (arctic@256 gana 36,
  pierde 0). **Entre finalistas, frente a arctic@256:** Qwen3 gana 10 / pierde 9; bge-m3 gana 6 /
  pierde 7. Empate (`resumen-vs-*.json`).
- **Matryoshka (truncar + L2):** arctic 768→256 no pierde (38→40, ruido); bge-m3, que no está
  entrenado para ello, sí (39→37→36 a 512/256); Qwen3 41→39 a 256; e5-small 31→25.

## Criterio 3 — coste de reingesta

- Lo que cuesta es **recalcular embeddings**, no el LLM: la caché de resúmenes se indexa por
  `(content_hash, prompt_version)` y `prompt_version` depende del prompt y del modelo de Ollama, no
  del de embeddings. Cambiar de modelo de embeddings da **0 misses** de resumen si la caché viaja.
- En este equipo: `rag-engine` (2004) y `micro-repo` (956) servidas, 384d, sin vector resumen; caché
  de resúmenes con 0 filas → **0 llamadas LLM**. 384d→256d exige recrear colecciones (el tamaño de
  vector es inmutable en Qdrant): 2960 chunks ≈ **28 min en TEI** (1,79 chunks/s) o ≈ 10 min en el
  host. `micro-repo` servida está desactualizada (956 puntos frente a 1903 chunks del repo actual).
- Las colecciones históricas del equipo original (`bsuite-repo` ~22,9k chunks con resumen) no están
  aquí: ≈ 3,6 h de TEI para los chunks más un vector por resumen; los misses de esa caché se cuentan
  allí antes de reingestar (no medibles desde este equipo).

## Criterio 4 — perfil por colección y sparse

- El contrato de 1.3 ya expresa el modelo como atributo de la colección: `POST
  api/colecciones/{coleccion}/embeddings` devuelve `modelo` y `dimension`. Se mantiene así: el modelo
  y la dimensión los fija la colección (manifiesto), nunca la petición. Cambiar de modelo = colección
  nueva y reingesta. Con un solo modelo no hace falta un catálogo de perfiles.
- El `sparse-code` (TF propio, `SparseTokenizer`) no depende del denso y se porta igual (1.4). Pero con
  un denso fuerte la fusión nativa resta: arctic@256 40→36, Qwen3 41→39, bge-m3 39→38; solo ayudaba a
  MiniLM (4→16). Pesos y forma de fusión calibrados para MiniLM: recalibrar en **1.10**.

## Criterio 5 — coste de cómputo en CPU

| Modelo | Host sentence-transformers (chunks/s) | TEI 1.8.3 en Docker (chunks/s · ms/consulta · RAM) | Paridad TEI vs referencia |
|---|---|---|---|
| e5-small | 12,7–21,1 | 5,08 · 34 · 6,3 GiB | cos mín 0,99902 |
| **arctic-m-v2** | 4,95–5,29 | **1,79 · 75 · 7,1 GiB** | cos mín 0,99856 (2 textos en el borde de 512 tokens) |
| bge-m3 | 1,96–1,99 | 1,40 · 208 · 9,1 GiB | cos mín 0,99848 |
| Qwen3-0.6B | 0,25–0,31 | 0,55 · 246 · 3,2 GiB | **rota**: cos 0,09–0,15 en lotes de textos largos (reproducido 2×) |

- TEI CPU rinde 2,5–4× menos que torch en el host con los mismos textos (ventana 512 recortada en el
  cliente, `max-batch-tokens 4096`). Es el coste real de la arquitectura destino; optimizarlo
  (lotes, cuantización) queda para 1.4/1.5, no cambia el ranking.
- Qwen3 en TEI CPU no es utilizable: además de lento, devuelve vectores incorrectos cuando un lote
  junta varias entradas largas (el mismo texto da 0,9986 en un lote mixto y 0,09 en uno de largos).

## Hallazgos que pasan a otros ítems

- **1.4:** paridad ONNX propio ↔ MiniLM de referencia medida: coseno ≥ 0,9998 en los 4730 chunks
  (`paridad-minilm-onnx-vs-st.json`). Es irrelevante si se adopta arctic (todo se reingesta), pero
  deja resuelta la medida que 1.4 pedía. Con arctic, `IVectorizationBrain` = cliente de TEI con
  recorte a 512 tokens, `dimensions: 256` (o truncar + L2 en cliente) y prompt `query: ` solo en
  consultas.
- **1.10 (nuevo):** recalibrar o retirar la fusión denso+sparse con el modelo nuevo.
- Ventana: MiniLM rinde más a 128 que a 256 tokens en docs (3→7 en innovapp, denso); el chunker
  produce chunks largos (mediana ~900 caracteres en los que divergen). Dato para el chunking, no
  para esta decisión.

## Límites

- 57 preguntas en tres corpus; diferencias de ±2 aciertos son ruido. `bsuite-repo`, la banda resumen
  y el reranker quedan fuera del banco.
- Fuentes de los corpus con cambios locales sin commitear (BusinessSuite.Xaf: 2 archivos;
  Tickets: 3), commits `6a7b5218`, `bb7f103`, `caa9e5f`.
- `nomic-embed-text-v2-moe` y `embeddinggemma-2` no se midieron (coste); granite-r2 tampoco
  (rendimiento ModernBERT en CPU con transformers 5.20).
- arctic y jina en el host necesitaron transformers 4.57 (su código remoto rompe con 5.x); TEI no
  usa ese código.
- Traducciones al inglés hechas por el asistente (revisables en `preguntas-en.json`).
- La ingesta de control dejó 5 corridas en `%LOCALAPPDATA%\rag-engine\ingestion-state.sqlite3`
  (journal: solo lo lee `rag ingest-status`; no afecta ingestas). No se tocó.
- Tiempo de máquina real: ~7 h de reloj (presupuesto ampliado por el usuario: 3 h → 4,7 h → sin
  tope).
