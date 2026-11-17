# Resumen de negocio por chunk: qwen3.8-27b-fast vs qwen3.8-27b-deep

Fecha: 2026-11-11. Corpus: colección Qdrant `reyoneweb` (5324 puntos, solo lectura por el túnel en
`localhost:6333`). Código fuente de verificación: `C:\Users\dsoft03.corp\Downloads\Release\Release\`.

**Decisión: se queda `qwen3.8-27b-fast`.** Deep no cumple la regla 1 (preferencia ≥ 60 % en ambos
jueces) ni la 2 (menos afirmaciones inventadas + contradictorias). Los dos modelos son prácticamente
equivalentes en esta tarea; ninguna diferencia es estadísticamente significativa.

## 1. Método

### Muestra (`privado\01-muestra.ps1`, `privado\muestra.json`, `privado\reparto.json`)
- Recorrido completo con `points/scroll` (payload `relative_path`, `language`, `chunk_type`; sin vectores).
- Estrato = segundo segmento de `relative_path` × `language` (el primer segmento es siempre `ReyOneWeb`).
  18 estratos; 14 con ≥ 3 puntos.
- N = 100. Reparto: los estratos con ≥ 3 puntos son elegibles; se fijan en 3 los que tendrían cuota
  proporcional < 3 (de forma iterativa) y el resto se reparte proporcionalmente por resto mayor
  (Hamilton). Los estratos con < 3 puntos (Datos.Produccion/PlainText 2, Datos.Trafico/Sql 2,
  Entidades.Produccion/PlainText 2, Trafico.Negocio/PlainText 1) reciben 0.
- Selección: ids ordenados ordinalmente dentro del estrato; `System.Random(20261111)` nuevo por
  estrato; Fisher-Yates parcial y se toman los k primeros.

| Estrato | Puntos | Muestra |
|---|---:|---:|
| ReyOneWeb / CSharp | 3325 | 49 |
| Reyma.Datos.Trafico / CSharp | 805 | 12 |
| Reyma.Negocio.Trafico / CSharp | 400 | 6 |
| Reyma.Datos.Produccion / CSharp | 171 | 3 |
| Reyma.Entidades.Trafico / CSharp | 143 | 3 |
| Reyma.Negocio.Produccion / CSharp | 94 | 3 |
| Reyma.Trafico.Negocio / CSharp | 83 | 3 |
| Reyma.Entidades.Produccion / CSharp | 15 | 3 |
| ReyOneWeb / JavaScript | 227 | 3 |
| ReyOneWeb / PlainText | 39 | 3 |
| Reyma.Datos.Trafico / PlainText | 4 | 3 |
| Reyma.Entidades.Trafico / PlainText | 4 | 3 |
| Reyma.Negocio.Trafico / PlainText | 4 | 3 |
| Reyma.Negocio.Produccion / PlainText | 3 | 3 |

Tipos de chunk en la muestra: Method 68, PlainTextWindow 19, Class 6, Property 5, Constructor 2.
Longitud del contenido: 61 a 4786 caracteres (media 1109).

### Generación (`privado\02-generar.ps1`, `privado\resultados.jsonl`)
- Petición exacta de `LlmResumenBrain` (Vector.Space.Engine). El script verifica en tiempo de
  ejecución que el prompt del sistema coincide carácter a carácter con el literal del motor
  (archivo con LF y la sangría común del raw string eliminada).
- Usuario: `[Contexto: Lenguaje: {language}, Ruta: {relative_path}]\n\n{content}`.
- `temperature 0.15`, `top_p 0.95`, `max_tokens 160`. fast sin `chat_template_kwargs`; deep con
  `{"enable_thinking": false}`.
- Orden alterno por chunk (pares: fast→deep, impares: deep→fast). `curl.exe --max-time 180 --cacert`;
  la clave va solo en un archivo de cabecera temporal que se borra en `finally`.
- Un reintento si no hay 200 (no hizo falta ninguno).

### Cegado (`privado\03-metricas-y-cegado.ps1`)
- Pares P001..P100 en el orden de `muestra.json`; `System.Random(7)`, `Next(2)==0` ⇒ A=fast
  (resultado: A=fast en 53 pares).
- `jueces\pares-ciegos.json` contiene solo par, ruta, lenguaje, fragmento, A, B. Se comprobó que no
  contiene ids de puntos ni nombres de modelo (los GUID que aparecen son de archivos .csproj/.sln dentro
  de los fragmentos).

### Juicio y descifrado (`privado\04-descifrar.ps1`)
- Dos subagentes nuevos e independientes (j1, j2), lanzados en paralelo con el prompt de juez literal.
- Los totales ciegos que informó cada juez (por letra A/B) coinciden con los totales descifrados.
- La regla de decisión y las estadísticas ya estaban escritas en el script antes de abrir los juicios.

## 2. Métricas automáticas

| Métrica | fast | deep |
|---|---:|---:|
| HTTP 200 | 100/100 | 100/100 |
| Reintentos | 0 | 0 |
| Respuestas vacías | 0 | 0 |
| finish_reason = length | 0 | 0 |
| `SIN_CONTENIDO_DE_NEGOCIO` exacto | 46 | 46 |
| Resúmenes con contenido | 54 | 54 |
| Palabras: media / máx. | 30,6 / 55 | 32,3 / 57 |
| Más de 60 palabras | 0 | 0 |
| Más de 3 oraciones (heurística por `.!?`) | 0 | 0 |
| Empieza con `Entidad:` (`^[^:\n]{1,80}:`) | 41/54 | 41/54 |
| Latencia mediana / p90 (ms) | 979 / 1991 | 970 / 2134 |
| completion_tokens medio | 28,1 | 29,5 |

Comunes: 48 textos idénticos fast = deep (45 son centinela en ambos y 3 resúmenes idénticos). Solo
fast devolvió centinela en 1 chunk (P082) y solo deep en otro (P061).

## 3. Resultados por juez (descifrados)

| | j1 fast | j1 deep | j2 fast | j2 deep |
|---|---:|---:|---:|---:|
| Afirmaciones "fragmento" | 143 | 143 | 139 | 146 |
| Afirmaciones "codigo" | 2 | 7 | 1 | 4 |
| Afirmaciones "inventada" | 12 | 12 | 8 | 6 |
| Afirmaciones "contradice" | 3 | 5 | 6 | 9 |
| Omisiones | 13 | 12 | 22 | 14 |
| centinela_ok = false | 21 | 21 | 9 | 9 |
| Σ fidelidad | 282 | 275 | 279 | 274 |
| Σ cobertura | 248 | 248 | 261 | 266 |
| Σ utilidad_negocio | 246 | 248 | 247 | 246 |
| Σ formato | 245 | 244 | 269 | 268 |
| Preferido | 15 | 13 | 13 | 15 |

| | j1 | j2 |
|---|---:|---:|
| Empates | 72 | 72 |
| Pares con ganador | 28 | 28 |
| % deep entre pares con ganador | 46,4 % | 53,6 % |
| Test de signos, p unilateral (H1: deep mejor) | 0,714 | 0,425 |
| Test de signos, p unilateral (H1: fast mejor) | 0,425 | 0,714 |

### Combinado (j1 + j2)

| | fast | deep |
|---|---:|---:|
| inventada + contradice | **29** (20 + 9) | **32** (18 + 14) |
| "codigo" (ciertas fuera del fragmento) | 3 | 11 |
| fragmento | 282 | 289 |
| Omisiones | 35 | 26 |
| centinela_ok = false | 30 | 30 |
| Σ fidelidad | 561 | 549 |
| Σ cobertura | 509 | 514 |
| Σ utilidad_negocio | 493 | 494 |
| Σ formato | 514 | 512 |

Acuerdo j1–j2 en `preferido`: 85/100 (85 %). Solo 3 discrepancias son opuestas (uno elige fast y el
otro deep); las otras 12 son ganador frente a empate. Acuerdo en `negocio_visible`: 88/100.

## 4. Decisión según la regla (sección 8, fijada antes de descifrar)

| Condición para adoptar deep | Valor | ¿Cumple? |
|---|---|---|
| 1. deep preferido en ≥ 60 % de los pares con ganador, en ambos jueces | j1 46,4 %, j2 53,6 % | No |
| 2. deep con menos inventada + contradice que fast (ambos jueces) | deep 32 vs fast 29 | No |
| 3. Σ utilidad_negocio deep ≥ fast | 494 vs 493 | Sí |

**Resultado: `qwen3.8-27b-fast`.** Fallan dos de las tres condiciones. Además, en ningún juez la
preferencia se distingue del azar (p ≥ 0,42 en ambas direcciones). Deep tampoco aporta formato ni
latencia: la mediana es igual y su p90 es algo peor.

Matices (no cambian la decisión):
- Deep produce más afirmaciones "codigo" (11 frente a 3): datos ciertos que no están en el fragmento.
  Incumple la instrucción "si no está en el código, no existe" y sugiere que deep tira más de
  conocimiento externo al chunk.
- Deep tiene menos omisiones (26 frente a 35) y algo más de cobertura, pero menos fidelidad (549 frente
  a 561) y más "contradice" (14 frente a 9).

## 5. Hallazgo colateral: el centinela se usa de menos (igual en ambos modelos)

Ningún juez encontró un centinela aplicado a un fragmento con negocio visible (0 en ambos modelos).
En cambio, ambos modelos resumieron fragmentos que los jueces consideran puramente técnicos: 21 por
modelo según j1 y 9 por modelo según j2. Es una propiedad del prompt y del corpus (p. ej. código
generado de diseñador o envoltorios de acceso a datos), no de la elección de modelo. Si importa, se
revisa el prompt, lo que cambia `PromptVersion` e invalida la caché de resúmenes. Los jueces
discrepan bastante en este criterio (j1 es más estricto).

Los dos chunks con centinela de un solo modelo fueron favorables al que puso el centinela, en ambos
jueces: P061 (deep, `RptRolesMonitoreoConductores.cs`) y P082 (fast, `RptValeGastos.cs`).

## 6. Límites
- Tamaño: 100 chunks, de los que solo 52 tenían textos distintos y 28 tuvieron ganador por juez.
  Potencia baja para detectar diferencias pequeñas: el resultado es «sin diferencia demostrada», no
  «equivalencia probada».
- Los estratos pequeños de PlainText están sobrerrepresentados por el mínimo de 3 (15 de 100 chunks
  vienen de estratos con ≤ 39 puntos). Las cifras no son estimaciones ponderadas de toda la colección.
- Los jueces son modelos (subagentes del mismo modelo base), no personas. Su acuerdo es alto en
  `preferido` (85 %), pero difieren en la severidad del centinela (21 frente a 9).
- El cegado lo da la separación de carpetas y el prompt. Los subagentes tienen acceso al sistema de
  archivos, así que no leer `privado\` depende de que cumplan la instrucción, no de una barrera técnica.
  No hay indicios de lo contrario: los totales ciegos de cada juez son coherentes con el descifrado.
- Un solo corpus (ReyOneWeb/Tráfico/Producción, mayoritariamente C# WebForms), un solo prompt y una
  sola temperatura (0,15). No se midió la varianza entre corridas.
- Las métricas de oraciones y de «Entidad:» son heurísticas por regex.

## 7. Pares para revisión humana (j1 y j2 discrepan en `preferido`)

| Par | Ruta | j1 | j2 |
|---|---|---|---|
| P002 | ReyOneWeb/Reyma.Datos.Produccion/DSilos.cs | fast | empate |
| P005 | ReyOneWeb/Reyma.Datos.Trafico/DViajesPedidos.cs | deep | empate |
| P006 | ReyOneWeb/Reyma.Datos.Trafico/DEvento.cs | empate | deep |
| P009 | ReyOneWeb/Reyma.Datos.Trafico/DPrestamo.cs | fast | empate |
| P011 | ReyOneWeb/Reyma.Datos.Trafico/DSiniestros.cs | fast | empate |
| P013 | ReyOneWeb/Reyma.Datos.Trafico/DViajesPedidos.cs | **fast** | **deep** |
| P028 | ReyOneWeb/Reyma.Negocio.Produccion/NRegistroSellosPlanta.cs | **deep** | **fast** |
| P035 | ReyOneWeb/Reyma.Negocio.Trafico/NMovimientosPrenomina.cs | fast | empate |
| P036 | ReyOneWeb/Reyma.Negocio.Trafico/NRevisionDispositivo.cs | **deep** | **fast** |
| P048 | ReyOneWeb/ReyOneWeb/Trafico/Procesos/TarjetaSellos.aspx.cs | empate | deep |
| P052 | ReyOneWeb/ReyOneWeb/Trafico/RepocitorioReportes/RptNewGastos.aspx.cs | empate | deep |
| P064 | ReyOneWeb/ReyOneWeb/Trafico/Procesos/PreliquidacionTrafico.aspx.cs | deep | empate |
| P089 | ReyOneWeb/ReyOneWeb/Trafico/Procesos/PreliquidacionTrafico.aspx.cs | empate | deep |
| P095 | ReyOneWeb/ReyOneWeb/Trafico/JS/jsPlanesRutasUrbanos.js | empate | deep |
| P097 | ReyOneWeb/ReyOneWeb/Trafico/JS/MonitorLicencias.js | empate | fast |

En negrita, las tres discrepancias opuestas. El detalle por afirmación está en
`jueces\juicio-j1.json` y `jueces\juicio-j2.json`; el mapa A/B, en `privado\mapa.json`.

## 8. Consumo observado

| | fast | deep |
|---|---:|---:|
| Llamadas a chat/completions | 100 | 100 |
| prompt_tokens | 59 770 | 59 770 |
| completion_tokens | 2 811 | 2 946 |
| total_tokens | 62 581 | 62 716 |

Más 1 llamada a `/v1/models` (comprobación previa). Total: 201 llamadas a AltheIA, dentro del
presupuesto (~200; límite 300). Qdrant: un `points/count`, 6 páginas de `points/scroll`, un
`points` (get por 100 ids) y un `points` (get de 1 id para conocer las claves del payload); ninguna
escritura. Los jueces (subagentes, fuera de AltheIA) consumieron unos 218 k (j1) y 281 k (j2) tokens.

## 9. Anexo: sonda de deep con pensamiento (posterior a la decisión, no la modifica)

`privado\05-sonda-thinking.ps1` → `privado\sonda-thinking.jsonl`. Son 5 llamadas a deep con la misma
petición, cambiando solo `chat_template_kwargs` y `max_tokens`. Los chunks son el más corto (P088),
el mediano (P009) y el más largo (P082) de la muestra.

| Caso | Chunk | kwargs | max_tokens | finish | completion_tokens | ms | Salida |
|---|---|---|---:|---|---:|---:|---|
| A | P009 | (no se envía) | 160 | length | 160 | 5 287 | **vacía**; razonamiento de 710 caracteres |
| B | P009 | (no se envía) | 4096 | stop | 1 303 | 41 158 | resumen |
| C | P088 | enable_thinking: true | 4096 | stop | 1 291 | 40 766 | resumen (los jueces: correspondía centinela) |
| D | P009 | enable_thinking: true | 4096 | stop | 1 078 | 34 143 | resumen |
| E | P082 | enable_thinking: true | 4096 | stop | 153 | 5 720 | centinela (los jueces: correcto) |

Sin pensamiento, en la corrida principal: P009 fast 47 tokens / 1 675 ms y deep 53 / 1 861; P088 ambos
centinela con 10 tokens (~480 ms); P082 fast centinela con 10 tokens, deep resumen con 45 tokens.

Medido:
- El servidor tiene el pensamiento activado por defecto en deep. Sin la bandera y con 160 tokens,
  devuelve vacío por `length` (caso A).
- El cliente lo controla por petición. El servidor aceptó `max_tokens` 4096 sin error.
- El razonamiento llega en `message.reasoning`, separado de `content` (sin `<think>`). `usage` no
  informa `reasoning_tokens`, pero `completion_tokens` lo incluye.

Solo son 3 chunks: no permiten estimar el coste medio ni la calidad. El centinela salió bien en uno
(P082) y mal en otro (P088).

## 10. Segunda comparación: fast vs deep con pensamiento (100 chunks, juicio ciego)

Regla fijada antes de generar: `privado\regla-thinking.md` (las mismas 3 condiciones; el coste se
informa aparte). fast reutiliza los textos de la corrida principal. deep-thinking es
`qwen3.8-27b-deep` con `enable_thinking: true` y `max_tokens: 4096`, mismo prompt y muestreo; se toma
solo `message.content`. Scripts: `06-generar-thinking.ps1`, `07-metricas-y-cegado-thinking.ps1` y
`04-descifrar.ps1` parametrizado (se comprobó que reproduce byte a byte `resultado-juicio.json`).
Cegado: Q001..Q100, `Random(11)`, A=fast en 47. Jueces nuevos j3 y j4, que solo ven `jueces2\`. Los
totales ciegos de cada juez coinciden con los descifrados.

### Coste medido

| | fast | deep-thinking |
|---|---:|---:|
| HTTP 200 / vacíos / `length` / reintentos | 100 / 0 / 0 / 0 | 100 / 0 / 0 / 0 |
| completion_tokens: media / mediana / p90 / máx. | 28 / 25 / 53 / 67 | 520 / 442 / 967 / 2 189 |
| completion_tokens totales | 2 811 | 51 957 (×18,5) |
| prompt_tokens totales | 59 770 | 63 370 |
| total_tokens | 62 581 | 115 327 (×1,8) |
| Latencia: mediana / p90 / máx. | 0,98 s / 2,0 s / 2,4 s | 14,1 s / 30,7 s / 69,1 s |
| Tiempo en serie, 100 chunks | 111 s | 1 657 s (×15) |
| deep-thinking con centinela: mediana | — | 170 tokens / 5,6 s |
| deep-thinking con resumen: mediana | — | 665 tokens / 21,1 s |
| Razonamiento (`message.reasoning`): media | — | 1 929 caracteres |

Extrapolación lineal, no medida: al ritmo serie de la muestra, 5 324 chunks serían ~1,6 h con fast y
~24,5 h con deep-thinking. No considera paralelismo, límites de cuota ni la caché de resúmenes.

### Métricas automáticas

| | fast | deep-thinking |
|---|---:|---:|
| Centinela exacto | 46 | 40 |
| Empieza con `Entidad:` | 41/54 | 60/60 |
| Palabras: media / máx. | 30,6 / 55 | 29,6 / 53 |
| > 60 palabras / > 3 oraciones | 0 / 0 | 0 / 0 |

Centinela en ambos: 36. Solo fast: 10. Solo deep-thinking: 4. Textos idénticos: 36.

### Juicio (descifrado)

| | j3 fast | j3 deep-th | j4 fast | j4 deep-th |
|---|---:|---:|---:|---:|
| fragmento | 136 | 170 | 211 | 284 |
| codigo | 2 | 1 | 2 | 0 |
| inventada | 8 | 3 | 5 | 0 |
| contradice | 8 | 2 | 9 | 1 |
| Omisiones | 31 | 6 | 24 | 5 |
| centinela_ok = false | 8 | 12 | 7 | 11 |
| Σ fidelidad | 274 | 282 | 275 | 288 |
| Σ cobertura | 257 | 274 | 265 | 276 |
| Σ utilidad_negocio | 245 | 237 | 260 | 250 |
| Σ formato | 271 | 276 | 273 | 278 |
| Preferido | 11 | **40** | 10 | **39** |
| Empates | 49 | | 51 | |
| % deep-thinking entre pares con ganador | 78,4 % (n=51) | | 79,6 % (n=49) | |
| Test de signos, p unilateral (deep-thinking mejor) | 3·10⁻⁵ | | 2·10⁻⁵ | |

Combinado: inventada + contradice, fast **30** y deep-thinking **6**; "codigo" 4 y 1; omisiones 55 y
11; Σ fidelidad 549 y 570; Σ cobertura 522 y 550; **Σ utilidad 505 y 487**; centinela_ok=false 15 y 23.
Acuerdo j3–j4 en `preferido`: 98/100, sin discrepancias opuestas. Los dos desacuerdos (Q008
`DTarjetaSellos.cs`: fast frente a empate; Q097 `MonitorLicencias.js`: deep-thinking frente a empate)
quedan para revisión humana.

### Decisión según la regla fijada

| Condición | Valor | ¿Cumple? |
|---|---|---|
| 1. ≥ 60 % de preferencia en ambos jueces | 78,4 % y 79,6 % | Sí |
| 2. Menos inventada + contradice | 6 frente a 30 | Sí |
| 3. Σ utilidad_negocio no menor | 487 frente a 505 | **No** |

**Resultado formal: se mantiene fast.** La regla no se cambia después de ver los datos.

### De dónde sale el déficit de utilidad (desglose descriptivo)

| Categoría (ambos jueces igual) | n | Utilidad fast / deep-th (j3; j4) | Preferido fast / deep-th (j3; j4) |
|---|---:|---|---|
| Ambos resumen | 50 | 110 / 115; 125 / 128 | 2 / 35; 1 / 34 |
| Ambos centinela | 36 | 108 / 108 | — |
| Solo deep-thinking centinela (correcto en los 4) | 4 | 0 / 12 | 0 / 4 |
| Solo fast centinela (correcto en 9 de 10) | 10 | 27 / 2 | 9 / 1 |

Todo el déficit viene de 9 chunks técnicos que fast marcó con el centinela y deep-thinking resumió.
Son envoltorios generados de FastReport (Q049, Q067, Q069, Q093), código de diseñador (Q046), un
`.csproj` (Q017), propiedades triviales (Q024, Q044) y una declaración de clase vacía (Q088). Q020
(`Silos.cs`) es el único donde los jueces dicen que el resumen de deep-thinking sí correspondía.
Donde ambos resumen, deep-thinking gana 35–2 y 34–1, con más utilidad.

Lectura: con pensamiento, deep escribe resúmenes claramente más fieles y completos, pero resume de
más el código técnico generado. Es un desglose posterior; no cambia la decisión formal.

### Límites
Los mismos del apartado 6. Además, fast no se regeneró, así que sus textos son de una corrida
anterior del mismo día; la varianza entre corridas no se midió. Los jueces j3 y j4 son distintos de
j1 y j2, y sus escalas no son directamente comparables entre rondas.

## Archivos
- `privado\`: `01-muestra.ps1`, `02-generar.ps1`, `03-metricas-y-cegado.ps1`, `04-descifrar.ps1`,
  `coleccion-payload.json`, `reparto.json`, `muestra.json`, `resultados.jsonl`, `metricas.json`,
  `mapa.json`, `resultado-juicio.json`, `informe.md`.
- `jueces\`: `pares-ciegos.json`, `juicio-j1.json`, `juicio-j2.json`.
- Segunda comparación: `privado\` `05-sonda-thinking.ps1`, `sonda-thinking.jsonl`, `regla-thinking.md`,
  `06-generar-thinking.ps1`, `resultados-thinking.jsonl`, `07-metricas-y-cegado-thinking.ps1`,
  `metricas-thinking.json`, `mapa-thinking.json`, `resultado-juicio-thinking.json`; `jueces2\`
  `pares-ciegos.json`, `juicio-j3.json`, `juicio-j4.json`.

Consumo total de AltheIA: 306 llamadas (1 a `/v1/models`, 200 en la corrida principal, 5 de sonda y
100 de deep-thinking), con aprobación expresa del usuario para pasar de 300.
