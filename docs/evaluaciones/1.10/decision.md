# 1.10 — Recalibrar la fusión denso+sparse con el modelo nuevo

Fecha: 2026-10-20. Ficha: `1.10-recalibrar-fusion-denso-sparse-con-el-modelo-nuevo`.
Entorno: host Windows (CPU), sin contenedores nuevos (reusa vectores y chunks ya calculados).
Banco: [`tools/embeddings-eval/`](../../../tools/embeddings-eval/README.md), script nuevo
`pesos_fusion.py`. Evidencia cruda: `resultados/*.json`.

## Decisión

**El endpoint de búsquedas usa denso solo.** Ninguna combinación de pesos de RRF ponderada
gana preguntas en ES sin perder ninguna frente al denso solo (de hecho ninguna gana *ninguna*
pregunta en ES, con cualquier peso probado). El sparse (`SparseTokenizer` + rama `sparse-code`)
**se conserva en el código como rama opcional documentada, no se usa por defecto en la fusión**.

No hubo ambigüedad que requiriera parar: en ES el resultado es monótono y nunca hay ganancia;
no fue necesario decidir si relajar la regla para EN (como sí se hizo en 1.11), porque ES ya
descarta cualquier fusión por sí solo.

## Alcance confirmado al arrancar

Reentrada resuelta por el usuario (`_reentrada_resuelta_2026_10_20` en el ledger): la calibración
se hace sobre la fragmentación **c1024-w8192** de 1.11 (lo que de verdad se va a servir), no
sobre w512 de 1.6. Los chunks (`chunks-<col>-c1024.jsonl`) y vectores arctic@256
(`emb/arctic-c1024-w8192/<col>.docs.npy`) de 1.11 ya estaban en `.cache/embeddings-eval/`; no
hizo falta regenerarlos ni levantar Qdrant/TEI efímeros.

## Protocolo

- Mismos tres corpus y eval-set que 1.6/1.11 (57 preguntas respondibles), arctic-embed-m-v2.0
  @256 (truncado + L2), chunks y sparse de la re-fragmentación `c1024-w8192` de 1.11.
- **Control = denso solo del propio brazo `c1024-w8192`** (ya medido en 1.11:
  `docs/evaluaciones/1.11/resultados/c1024-w8192.json`, campos `denso-es`/`denso-en`): ES 46/57,
  EN 36/57. Es el control correcto para esta ficha — no w512, que es otra fragmentación.
- **RRF nativa** = `QdrantSemanticRetriever` tal cual: `1/(rank+2)` en las dos ramas (denso
  exacto, coseno ≥ 0,1, 40 candidatos; sparse-code, 40), sin pesos — equivale a pesos `(1,1)`.
  Ya estaba medida en 1.11 (`fusion-es`/`fusion-en` del mismo archivo): ES 38, EN 29.
- **RRF ponderada** (`pesos_fusion.py`, nuevo): `score = w_d · 1/(rank_d+2) + w_s · 1/(rank_s+2)`
  sobre los mismos candidatos de las dos ramas, mismo desempate por `ChunkId` ordinal que la
  fusión nativa. Como la fórmula es homogénea de grado 1, el ranking solo depende de la razón
  `w_s/w_d`; barrer `(1, w_s)` con `w_s` de 0 a 0,75 cubre el espacio completo (equivalente a
  escalar `w_d`).
- Barrido: `w_s ∈ {0, 0.02, 0.05, 0.1, 0.25, 0.5, 0.75}` con `w_d = 1` fijo (7 combinaciones,
  más la nativa `(1,1)` ya medida en 1.11 y el denso solo).
- Comparación pareada: por cada combinación, intersección de preguntas respondibles con el
  denso solo del mismo brazo (`n`), conteo de aciertos en hit@10, y gana/pierde pregunta a
  pregunta frente al denso solo.

## Resultados — hit@10 pareado contra denso solo (c1024-w8192)

| Fusión | ES hit@10 | ES +gana/−pierde | EN hit@10 | EN +gana/−pierde |
|---|---|---|---|---|
| **denso solo** (control) | **46** | — | **36** | — |
| RRF nativa (1,1) | 38 | +0/−8 | 29 | +1/−8 |
| ponderada (1, 0.75) | 38 | +0/−8 | 30 | +1/−7 |
| ponderada (1, 0.5) | 40 | +0/−6 | 33 | +1/−4 |
| ponderada (1, 0.25) | 44 | +0/−2 | 34 | +1/−3 |
| ponderada (1, 0.1) | 46 | +0/−0 | 36 | +0/−0 |
| ponderada (1, 0.05) | 46 | +0/−0 | 36 | +0/−0 |
| ponderada (1, 0.02) | 46 | +0/−0 | 36 | +0/−0 |
| ponderada (1, 0) | 46 | +0/−0 | 36 | +0/−0 |

