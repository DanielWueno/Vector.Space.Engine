# Vector.Space.Engine

Shared ingestion, embedding and native search service, separated from
`Rag.Context.Engine`. VectorSpace manages the indexed data; its consumers retain
their RAG orchestration, conversational answer generation and presentation.

## Current state

The implementation includes chunking, embedding profiles, Qdrant indexing,
indexed business summaries, native dense/sparse search, summary search, source
metadata and explicit filters. Qdrant was selected in item `1.2`; that decision
is no longer pending. HTTP authentication remains in place; the former
per-collection `ControlDeAcceso` mechanism was removed in item `1.14`.

The `Rag.VectorSpace.Client` integration remains under evaluation in `1.12`.
H3 search screening passed, but primary acceptance was rejected by a new
fabrication. The client is not accepted or enabled by updating this README.
Current state and historical results are recorded in
`docs\plan\ejecucion-plan.estado.json`.

## Scope and ownership

- **VectorSpace:** ingestion, chunking, embeddings, index lifecycle, indexed
  business summaries and native search/filtering over code and summary spaces.
  Native dense/sparse fusion and summary generation during ingestion belong here.
- **RAG consumers:** final code/summary fusion, symbol-hop orchestration,
  reranking/calibration, meta-intent, confidence gating, context assembly,
  prompts, conversational answer generation, history and response streaming.
- **Client applications:** API/CLI, presentation, explicit profile/collection
  selection and transport. `Rag.VectorSpace.Client` is a client of the data
  service, not a presentation-only application.
- Moving the remaining RAG orchestration into VectorSpace would be a separate
  architecture expansion. The unexecuted `1.12.1` through `1.12.6` proposals were
  discarded from the current plan; they are not acceptance prerequisites.

## Remaining work

For local client acceptance, no additional RAG relocation is required. The
remaining client-owned work is tracked by `1.12.7` through `1.12.10`: complete
source oracles, genuine risk evidence, a supplementary evaluation producer and
effective-ask diagnosis. The parent `1.12` retains the full quality gate.
Integration tests can expose engine defects; any such defect needs evidence and
a scoped fix rather than a presumed architectural migration.

Separate, pre-existing conditional work remains: Azure DevOps ingestion
automation (`1.7.2`), tenant semantics (`1.21.5`) and restricted-access design
(`1.21.6`). These are not prerequisites for the explicit Local client.
The old access-condition reference in `1.7.2` must be reconciled with `1.14`
before that separate task executes; this scope alignment does not authorize it.

## Documentation and development

- [HTTP contract and authentication](docs/servicio/contrato-api.md).
- [Search filters and supported capabilities](docs/servicio/filtros.md).
- [Service setup, Docker and operations](docs/servicio/README.md).
- [Current 1.12 preparation and limits](docs/evaluaciones/1.12/preparacion.md).

This documentation is not deployment or connection authorization. Environment
access, credentials and shared data retain their explicit scope restrictions.

## Origin and plan

Separated from `Rag.Context.Engine` on 2026-10-19, following its item `25.7`.
The original analysis is retained in that repository at
`docs\analisis-futuro\motor-embeddings-ingesta-servicio-compartido.md`.

`docs\plan\ejecucion-plan.estado.json` is the authoritative plan, including
client-owned 1.12 tasks. New active documentation is written in English;
existing ledger keys, stable IDs and historical evidence retain their original
form for compatibility and provenance.
