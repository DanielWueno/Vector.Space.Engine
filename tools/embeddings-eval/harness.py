"""1.6: compara modelos de embeddings sobre los MISMOS chunks de Rag.Context.Engine.

Solo cambia el vector denso. El sparse (SparseTokenizer real), los chunks y la regla de acierto
son los de produccion:
  - chunks y sparse: exportados del Qdrant efimero que llena ingestar-control.ps1;
  - sparse de la consulta: QueryVectors (componente real, no reimplementado);
  - retrieval: reproduccion exacta de QdrantSemanticRetriever sin banda resumen
    (rama denso: exacto, coseno >= 0,1, 40 candidatos; rama sparse: 40; RRF nativa
    1/(rank+2) en f32, desempate por ChunkId ordinal; top 10) y modo denso-solo (top 10);
  - acierto: EvalCommand.Evaluate (archivo objetivo por nombre, ancla por subcadena ordinal).

Subcomandos:
  exportar                 baja chunks/sparse/denso-ONNX del Qdrant efimero a .cache/embeddings-eval
  descargar <arm>...       baja los pesos de cada modelo al cache local
  embeber <arm>...         calcula el denso de chunks y preguntas con el modelo del arm (sentence-transformers)
  traducir                 copias en ingles de los eval-sets (docs/evaluaciones/1.6/preguntas-en.json)
  evaluar <arm>...         recall por coleccion y modo; escribe docs/evaluaciones/1.6/resultados/<arm>.json
                           (<arm>@<d>: truncado Matryoshka; <arm>+en: consultas en ingles)
  fidelidad                compara el arm onnx-produccion con el `rag eval` real, pregunta por pregunta
"""
import hashlib
import json
import os
import pathlib
import sys
import time
import urllib.request

import numpy as np

RAIZ = pathlib.Path(__file__).resolve().parents[2]
CACHE = RAIZ / ".cache" / "embeddings-eval"
SALIDA = RAIZ / "docs" / "evaluaciones" / "1.6" / "resultados"
RAG = pathlib.Path(os.environ.get("RAG_CONTEXT_ENGINE", r"D:\Git\Rag.Context.Engine"))
QDRANT = "http://127.0.0.1:16333"  # efimero; nunca rag-qdrant
COLECCIONES = {
    "bsuite-auditorias-test": "bsuite-auditorias.eval-set.json",
    "innovapp-docs": "innovapp-docs.eval-set.json",
    "micro-repo": "tickets-microservice.eval-set.json",
}
TOP_K, PREFETCH, MIN_SCORE = 10, 40, np.float32(0.10)
CORTES = (1, 3, 5, 10)

# Arms: modelo, ventana y prompts. Los prompts salen de la model card / config de
# sentence-transformers de cada modelo (ver decision.md, paso 0); None = sin prompt.
ARMS = {
    "minilm-st": dict(repo="sentence-transformers/paraphrase-multilingual-MiniLM-L12-v2", max_len=256),
    "minilm-128": dict(repo="sentence-transformers/paraphrase-multilingual-MiniLM-L12-v2", max_len=128),
    "e5-small": dict(repo="intfloat/multilingual-e5-small", max_len=512, q="query: ", d="passage: "),
    "granite-97m-r2": dict(repo="ibm-granite/granite-embedding-97m-multilingual-r2", max_len=512),
    "granite-311m-r2": dict(repo="ibm-granite/granite-embedding-311m-multilingual-r2", max_len=512),
    # Model card: en CPU sin xformers, desactivar la atencion eficiente y el unpadding.
    "arctic-m-v2": dict(repo="Snowflake/snowflake-arctic-embed-m-v2.0", max_len=512, q_name="query", remote=True,
                        config_kwargs={"use_memory_efficient_attention": False, "unpad_inputs": False}),
    "nomic-v2-moe": dict(repo="nomic-ai/nomic-embed-text-v2-moe", max_len=512, q_name="query", d_name="passage", remote=True),
    "bge-m3": dict(repo="BAAI/bge-m3", max_len=512),
    "qwen3-0.6b": dict(repo="Qwen/Qwen3-Embedding-0.6B", max_len=512, q_name="query"),
    "gemma2": dict(repo="google/embeddinggemma-2", max_len=512, q_name="query", d_name="document"),
    "jina-v2-code": dict(repo="jinaai/jina-embeddings-v2-base-code", max_len=512, remote=True, solo=["micro-repo"]),
}


