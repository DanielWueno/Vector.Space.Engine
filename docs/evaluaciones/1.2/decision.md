# 1.2 — ¿Qdrant o Milvus como vector store del servicio compartido?

Fecha: 2026-10-19. Ficha: `1.2-comparar-qdrant-vs-milvus`.
Entorno: Docker Desktop local (WSL2, 28 CPU, ~15,5 GiB en la VM), solo CPU.
Banco reproducible: `tools/vectorstore-eval/` (compose + harness .NET con los clientes oficiales).
Evidencia cruda en esta carpeta:

| Archivo | Servidores | Semilla |
|---|---|---|
| `harness-resultado.json` | Qdrant 1.19.2 / Milvus 2.6.25 | 12345 (principal) |
| `harness-resultado-semilla2026.json` | Qdrant 1.19.2 / Milvus 2.6.25 | 2026 |
| `harness-resultado-milvus-3.0.3.json` | Qdrant 1.19.2 / Milvus 3.0.3 | 12345 |

Oráculo fijado **antes** de medir, con el usuario: los cuatro puntos de la ficha, tal cual.

## Decisión

**Qdrant se mantiene como vector store del servicio.** 1.4 porta `QdrantVectorStore` y
`QdrantSemanticRetriever` de Rag.Context.Engine sin adaptador nuevo.

Milvus **no se descarta por capacidad**: con datos sintéticos alcanzó la misma paridad de búsqueda
(0 fallos frente al oráculo). Pierde por el coste de conseguirla y de operarlo. Hacen falta cinco
ajustes de adaptador, uno de ellos con riesgo de corrección. Opera tres servicios en lugar de uno. El
backup va por una herramienta externa que tiene una trampa medida. Y su cliente .NET publicó su
primera versión estable hace 4 días. La experiencia del administrador con Milvus es real, pero
no compensa nada de lo anterior (ver criterio 3).

Reversible: si TI exige Milvus en el servidor nuevo, `tools/vectorstore-eval/Harness/MilvusStore.cs`
y la lista de brechas de abajo son el punto de partida y el plan de trabajo.

## Criterio 1 — Paridad funcional sobre los puertos (medido)

Dataset sintético con la forma exacta del payload de Rag.Context.Engine. Son 3000 puntos de 384d con
sparse léxico (índices < 2^20, como `SparseTokenizer`) y `dense-resumen` en ~60%. Incluye tenant
ausente/`tenant-a`/`tenant-b`, `defined_symbols` con índice keyword, punto de manifiesto, un grupo de
40–41 vectores idénticos y 20 tríos idénticos. El oráculo es fuerza bruta en memoria. Usa el mismo
desempate (`score` desc, id ordinal) y el mismo prefijo completo de empates que
`DeterministicVectorQuery`. Se cargó en dos tandas, con flush entre ellas, para cubrir segmentos
sellados y "growing".

| Prueba (por motor) | Qdrant 1.19.2 | Milvus 2.6.25 (y 3.0.3) |
|---|---|---|
| Esquema híbrido: denso + sparse + resumen opcional + índice keyword | OK nativo | OK con nombres renombrados (`sparse_code`, `dense_resumen`): Milvus rechaza `sparse-code` (*"Field name can only contain numbers, letters, and underscores"*) |
| Búsqueda exacta, 3 ramas × 7 filtros × 30 consultas, top-40 (630 búsquedas), antes y después del flush | 630/630 idénticas al oráculo | 630/630 idénticas |
| Filtros: tenant, módulo (frontera), lenguaje, `defined_symbols` any, salto cualificado (clase+método), ids | OK | OK |
| Empates: prefijo completo (3 consultas, límite 44) con grupo en segmentos mixtos | top-10 idéntico; scores bit a bit iguales | ídem |
| Umbral de score (`scoreThreshold` / `radius`) | correcto; **estricto (>)** en el borde | correcto; **estricto (>)**: misma semántica |
| Manifiesto (denso de ceros, sin sparse) ida y vuelta | OK | OK: acepta denso de ceros con COSINE y sparse vacío |
| Upsert = reemplazo completo, `UpdateVectors`, `SetPayload`, conteo exacto, scroll, retrieve, delete, lectura tras escritura | OK nativo | OK, pero **sin escritura parcial** (ver brechas) |
| Límite de candidatos | acepta 16384, 16385 y 32767 | **rechaza > 16384** (`topk ... should be in range [1, 16384]`) |

### Brechas de un adaptador Milvus (lo que costaría la paridad)

