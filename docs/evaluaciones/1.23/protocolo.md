# 1.23 — Simulación fuera de línea de la expansión por cercanía: protocolo

Fecha: 2026-11-12. Ficha: `1.23-expansion-por-cercania-en-el-archivo-al-consultar` del ledger rector.
**Fijado y commiteado antes de ejecutar la simulación.** Solo cubre la "primera fase sin motor" de la ficha: no hay
endpoint, ni cambio en el cliente, ni Qdrant, ni AltheIA, ni embeddings nuevos (0 tokens).

## Datos (los de 1.11, sin regenerar nada)

- Chunks: `.cache/embeddings-eval/chunks-<col>-c1024.jsonl` (fragmentación vigente c1024, 4157 chunks).
- Top-10 denso: `docs/evaluaciones/1.11/resultados/c1024-w8192.json`, modos `denso-es` y `denso-en` (`top_ids`).
- Preguntas y anclas: eval-sets de Rag.Context.Engine (`docs/eval/*.eval-set.json`, sha256 comprobado contra el
  registrado en el resultado de 1.11) y su traducción EN (`.cache/embeddings-eval/eval-en/`), emparejadas por posición
  como en `ventana.py comparar`. 57 preguntas respondibles.
- Regla de acierto: la de `harness.evaluar_item` (archivo objetivo por nombre, ancla por subcadena ordinal), aplicada
  al **contexto ensamblado** en lugar de al top-10.

## Metadatos de posición (el export de 1.11 no los trae)

- Línea: el archivo fuente (`D:\Git\Reyma.TI.Tickets.Microservice`, `D:\Git\docs-bsute-innovapp-plan` y las tres
  raíces de `BusinessSuite.Xaf` de `ingestar-control.ps1`) con finales LF; `start_line`/`end_line` = primera aparición
  literal de `content`. Un chunk que no se encuentra no tiene vecinos (puede seguir siendo hit) y se cuenta.
- Clase y método: cabecera de `enriched_content` (`// class: X: Base` → `X`; `// method: Nombre(...)` → `Nombre`);
  sección de Markdown: primera línea `[Sección: ...]`.

## Control: el ensamblador actual del cliente

Réplica de `GenerationContextAssembler.BuildContextBlock` (Rag.VectorSpace.Client): por hit en orden de ranking,
`entrada = cabecera + "\n" + content`; separador `"\n\n---\n\n"`; si `len(contexto) + len(entrada) + len(separador)
> 12000` el chunk se omite entero y se sigue con el siguiente. Cabecera con el formato de `BuildChunkHeader`
(`[Chunk #r | Score: 0.00]`, `Repository`, `File (lines a–b)`, `Namespace`, `Class`, `Method`/`Section`); el score no
está en el resultado de 1.11 y se escribe con el mismo ancho fijo (`0.00`), que no cambia longitudes.

## Variantes (fijas; N y topes no se tocan después de ver resultados)

Todas actúan DESPUÉS del corte top-10 y ANTES del ensamblador; el grupo (hit + vecinos aún no admitidos) se ordena por
línea y se admite **atómicamente** con la prioridad del hit; si no cabe entero se registra el descarte y se intenta el
siguiente hit (sin caer al hit solo). Un hit ya admitido como vecino de otro no se repite. Los 12.000 caracteres, el
top-10 y el orden no cambian.

| Variante | Vecinos de cada hit |
|---|---|
| `vecinos-1` | El chunk inmediatamente anterior y el inmediatamente posterior por `start_line` en el mismo archivo (N = 1). Todos los lenguajes. |
| `clase` | Chunks del mismo archivo y la misma clase (solo C#), los 4 más cercanos por línea al hit (grupo ≤ 5). |
| `seccion-md` | Chunks del mismo archivo y la misma sección (solo Markdown), los 4 más cercanos por línea al hit (grupo ≤ 5). |

## Métricas, por variante, idioma y corpus

1. ancla-en-contexto@12k (preguntas), pareado contra el control: +gana / −pierde.
2. Caracteres y tokens de arctic (sin especiales) del contexto final; chunks admitidos; hits omitidos por presupuesto;
   grupos descartados.
3. hit@10 del control como comprobación: debe reproducir 1.11 (ES 46/57, EN 36/57).
4. Latencia: no aplica fuera de línea (se mide si se implementa el endpoint).

## Criterio (el de la ficha)

Una variante se adopta solo si ancla-en-contexto@12k **ES** gana preguntas sin perder ninguna (**+N/−0**) y el aumento
de caracteres/chunks omitidos queda declarado; **EN se reporta como dato, no como veto**. Una variante que gana en
documentos y pierde en código se adopta por lenguaje, no global. Si ninguna cumple: hipótesis nula, y el endpoint y el
cambio del cliente no se justifican con esta evidencia. Nada de esto acredita 1.12 ni usa sus 116 preguntas.

## Límites conocidos de antemano

- 57 preguntas en tres corpus; el control ya acierta 46 ES en el top-10, así que el margen de ganancia es de 11.
- La posición se reconstruye por búsqueda de texto, no con el payload real del motor.
- Simula el ensamblador, no la generación: no mide respuestas ni invenciones.
