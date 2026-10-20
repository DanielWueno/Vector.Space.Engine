# 1.11 — Aprovechar la ventana de contexto de arctic

Fecha: 2026-10-20. Ficha: `1.11-aprovechar-ventana-de-contexto-de-arctic`.
Entorno: host Windows (CPU) y Docker Desktop (TEI `cpu-1.8`, Qdrant efímero `v1.14.1`), **solo CPU**.
Banco: [`tools/embeddings-eval/`](../../../tools/embeddings-eval/README.md) (`ventana.py`, `Refragmentar/`).
Evidencia cruda: `resultados/*.json`.

## Decisión

**Se re-fragmenta con `MaxTokensPerChunk = 1024` y se embebe cada chunk completo** (ventana de arctic, 8192;
sin el recorte a 512 que 1.6 dejó para 1.4). **Ampliar solo la ventana sobre los chunks actuales no se adopta.**

Criterio **enmendado por el usuario** durante la ficha. La regla original pedía "ganar sin perder ninguna en ES
ni en EN". El usuario fijó que las consultas reales serán en español, porque todos los usuarios finales son
hispanohablantes, y que lo que se indexa es una mezcla de español, inglés y espanglish. El eval ES (preguntas en
español sobre los corpus tal como están) es el que representa ese uso. El EN (las mismas preguntas traducidas)
queda como dato informativo, no como veto. Con la regla original, c1024 no se adoptaría porque pierde 3
preguntas EN.

## Protocolo

- Mismos tres corpus y eval-sets que 1.6 (57 preguntas respondibles), arctic-embed-m-v2.0 @256 (truncado +
  L2), denso solo y fusión nativa, retrieval reproducido como en 1.6. Control `w512` = vectores de 1.6
  re-evaluados: reproduce 40/57 ES denso, 36 fusión y 36/57 EN denso.
- Brazos: `w1024` y `w8192` (mismos chunks, ventana mayor) y `c1024-w8192` (re-fragmentación a 1024).
- **Re-fragmentación.** El CLI de Rag.Context.Engine fija `MaxTokensPerChunk = 512` en código
  (`IngestCommand.cs`) y no lo lee de configuración. `Refragmentar/` llama al `IIngestionPipeline` real con
  la misma composición y la misma `IngestionRequest` que el CLI, y solo cambia ese valor.
  **Fidelidad:** a 512 reproduce los 4730 chunks del CLI con los mismos IDs, contenido, `content_hash` y
  sparse (0 diferencias), lo que también confirma que las fuentes no cambiaron desde 1.6.
- **Vectores.** Solo cambian los chunks de más de 512 tokens (o los textos nuevos de la re-fragmentación): el
  resto se reutiliza. Control: 32 chunks reutilizados por colección y brazo, re-embebidos, dieron cos = 1,0.
- **Anclas:** las 57 anclas existen en algún chunk del archivo objetivo en ambas fragmentaciones. No se
  excluye ninguna pregunta.
- **Contrapeso:** tokens del `content` del top-10 (lo que `GenerationContextAssembler` pasa al LLM), contados
  con el tokenizador de arctic, y chunks/s de TEI con la misma configuración para todos los brazos.

## Tamaño de los chunks (tokens de arctic, con especiales)

| Fragmentación | chunks | p50 | p90 | máx | > 512 | > 1024 |
|---|---|---|---|---|---|---|
| 512 (1.6) | 4730 | 195–232 | 494–1658 | 3576 | 619 | 250 |
| 1024 | 4157 | 172–221 | 528–784 | 3576 | 561 | 190 |

El presupuesto del chunker es una estimación por caracteres: un chunk de "512" llega a 2025 tokens reales en
código (`bsuite-auditorias-test`, p90 1658). A 512, el 13 % de los chunks perdía la cola.

## Resultados — hit@10 pareado contra w512

| Brazo | ES denso | EN denso | ES fusión | EN fusión | tokens top-10 (ES denso) |
|---|---|---|---|---|---|
| w512 (1.6) | 40 | 36 | 36 | 33 | 2785 |
| w1024 | 40 (+1/−1) | 35 (+1/−2) | 37 (+2/−1) | 31 (+0/−2) | 2802 |
| w8192 | 42 (+3/−1) | 35 (+1/−2) | 37 (+2/−1) | 31 (+0/−2) | 2817 |
| **c1024-w8192** | **46 (+6/−0)** | 36 (+3/−3) | 38 (+2/−0) | 29 (+1/−5) | **3518 (+26 %)** |