1. **Escritura parcial no atómica.** Milvus.Client 2.6.0 no expone *partial update*: `UpsertAsync`
   no tiene la bandera, aunque el proto la trae como `FieldPartialUpdateOp`. Por eso
   `UpdateSummaryVectorAsync` y `MarkResumenCompleteAsync` pasan a leer la fila completa, modificarla
   y hacer upsert completo. Funciona en el harness. Pero son dos viajes sin atomicidad, y la fase 2
   de resúmenes concurrente con una reingesta puede pisar escrituras. En Qdrant son operaciones
   atómicas del servidor. **Es la brecha más seria.**
2. **`dense-resumen` opcional por punto.** El servidor acepta un campo vectorial `nullable`, y la
   sonda insertó una fila sin él. Pero el cliente no puede enviar en un mismo lote filas con y sin
   vector, porque `FloatVectorFieldData` no tiene máscara de validez. Solución usada: vector de
   relleno + `has_resumen`, filtrado en la rama de resumen.
3. **Payload ausente → cadena vacía** (`tenant`, `namespace`...). Además el esquema es explícito, así
   que cada clave nueva de payload (como pasó con `defined_symbols` en 5.d o `tenant` en 5.e) es una
   alteración de esquema, no un campo más.
4. **`topK` máximo 16384** frente al `MaxCandidates = 32768` del prefijo completo. Se resuelve
   subiendo el límite en la configuración del servidor o bajando `MaxCandidates` en el adaptador.
   Solo importa con más de 16384 empates exactos en el corte.
5. **Filtro de módulo:** hay que expresar la frontera exacta
   (`== m or like "m.%"` / `like "m/%"`). Un `like "%m%"` deja pasar `Reyma.VentasExtra`, y el
   post-filtro `MatchesModuleBoundary` recortaría el top-K. Este bug apareció en la primera
   corrida del harness.

### Hallazgo que afecta también a quedarse en Qdrant

`Match.Text` sobre un campo **sin índice full-text** cambió de semántica entre versiones. Lo medí con
una sonda en contenedores efímeros:

| Qdrant | `"Reyma.Ventas"` contra `Reyma.Ventas.Api` / `Reyma.VentasExtra.Api` / `xReyma.Ventas` | `"reyma.ventas"` |
|---|---|---|
| 1.14.1 (el que corre hoy `rag-qdrant`) | subcadena, distingue mayúsculas: casa los tres | nada |
| 1.19.2 | tokens, sin distinguir mayúsculas: casa solo `Reyma.Ventas.Api` | casa |

En los dos casos el resultado es un superconjunto de la frontera exacta, y el post-filtro
`MatchesModuleBoundary` de Rag.Context.Engine deja el resultado final correcto. Pero el prefiltro
cambia al actualizar Qdrant, así que 1.4 debe verificarlo contra la versión que se despliegue.

## Criterio 2 — Coste y plan de migración (medido)

- **Qué hay hoy** (lectura de solo metadatos de `rag-qdrant`, sin tocar datos): dos colecciones reales,
  `rag-engine` (2004 puntos) y `micro-repo` (956). Las ~170 restantes son residuos de tests
  `rag-engine-test-*`, casi todas vacías. **No encontré colecciones con nombre de las olas 21/23**
  que cita la ficha.
- **Migración medida de Qdrant a Milvus** con el harness: se pagina con vectores + payload y se hace
  upsert en Milvus con el esquema del adaptador. Resultado: 3000 puntos en 8,2–8,4 s
  (~360 puntos/s) en tres corridas, manifiesto idéntico, y 180/180 búsquedas (3 ramas × 2 filtros
  × 30 consultas) idénticas entre origen y destino. Extrapolado a ~2960 puntos reales: **menos de
  10 s de transferencia, sin reembeber.**
- **El coste real no son los datos, es el código.** Habría que reescribir unas 1650 líneas
  específicas de Qdrant (`QdrantVectorStore` + `QdrantSemanticRetriever`) con las brechas de arriba
  y volver a validar contra la baseline de evaluación de Rag.Context.Engine. Quedándose en Qdrant,
  la migración es copiar colecciones (snapshot, criterio 3) o nada.
- **Matiz:** 1.6 puede cambiar el modelo (MiniLM se queda corto), y entonces habría que reingestar
  igual. Eso abarata la migración de datos en cualquiera de las dos opciones, pero no la de código.

## Criterio 3 — Madurez operativa (medido + fuentes)

