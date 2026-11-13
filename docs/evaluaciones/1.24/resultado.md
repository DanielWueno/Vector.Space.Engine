# 1.24 — Resumen añadido al chunk frente a espacio separado: resultado

Fecha: 2026-11-12. Protocolo fijado antes de generar: [protocolo.md](protocolo.md) (commit `234588c`; enmiendas 1 y 2
registradas allí antes de embeber/evaluar: B con arctic en vez del bge-m3 descartado, y embebidos por el TEI del
servidor). Script: `tools/embeddings-eval/resumen_en_chunk.py`. Datos: `resultados/brazos.json` (conteos, IDs de
chunk y preguntas; los textos de los resúmenes quedan en `.cache/`, fuera de git).

## Decisión

**Hipótesis nula según la regla fijada: ningún brazo gana preguntas ES sin perder ninguna frente a A.** La rama
resumen sigue como está y, como dicen la ficha de 1.25 y este protocolo, **1.25 se descarta con motivo**.

## Generación y embebidos

| | Valor |
|---|---|
| Chunks resumidos (3 corpus, c1024) | 4157 / 4157, 0 vacíos, 0 errores, 0 reintentos |
| Con resumen / centinela | 3001 / 1156 (bsuite 1164/630, innovapp 621/8, micro-repo 1216/518) |
| Modelo y PromptVersion | `qwen3.8-27b-fast`, `enable_thinking=false`, 160, 0,15, 0,95 → v2 `8f6ff501` (coincide con la del motor) |
| Tokens de AltheIA | **2 332 597** (2 325 778 de la corrida + 6 819 de la prueba de humo); tope de la corrida 4 M, tope diario 10 M |
| Tiempo | ~25 min de generación (4 en paralelo); ~6,5 min de embebidos en el TEI del servidor |
| Paridad TEI servidor frente a A (64 chunks por corpus) | coseno mínimo 1,000000 en los tres |
| Control | A reproduce el top-10 de 1.11 en 114/114 consultas (ES 46, EN 36) |
| Brazo D: chunks con resumen que el filtro de 1.27 devuelve a A | 148 (bsuite 117, micro-repo 31) |

## hit@10 pareado contra A (57 preguntas)

| Idioma | Brazo | hit@10 | gana / pierde | tokens arctic del top-10 | chunks técnicos en el top-10 |
|---|---|---:|---|---:|---:|
| ES | **A** denso (control) | **46** | — | 3 518 | 3,7 % |
| ES | B espacio resumen + RRF 1/1 k=60 | 47 | +3 / −2 | 3 550 | 2,6 % |
| ES | C resumen dentro del chunk | 44 | +3 / −5 | 2 987 | 2,1 % |
| ES | D = C con filtro previo | 44 | +3 / −5 | 2 994 | 0,9 % |
| EN | A | 36 | — | 3 316 | 3,0 % |
| EN | B | 49 | **+14 / −1** | 3 701 | 2,3 % |
| EN | C | 44 | +8 / −0 | 2 998 | 2,1 % |
| EN | D | 43 | +7 / −0 | 3 028 | 1,1 % |

Por corpus en ES (gana/pierde): bsuite B +0/−0, C y D +0/−3; innovapp B +1/−2, C y D +1/−1; micro-repo B +2/−0,
C y D +2/−1. Ningún brazo aumenta la proporción de chunks técnicos (la segunda condición se cumple en todos, pero no
basta sin la primera).

Preguntas ES que cambian:
- B: gana "¿Cómo sé qué pasos ya se completaron en mi caso?", "¿Cómo recibo un ticket que me asignaron?" y "¿Qué
  tipos de servicio corresponden a un departamento?"; pierde "¿Me avisan por correo cuando resuelven mi problema?" y
  "¿Puedo ver la encuesta de un ticket ajeno?".
- C y D: ganan las dos de micro-repo de B y "¿En qué momento me sale la pregunta de qué tan contento quedé con el
  servicio?"; pierden tres de bsuite (reprogramar la ejecución, determinar el auditado, registrar el plan de acción),
  la encuesta ajena y "¿Qué información se guarda de un ticket?". Meter el resumen en el vector diluye el código
  literal en las preguntas técnicas de bsuite, el riesgo que anotaba la ficha.

## Lo que se ve, sin cambiar la decisión

- **El espacio resumen tal como existe hoy (B) es casi neutro en español y muy favorable en inglés.** Es la primera
  medición de recall de la rama resumen de producción (1.20 no la midió): ES +3/−2 y EN +14/−1. La regla de 1.11
  (ES manda, EN informativo) no permite adoptar nada con esto, pero sí indica que no hace daño en ES y ayuda a
  consultas en otro idioma que el del código.
- **C y D mejoran EN sin perder (+8/−0, +7/−0) y bajan ~15 % los tokens del top-10, pero pierden 5 en ES.**
- **D frente a C:** el filtro previo baja los chunks técnicos del top-10 del 2,1 % al 0,9 % sin cambiar el hit@10 ES.
- Todo esto es descriptivo: una regla nueva (por ejemplo, adoptar por idioma de consulta o por corpus) tendría que
  fijarse antes de otra corrida, no deducirse de esta tabla.

## Límites

- 57 preguntas en tres corpus; una sola corrida de resúmenes (sin varianza entre corridas) y un solo prompt.
- Los resúmenes son de `fast`; el perfil por defecto del motor hoy es `deep` sin razonar (decisión del 2026-11-09).
- La fusión de B replica la del cliente con pools de 100; no incluye salto por símbolo, reranker ni banda de
  confianza, que siguen apagados.