- **Solo ventana:** gana y pierde también en ES. Con w8192 se pierde "¿Cómo se determina si un empleado es el
  auditado…?" en ES y en EN, porque el chunk completo diluye el ancla. No se adopta.
- **c1024:** gana 6 preguntas ES sin perder ninguna (plan de acción de auditoría, PDCA, encuesta de ticket
  ajeno, notificación por correo/app, datos obligatorios de un ticket, cierre de ticket). Con 6 a 0, la prueba
  de signo da p ≈ 0,03.

### Control de contexto: ¿mejores chunks o solo más texto?

w512 alargado más allá del top-10 hasta igualar, pregunta por pregunta, los tokens del top-10 de c1024
(k medio 12,6 en ES y 12,2 en EN; `presupuesto-c1024-w8192.json`):

| | c1024 top-10 | w512 con el mismo presupuesto | pareado |
|---|---|---|---|
| ES denso | 46 | 42 | **+4 / −0** |
| EN denso | 36 | 40 | +2 / −6 |

En ES, 4 de las 6 ganancias son de la fragmentación y no del contexto extra. En EN, el empate a top-10 se
debe al contexto adicional: con el mismo presupuesto, 512 recupera 4 preguntas EN más. Es el coste que el
usuario acepta al priorizar las consultas en español.

## Coste en TEI (muestra 1 de cada 19 chunks, `--max-batch-tokens 8192`, mismo contenedor)

| Brazo | textos | máx tokens | chunks/s | tokens/s | reingesta estimada del corpus |
|---|---|---|---|---|---|
| w512 | 251 | 512 | 1,93 | 510 | 4730 / 1,93 ≈ 41 min |
| w1024 | 251 | 1024 | 1,56 | 468 | — |
| w8192 | 251 | 2025 | 1,22 | 406 | — |
| **c1024-w8192** | 221 | 3576 | **0,82** | 282 | 4157 / 0,82 ≈ **85 min (≈ 2×)** |

El rendimiento por token baja con la longitud (la atención crece con L²). La muestra de c1024 incluye el chunk
más largo (3576 tokens), así que la estimación de 2× es aproximada.

## Hallazgos que pasan a otros ítems

- **1.4:** el porte debe dejar `MaxTokensPerChunk` configurable (el CLI lo fija en código) y en 1024. El brain
  HTTP **no** recorta a 512: envía el chunk completo y TEI trunca solo por encima de 8192 con
  `--auto-truncate`. En TEI, `--max-batch-tokens` debe ser ≥ 8192. Los tokens del chunker no son tokens de
  arctic: el chunk más largo medido tiene 3576.
- **1.10:** calibrar la fusión sobre los vectores `c1024-w8192`, no sobre los de 1.6. Con c1024, la fusión EN
  cae a 29 (−5/+1).
- Fuera de alcance: subir el top-k con 512 (~13) daría ES 42 y EN 40 con el mismo contexto que c1024. Queda
  como dato, no como alternativa medida en sus propios términos.

## Límites

- 57 preguntas en tres corpus. Fuera del banco: `bsuite-repo`, la banda resumen y el reranker.
- El límite de 160 tokens del resumen LLM (`OllamaBusinessSummaryGenerator.cs`, "para caber bajo los 256 del
  embedder") **se anota y no se mide** aquí, como pide la ficha: con arctic ya no aplica esa razón.
- Solo se probó una re-fragmentación (1024). Un tamaño mayor podría ganar más en ES o diluir más.
- Los tokens del top-10 se cuentan con el tokenizador de arctic. El LLM de generación tiene el suyo.
- Las preguntas EN las tradujo el asistente en 1.6. No hay eval-set de preguntas mixtas ES/EN.
- La ingesta de `Refragmentar` deja corridas en `%LOCALAPPDATA%\rag-engine\ingestion-state.sqlite3`, como la
  de 1.6 (es un journal: no afecta las ingestas).
- Tiempo de máquina: ~1 h 15 min (presupuesto: 1,5 h). Qdrant efímero y TEI borrados. `rag-qdrant` no se
  tocó. Los chunks re-fragmentados quedan en `.cache/embeddings-eval/` (ignorado por git).
