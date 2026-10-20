"""1.6: tabla de recall y comparacion pareada (gana/pierde por pregunta en hit@10) contra el control.

Con 57 preguntas respondibles, un total agregado esconde intercambios: lo que decide es cuantas
preguntas gana un arm frente al control y cuantas pierde (criterio de Rag.Context.Engine).
Uso: python comparar.py [control=onnx-produccion]
"""
import json
import pathlib
import sys

RES = pathlib.Path(__file__).resolve().parents[2] / "docs" / "evaluaciones" / "1.6" / "resultados"
CONTROL = sys.argv[1] if len(sys.argv) > 1 else "onnx-produccion"
MODOS = ("denso", "fusion")


def cargar(nombre):
    return json.loads((RES / f"{nombre}.json").read_text(encoding="utf-8"))


def hits(datos, modo):
    out = {}
    for col, b in datos["colecciones"].items():
        if "pendiente" in b:
            continue
        for p in b[modo]["preguntas"]:
            if p["hit_any_at_k"] is not None:
                out[(col, p["question"])] = {int(k): v for k, v in p["hit_any_at_k"].items()}
    return out


ctrl = cargar(CONTROL)
tabla = {}
for f in sorted(RES.glob("*.json")):
    d = json.loads(f.read_text(encoding="utf-8"))
    if "colecciones" not in d or "arm" not in d or d["arm"].endswith("+en"):
        continue  # +en: otra pregunta, no se empareja con el control en espanol
    fila = {"modelo": d["modelo"], "dimensiones": d.get("dimensiones")}
    for modo in MODOS:
        h, c = hits(d, modo), hits(ctrl, modo)
        comunes = sorted(set(h) & set(c))
        por_col = {}
        for col in d["colecciones"]:
            ks = [k for k in comunes if k[0] == col]
            if ks:
                por_col[col] = {"n": len(ks), **{f"@{k}": sum(h[q][k] for q in ks) for k in (1, 5, 10)},
                                "control@10": sum(c[q][10] for q in ks)}
        fila[modo] = {"n": len(comunes), **{f"@{k}": sum(h[q][k] for q in comunes) for k in (1, 5, 10)},
                      "control@10": sum(c[q][10] for q in comunes),
                      "gana@10": sum(h[q][10] and not c[q][10] for q in comunes),
                      "pierde@10": sum(c[q][10] and not h[q][10] for q in comunes),
                      "por_coleccion": por_col}
    tabla[d["arm"]] = fila

nombre = "resumen.json" if CONTROL == "onnx-produccion" else f"resumen-vs-{CONTROL}.json"
(RES / nombre).write_text(json.dumps({"control": CONTROL, "arms": tabla}, indent=1, ensure_ascii=False), encoding="utf-8")
print(f"{'arm':22} {'modo':7} {'n':>3} {'@1':>3} {'@5':>3} {'@10':>4} {'ctrl':>5} {'gana':>5} {'pierde':>6}")
for arm, fila in sorted(tabla.items(), key=lambda kv: -kv[1]["denso"]["@10"] / max(kv[1]["denso"]["n"], 1)):
    for modo in MODOS:
        m = fila[modo]
        print(f"{arm:22} {modo:7} {m['n']:>3} {m['@1']:>3} {m['@5']:>3} {m['@10']:>4} {m['control@10']:>5} {m['gana@10']:>5} {m['pierde@10']:>6}")
