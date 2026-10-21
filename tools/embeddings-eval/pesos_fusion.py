"""1.10: recalibra la fusion denso+sparse sobre los chunks/vectores c1024-w8192 de 1.11.

Compara, pareado contra el DENSO SOLO del propio brazo c1024-w8192 (no contra w512 de 1.6,
que es otra fragmentacion):
  denso        ya medido en docs/evaluaciones/1.11/resultados/c1024-w8192.json (denso-es/denso-en)
  nativa       RRF 1/(rank+2) con peso 1:1 (QdrantSemanticRetriever tal cual) = pesos (1,1)
  ponderada    RRF con pesos (w_denso, w_sparse) barridos

RRF ponderada = w_d * 1/(rank_d+2) + w_s * 1/(rank_s+2) sobre los candidatos de las dos ramas
(denso: exacto, coseno >= 0,1, 40 candidatos; sparse-code: 40), mismo desempate por ChunkId
ordinal que fusion_nativa. Reusa harness.py/ventana.py: mismos chunks (chunks-<col>-c1024.jsonl),
mismos vectores (emb/arctic-c1024-w8192), mismas consultas (emb/arctic-m-v2, dims 256).

Subcomandos:
  evaluar <w_d>:<w_s>...   corre el barrido de pesos; escribe docs/evaluaciones/1.10/resultados/pesos.json
  comparar                 tabla pareada final (denso solo / nativa / cada peso) en docs/evaluaciones/1.10/resultados/comparacion.json
"""
import json
import os
import pathlib
import sys

import numpy as np

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parent))
import harness  # noqa: E402
import ventana  # noqa: E402

SALIDA = harness.RAIZ / "docs" / "evaluaciones" / "1.10" / "resultados"
BRAZO = "c1024-w8192"
SUF = "-c1024"
DIMS = 256


def rrf_ponderada(dense, sparse, ids, w_d, w_s):
    pts = {}
    for rama, w in ((dense, w_d), (sparse, w_s)):
        for rank, i in enumerate(rama):
            pts[i] = pts.get(i, np.float32(0)) + np.float32(w) * np.float32(1) / np.float32(rank + 2)
    return sorted(pts, key=lambda i: (-float(pts[i]), ids[i]))[:harness.TOP_K]


def recuperar_ponderado(chunks, ids, docs, q_dense, q_sp_idx, q_sp_val, w_d, w_s):
    dscore = docs @ q_dense
    dense = harness.ranking(dscore, ids, harness.PREFETCH, harness.MIN_SCORE)
    ss, toca = harness.sparse_scores(chunks, q_sp_idx, q_sp_val)
    ss = np.where(toca, ss, -np.inf).astype(np.float32)
    sparse = [i for i in harness.ranking(ss, ids, harness.PREFETCH) if toca[i]]
    return rrf_ponderada(dense, sparse, ids, w_d, w_s)


def nombre_peso(w_d, w_s):
    return f"w{w_d:g}-{w_s:g}"


def evaluar(pares):
    SALIDA.mkdir(parents=True, exist_ok=True)
    for par in pares:
        w_d, w_s = (float(x) for x in par.split(":"))
        nombre = nombre_peso(w_d, w_s)
        out = {"brazo": BRAZO, "pesos": {"denso": w_d, "sparse": w_s}, "modelo": ventana.ARM["repo"], "dimensiones": DIMS,
               "protocolo": {"top_k": harness.TOP_K, "prefetch": harness.PREFETCH, "min_score_denso": float(harness.MIN_SCORE),
                             "formula": "w_d*1/(rank_d+2) + w_s*1/(rank_s+2), desempate ChunkId ordinal"},
               "colecciones": {}}
        for col in harness.COLECCIONES:
            ch = harness.cargar_chunks(col + SUF)
            ids = [c["id"] for c in ch]
            docs = harness.normalizar(np.load(ventana.dir_emb(BRAZO) / f"{col}.docs.npy")[:, :DIMS])
            bloque = {"chunks": len(ch), "eval_set_sha256": harness.sha_eval_set(col)}
            for idioma in ("es", "en"):
                os.environ["VSE_IDIOMA"] = idioma
                items, qv = harness.cargar_preguntas(col)
                qd = harness.normalizar(np.load(ventana.dir_emb("w512") / f"{col}.queries{'-en' if idioma == 'en' else ''}.npy")[:, :DIMS])
                filas = []
                for n, (it, v) in enumerate(zip(items, qv)):
                    hits = recuperar_ponderado(ch, ids, docs, qd[n], v["sparse_indices"], v["sparse_values"], w_d, w_s)
                    filas.append({"question": it["Question"], "hit_any_at_k": harness.evaluar_item(it, hits, ch), "top_ids": [ids[i] for i in hits]})
                anc = [f for f in filas if f["hit_any_at_k"] is not None]
                bloque[f"ponderada-{idioma}"] = {"respondibles": len(anc),
                                                 "aciertos": {str(k): sum(f["hit_any_at_k"][k] for f in anc) for k in harness.CORTES},
                                                 "preguntas": filas}
            out["colecciones"][col] = bloque
            print(nombre, col, {k: v for k, v in {m: bloque[m]["aciertos"]["10"] for m in bloque if "-" in m}.items()}, flush=True)
        os.environ["VSE_IDIOMA"] = "es"
        (SALIDA / f"{nombre}.json").write_text(json.dumps(out, indent=1, ensure_ascii=False), encoding="utf-8")


