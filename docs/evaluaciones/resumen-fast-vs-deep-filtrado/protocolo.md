# Fast frente a deep con pensamiento, con el filtro previo de 1.27 (protocolo preregistrado)

Fecha: 2026-11-17. Se escribe y se commitea **antes** de muestrear, generar y juzgar. No se cambia después;
cualquier corrección se añade como enmienda fechada, con su motivo, sin tocar este texto.

## Pregunta

En la segunda comparación del 2026-11-11 (`../resumen-fast-vs-deep/privado/informe.md`, apartado 10) deep con
pensamiento ganó en preferencia (78–80 %) y en fidelidad (6 frente a 30 afirmaciones inventadas o contradictorias),
pero perdió la condición 3 (utilidad) solo por 9 chunks técnicos que resumió de más. El filtro previo de 1.27
(`FiltroPrevioDeResumen`, configuración por defecto) excluye esos chunks antes del LLM. La pregunta es si, en la
población que de verdad llega al LLM con el filtro activo, deep con pensamiento cumple las tres condiciones.

## Población y muestra (`privado/01-muestra-filtrada.ps1`)

- Colección `reyoneweb` del servidor, leída por el túnel existente (`localhost:6333`), solo lectura: `points/scroll`
  con `content`, `language`, `relative_path`, `chunk_type` y los demás campos de la muestra anterior, sin vectores.
  Al escribir este protocolo tiene 5 324 puntos.
- Se excluyen: (a) los puntos que el filtro previo excluye con la configuración por defecto, decididos por el código
  real (`FiltroPrevioDeResumen` de este commit, ejecutado desde `privado/FiltroPrevio/`); (b) los 100 ids de la
  muestra del 2026-11-11, porque el filtro se diseñó mirándolos.
- Estratos, reparto y selección idénticos a `01-muestra.ps1` del experimento anterior (segundo segmento de
  `relative_path` × `language`; mínimo 3; Hamilton; Fisher-Yates parcial con `System.Random` nuevo por estrato),
  N = 100, semilla **20261117**.

## Brazos (`privado/02-generar.ps1`)

Petición de `LlmResumenBrain` (prompt del sistema verificado carácter a carácter contra el literal del motor;
usuario `[Contexto: Lenguaje: {language}, Ruta: {relative_path}]\n\n{content}`; temperature 0,15; top_p 0,95).

- **fast**: `qwen3.8-27b-fast`, sin `chat_template_kwargs`, `max_tokens` 160. Se regenera en esta corrida.
- **deep-thinking**: `qwen3.8-27b-deep`, `chat_template_kwargs: {"enable_thinking": true}`, `max_tokens` 4096.

Se toma solo `message.content`. Un reintento si no hay HTTP 200. Hasta 4 llamadas simultáneas (afecta solo a la
latencia, que se informa como descriptiva). Credenciales solo desde `ALTHEIA_KEY`/`ALTHEIA_CERT`.

## Cegado y juicio (`privado/03-metricas-y-cegado.ps1`)

- Pares R001..R100 en el orden de la muestra; `System.Random(17)`, `Next(2) == 0` ⇒ A = fast. Los jueces solo ven
  `jueces/pares-ciegos.json` (par, ruta, lenguaje, fragmento, A, B).
- Dos subagentes nuevos (j5, j6) con el prompt de juez literal de la ronda anterior; solo cambian la carpeta y el
  nombre del archivo de salida. Código fuente: `C:\Users\dsoft03.corp\Downloads\Release\Release\`.

## Regla de decisión (la misma de `regla-thinking.md`)

deep-thinking se considera mejor SOLO si se cumplen las tres:

1. deep-thinking es preferido en ≥ 60 % de los pares con ganador, en AMBOS jueces;
2. deep-thinking tiene menos afirmaciones "inventada" + "contradice" que fast, sumando ambos jueces;
3. la suma de `utilidad_negocio` de deep-thinking no es menor que la de fast.

Si no se cumplen, se mantiene fast. Se aplica con `../resumen-fast-vs-deep/privado/04-descifrar.ps1` parametrizado.

## Coste (sin umbral; decide el usuario)

completion_tokens y latencia por llamada (media, mediana, p90, máx.), totales, vacíos, `finish_reason=length`,
reintentos, y la extrapolación lineal a la población filtrada de `reyoneweb`, marcada como no medida.

## Además (descriptivo, no decide)

Test de signos unilateral por juez, acuerdo j5–j6 en `preferido`, afirmaciones "codigo", `centinela_ok=false` por
brazo, chunks que los jueces consideran técnicos y que el filtro dejó pasar, y pares en desacuerdo.

## Presupuesto

AltheIA: 200 llamadas previstas (+ reintentos), ~180 k tokens estimados (tope diario 10 M). Qdrant: solo lecturas.
Ninguna escritura en Qdrant ni despliegue. Este experimento no ejecuta la directiva de ingesta desatendida.