def qdrant(ruta, cuerpo=None):
    req = urllib.request.Request(QDRANT + ruta, data=None if cuerpo is None else json.dumps(cuerpo).encode(),
                                 headers={"Content-Type": "application/json"})
    with urllib.request.urlopen(req, timeout=120) as r:
        return json.loads(r.read())["result"]


def exportar():
    CACHE.mkdir(parents=True, exist_ok=True)
    for col in COLECCIONES:
        filas, offset = [], None
        while True:
            cuerpo = {"limit": 256, "with_payload": ["relative_path", "content", "enriched_content", "content_hash", "is_manifest"],
                      "with_vector": ["dense", "sparse-code"]}
            if offset is not None:
                cuerpo["offset"] = offset
            r = qdrant(f"/collections/{col}/points/scroll", cuerpo)
            for p in r["points"]:
                if p["payload"].get("is_manifest"):
                    continue
                sp = p["vector"].get("sparse-code", {"indices": [], "values": []})
                filas.append({"id": str(p["id"]), **{k: p["payload"].get(k) for k in ("relative_path", "content", "enriched_content", "content_hash")},
                              "sparse_indices": sp["indices"], "sparse_values": sp["values"], "onnx_dense": p["vector"]["dense"]})
            offset = r.get("next_page_offset")
            if offset is None:
                break
        filas.sort(key=lambda f: f["id"])
        with open(CACHE / f"chunks-{col}.jsonl", "w", encoding="utf-8") as f:
            for fila in filas:
                f.write(json.dumps(fila, ensure_ascii=False) + "\n")
        print(col, len(filas), "chunks")


def cargar_chunks(col):
    with open(CACHE / f"chunks-{col}.jsonl", encoding="utf-8") as f:
        return [json.loads(l) for l in f]


def idioma():
    """VSE_IDIOMA=en: preguntas traducidas (docs/evaluaciones/1.6/preguntas-en.json), mismas anclas."""
    return os.environ.get("VSE_IDIOMA", "es")


def traducir():
    trad = json.loads((RAIZ / "docs" / "evaluaciones" / "1.6" / "preguntas-en.json").read_text(encoding="utf-8"))
    dest = CACHE / "eval-en"
    dest.mkdir(parents=True, exist_ok=True)
    for eval_set in COLECCIONES.values():
        items = json.loads((RAG / "docs" / "eval" / eval_set).read_text(encoding="utf-8"))
        for i in items:
            i["Question"] = trad[eval_set][i["Question"]]  # KeyError si falta una traduccion
        (dest / eval_set).write_text(json.dumps(items, ensure_ascii=False, indent=1), encoding="utf-8")
        print(eval_set, len(items), "->", dest / eval_set)


def cargar_preguntas(col):
    eval_set = COLECCIONES[col]
    en = idioma() == "en"
    origen = (CACHE / "eval-en" / eval_set) if en else (RAG / "docs" / "eval" / eval_set)
    items = json.loads(origen.read_text(encoding="utf-8"))
    vecs = {}
    with open(CACHE / ("query-vectors-en.jsonl" if en else "query-vectors.jsonl"), encoding="utf-8") as f:
        for l in f:
            v = json.loads(l)
            if v["eval_set"] == eval_set:
                vecs[v["question"]] = v
    return items, [vecs[i["Question"]] for i in items]


def sha_eval_set(col):
    return hashlib.sha256((RAG / "docs" / "eval" / COLECCIONES[col]).read_bytes()).hexdigest()


def normalizar(m):
    m = np.asarray(m, dtype=np.float32)
    return m / np.linalg.norm(m, axis=1, keepdims=True)