| | Qdrant 1.19.2 | Milvus 2.6.25 standalone |
|---|---|---|
| Servicios | 1 contenedor | 3 (milvus + etcd + MinIO) |
| Imágenes | 291 MB | 1,36 GB + 94 MB + 244 MB ≈ 1,7 GB |
| RAM con el dataset cargado (`docker stats`) | 167 MiB | 324 + 369 + 37 ≈ 730 MiB |
| Disco real con 2 colecciones × 3000 puntos | 47 MB (`du`; `docker system df` dice 2,4 GB aparente por archivos dispersos) | MinIO 138 MB + etcd 145 MB (`docker system df`; imagen distroless, sin `du`) + 5,6 MB |
| Arranque hasta *healthy* | inmediato | < 30 s (2.6.25), 19 s (3.0.3) |
| Respaldo/restauración | **integrado**: snapshot 0,4 s (22 MB), recuperación en otra colección 1,3 s; esquema, índice, 3001 puntos y top-5 idénticos | **herramienta aparte** `milvus-backup` v0.6.0: crear 1,45 s; restaurar 19,4 s. **Restaurar sin `--rebuild_index` deja la colección sin índices y no carga** (código 700). Con la bandera: conteo, 5 índices, manifiesto y top-5 idénticos. Configuración v2 con claves `milvus.grpc.address`; las claves v1 de la documentación se ignoran con un aviso |
| Fricción observada | ninguna | `Flush` limitado por defecto (`rate=0.1/s`): abortó la primera corrida y hubo que reintentar |
| Comunidad (GitHub, 2026-10-19) | 35,1k estrellas, 786 issues abiertos, Apache-2.0, activo | 46,4k estrellas, 1410 issues abiertos, Apache-2.0, activo |
| Quién lo opera | El equipo ya opera Qdrant (`rag-qdrant`), incluidos los snapshots | El administrador del servidor nuevo tiene experiencia con Milvus |

La experiencia del administrador es el único punto a favor de Milvus en este criterio. No alcanza
para pagar 3 servicios, ~4× RAM, ~6× imagen y un backup externo con una trampa conocida, cuando el
volumen previsto es de miles de puntos y no de millones.

## Criterio 4 — Licencia, versión y cliente .NET (medido + NuGet)

| | Qdrant.Client | Milvus.Client |
|---|---|---|
| Versión probada | 1.19.0 (2026-08-04) | 2.6.0 (**2026-10-15**, primera estable) |
| Historial | 21 versiones, 18 estables, 6 en los últimos 12 meses | 7 versiones; antes de 2.6.0, solo `preview`. La anterior (2.3.0-preview.1) es de marzo de 2024: **2,5 años sin publicar** |
| Descargas NuGet | 2,85 M | 0,67 M |
| Licencia | Apache-2.0 (paquete y repo) | Apache-2.0 en el repo; el paquete NuGet no declara licencia |
| Servidor | 1.19.2 | probado contra 2.6.25 **y** 3.0.3: mismo 25/3 en ambos |
| Asperezas medidas | `Vector.Data`, `VectorOutput.Data` y `VectorParams.OnDisk`, que Rag.Context.Engine usa hoy, son `obsolete` en 1.19: retoque menor al actualizar | `FieldData.GetRowValue` es interno (hay que leer por tipo); sin celdas vectoriales nulas; sin *partial update*; **el tipo de `count(*)` cambia según el servidor** (`FieldData<long>` en 2.6.25, `FieldData<long?>` en 3.0.3) |

Servidores: los dos Apache-2.0. Qdrant 1.19.2 es la versión estable vigente. Milvus tiene dos
líneas activas, 2.6.x (2.6.25) y 3.0.x (3.0.3, 2026-10-13).

## Límites de esta evaluación

- Datos sintéticos, 3000 puntos, solo CPU. No se midió rendimiento a escala ni con HNSW o índices
  aproximados: el adaptador de Rag.Context.Engine usa búsqueda **exacta**, y eso es lo que se probó.
- Las cifras de RAM y disco son de una VM Docker Desktop local, no del servidor destino.
- No se probó el Milvus del administrador (versión y modo desconocidos). Ya no era requisito.
- La concurrencia de la brecha 1 se razonó a partir del diseño (dos viajes no atómicos). No se
  reprodujo una carrera.
- No se probó la API de *partial update* del servidor por gRPC crudo, sin el cliente.

## Reproducir

```powershell
docker compose -f tools/vectorstore-eval/docker-compose.yml up -d
dotnet run -c Release --project tools/vectorstore-eval/Harness -- docs/evaluaciones/1.2/harness-resultado.json [semilla]
# Milvus 3.0.3: $env:MILVUS_TAG='v3.0.3' antes del up, con volúmenes nuevos.
docker compose -f tools/vectorstore-eval/docker-compose.yml down -v   # rollback
```