def _hits_por_pregunta(d, clave):
    out = {}
    for col, b in d["colecciones"].items():
        for p in b[clave]["preguntas"]:
            if p["hit_any_at_k"] is not None:
                out[(col, p["question"])] = p["hit_any_at_k"]["10"]
    return out


def comparar(pares):
    c1024 = json.loads((harness.RAIZ / "docs" / "evaluaciones" / "1.11" / "resultados" / f"{BRAZO}.json").read_text(encoding="utf-8"))
    denso = {"es": _hits_por_pregunta(c1024, "denso-es"), "en": _hits_por_pregunta(c1024, "denso-en")}
    nativa = {"es": _hits_por_pregunta(c1024, "fusion-es"), "en": _hits_por_pregunta(c1024, "fusion-en")}

    def fila_vs_denso(h, idioma):
        d = denso[idioma]
        comunes = sorted(set(h) & set(d))
        return {"n": len(comunes), "hit10": sum(h[q] for q in comunes), "denso_hit10": sum(d[q] for q in comunes),
                "gana": sum(h[q] and not d[q] for q in comunes), "pierde": sum(d[q] and not h[q] for q in comunes),
                "gana_preguntas": [q[1] for q in comunes if h[q] and not d[q]], "pierde_preguntas": [q[1] for q in comunes if d[q] and not h[q]]}

    tabla = {"brazo": BRAZO, "control": "denso solo (c1024-w8192)", "filas": {}}
    tabla["filas"]["denso"] = {"es": {"hit10": sum(denso["es"].values()), "n": len(denso["es"])},
                               "en": {"hit10": sum(denso["en"].values()), "n": len(denso["en"])}}
    tabla["filas"]["nativa (1,1)"] = {"es": fila_vs_denso(nativa["es"], "es"), "en": fila_vs_denso(nativa["en"], "en")}
    for par in pares:
        w_d, w_s = (float(x) for x in par.split(":"))
        nombre = nombre_peso(w_d, w_s)
        d = json.loads((SALIDA / f"{nombre}.json").read_text(encoding="utf-8"))
        h = {"es": _hits_por_pregunta(d, "ponderada-es"), "en": _hits_por_pregunta(d, "ponderada-en")}
        tabla["filas"][f"ponderada {par}"] = {"es": fila_vs_denso(h["es"], "es"), "en": fila_vs_denso(h["en"], "en")}

    SALIDA.mkdir(parents=True, exist_ok=True)
    (SALIDA / "comparacion.json").write_text(json.dumps(tabla, indent=1, ensure_ascii=False), encoding="utf-8")
    print(f"{'fila':18} {'ES hit10':>9} {'ES +/-':>8} {'EN hit10':>9} {'EN +/-':>8}")
    for nombre, f in tabla["filas"].items():
        if nombre == "denso":
            print(f"{nombre:18} {f['es']['hit10']:>9} {'base':>8} {f['en']['hit10']:>9} {'base':>8}")
        else:
            print(f"{nombre:18} {f['es']['hit10']:>9} {('+%d/-%d' % (f['es']['gana'], f['es']['pierde'])):>8} "
                  f"{f['en']['hit10']:>9} {('+%d/-%d' % (f['en']['gana'], f['en']['pierde'])):>8}")


if __name__ == "__main__":
    cmd, *resto = sys.argv[1:]
    if cmd == "evaluar":
        evaluar(resto)
    elif cmd == "comparar":
        comparar(resto)