def embeber(arm):
    import psutil
    import torch
    import truststore

    # Descarga desde el host con el almacen de Windows (inspeccion TLS corporativa); sin desactivar TLS.
    truststore.inject_into_ssl()
    from sentence_transformers import SentenceTransformer

    cfg = ARMS[arm]
    t0 = time.perf_counter()
    modelo = SentenceTransformer(cfg["repo"], device="cpu", trust_remote_code=cfg.get("remote", False),
                                 cache_folder=str(RAIZ / ".cache" / "hf"), config_kwargs=cfg.get("config_kwargs"))
    # Ventana declarada por arm, no la del config de sentence-transformers: MiniLM trae 128 alli,
    # pero produccion (OnnxBrainOptions) usa 256; heredarla en silencio mide otro modelo.
    modelo.max_seq_length = cfg["max_len"]
    carga_s = time.perf_counter() - t0
    proc = psutil.Process()
    dest = CACHE / "emb" / arm
    dest.mkdir(parents=True, exist_ok=True)
    meta = {"arm": arm, "repo": cfg["repo"], "max_seq_length": modelo.max_seq_length, "carga_s": round(carga_s, 1),
            "prompts_modelo": getattr(modelo, "prompts", {}), "torch_threads": torch.get_num_threads(),
            "dim": modelo.get_sentence_embedding_dimension(), "colecciones": {}}

    def codificar(textos, prefijo=None, nombre=None):
        kw = {"batch_size": 16, "normalize_embeddings": True, "convert_to_numpy": True, "show_progress_bar": False}
        if nombre and nombre in (modelo.prompts or {}):
            kw["prompt_name"] = nombre
        elif prefijo:
            textos = [prefijo + t for t in textos]
        return modelo.encode(textos, **kw).astype(np.float32)

    # VSE_COLECCIONES=a,b: completa solo esas (conserva el meta de las ya calculadas).
    solo = [c for c in os.environ.get("VSE_COLECCIONES", "").split(",") if c]
    if solo and (dest / "meta.json").exists():
        meta["colecciones"] = json.loads((dest / "meta.json").read_text(encoding="utf-8"))["colecciones"]
    if idioma() == "en":
        # Solo consultas: los chunks no cambian de idioma, se reutilizan sus vectores.
        for col in solo or cfg.get("solo", COLECCIONES):
            items, _ = cargar_preguntas(col)
            np.save(dest / f"{col}.queries-en.npy", codificar([i["Question"] for i in items], cfg.get("q"), cfg.get("q_name")))
            print(arm, col, "consultas en ingles:", len(items), flush=True)
        return
    for col in solo or cfg.get("solo", COLECCIONES):
        chunks = cargar_chunks(col)
        items, _ = cargar_preguntas(col)
        # Orden por longitud: lotes homogeneos (menos padding), resultado reordenado al final.
        orden = sorted(range(len(chunks)), key=lambda i: len(chunks[i]["enriched_content"]))
        t = time.perf_counter()
        docs = codificar([chunks[i]["enriched_content"] for i in orden], cfg.get("d"), cfg.get("d_name"))
        doc_s = time.perf_counter() - t
        matriz = np.empty_like(docs)
        matriz[orden] = docs
        t = time.perf_counter()
        qs = codificar([i["Question"] for i in items], cfg.get("q"), cfg.get("q_name"))
        q_s = time.perf_counter() - t
        np.save(dest / f"{col}.docs.npy", matriz)
        np.save(dest / f"{col}.queries.npy", qs)
        meta["colecciones"][col] = {"chunks": len(chunks), "doc_s": round(doc_s, 1), "chunks_por_s": round(len(chunks) / doc_s, 2),
                                    "preguntas": len(items), "ms_por_pregunta": round(1000 * q_s / len(items), 1)}
        print(arm, col, meta["colecciones"][col], flush=True)
    meta["rss_pico_mib"] = round(proc.memory_info().peak_wset / 2**20) if hasattr(proc.memory_info(), "peak_wset") else None
    (dest / "meta.json").write_text(json.dumps(meta, indent=2, ensure_ascii=False), encoding="utf-8")


