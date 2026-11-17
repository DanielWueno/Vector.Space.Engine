# Fast frente a deep con pensamiento, con el filtro previo de 1.27: resultado

Fecha: 2026-11-17. Protocolo preregistrado en [protocolo.md](protocolo.md), commit 2a6045a, anterior a la muestra.
La muestra y los scripts están en e2dbebf y las generaciones y pares ciegos en 554f2f6, ambos anteriores al juicio.

**Decisión formal: se mantiene `qwen3.8-27b-fast`.** deep con pensamiento cumple las condiciones 1 y 2 con mucho
margen, pero vuelve a fallar la 3 (Σ utilidad_negocio 432 frente a 446). La regla no se cambia después de ver los
datos.

**Decisión del usuario (2026-11-17), por encima de la regla:** deep con pensamiento pasa a ser el perfil por defecto
del motor. En un primer momento se mantuvo fast por coste. Se revisó al comprobar que, en modo simple, el cliente
sustituye el código por el resumen al generar la respuesta (`GenerationContextAssembler`). Ahí la fidelidad
(15 frente a 44 errores) importa más que una diferencia de utilidad del 3 %, que además depende mucho del juez. El
coste extra (~10× de latencia, ~2× de tokens) se paga en la carga inicial; luego solo se resume lo que cambia. La
regla preregistrada no se reescribe: esto es una decisión con motivo explícito, no un resultado de la regla.

## Muestra

Volcado completo de `reyoneweb` (5 324 puntos, solo lectura por el túnel). El `FiltroPrevioDeResumen` real
(`filtro-previo:v1`, identidad `da9a9467`) excluye 1 931 puntos: 1 809 por ruta, 101 por tipo y 21 por
propiedad automática. Pasan 3 393. Se quitan además los 55 de la muestra del 2026-11-11 que también pasan el
filtro, así que la población es de 3 338 puntos. De ella salen N = 100 con la semilla 20261117; el reparto está en
`privado/reparto.json`. Con el filtro desaparecen los estratos PlainText.

## Coste medido (`privado/metricas.json`)

| | fast | deep-thinking |
|---|---:|---:|
| HTTP 200 / vacíos / `length` / reintentos | 100 / 0 / 0 / 0 | 100 / 0 / 0 / 0 |
| completion_tokens: media / mediana / p90 / máx. | 43 / 42 / 61 / 91 | 740 / 633 / 1 249 / 2 084 |
| total_tokens | 64 259 | 137 548 (×2,1) |
| Latencia: mediana / p90 / máx. (4 en paralelo) | 1,8 s / 2,6 s / 3,8 s | 19,2 s / 37,5 s / 63,4 s |
| Suma de latencias | 188 s | 2 247 s (×12) |
| Centinela exacto | 7 | 4 |
| Empieza con `Entidad:` | 76/93 | 96/96 |
| Más de 60 palabras | 1 | 0 |

Extrapolación lineal, no medida, a los 3 393 puntos que pasan el filtro: con fast, ~2,2 M tokens y ~1,8 h en
serie; con deep con pensamiento, ~4,7 M tokens y ~21 h en serie (~5–6 h con 4 en paralelo si el servidor lo
sostiene).

## Juicio descifrado (`privado/resultado-juicio.json`)

| | j5 fast | j5 deep-th | j6 fast | j6 deep-th |
|---|---:|---:|---:|---:|
| fragmento | 204 | 240 | 187 | 231 |
| codigo | 5 | 3 | 5 | 0 |
| inventada | 15 | 2 | 16 | 4 |
| contradice | 3 | 3 | 10 | 6 |
| Omisiones | 28 | 4 | 30 | 2 |
| centinela_ok = false | 25 | 26 | 12 | 13 |
| Σ fidelidad | 271 | 291 | 269 | 291 |
| Σ cobertura | 265 | 297 | 245 | 271 |
| Σ utilidad_negocio | 193 | 189 | 253 | 243 |
| Σ formato | 220 | 248 | 261 | 274 |
| Preferido | 10 | **46** | 10 | **49** |
| % deep-thinking entre pares con ganador | 82,1 % (n=56) | | 83,1 % (n=59) | |

