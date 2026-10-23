# 1.7 — Modelo de consumo de ingesta

Fecha: 2026-10-23. Ficha: `1.7-definir-modelo-de-consumo-de-ingesta`.
Entorno: API del servicio en el host (Kestrel real en proceso, identidad de prueba) contra el stack ya desplegado
en corpreyiaprod (Qdrant 1.19.2 y TEI cpu-1.8.3 con arctic-embed-m-v2.0@256) por un túnel SSH con port-forward
local abierto por el usuario. Sin AltheIA (no gasta cuota). Herramienta:
[`tools/consumo-ingesta/`](../../../tools/consumo-ingesta/README.md). Evidencia cruda: `resultados/*.json`.

## Decisión

**Híbrido con el envío por API como mecanismo de producción.** La ingesta real se dispara desde un pipeline de
Azure DevOps (al hacer push) que ya tiene el repo descargado y envía por `POST ingestas` (origen `documentos`)
los archivos cambiados, y los borrados con contenido vacío. El origen `ruta` se mantiene en el contrato (501),
reservado: no se implementa ahora porque lo único que ahorra (la transferencia cliente → API) es
despreciable, y lo que exige (credenciales de ADO en el servidor, servidor alcanzable o *polling*, raíces
permitidas por colección) es caro y hoy no está resuelto. **Límite de admisión implementado**: una ingesta en
curso por instancia y cuatro en cola; la siguiente recibe 429 con `Retry-After`.

## Alcance confirmado al arrancar

Reentrada resuelta por el usuario (`_reentrada_resuelta_2026_10_23` en el ledger): (1) medir con TEI y
micro-repo contra el stack de corpreyiaprod por el túnel, no con un TEI efímero ni con AltheIA; (2) estimar el
disparador de Azure DevOps sin conectarse a ADO; (4) implementar el límite, no solo documentarlo. Solo se
crearon y borraron colecciones `it-1-7-*`.

## (1) Coste real de transferir un repo completo por HTTP

Corpus: micro-repo (`Reyma.TI.Tickets.Microservice`) con el escáner real de Rag.Context.Engine: 567 documentos,
1,23 MB de contenido UTF-8 (mayor: 23 KB), escanear y leer del disco 0,6–1,0 s. Por el contrato (máximo 100
documentos por solicitud) son 6 solicitudes y 1,53 MB de JSON (la mayor, 0,47 MB).

| Medida | Sin corte (`transferencia-sin-corte.json`) | Con corte (`transferencia.json`) |
|---|---|---|
| Primera pasada (vectoriza todo) | 158,2 s, 1734 puntos | 111,1 s, 1734 puntos |
| Segunda pasada (todo «sin cambios») | 0,4 s | 0,4 s |
| Túnel, salida / entrada (aprox.) | 7,9 / 5,3 MB | 8,4 / 5,6 MB |
| Working set máximo del proceso | 168 MB | 172 MB |

- **La transferencia no es el coste**: la segunda pasada manda el repo entero por HTTP (deserialización,
  troceo, huellas y consulta a Qdrant incluidos) en 0,4 s. Lo que cuesta son los embeddings: 111–158 s en el
  TEI del servidor (~11–16 chunks/s; la primera corrida incluye arranque en frío, lote 0 en 82 s).
- El tráfico del túnel (textos al TEI, vectores de vuelta, upserts a Qdrant: ~4–5 veces el contenido) es
  **API ↔ motor** y existe igual con `ruta`. No distingue entre los modelos.
- **Reintentos**: el cliente cortó el lote 0 a los 33 s, cuando ya había 416 puntos guardados. Al repetir la
  solicitud entera solo se vectorizaron 14 de 100 documentos (11 s) y 86 dieron «sin cambios». El total final
  es el mismo, 1734 puntos, sin duplicados ni huérfanos. Repetir una solicitud ya completada cuesta 0,07 s.
- **Cuerpo grande** (`cuerpo.json`, documentos `.bin`, que no se vectorizan): 7,6 MB → 200; 26,7 MB → 200, con
  el working set subiendo de 166 a 354 MB (~7 veces el cuerpo); 38,2 MB → **500** «Request body too large. The
  max request body size is 30000000 bytes». El límite efectivo es el de Kestrel (~28,6 MiB), no el teórico del
  contrato (100 × 1 MiB), y responde 500 en vez de 413. Queda como ítem `1.7.1`; no se corrige aquí.

## (2) Coste de un disparador local equivalente al CLI (estimado sin tocar ADO)

