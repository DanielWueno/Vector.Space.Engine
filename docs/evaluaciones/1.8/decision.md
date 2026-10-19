# 1.8 — ¿Adoptar un motor de embeddings existente?

Fecha: 2026-10-19. Ficha: `1.8-evaluar-adopcion-de-motor-de-embeddings-existente`.
Entorno: Docker Desktop local (WSL2, 28 CPU, ~14 GB disponibles en la VM), **solo CPU**.
Evidencia cruda en esta carpeta (`*.json`); banco reproducible en `tools/sidecar-eval/`.

## Decisión

1. **Se adopta un sidecar HTTP para el cálculo del vector denso.** 1.4 ya no porta
   `OnnxVectorizationBrain` tal cual: escribe una implementación de `IVectorizationBrain` que
   llama al sidecar por la ruta OpenAI-compatible (ver "Cambio de alcance de 1.4").
2. **Qué motor (TEI o Infinity) no se fija aquí**: se fija en 1.6, cuando haya modelo elegido,
   porque la compatibilidad resultó depender del modelo (TEI no carga nomic-embed-text-v1.5 ni
   bge-m3 desde `.bin`). El harness de 1.6 se construye contra este mismo compose con ambos.
3. **El vector sparse sigue siendo propio** (`SparseTokenizer` de Rag.Context.Engine, se porta en
   1.4): ninguno de los dos motores entrega el sparse de bge-m3.
4. **EmbedAnything y ragit completos: descartados** (motivos abajo). Quedan como referencia.

## Lo medido (criterio 1 de la ficha)

| Prueba | Infinity 0.0.78 (`latest-cpu`, torch) | TEI 1.8.3 (`cpu-1.8`) |
|---|---|---|
| Carga bge-m3 (solo `pytorch_model.bin`) | Sí, a la primera | **No**: Candle muere por OOM (exit 137) leyendo el `.bin` |
| Carga bge-m3 con su `onnx/` oficial | (no necesario) | Sí, backend ORT; exige `max-batch-tokens` ≥ 8192 o `--auto-truncate` |
| Carga nomic-embed-text-v1.5 | Sí (código remoto de `nomic-bert-2048` descargado aparte) | **No**: `duplicate field max_position_embeddings` al parsear su `config.json` |
| Denso bge-m3 | 1024d, norma 1 | 1024d, norma 1 |
| Mismo embedding entre motores | Las 12 similitudes ES/EN (full y 256d) idénticas a 4 decimales entre ambos | (ídem) |
| Parámetro `dimensions` (Matryoshka) | Aceptado; **trunca sin renormalizar** (norma 0,45 a 256d); dirección = truncar+L2 (cos 1,0) | Aceptado; trunca **y renormaliza** (norma 1,0); cos 1,0 contra truncar+L2 |
| Sparse de bge-m3 | **No existe ruta sparse** (openapi: `/embeddings`, `/rerank`, `/classify`, imagen/audio) | **No**: `/embed_sparse` → 424 "Model is not an embedding model with SPLADE pooling" |
| bge-m3, lote 32 × ~120 palabras | 3,2 textos/s | 4,0 textos/s (mbt 8192); 3,0 (mbt 2048 + auto-truncate) |
| Memoria con bge-m3 (`docker stats`) | ~1,2–1,6 GiB *(probablemente subcontada: no incluye páginas mapeadas del archivo)* | **13,8 GiB** (mbt 8192); 7,5 GiB (mbt 2048 + auto-truncate) |
| Imagen | 3,39 GB | 938 MB |

### Constancia sobre Matryoshka

- Ninguno de los dos motores hace nada específico del modelo: `dimensions` es **truncar el
  vector** (y, en TEI, renormalizar). Pasa igual con bge-m3, que **no** está entrenado con
  Matryoshka, que con nomic-embed-text-v1.5, que sí lo está. "Soportar Matryoshka" no es una
  capacidad que justifique el motor: en .NET son dos líneas (`Take(d)` + L2).
