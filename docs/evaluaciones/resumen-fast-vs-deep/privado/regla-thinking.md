# Regla de decisión: fast vs deep con pensamiento (fijada antes de generar y de descifrar)

Fecha: 2026-11-11, antes de lanzar `06-generar-thinking.ps1`. No se cambia después.

Brazos:
- **fast**: los mismos textos de la corrida principal (`resultados.jsonl`, sin regenerar).
- **deep-thinking**: `qwen3.8-27b-deep` con `chat_template_kwargs: {"enable_thinking": true}` y
  `max_tokens: 4096`. Todo lo demás igual a `LlmResumenBrain` (prompt, usuario, temperature 0.15,
  top_p 0.95). Se toma solo `message.content`, como el motor.

Muestra: los mismos 100 chunks de `muestra.json`. Un reintento si no es HTTP 200.

Cegado: pares Q001..Q100 en el orden de `muestra.json`, `System.Random(11)`, `Next(2)==0` ⇒ A=fast.
Los jueces solo ven `jueces2\`. Son dos subagentes nuevos (j3, j4) con el prompt de juez literal; solo
cambian la carpeta y el nombre. En `privado\mapa-thinking.json` la etiqueta `deep` significa
deep-thinking.

Calidad: deep-thinking se considera mejor SOLO si se cumplen las tres:
1. deep-thinking es preferido en ≥ 60 % de los pares con ganador, en AMBOS jueces;
2. deep-thinking tiene menos afirmaciones "inventada" + "contradice" que fast, sumando ambos jueces;
3. la suma de utilidad_negocio de deep-thinking no es menor que la de fast.
Si no se cumplen, se mantiene fast.

Coste: se informa aparte, sin umbral fijado (decide el usuario): completion_tokens y latencia por
llamada (media, mediana, p90, máx.), totales, respuestas vacías o `finish_reason=length`, frente a fast.

Además: test de signos unilateral por juez, acuerdo j3–j4 en `preferido`, afirmaciones "codigo" por
separado, aciertos del centinela según los jueces y pares en desacuerdo para revisión humana.