| Aspecto | Evidencia | Coste |
|---|---|---|
| Leer el repo en local (`ruta`) | 0,6–1,0 s (medido) | Ahorra solo la transferencia: <0,5 s y ~1,5 MB por repo completo |
| Tener el repo en el servidor | historial de micro-repo: pack de 1,39 MiB + 0,65 MiB sueltos, 685 commits; `git diff --name-status` de 20 commits: 90 ms (medido en local) | Clone de ~2 MB y fetch incremental: barato |
| Credenciales de ADO en el servidor | 1.5: la contraseña de dominio por Basic Auth contra Azure DevOps Services da 401; hace falta un PAT (vence; la empresa evita rotarlos proyecto por proyecto) o un token de Entra ID | **El coste real**: no hay hoy una identidad no personal verificada para el servidor |
| Aviso de cambios | Los service hooks de ADO Services llaman desde la nube; corpreyiaprod hoy solo se alcanza por túnel SSH | Haría falta exponer el servicio o hacer *polling* |
| Idempotencia (dos disparos) | medido: el mismo lote dos veces a la vez sobre la misma colección → uno indexa 100 y el otro 100 «sin cambios», 406 puntos como uno solo; repo completo repetido: 0,4 s | Igual en los dos modelos: es el mismo pipeline |
| Concurrencia (varios repos) | ver (4) | Igual en los dos modelos: comparten el TEI |
| Borrados | `documentos`: se mandan con contenido vacío y sus puntos se borran (prueba de 1.4); `ruta`: el servicio necesitaría además un barrido del árbol completo, que no existe | Parecido |

Un **pipeline de ADO** en un agente de la red corporativa (la empresa ya usa pipelines con service
connections) ya tiene el repo descargado con `System.AccessToken`. No hace falta un PAT ni dejar credenciales
de ADO en el servicio. El pipeline envía por HTTP lo que cambió. El coste de un run del pipeline (agente y
checkout) **no se midió**, porque no hay acceso a ADO. Hace falta además un cliente registrado en el
IdentityServer con una concesión `Clientes` en la tabla de acceso. Implementarlo queda como `1.7.2`.

## (3) Superficies separadas

Ya lo estaban desde 1.3 (`embeddings` no escribe; `ingestas` es otra ruta). El límite de (4) solo se aplica a
`ingestas`: con el cupo lleno, `embeddings` y `busquedas` siguen respondiendo 200 (test
`IngestaQueRebasaLaColaDevuelve429ConRetryAfterYNoBloqueaConsultas`). Comparten el TEI, así que la ingesta sí
les sube la latencia (`concurrencia-400000.json`, mediana / máximo de búsqueda): en reposo 0,02 / 0,24 s; con
una ingesta, 0,41 / 1,49 s; con dos, 0,80 / 2,65 s.

## (4) Saturación: límite explícito

Medido (`concurrencia-400000.json`; la corrida de 40 000 caracteres da lo mismo con n menor):

| | A (100 docs, 406 chunks) | B (99 docs, 202 chunks) | Total |
|---|---|---|---|
| Una tras otra | 26,5 s | 9,3 s | 36,8 s |
| Las dos a la vez | 35,1 s | 15,8 s | 36,1 s |

Dos ingestas simultáneas **no terminan antes** (~16,5 chunks/s en ambos casos): solo alarga cada una y las
consultas. Por eso:

- `LimiteDeIngestas:MaximoSimultaneas = 1`: una ingesta en curso por instancia, entre todas las colecciones.
  Sigue el candado por colección del pipeline.
- `LimiteDeIngestas:MaximoEnCola = 4`: con lotes de ≤ 100 documentos (el mayor observado, 82,5 s en frío), la
  peor espera en cola ronda los 5,5 min.
- Rebasarla → **429** con `Retry-After: 60` (`SegundosParaReintentar`), sin llegar al motor. Autorización y
  validación van antes: un 403 o un 400 no ocupan lugar.
- Real, contra el stack de corpreyiaprod (`saturacion.json`): 8 ingestas simultáneas a colecciones distintas
  → 5 × 200 y 3 × 429 (en 0,24 s, `Retry-After: 60`). Los 200 terminan escalonados (3,3 / 6,0 / 8,9 / 11,8 /
  14,6 s): corren de una en una.
- El límite es **por instancia**: varias réplicas lo multiplican y no comparten cola. Hoy hay una sola
  instancia; con más haría falta un limitador externo.

Consecuencia para los clientes: una solicitud puede esperar en cola y luego correr, unos 7 min en el peor caso.
El timeout por defecto de `HttpClient` (100 s) no alcanza: el cliente debe usar ≥ 10 min, reintentar el 429
después de `Retry-After` y reintentar ante fallos de red (es idempotente, ver (1)).

## No medido / límites

- La API corrió en el host: el tramo cliente → API fue loopback y el tramo API → motor pasó por el túnel SSH,
  que añade su propio cifrado. Con la API desplegada en el servidor la red se invierte. Como el envío del repo
  completo es ~1,5 MB, no cambia la decisión.
- Un solo corpus (micro-repo). bsuite-repo (~22,6 k chunks, 1.6) no se midió: a ~11–16 chunks/s serían
  ~25–35 min de TEI por reingesta completa en lotes de 100, y repetirla sin cambios seguiría costando
  segundos. Es una estimación, no una medida.
- AltheIA (1.13) puede pasar a calcular los embeddings en producción. Eso cambia la velocidad, no el modelo
  de consumo: la transferencia no es el cuello de botella en ningún caso.
- El run del pipeline de ADO, su identidad y su alcance de red: estimados, no medidos.

## Rollback

Revertir el commit: el handler de ingesta vuelve a no tener límite (ingestas de colecciones distintas en
paralelo y cola sin tope). Las colecciones `it-1-7-*` del Qdrant de corpreyiaprod se borraron con
`tools/consumo-ingesta limpiar`; ninguna otra colección se tocó.
