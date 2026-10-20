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

Arms con código remoto antiguo (`arctic-m-v2`, `jina-v2-code`) necesitan `$env:VSE_LEGACY=1`
(transformers 4.x). Rollback: `docker rm -f vse-16-qdrant vse-16-tei` y borrar `.cache/embeddings-eval`.
