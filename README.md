# Vector.Space.Engine

Servicio independiente de embeddings e ingesta: el "músculo" de vectorización,
separado de `Rag.Context.Engine` para poder exponerse como API compartida al
equipo sin arrastrar retrieval, generación RAG, vista o analíticas.

## Estado

2026-10-19: API generada desde `Reyma.Microservice.Template` (`dotnet new reyma-api`,
`Reyma.VectorSpace.Engine`) con el contrato de embeddings/ingesta y control de acceso por
coleccion; el motor aun no esta portado (responde 501 hasta el item 1.4). Sin repositorio
remoto, sin despliegue, sin conexion a ningun servidor. Ver `docs/plan/ejecucion-plan.estado.json`.

- Contrato HTTP y multi-tenencia: [docs/servicio/contrato-api.md](docs/servicio/contrato-api.md).
- Operacion del servicio generado (configuracion, Docker, pipeline):
  [docs/servicio/README.md](docs/servicio/README.md).

```powershell
dotnet build Reyma.VectorSpace.Engine.Microservice.sln
dotnet test --no-build
```

## Origen

Separado de `Rag.Context.Engine` el 2026-10-19 (`25.7-separar-motor-en-proyecto-independiente`
en el ledger de ese repo). El análisis completo, la evidencia de código de origen y las
preguntas abiertas que motivaron la separación están en
`Rag.Context.Engine/docs/analisis-futuro/motor-embeddings-ingesta-servicio-compartido.md`.
Este repositorio no duplica ese documento; lo referencia.

## Alcance

- Cálculo de embeddings y pipeline de ingesta (chunking, vectorización, upsert al
  vector store que se decida) como servicio de red para cualquier equipo que lo
  necesite.
- Explícitamente fuera: generación RAG, gate de confianza, caché de resúmenes de
  negocio, historial de conversación, consola de administración, vistas y
  analíticas/paneles. Eso se queda en `Rag.Context.Engine` u otros proyectos.
- Vector store: hoy Qdrant es el vigente en `Rag.Context.Engine`; Milvus está en
  pruebas por el administrador del servidor destino. Se decide con evidencia en
  `1.1-comparar-qdrant-vs-milvus`, no por preferencia.

## Servidor destino

Servidor de la empresa, distinto de `reyma-dev` y de `Servidor.IA` (Mac mini,
acotado a Ollama). Acceso SSH anunciado, no otorgado. Sin despliegue ni conexión
hasta tener acceso y aprobación explícita.

## Ledger

`docs/plan/ejecucion-plan.estado.json` es la fuente de verdad del avance de este
proyecto, igual que en `Rag.Context.Engine` y `Mcp.ContextForge`.