Los totales ciegos que informó cada juez por letra cuadran con los descifrados. Combinado: inventada + contradice,
fast **44** y deep-thinking **15**; omisiones 58 y 6; Σ fidelidad 540 y 582; Σ cobertura 510 y 568; **Σ utilidad
446 y 432**. Test de signos unilateral (deep-thinking mejor): p < 10⁻⁵ en ambos jueces. Acuerdo j5–j6 en
`preferido`: 90/100, con 3 opuestos (R074, R099, R100).

| Condición | Valor | ¿Cumple? |
|---|---|---|
| 1. ≥ 60 % de preferencia en ambos jueces | 82,1 % y 83,1 % | Sí |
| 2. Menos inventada + contradice | 15 frente a 44 | Sí |
| 3. Σ utilidad_negocio no menor | 432 frente a 446 | **No** |

## De dónde sale ahora el déficit de utilidad (`privado/05-desglose.ps1`, descriptivo)

En la ronda anterior el déficit venía de 9 chunks técnicos que solo fast marcaba con centinela. El filtro los quitó:
ahora solo hay 3 casos de "solo fast centinela" (R039, donde acertó deep, y R088 y R089, donde acertó fast). El
déficit de −14 se reparte así:

| | j5 | j6 |
|---|---:|---:|
| Pares sin negocio visible según el juez (n) | 30 | 17 |
| Utilidad en esos pares, fast / deep-th | 42 / 38 | 28 / 24 |
| Pares con negocio visible (n) | 70 | 83 |
| Utilidad en esos pares, fast / deep-th | 151 / 151 | 225 / 219 |
| Preferido en pares con negocio visible, fast / deep-th | 7 / 42 | 8 / 49 |

- 8 puntos (4 por juez) vienen de chunks que ambos jueces consideran técnicos. Casi siempre ambos modelos los
  resumen, y deep, que escribe un resumen más completo, saca menos utilidad.
- Los otros 6 vienen de j6 en chunks con negocio: puntúa algo menos la utilidad de deep-thinking aunque lo prefiere
  49 a 8 por fidelidad y cobertura. j5 los empata.
- La utilidad es la única métrica en la que deep-thinking no gana, y la diferencia es pequeña (−3 %) frente a la de
  fidelidad (+8 %), cobertura (+11 %) y errores (−66 %).

## Hallazgo sobre el filtro de 1.27

El patrón de los envoltorios de FastReport, `(^|/)RepocitorioReportes/Rpt[^/]*…`, solo cubre archivos colgados
directamente de `RepocitorioReportes/`. Los de la subcarpeta `RepocitorioReportes/FastReport/` pasan: son
**124 chunks** de la población. R080 y R089 son de ahí y los dos jueces los consideran técnicos. Ampliar el patrón
sería un cambio de la regla (nueva `Version` del filtro), no de este experimento.

## Límites

- 100 chunks y jueces que son modelos (subagentes nuevos con el prompt literal), no personas.
- `utilidad_negocio` depende mucho del juez: j5 da 193/189 y j6 253/243 sobre los mismos textos.
- La condición 3 compara sumas sin margen. Con esta regla, cualquier déficit, por pequeño que sea, mantiene fast.
- La latencia se midió con 4 llamadas simultáneas, no en serie como en la ronda anterior.
- El cegado depende de que los jueces no abran `privado/`. Sus totales ciegos son coherentes con el descifrado.

## Pares para revisión humana (j5 y j6 discrepan)

R040 `NRutaLocal.cs` (empate/fast), R043 `NPrenomina.cs` (empate/deep), R044 `NPoliza.cs` (empate/deep),
R046 `NRecepcionFacturas.cs` (empate/deep), R065 `CatalogoTipoSiniestro.aspx.cs` (empate/deep),
**R074** `AgregarViajeManual.aspx.Business.cs` (fast/deep), R085 `PreliquidacionTrafico.aspx.cs` (deep/empate),
R086 `CatalogoPagosGPS.aspx.cs` (deep/empate), **R099** `jsInfracciones.js` (fast/deep), **R100** `jsSiniestros.js`
(deep/fast).

## Consumo

AltheIA: 200 llamadas, 201 807 tokens (fast 64 259 y deep-thinking 137 548), sin reintentos. Qdrant: 6 páginas de
`points/scroll` y un `points/count`, sin escrituras. Jueces (subagentes, fuera de AltheIA): ~228 k (j5) y ~277 k
(j6) tokens.