def descargar(arms):
    """Baja cada modelo antes de medir (pesos safetensors/bin y codigo remoto; sin onnx/openvino)."""
    import truststore

    truststore.inject_into_ssl()
    from huggingface_hub import snapshot_download

    for arm in arms:
        ruta = snapshot_download(ARMS[arm]["repo"], cache_dir=str(RAIZ / ".cache" / "hf"),
                                 ignore_patterns=["onnx/*", "openvino/*", "*.onnx", "*.gguf", "*.h5", "*.msgpack", "*.ot"])
        print(arm, "->", ruta, flush=True)


def ranking(scores, ids, limite, umbral=None):
    """Orden de DeterministicVectorQuery/RankingOrder: score desc, ChunkId ordinal asc."""
    cand = np.arange(len(ids)) if umbral is None else np.nonzero(scores >= umbral)[0]
    orden = sorted(cand.tolist(), key=lambda i: (-float(scores[i]), ids[i]))
    return orden[:limite]


def sparse_scores(chunks, q_idx, q_val):
    q = dict(zip(q_idx, q_val))
    s = np.zeros(len(chunks), dtype=np.float32)
    toca = np.zeros(len(chunks), dtype=bool)
    for n, c in enumerate(chunks):
        acc = np.float32(0)
        for i, v in zip(c["sparse_indices"], c["sparse_values"]):
            w = q.get(i)
            if w is not None:
                acc += np.float32(v) * np.float32(w)
                toca[n] = True
        s[n] = acc
    return s, toca


def fusion_nativa(dense, sparse, ids):
    pts = {}
    for rama in (dense, sparse):
        for rank, i in enumerate(rama):
            pts[i] = np.float32(pts.get(i, np.float32(0)) + np.float32(1) / np.float32(rank + 2))
    return sorted(pts, key=lambda i: (-float(pts[i]), ids[i]))[:TOP_K]


def evaluar_item(item, hits, chunks):
    objetivos = item.get("SourceFiles") or ([item["SourceFile"]] if item.get("SourceFile") else [])
    anclas = item.get("TargetContentContains") or []
    if not objetivos or not anclas:
        return None
    obj = {o.lower() for o in objetivos}
    res = {}
    for k in CORTES:
        win = [chunks[i] for i in hits[:k] if os.path.basename((chunks[i]["relative_path"] or "").replace("\\", "/")).lower() in obj]
        res[k] = any(a in c["content"] for a in anclas for c in win)
    return res


def recuperar(chunks, ids, docs, q_dense, q_sp_idx, q_sp_val, modo):
    dscore = docs @ q_dense
    if modo == "denso":
        return ranking(dscore, ids, TOP_K, MIN_SCORE)
    dense = ranking(dscore, ids, PREFETCH, MIN_SCORE)
    ss, toca = sparse_scores(chunks, q_sp_idx, q_sp_val)
    ss = np.where(toca, ss, -np.inf).astype(np.float32)
    sparse = [i for i in ranking(ss, ids, PREFETCH) if toca[i]]
    return fusion_nativa(dense, sparse, ids)


