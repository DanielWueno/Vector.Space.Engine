# Equivalencia del motor portado (1.4)

Comprueba que el servicio, por su contrato HTTP, indexa lo mismo que el pipeline real de Rag.Context.Engine sobre las
mismas fuentes. No es producto: hospeda la API en proceso con un emisor JWT de prueba (como `tests/`) y el motor real
contra `docker-compose.motor.yml`. Solo usa el Qdrant de ese compose (127.0.0.1:16334); nunca `rag-qdrant`.

## Piezas

| Comando | Que hace |
|---|---|
| `ingestar <raiz> <coleccion>` | Enumera documentos con el `FileSystemIngestionScanner` **real** del origen (mismo perfil y reglas de exclusion), los lee como el pipeline (`File.ReadAllTextAsync`) y los envia a `POST ingestas` en solicitudes de hasta 64 KB. Dos pasadas: la segunda debe dar todo "sin cambios" y el mismo numero de puntos. |
| `comparar <coleccion> <referencia>` | Punto a punto contra la coleccion que dejo `Refragmentar`: mismos Ids, mismo payload (salvo `last_modified` y el `document_fingerprint` propio) y mismo vector `sparse-code`. Sale con 3 si hay diferencias. |
| `vectores <coleccion> <chunks.jsonl> <docs.npy>` | Ids y `content_hash` contra el banco de 1.11 y coseno del vector denso del servicio contra el de arctic del banco (truncado a 256 + L2). |
| `buscar <coleccion> <eval-set> <resultados.json> <clave>` | Las preguntas del eval-set por `POST busquedas` (top 10) con la regla de acierto de `harness.evaluar_item`, frente a `denso-es` de 1.11. |

Salida en `.cache/equivalencia-1.4/` (ignorado por git).

## Orden

```powershell
docker compose -f docker-compose.motor.yml up -d
dotnet build tools/embeddings-eval/Refragmentar -c Release; dotnet build tools/equivalencia-motor -c Release
# Referencia: pipeline real del origen a 1024 (variables como en tools/embeddings-eval/ingestar-control.ps1)
dotnet tools/embeddings-eval/Refragmentar/bin/Release/net10.0/Refragmentar.dll D:\Git\Reyma.TI.Tickets.Microservice ref-micro-repo 1024 true my-repo
dotnet tools/embeddings-eval/Refragmentar/bin/Release/net10.0/Refragmentar.dll D:\Git\docs-bsute-innovapp-plan ref-innovapp-docs 1024 true innovapp-docs-14
$d = "tools/equivalencia-motor/bin/Release/net10.0/EquivalenciaMotor.dll"
dotnet $d ingestar D:\Git\docs-bsute-innovapp-plan innovapp-docs-14
dotnet $d ingestar D:\Git\Reyma.TI.Tickets.Microservice my-repo
dotnet $d comparar innovapp-docs-14 ref-innovapp-docs
dotnet $d comparar my-repo ref-micro-repo
dotnet $d vectores my-repo .cache/embeddings-eval/chunks-micro-repo-c1024.jsonl .cache/embeddings-eval/emb/arctic-c1024-w8192/micro-repo.docs.npy
dotnet $d buscar my-repo D:\Git\Rag.Context.Engine\docs\eval\tickets-microservice.eval-set.json docs/evaluaciones/1.11/resultados/c1024-w8192.json micro-repo
```

La coleccion del servicio hace de repositorio en la clave de identidad: para comparar con la referencia, `Refragmentar`
recibe como `repo-name` el nombre de la coleccion del servicio. `my-repo` es ademas el nombre con que 1.11 ingesto
micro-repo, asi que esa coleccion es comparable tambien con su banco (vectores y busquedas).

Rollback: `docker compose -f docker-compose.motor.yml down -v` y borrar `.cache/equivalencia-1.4`. `Refragmentar` deja
corridas en `%LOCALAPPDATA%\rag-engine\ingestion-state.sqlite3` de Rag.Context.Engine (journal, sin efecto en ingestas),
igual que en 1.6/1.11.
