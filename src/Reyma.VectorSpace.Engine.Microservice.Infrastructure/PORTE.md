# Porte del motor desde Rag.Context.Engine (item 1.4)

Origen: `D:\Git\Rag.Context.Engine`, commit `aae1de8`, proyecto `src/RagEngine.Core`. Solo lectura: Rag.Context.Engine
conserva su pipeline local (decision del usuario del 2026-10-21; su paso a cliente es el item 1.12).

## Copiado sin cambios de comportamiento

Solo cambia el namespace (`RagEngine.Core.*` -> `Reyma.VectorSpace.Engine.Microservice.Infrastructure.*`; `Domain` pasa
a `Model` para no confundirse con el proyecto Domain del servicio). Si alguno se toca, el golden master de abajo debe
seguir pasando o el cambio tiene que subir `ChunkingContract.Version`.

| Origen | Destino |
|---|---|
| `Infrastructure/Chunking/*` (4 estrategias, `ChunkBuilder`, `SourceLines`, `ParagraphBudget`, `SymbolExtractor`, router, contrato) | `Chunking/` |
| `Infrastructure/Vectorization/SparseTokenizer.cs`, `Abstractions/ISparseTokenizer.cs`, `IChunkingStrategy.cs` | `Vectorization/`, `Abstractions/` |
| `Utilities/ContentHasher.cs`, `DeterministicGuid.cs`, `TokenEstimator.cs` | `Utilities/` |
| `Domain/CodeChunk*.cs`, `RawArtifact.cs`, `ChunkingOptions.cs`, `ScanProfile.cs`, `RankingOrder.cs`, `SourceLanguage.cs` (sin sus extensiones de generacion) | `Model/` |
| `Infrastructure/VectorStore/DeterministicVectorQuery.cs` (sin el contador de diagnostico) | `VectorStore/` |
| `DefaultIngestionPipeline.IsIndexable`, `QdrantSemanticRetriever.FuseNativeRanks`, `ExtensionMap` del escaner | dentro de `Ingestion/`, `VectorStore/`, `Model/DocumentArtifacts.cs` |

Pruebas portadas igual (`tests/.../Motor/`): golden master del chunking con sus fixtures LF/CRLF y su
`chunking.json` **sin regenerar**, `SparseTokenizerTests`, `IngestionChunkFilterTests`, `TypeDeclarationChunkingTests`,
`TypeScriptChunkingStrategyTests`.

## Adaptado al contrato de 1.3

- `Ingestion/DocumentIngestionPipeline`: fase 1 de `DefaultIngestionPipeline` sobre documentos recibidos por HTTP.
  Repositorio = coleccion. `MaxTokensPerChunk` de configuracion, 1024 por defecto (1.11). Huella por documento para
  contar "sin cambios" sin volver a vectorizar; barrido de obsoletos acotado a los documentos de la solicitud; un
  fallo de dependencia aborta la solicitud.
- `VectorStore/QdrantVectorStore`: mismo esquema y payload; sin `dense-resumen`, `resumen_pending`, `tenant` ni
  manifiesto; con `document_fingerprint` e indice de `file_path`. Qdrant.Client 1.19.0 (1.2).
- `VectorStore/QdrantSemanticRetriever`: densa sola por defecto (1.10); RRF nativa tras `Motor:Busqueda:Modo`.
- `Vectorization/TeiVectorizationBrain`: sustituye a `OnnxVectorizationBrain` (1.8). Ruta OpenAI-compatible, sin
  recortar el texto, `query: ` solo en consultas, truncado a 256 y L2 en el cliente (1.6, 1.8, 1.11).

## No portado

Fase 2 de resumen LLM y su cache, reranker cross-encoder ONNX, salto por simbolo (6.a), perfiles por coleccion (7.a),
filtros de modulo/tenant/lenguaje, manifiesto de coleccion, auditoria y estado SQLite por corrida, escaner de disco
(el origen `ruta` es el item 1.7) y todo el lado de generacion (`/api/ask`).

## Como se verifico

`tools/equivalencia-motor/` (ver su README): mismos Ids, `content_hash`, payload y `sparse-code` que el pipeline real
del origen sobre las mismas fuentes, y vectores frente al banco de 1.11. Resultado en el ledger, item 1.4.
