# 1.23 — Simulación fuera de línea de la expansión por cercanía: resultado

Fecha: 2026-11-12. Protocolo fijado antes de correr: [protocolo.md](protocolo.md) (commit `5b1bec9`).
Script: `tools/embeddings-eval/expansion.py`. Datos crudos: `resultados/simulacion.json` (solo IDs, conteos y
booleanos; ningún contenido de los repos fuente). 0 tokens de AltheIA, sin Qdrant, sin embeddings nuevos.

## Decisión

**Hipótesis nula: ninguna variante cumple el criterio.** En español, ancla-en-contexto@12k pierde preguntas con las
tres variantes, en el total y en cada corpus, así que tampoco hay adopción por lenguaje. Con esta evidencia **no se
justifican el endpoint `fragmentos` del motor ni el paso de expansión del cliente**, y no se implementan.

## Enmienda del instrumento (declarada)

La primera corrida tuvo dos fallos del instrumento, no de las variantes, que se corrigieron antes de leer el
resultado final; variantes, N, topes, métricas y criterio no cambiaron:

1. El `tokenizer.json` de arctic trae truncado a 512: todos los contextos contaban 512 tokens. Se desactivó el truncado.
2. La ubicación por "primera aparición literal de `content`" no encontró ningún chunk de Markdown (su `content` empieza
   con la etiqueta `[Sección: …]`) ni el ~20 % de los de C# (grupos de campos/propiedades y cabeceras de clase, que no son
   texto contiguo del archivo). Ahora se quita la etiqueta y, si la búsqueda literal falla, se ancla en la línea más
   distintiva del chunk y se exige que la ventana explique al menos la mitad de sus líneas.

Ubicación final: 4152 de 4157 chunks (literal 3600, por líneas 552, sin ubicar 5). La corrida inválida tampoco
cumplía el criterio con ninguna variante (ES: vecinos-1 +1/−3, clase +0/−5, sección-md sin efecto porque no tenía
posiciones), así que la enmienda no cambió la decisión.

## Comprobaciones

- Los tres eval-sets tienen el mismo sha256 que en 1.11.
- hit@10 del control reproduce 1.11 pregunta por pregunta (ES 46/57, EN 36/57); el script aborta si no.

## Resultados (57 preguntas respondibles; pareado contra el control)

| Idioma | Variante | ancla-en-contexto@12k | gana / pierde | caracteres medios | tokens arctic medios | chunks medios | hits omitidos | grupos descartados |
|---|---|---:|---|---:|---:|---:|---:|---:|
| ES | **control** (ensamblador actual) | **38** | — | 10 859 | 3 057 | 8,2 | 105 | 0 |
| ES | vecinos-1 | 37 | +5 / −6 | 11 246 | 3 126 | 10,3 | 246 | 243 |
| ES | clase | 29 | +0 / −9 | 11 076 | 3 132 | 9,9 | 199 | 142 |
| ES | seccion-md | 35 | +0 / −3 | 10 847 | 3 043 | 8,8 | 159 | 63 |
| EN | control | 35 | — | 10 647 | 2 982 | 8,2 | 101 | 0 |
| EN | vecinos-1 | 32 | +3 / −6 | 11 161 | 3 147 | 10,6 | 245 | 241 |
| EN | clase | 29 | +0 / −6 | 10 693 | 3 031 | 9,9 | 197 | 138 |
| EN | seccion-md | 33 | +1 / −3 | 10 761 | 3 018 | 9,2 | 138 | 64 |

Por corpus, en ES (ganadas/perdidas):

| Corpus | vecinos-1 | clase | seccion-md |
|---|---|---|---|
| bsuite-auditorias-test (C#) | +2 / −1 | +0 / −5 | sin efecto |
| innovapp-docs (Markdown) | +3 / −1 | sin efecto | +0 / −3 |
| micro-repo (C# + algo de Markdown) | +0 / −4 | +0 / −4 | sin efecto |

Mecanismo de las pérdidas: con 12 000 caracteres el contexto ya va casi lleno (10,9 k de media); cada grupo es más
grande que el hit solo, así que se descartan grupos enteros (243 con vecinos-1) y con ellos hits que antes entraban.
Las ganancias de vecinos-1 son preguntas donde un vecino de un hit anterior trae el ancla, o donde el hit del ancla
estaba en un rango bajo que el control ya descartaba.

## Hallazgo: el presupuesto de 12 000 caracteres ya pierde anclas sin expansión

hit@10 sobrestima lo que llega al LLM. Con el ensamblador actual, en **8 de las 46 preguntas ES** que aciertan en el
top-10 el chunk del ancla se omite por presupuesto (EN: 1 de 36). Están en los rangos 5 a 10; algunos son grandes
(3,5 k–5,7 k caracteres) y otros pequeños (525 caracteres en el rango 7) que no caben porque los anteriores ya
gastaron el presupuesto. Afecta a los tres corpus (bsuite 3, innovapp 2, micro-repo 3).

Es la "interacción crítica" que la tabla del análisis anotaba (fila 7) y apoya la hipótesis falsable de 1.12 sobre el
descarte por presupuesto, pero aquí está medida sobre el banco de 1.11, no sobre las preguntas de 1.12. Una mejora del
ensamblador (por ejemplo, recortar en lugar de omitir, o presupuesto en tokens del modelo de generación) es otra ficha
del cliente, posterior a 1.12; no se propone ni se mide aquí.

## Límites

- 57 preguntas en tres corpus. La posición se reconstruye desde las fuentes, no desde el payload real del motor.
- Se simula el ensamblador, no la generación: no se miden respuestas ni invenciones.
- La admisión atómica sin caída al hit solo es la de la ficha. Una variante con caída al hit solo no se midió: haría
  falta una regla nueva fijada de antemano.
- Si 1.12.10 llega a correr y su diagnóstico atribuye fallos al descarte de condiciones vecinas, esta ficha puede
  reabrirse con esa evidencia.
