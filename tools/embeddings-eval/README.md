# Banco de modelos de embeddings (1.6)

Compara modelos de embeddings sobre los **mismos chunks** que produce Rag.Context.Engine, cambiando
solo el vector denso. Decisión y cifras: [docs/evaluaciones/1.6/decision.md](../../docs/evaluaciones/1.6/decision.md).

No toca `rag-qdrant` ni ninguna colección servida: la ingesta de control va a un Qdrant efímero
(`vse-16-qdrant`, 127.0.0.1:16333/16334) y todo lo derivado queda en `.cache/embeddings-eval/`
(ignorado por git: contiene código y documentos de los repos fuente).

## Piezas

| Archivo | Qué hace |
|---|---|
| `ingestar-control.ps1` | Ingesta real (CLI de Rag.Context.Engine, MiniLM ONNX fp32) de `bsuite-auditorias-test`, `innovapp-docs` y `micro-repo` en el Qdrant efímero, sin `--con-resumen`, y su `rag eval --json --dump-hits`. |
| `QueryVectors/` | Vectoriza las preguntas del eval-set con el `SparseTokenizer` y el ONNX brain **reales** (referencia de proyecto al checkout hermano de Rag.Context.Engine). |
| `harness.py` | `exportar` (chunks del Qdrant efímero), `descargar`, `embeber <arm>` (sentence-transformers, CPU), `traducir`, `evaluar <arm>[@dims][+en]`, `fidelidad`. |
| `sidecar.py` | El mismo modelo servido por TEI/Infinity: paridad coseno contra la referencia y rendimiento. |
| `comparar.py` | Tabla de recall y comparación pareada (gana/pierde por pregunta) contra un control. |
| `paridad_minilm.py` | ONNX propio frente a MiniLM de referencia (medida que pide 1.4 antes de conservar colecciones 384d). |
| `py.ps1`, `cola.ps1`, `tei.ps1` | Entorno efímero de `uv` (sin instalar nada global), cola secuencial de arms y TEI local offline. |

## Retrieval que se reproduce

`QdrantSemanticRetriever` sin banda resumen: rama densa exacta (coseno ≥ 0,1, 40 candidatos), rama
`sparse-code` (40), RRF nativa `1/(rank+2)` en f32 con desempate por `ChunkId` ordinal, top 10; y un
modo denso-solo. Acierto = regla de `EvalCommand.Evaluate`. `harness.py fidelidad` exige que el arm
`onnx-produccion` reproduzca el `rag eval` real: medido 61/61 en hit@k y en top-10 exacto.

## Orden

```powershell
docker run -d --rm --name vse-16-qdrant -p 127.0.0.1:16333:6333 -p 127.0.0.1:16334:6334 qdrant/qdrant:v1.14.1
dotnet build D:\Git\Rag.Context.Engine\src\RagEngine.Cli -c Release
.\ingestar-control.ps1
# QueryVectors: ver cabecera de Program.cs (variables OnnxBrain__ModelPath/VocabPath)
.\py.ps1 exportar; .\py.ps1 evaluar onnx-produccion; .\py.ps1 fidelidad
.\py.ps1 descargar <arm>...; .\py.ps1 embeber <arm>; .\py.ps1 evaluar <arm>
python comparar.py
# Consultas en ingles (criterio 1): traducir, QueryVectors sobre .cache/embeddings-eval/eval-en/*,
# $env:VSE_IDIOMA="en"; .\py.ps1 embeber <arm>; .\py.ps1 evaluar <arm>+en
# Sidecar: .\tei.ps1 <repo>; .\py.ps1 sidecar.py medir <arm> tei http://127.0.0.1:18080 <nombre>
```

## Ventana y re-fragmentación (1.11)

Decisión: [docs/evaluaciones/1.11/decision.md](../../docs/evaluaciones/1.11/decision.md). `ventana.py` compara
arctic@256 con ventana 512 (vectores de 1.6), 1024 y 8192, y con chunks re-fragmentados. `Refragmentar/` corre el
`IIngestionPipeline` real con otro `MaxTokensPerChunk`, porque el CLI lo fija en 512 en código.

```powershell
dotnet build Refragmentar -c Release
.\ingestar-control.ps1 -Sufijo -r512 -MaxTokensPerChunk 512; .\py.ps1 exportar -r512; .\py.ps1 ventana.py fidelidad -r512
.\ingestar-control.ps1 -Sufijo -c1024 -MaxTokensPerChunk 1024; .\py.ps1 exportar -c1024
$env:VSE_LEGACY=1; .\py.ps1 ventana.py embeber w1024 w8192 c1024-w8192
.\py.ps1 ventana.py evaluar w512 w1024 w8192 c1024-w8192; .\py.ps1 ventana.py comparar w1024 w8192 c1024-w8192
.\py.ps1 ventana.py presupuesto c1024-w8192
.\tei.ps1 Snowflake/snowflake-arctic-embed-m-v2.0 8192; .\py.ps1 ventana.py tei <brazo> http://127.0.0.1:18080
```

## Expansión por cercanía en el archivo (1.23)

Resultado: [docs/evaluaciones/1.23/resultado.md](../../docs/evaluaciones/1.23/resultado.md). `expansion.py` reproduce
el ensamblador del cliente (12 000 caracteres) sobre el top-10 denso de `c1024-w8192` ya guardado y lo compara con tres
variantes de expansión. No necesita Qdrant ni embeddings: lee los chunks `-c1024`, los eval-sets y las fuentes en
`D:\Git` (para ubicar cada chunk por línea).

```powershell
uv run --system-certs --no-project --python 3.12 --with tokenizers python expansion.py           # simula y resume
uv run --system-certs --no-project --python 3.12 --with tokenizers python expansion.py resumen   # solo la tabla
```

No lances dos `py.ps1` a la vez con distinto `VSE_LEGACY`: `uv` choca al preparar los entornos y el proceso
termina con código 10.

Arms con código remoto antiguo (`arctic-m-v2`, `jina-v2-code`) necesitan `$env:VSE_LEGACY=1`
(transformers 4.x). Rollback: `docker rm -f vse-16-qdrant vse-16-tei` y borrar `.cache/embeddings-eval`.
