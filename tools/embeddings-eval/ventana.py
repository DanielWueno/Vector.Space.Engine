"""1.11: ventana de contexto de arctic-embed-m-v2.0 (8192 tokens) frente al recorte a 512 de 1.6.

Brazos (todos arctic@256, comparados en pareado contra w512 = resultado de 1.6):
  w512            vectores de 1.6 (emb/arctic-m-v2), re-evaluados aqui como control
  w1024, w8192    mismos chunks, ventana mayor
  c<N>-w8192      re-fragmentacion con MaxTokensPerChunk=N (Refragmentar/), ventana que cubre todo

Con los mismos chunks solo cambian los que pasan de 512 tokens: se re-embeben esos y se reutiliza el
resto (se comprueba sobre una muestra de cortos que el vector es el mismo). Contrapeso por brazo:
tokens del `content` del top-10 (lo que GenerationContextAssembler pasa al LLM), con el tokenizador
de arctic, y chunks/s de TEI (subcomando `tei`).

Subcomandos:
  tokens [sufijo]...            distribucion de longitudes (tokens de arctic, con especiales)
  embeber <brazo>...            vectores en el host (sentence-transformers, como 1.6)
  fidelidad <sufijo>            Refragmentar a 512 frente a los chunks del CLI (deben ser identicos)
  anclas <sufijo>...            preguntas cuya ancla no existe en ningun chunk de alguna fragmentacion
  evaluar <brazo>...            hit@k ES/EN y tokens del top-10; docs/evaluaciones/1.11/resultados/<brazo>.json
  comparar <brazo>...           pareado contra w512, excluyendo en todos las preguntas sin ancla
  presupuesto <brazo>           w512 alargado hasta los mismos tokens del top-10 del brazo (control del contrapeso)
  tei <brazo> <base_url>        chunks/s de TEI con la ventana del brazo sobre la muestra de 1.6
"""
import json
import os
import pathlib
import sys
import time
import urllib.request

import numpy as np

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parent))
import harness  # noqa: E402

SALIDA = harness.RAIZ / "docs" / "evaluaciones" / "1.11" / "resultados"
ARM = harness.ARMS["arctic-m-v2"]
DIMS = 256
BASE_512 = "arctic-m-v2"  # vectores de 1.6, ventana 512
TOKENS_POR_LOTE = 8192  # lotes por presupuesto de tokens: la atencion crece con L^2


def brazo(nombre):
    """'w1024' -> ('', 1024); 'c1024-w8192' -> ('-c1024', 8192)."""
    frag, _, w = nombre.rpartition("w")
    return ("-" + frag.rstrip("-")) if frag else "", int(w)


def dir_emb(nombre):
    return harness.CACHE / "emb" / (BASE_512 if nombre == "w512" else f"arctic-{nombre}")


def tokenizador():
    from transformers import AutoTokenizer

    snap = next((harness.RAIZ / ".cache" / "hf" / ("models--" + ARM["repo"].replace("/", "--")) / "snapshots").iterdir())
    return AutoTokenizer.from_pretrained(str(snap))


def longitudes(tok, textos, especiales=True):
    return np.array([len(ids) for ids in tok(textos, add_special_tokens=especiales)["input_ids"]])


def tokens(sufijos):
    tok = tokenizador()
    out = {}
    for suf in sufijos or [""]:
        for col in harness.COLECCIONES:
            ch = harness.cargar_chunks(col + suf)
            n = longitudes(tok, [c["enriched_content"] for c in ch])
            out[col + suf] = {"chunks": len(ch), "p50": int(np.median(n)), "p90": int(np.percentile(n, 90)), "max": int(n.max()),
                              "gt512": int((n > 512).sum()), "gt1024": int((n > 1024).sum()), "tokens_total": int(n.sum()),
                              "tokens_gt512": int(n[n > 512].sum())}
            print(col + suf, out[col + suf], flush=True)
    return out


def modelo_st(ventana):
    import truststore

    truststore.inject_into_ssl()
    from sentence_transformers import SentenceTransformer

    m = SentenceTransformer(ARM["repo"], device="cpu", trust_remote_code=True, cache_folder=str(harness.RAIZ / ".cache" / "hf"),
                            config_kwargs=ARM["config_kwargs"])
    m.max_seq_length = ventana
    return m