n (preguntas pareadas comunes con el denso) = 57 en ambos idiomas en todas las filas.

Lectura: a medida que baja el peso del sparse, la fusión se acerca monótonamente al denso solo
por abajo (nunca lo supera) hasta volverse indistinguible de él (`w_s ≤ 0,1`: ES y EN idénticos,
0 ganancias y 0 pérdidas — el sparse deja de mover el top-10 en esta muestra). **En ningún punto
del barrido ES gana ni una sola pregunta frente al denso solo**; EN gana como mucho 1 pregunta,
pero siempre pierde más de las que gana salvo en el tramo donde ya no cambia nada. No existe una
zona intermedia donde se gane sin perder en ninguno de los dos idiomas: el criterio de adopción
(`+N/−0` pareado en ES y en EN) no se cumple para ninguna combinación medida.

## Decisión y configuración

- **Denso solo** en el endpoint de búsquedas. No se cumple el criterio de adopción de ninguna
  fusión alternativa (ni nativa ni ponderada) en el rango de pesos barrido.
- El sparse (`SparseTokenizer`, rama `sparse-code` de `QdrantVectorStore`/`QdrantSemanticRetriever`)
  **no se elimina del código**: queda como rama opcional, sin usarse por defecto en la fusión de
  resultados. Si 1.4 porta el retriever, el modo de recuperación por defecto debe ser "denso solo"
  (sin invocar la rama RRF), dejando la fusión (nativa o ponderada) disponible detrás de un flag
  de configuración explícito, no como comportamiento por defecto. Esto es una nota para 1.4
  (bloqueado); esta ficha no toca código de producción.
- No hace falta fijar un "peso recomendado" porque no se adopta ponderación: documentar que, si
  en el futuro se revisita la fusión (más preguntas, otro corpus, otro sparse), el barrido debe
  repetirse — este resultado es específico de las 57 preguntas y los tres corpus del banco.

## Cómo reproducir

Requiere `.cache/embeddings-eval/chunks-<col>-c1024.jsonl` y
`.cache/embeddings-eval/emb/arctic-c1024-w8192/<col>.docs.npy` (de 1.11; regenerar con
`tools/embeddings-eval/README.md`, sección "Ventana y re-fragmentación", si se borraron).

```powershell
cd tools/embeddings-eval
.\py.ps1 pesos_fusion.py evaluar 1:0.75 1:0.5 1:0.25 1:0.1 1:0.05 1:0.02 1:0
.\py.ps1 pesos_fusion.py comparar 1:0.75 1:0.5 1:0.25 1:0.1 1:0.05 1:0.02 1:0
```

El control (denso solo y RRF nativa) se lee de
`docs/evaluaciones/1.11/resultados/c1024-w8192.json` (campos `denso-es`, `denso-en`,
`fusion-es`, `fusion-en`); `pesos_fusion.py comparar` los reusa tal cual, sin re-evaluarlos.

## Artefactos

- `resultados/w1-0.75.json` … `resultados/w1-0.json`: salida cruda de `pesos_fusion.py evaluar`
  por combinación de pesos (aciertos por corte 1/3/5/10, hit por pregunta, top-10 de IDs).
- `resultados/comparacion.json`: tabla pareada final (la fuente de este documento).
- `tools/embeddings-eval/pesos_fusion.py`: script nuevo de esta ficha (RRF ponderada +
  comparación pareada contra el denso solo del propio brazo).

## Límites

- 57 preguntas en tres corpus, las mismas de 1.6/1.11 — el mismo límite de tamaño de muestra que
  ya advertían esas fichas.
- Solo se barrió la razón `w_s/w_d` (el peso del sparse relativo al denso); no se probaron
  variantes de la fórmula de fusión en sí (p. ej. RRF con constante de suavizado distinta a 2, o
  fusión por combinación convexa de scores normalizados en vez de por rango). Con una fusión
  puramente basada en rango (RRF), bajar el peso del sparse por debajo de cierto umbral (aquí
  ~0,1) simplemente deja de poder cambiar el top-10 frente al denso solo: no hay mecanismo para
  que "ganar algo" sin también heredar las pérdidas del sparse en ese rango de pesos probado.
- No se repitió el banco sobre `bsuite-repo`, la banda resumen ni el reranker (fuera de alcance,
  igual que en 1.6/1.11).
- No se levantó ningún Qdrant ni TEI efímero para esta ficha: se reutilizaron vectores y chunks
  ya en `.cache/embeddings-eval/` de 1.11. `rag-qdrant` no se tocó.
- Tiempo de máquina: evaluación de 7 combinaciones de pesos sobre vectores ya calculados, sin
  descargar ni embeber modelos; del orden de minutos (muy por debajo del presupuesto de 0,3 h
  asignado a la ficha).