def evaluar(arms):
    SALIDA.mkdir(parents=True, exist_ok=True)
    for arm in arms:
        # "<arm>@<d>": mismos vectores truncados a d dimensiones; "+en": consultas en ingles.
        nombre, en = (arm[:-3], True) if arm.endswith("+en") else (arm, False)
        os.environ["VSE_IDIOMA"] = "en" if en else "es"
        base, _, d = nombre.partition("@")
        dims = int(d) if d else None
        out = {"arm": arm, "modelo": "onnx-produccion (MiniLM fp32 x64, vectores de la ingesta real)" if base == "onnx-produccion" else ARMS[base]["repo"],
               "dimensiones": dims,
               "protocolo": {"top_k": TOP_K, "prefetch": PREFETCH, "min_score_denso": float(MIN_SCORE), "bandas": "denso+sparse-code, sin resumen"},
               "colecciones": {}}
        meta = CACHE / "emb" / base / "meta.json"
        if base != "onnx-produccion" and meta.exists():
            out["embeber"] = json.loads(meta.read_text(encoding="utf-8"))
        for col in (COLECCIONES if base == "onnx-produccion" else ARMS[base].get("solo", COLECCIONES)):
            chunks = cargar_chunks(col)
            ids = [c["id"] for c in chunks]
            items, qv = cargar_preguntas(col)
            if base == "onnx-produccion":
                docs = normalizar([c["onnx_dense"] for c in chunks])
                qd = normalizar([v["onnx_dense"] for v in qv])
            elif not (CACHE / "emb" / base / f"{col}.docs.npy").exists():
                out["colecciones"][col] = {"pendiente": "sin vectores calculados"}
                print(arm, col, "PENDIENTE: sin vectores", flush=True)
                continue
            else:
                docs = np.load(CACHE / "emb" / base / f"{col}.docs.npy")
                qd = np.load(CACHE / "emb" / base / f"{col}.queries{'-en' if en else ''}.npy")
                if dims:
                    # Matryoshka = truncar + L2 (lo mismo que hacen TEI/Infinity con 'dimensions', ver 1.8).
                    docs, qd = normalizar(docs[:, :dims]), normalizar(qd[:, :dims])
            bloque = {"eval_set_sha256": sha_eval_set(col), "chunks": len(chunks)}
            for modo in ("denso", "fusion"):
                filas = []
                for n, (item, v) in enumerate(zip(items, qv)):
                    hits = recuperar(chunks, ids, docs, qd[n], v["sparse_indices"], v["sparse_values"], modo)
                    r = evaluar_item(item, hits, chunks)
                    filas.append({"question": item["Question"], "category": item["Category"],
                                  "hit_any_at_k": r, "top_ids": [ids[i] for i in hits]})
                anc = [f for f in filas if f["hit_any_at_k"] is not None]
                bloque[modo] = {"respondibles": len(anc),
                                "aciertos": {str(k): sum(f["hit_any_at_k"][k] for f in anc) for k in CORTES},
                                "preguntas": filas}
            out["colecciones"][col] = bloque
            print(arm, col, {m: bloque[m]["aciertos"] for m in ("denso", "fusion")}, "de", bloque["denso"]["respondibles"], flush=True)
        (SALIDA / f"{arm}.json").write_text(json.dumps(out, indent=1, ensure_ascii=False), encoding="utf-8")


def fidelidad():
    """El arm onnx-produccion debe reproducir el `rag eval` real (fusion de dos bandas)."""
    arm = json.loads((SALIDA / "onnx-produccion.json").read_text(encoding="utf-8"))
    total = iguales_hit = iguales_top = 0
    for col in COLECCIONES:
        raw = (CACHE / f"rag-eval-{col}.json").read_text(encoding="utf-8-sig")
        real = json.loads(raw[raw.index("{"):])
        nuestro = {f["question"]: f for f in arm["colecciones"][col]["fusion"]["preguntas"]}
        for r in real["results"]:
            total += 1
            f = nuestro[r["question"]]
            hit_real = {int(k): v for k, v in r["hit_any_at_k"].items()} or None
            hit_nuestro = {int(k): v for k, v in f["hit_any_at_k"].items()} if f["hit_any_at_k"] else None
            iguales_hit += (hit_real == hit_nuestro)
            ids_real = [h["chunk_id"] for h in (r.get("hits") or [])][:TOP_K]
            iguales_top += (ids_real == f["top_ids"])
            if ids_real != f["top_ids"]:
                print(f"  difiere top-10 [{col}] {r['question'][:60]}")
    print(f"fidelidad: hit_any identico {iguales_hit}/{total}; top-10 identico {iguales_top}/{total}")
    return iguales_hit == total


if __name__ == "__main__":
    cmd, *resto = sys.argv[1:]
    if cmd == "exportar":
        exportar()
    elif cmd == "traducir":
        traducir()
    elif cmd == "descargar":
        descargar(resto)
    elif cmd == "embeber":
        for a in resto:
            embeber(a)
    elif cmd == "evaluar":
        evaluar(resto)
    elif cmd == "fidelidad":
        sys.exit(0 if fidelidad() else 1)