- Consecuencia práctica: **renormalizar siempre del lado cliente**, porque Infinity no lo hace.
  Con distancia coseno en el store da igual; con producto punto no.
- Lo que Matryoshka sí aporta —que la calidad aguante al truncar— depende del entrenamiento del
  modelo y **no se midió aquí** (3 pares no son un recall). Es parte del recall@10 de 1.6.

### Hallazgos que pasan a 1.6 (no son decisión de esta ficha)

- nomic-embed-text-v1.5 **no separa ES/EN** en la sonda: una frase española no relacionada queda
  más cerca que la traducción inglesa en 2 de 3 pares (`infinity-nomic-v1.5.json`). n=3: es una
  señal, no un descarte; el criterio (1) de 1.6 lo decide.
- bge-m3 en CPU da ~3–4 textos/s de ~120 palabras con 28 núcleos: el coste de cómputo del
  modelo, no del motor. Entra en el criterio (5) de 1.6.

## Cambio de alcance de 1.4 (criterio 2)

- **Antes:** portar `OnnxVectorizationBrain` (tokenizadores WordPiece/SentencePiece, mean
  pooling, L2) a este repositorio.
- **Ahora:** implementar `IVectorizationBrain` como cliente HTTP del sidecar (ruta
  OpenAI-compatible `POST /v1/embeddings` en TEI, `POST /embeddings` en Infinity; mismo
  formato de petición y respuesta), con L2 del lado cliente. Pooling, tokenizador y prefijos por
  modelo los resuelve el sidecar a partir del modelo: cambiar de candidato en 1.6 deja de
  requerir código por modelo.
- **Se sigue portando:** `SparseTokenizer` (sparse léxico propio) y el pipeline de ingesta.
- **Riesgo nuevo que 1.4 debe verificar:** las colecciones de Rag.Context.Engine están en MiniLM
  384d calculado por el ONNX propio. Si se quieren conservar sin reingestar, 1.4 tiene que
  comprobar paridad numérica (coseno ≈ 1) entre el ONNX propio y el sidecar sirviendo el mismo
  MiniLM. No se probó aquí.

## Qué se descarta y por qué (criterios 3 y 4)

- **EmbedAnything (completo: ingesta + chunking + embeddings + store).** Descartado como
  plataforma. Sustituiría el pipeline de ingesta de Rag.Context.Engine y, con él, su
  `content_hash` y sus IDs deterministas, que son justo el criterio de equivalencia de 1.4.
  También traería un runtime Rust/Python dentro de la superficie de ingesta, que 1.3 ya resuelve
  sobre la plantilla .NET con identidad Reyma. Queda como **referencia** de chunking
  semántico/tardío. **No se corrió localmente**: el motivo del descarte es estructural, no de
  rendimiento medido.
- **ragit.** Descartado por el mismo motivo estructural; además, solo se vio por fragmentos de
  búsqueda, sin leerlo a fondo. **No se corrió.**
- **Infinity/TEI como plataforma completa:** no aplica; solo calculan embeddings y rerank, y así se
  adoptan.

## Límites de esta evaluación

- Solo CPU; no se probó GPU ni cuantización (int8/fp16).
- Las cifras de memoria de Infinity probablemente están subcontadas (ver tabla).
- No se probó un modelo SPLADE en `/embed_sparse` de TEI. Que eso funcione con SPLADE es
  documentación, no medida.
- No se verificó la actividad o mantenimiento actual de los repositorios (estrellas y commits
  vienen de la investigación previa de la ficha).
- Para descargar se usó el host (`descargar_modelos.py` con `truststore`) porque la inspección
  TLS corporativa rompe la descarga desde los contenedores. En el servidor real habrá que repetir
  esto o montar la CA corporativa en la imagen; **no** se desactiva la validación TLS.