def codificar(m, textos, largos):
    """Lotes ordenados por longitud con presupuesto de tokens (no 16 fijos: 16x8192 no cabe en RAM)."""
    orden = np.argsort(largos, kind="stable")
    salida = np.empty((len(textos), m.get_sentence_embedding_dimension()), dtype=np.float32)
    i = 0
    while i < len(orden):
        l = min(int(largos[orden[i]]), m.max_seq_length)
        lote = 1
        while i + lote < len(orden) and lote < 16 and (lote + 1) * min(int(largos[orden[i + lote]]), m.max_seq_length) <= TOKENS_POR_LOTE:
            lote += 1
        idx = orden[i:i + lote]
        salida[idx] = m.encode([textos[j] for j in idx], batch_size=lote, normalize_embeddings=True,
                               convert_to_numpy=True, show_progress_bar=False).astype(np.float32)
        i += lote
    return salida


def embeber(nombre):
    suf, ventana = brazo(nombre)
    tok = tokenizador()
    m = modelo_st(ventana)
    dest = dir_emb(nombre)
    dest.mkdir(parents=True, exist_ok=True)
    meta = {"brazo": nombre, "repo": ARM["repo"], "ventana": ventana, "fragmentacion": suf or "512 (1.6)", "colecciones": {}}
    for col in harness.COLECCIONES:
        ch = harness.cargar_chunks(col + suf)
        textos = [c["enriched_content"] for c in ch]
        n = longitudes(tok, textos)
        t = time.perf_counter()
        if suf:
            # Texto identico a un chunk de 1.6 -> mismo vector que w<ventana> (sin recorte, no depende del
            # resto del lote). Solo se embeben los textos nuevos; control: 32 reutilizados re-embebidos.
            previo = dir_emb(f"w{ventana}") / f"{col}.docs.npy"
            conocidos = {}
            if previo.exists():
                v = np.load(previo)
                conocidos = {c["enriched_content"]: v[i] for i, c in enumerate(harness.cargar_chunks(col))}
            docs = np.empty((len(ch), m.get_sentence_embedding_dimension()), dtype=np.float32)
            reusa = [i for i, t in enumerate(textos) if t in conocidos]
            nuevos = np.array([i for i, t in enumerate(textos) if t not in conocidos], dtype=int)
            for i in reusa:
                docs[i] = conocidos[textos[i]]
            if len(nuevos):
                docs[nuevos] = codificar(m, [textos[i] for i in nuevos], n[nuevos])
            info = {"chunks": len(ch), "embebidos": len(nuevos), "reutilizados": len(reusa)}
            if reusa:
                ctrl_i = np.array(reusa[::max(1, len(reusa) // 32)][:32])
                ctrl = codificar(m, [textos[i] for i in ctrl_i], n[ctrl_i])
                info["control_reutilizados_cos_min"] = round(float(np.sum(ctrl * docs[ctrl_i], axis=1).min()), 6)
            n = n[nuevos] if len(nuevos) else n[:0]
        else:
            # Mismos chunks: solo cambian los de mas de 512 tokens. Control: 32 cortos re-embebidos.
            docs = np.load(dir_emb("w512") / f"{col}.docs.npy").copy()
            largos = np.nonzero(n > 512)[0]
            docs[largos] = codificar(m, [textos[i] for i in largos], n[largos])
            cortos = np.nonzero(n <= 512)[0][::max(1, int((n <= 512).sum()) // 32)][:32]
            ctrl = codificar(m, [textos[i] for i in cortos], n[cortos])
            cos = np.sum(ctrl * docs[cortos], axis=1)
            info = {"chunks": len(ch), "embebidos": len(largos), "control_cortos_cos_min": round(float(cos.min()), 6)}
        s = time.perf_counter() - t
        info |= {"s": round(s, 1), "tokens_embebidos": int(n.sum()) if suf else int(n[n > 512].sum())}
        np.save(dest / f"{col}.docs.npy", docs)
        meta["colecciones"][col] = info
        print(nombre, col, info, flush=True)
    (dest / "meta.json").write_text(json.dumps(meta, indent=2, ensure_ascii=False), encoding="utf-8")


def fidelidad(sufijo):
    """Refragmentar a 512 debe dar los mismos chunks que el CLI: ids, contenido, hash y sparse."""
    ok = True
    for col in harness.COLECCIONES:
        a = {c["id"]: c for c in harness.cargar_chunks(col)}
        b = {c["id"]: c for c in harness.cargar_chunks(col + sufijo)}
        campos = ("relative_path", "content", "enriched_content", "content_hash", "sparse_indices", "sparse_values")
        distintos = [i for i in set(a) & set(b) if any(a[i][k] != b[i][k] for k in campos)]
        r = {"cli": len(a), "refragmentar": len(b), "solo_cli": len(set(a) - set(b)), "solo_refragmentar": len(set(b) - set(a)),
             "distintos": len(distintos)}
        ok &= r["solo_cli"] == r["solo_refragmentar"] == r["distintos"] == 0
        print(col, r, flush=True)
    print("fidelidad Refragmentar:", "IDENTICA" if ok else "DIFIERE")
    return ok


def sin_ancla(sufijos):
    """Preguntas (col, pregunta) cuya ancla no aparece en ningun chunk del archivo objetivo en alguna fragmentacion."""
    faltan = {}
    for suf in sufijos:
        for col in harness.COLECCIONES:
            ch = harness.cargar_chunks(col + suf)
            items, _ = harness.cargar_preguntas(col)
            for it in items:
                objetivos = it.get("SourceFiles") or ([it["SourceFile"]] if it.get("SourceFile") else [])
                anclas = it.get("TargetContentContains") or []
                if not objetivos or not anclas:
                    continue
                obj = {o.lower() for o in objetivos}
                existe = any(a in c["content"] for c in ch
                             if os.path.basename((c["relative_path"] or "").replace("\\", "/")).lower() in obj for a in anclas)
                if not existe:
                    faltan.setdefault((col, it["Question"]), []).append(suf or "512")
    return faltan


def evaluar(nombres):
    SALIDA.mkdir(parents=True, exist_ok=True)
    tok = tokenizador()
    for nombre in nombres:
        suf, ventana = brazo(nombre)
        out = {"brazo": nombre, "modelo": ARM["repo"], "dimensiones": DIMS, "ventana": ventana, "fragmentacion": suf or "512 (1.6)",
               "protocolo": {"top_k": harness.TOP_K, "prefetch": harness.PREFETCH, "min_score_denso": float(harness.MIN_SCORE),
                             "bandas": "denso+sparse-code, sin resumen", "tokens_top10": "tokens de arctic sobre `content`, sin especiales"},
               "colecciones": {}}
        meta = dir_emb(nombre) / "meta.json"
        if meta.exists():
            out["embeber"] = json.loads(meta.read_text(encoding="utf-8"))
        for col in harness.COLECCIONES:
            ch = harness.cargar_chunks(col + suf)
            ids = [c["id"] for c in ch]
            ntok = longitudes(tok, [c["content"] for c in ch], especiales=False)
            docs = harness.normalizar(np.load(dir_emb(nombre) / f"{col}.docs.npy")[:, :DIMS])
            bloque = {"chunks": len(ch), "eval_set_sha256": harness.sha_eval_set(col)}
            for idioma in ("es", "en"):
                os.environ["VSE_IDIOMA"] = idioma
                items, qv = harness.cargar_preguntas(col)
                qd = harness.normalizar(np.load(dir_emb("w512") / f"{col}.queries{'-en' if idioma == 'en' else ''}.npy")[:, :DIMS])
                for modo in ("denso", "fusion"):
                    filas = []
                    for n, (it, v) in enumerate(zip(items, qv)):
                        hits = harness.recuperar(ch, ids, docs, qd[n], v["sparse_indices"], v["sparse_values"], modo)
                        filas.append({"question": it["Question"], "hit_any_at_k": harness.evaluar_item(it, hits, ch),
                                      "tokens_top10": int(ntok[hits].sum()), "top_ids": [ids[i] for i in hits]})
                    anc = [f for f in filas if f["hit_any_at_k"] is not None]
                    bloque[f"{modo}-{idioma}"] = {"respondibles": len(anc),
                                                  "aciertos": {str(k): sum(f["hit_any_at_k"][k] for f in anc) for k in harness.CORTES},
                                                  "tokens_top10_media": round(float(np.mean([f["tokens_top10"] for f in filas])), 1),
                                                  "preguntas": filas}
            out["colecciones"][col] = bloque
            print(nombre, col, {m: bloque[m]["aciertos"]["10"] for m in bloque if "-" in m}, flush=True)
        os.environ["VSE_IDIOMA"] = "es"
        (SALIDA / f"{nombre}.json").write_text(json.dumps(out, indent=1, ensure_ascii=False), encoding="utf-8")


def comparar(nombres):
    sufijos = sorted({brazo(n)[0] for n in ["w512", *nombres]})
    excluidas = sin_ancla(sufijos)
    datos = {n: json.loads((SALIDA / f"{n}.json").read_text(encoding="utf-8")) for n in ["w512", *nombres]}

    def hits(d, clave):
        # Clave = pregunta ES: la EN ocupa la misma posicion del eval-set y tiene las mismas anclas.
        out = {}
        for col, b in d["colecciones"].items():
            es = b[clave[:-3] + "-es"]["preguntas"]
            for p_es, p in zip(es, b[clave]["preguntas"]):
                if p["hit_any_at_k"] is not None and (col, p_es["question"]) not in excluidas:
                    out[(col, p_es["question"])] = (p["hit_any_at_k"]["10"], p["tokens_top10"])
        return out

    tabla = {"control": "w512", "excluidas_sin_ancla": [{"coleccion": c, "pregunta": q, "falta_en": f} for (c, q), f in excluidas.items()],
             "brazos": {}}
    for n in ["w512", *nombres]:
        fila = {}
        for clave in ("denso-es", "denso-en", "fusion-es", "fusion-en"):
            h, c = hits(datos[n], clave), hits(datos["w512"], clave)
            comunes = sorted(set(h) & set(c))
            fila[clave] = {"n": len(comunes), "hit10": sum(h[q][0] for q in comunes), "control_hit10": sum(c[q][0] for q in comunes),
                           "gana": sum(h[q][0] and not c[q][0] for q in comunes), "pierde": sum(c[q][0] and not h[q][0] for q in comunes),
                           "tokens_top10_media": round(float(np.mean([h[q][1] for q in comunes])), 1),
                           "control_tokens_top10_media": round(float(np.mean([c[q][1] for q in comunes])), 1),
                           "gana_preguntas": [q[1] for q in comunes if h[q][0] and not c[q][0]],
                           "pierde_preguntas": [q[1] for q in comunes if c[q][0] and not h[q][0]]}
        tabla["brazos"][n] = fila
    (SALIDA / "comparacion.json").write_text(json.dumps(tabla, indent=1, ensure_ascii=False), encoding="utf-8")
    print("excluidas por ancla:", len(excluidas))
    print(f"{'brazo':14} {'modo':9} {'n':>3} {'hit10':>5} {'ctrl':>4} {'gana':>4} {'pierde':>6} {'tok/top10':>9} {'ctrl tok':>8}")
    for n, fila in tabla["brazos"].items():
        for clave, m in fila.items():
            print(f"{n:14} {clave:9} {m['n']:>3} {m['hit10']:>5} {m['control_hit10']:>4} {m['gana']:>4} {m['pierde']:>6} "
                  f"{m['tokens_top10_media']:>9} {m['control_tokens_top10_media']:>8}")


def presupuesto(nombre):
    """Control del contrapeso: w512 (denso) alargado mas alla del top-10 hasta igualar, pregunta a pregunta,
    los tokens del top-10 de <nombre>. Si alcanza lo mismo, la ganancia es contexto, no fragmentacion."""
    tok = tokenizador()
    otro = json.loads((SALIDA / f"{nombre}.json").read_text(encoding="utf-8"))
    out = {"brazo": nombre, "control": "w512 denso con el mismo presupuesto de tokens por pregunta", "modos": {}}
    for idioma in ("es", "en"):
        os.environ["VSE_IDIOMA"] = idioma
        clave = f"denso-{idioma}"
        filas = []
        for col in harness.COLECCIONES:
            ch = harness.cargar_chunks(col)
            ids = [c["id"] for c in ch]
            ntok = longitudes(tok, [c["content"] for c in ch], especiales=False)
            docs = harness.normalizar(np.load(dir_emb("w512") / f"{col}.docs.npy")[:, :DIMS])
            items, _ = harness.cargar_preguntas(col)
            qd = harness.normalizar(np.load(dir_emb("w512") / f"{col}.queries{'-en' if idioma == 'en' else ''}.npy")[:, :DIMS])
            for n, (it, p) in enumerate(zip(items, otro["colecciones"][col][clave]["preguntas"])):
                orden = harness.ranking(docs @ qd[n], ids, len(ids), harness.MIN_SCORE)
                acum = np.cumsum(ntok[orden])
                k = int(np.searchsorted(acum, p["tokens_top10"]) + 1)  # chunks hasta igualar o pasar el presupuesto
                r = harness.evaluar_item(it, orden[:k], ch)
                if r is not None:
                    filas.append({"col": col, "question": it["Question"], "k": k, "w512_presupuesto": _hit(it, orden[:k], ch),
                                  "brazo_hit10": p["hit_any_at_k"]["10"]})
        g = sum(f["brazo_hit10"] and not f["w512_presupuesto"] for f in filas)
        pq = sum(f["w512_presupuesto"] and not f["brazo_hit10"] for f in filas)
        out["modos"][clave] = {"n": len(filas), "w512_presupuesto_hit": sum(f["w512_presupuesto"] for f in filas),
                               "brazo_hit10": sum(f["brazo_hit10"] for f in filas), "brazo_gana": g, "brazo_pierde": pq,
                               "k_medio_w512": round(float(np.mean([f["k"] for f in filas])), 1), "preguntas": filas}
        print(nombre, clave, {k: v for k, v in out["modos"][clave].items() if k != "preguntas"}, flush=True)
    os.environ["VSE_IDIOMA"] = "es"
    (SALIDA / f"presupuesto-{nombre}.json").write_text(json.dumps(out, indent=1, ensure_ascii=False), encoding="utf-8")


def _hit(item, hits, chunks):
    """Acierto en toda la lista (sin corte k): misma regla que harness.evaluar_item."""
    objetivos = item.get("SourceFiles") or ([item["SourceFile"]] if item.get("SourceFile") else [])
    obj = {o.lower() for o in objetivos}
    return any(a in chunks[i]["content"] for i in hits for a in item.get("TargetContentContains") or []
               if os.path.basename((chunks[i]["relative_path"] or "").replace("\\", "/")).lower() in obj)


def tei(nombre, base):
    """chunks/s de TEI: misma muestra que sidecar.medir (1 de cada 19), recorte en cliente a la ventana del brazo."""
    suf, ventana = brazo(nombre)
    tok = tokenizador()
    textos = []
    for col in harness.COLECCIONES:
        ch = harness.cargar_chunks(col + suf)
        textos += [ch[i]["enriched_content"] for i in range(0, len(ch), 19)]
    entradas = []
    for t in textos:
        ids = tok(t, add_special_tokens=False)["input_ids"]
        entradas.append(t if len(ids) <= ventana - 2 else tok.decode(ids[:ventana - 2]))
    n = longitudes(tok, entradas)
    orden = np.argsort(n, kind="stable")
    ruta = base.rstrip("/") + "/v1/embeddings"

    def post(lote):
        cuerpo = json.dumps({"input": lote, "model": "arctic"}).encode()
        req = urllib.request.Request(ruta, data=cuerpo, headers={"Content-Type": "application/json"})
        with urllib.request.urlopen(req, timeout=1800) as r:
            return json.loads(r.read())["data"]

    post([entradas[i] for i in orden[:16]])  # calentamiento
    t = time.perf_counter()
    for i in range(0, len(orden), 16):
        post([entradas[j] for j in orden[i:i + 16]])
    s = time.perf_counter() - t
    res = {"brazo": nombre, "ventana": ventana, "textos": len(entradas), "tokens": int(n.sum()), "max_tokens": int(n.max()),
           "s": round(s, 1), "chunks_por_s": round(len(entradas) / s, 2), "tokens_por_s": round(float(n.sum()) / s)}
    print(json.dumps(res), flush=True)
    SALIDA.mkdir(parents=True, exist_ok=True)
    (SALIDA / f"tei-{nombre}.json").write_text(json.dumps(res, indent=1), encoding="utf-8")


if __name__ == "__main__":
    cmd, *resto = sys.argv[1:]
    if cmd == "tokens":
        r = tokens(resto)
        SALIDA.mkdir(parents=True, exist_ok=True)
        (SALIDA / f"tokens{''.join(resto) or ''}.json").write_text(json.dumps(r, indent=1), encoding="utf-8")
    elif cmd == "embeber":
        for b in resto:
            embeber(b)
    elif cmd == "fidelidad":
        sys.exit(0 if fidelidad(resto[0]) else 1)
    elif cmd == "anclas":
        f = sin_ancla([("" if s == "512" else s) for s in resto])
        print(json.dumps([{"coleccion": c, "pregunta": q, "falta_en": v} for (c, q), v in f.items()], ensure_ascii=False, indent=1))
    elif cmd == "evaluar":
        evaluar(resto)
    elif cmd == "comparar":
        comparar(resto)
    elif cmd == "presupuesto":
        presupuesto(resto[0])
    elif cmd == "tei":
        tei(*resto)
