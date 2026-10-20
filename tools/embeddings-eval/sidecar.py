"""1.6: el mismo modelo servido por el sidecar (TEI o Infinity en Docker, CPU) frente a la referencia
sentence-transformers del harness: paridad numerica (coseno por chunk) y rendimiento. Es la arquitectura
destino de 1.8/1.4 (IVectorizationBrain como cliente HTTP de la ruta OpenAI-compatible).

Uso:
  python sidecar.py medir   <arm> <tei|infinity> <base_url> <modelo>
  python sidecar.py embeber <arm> <tei|infinity> <base_url> <modelo> <coleccion>...

El sidecar trunca a la ventana del modelo (8k-32k), no a la del harness: para que los vectores sean
comparables se recorta en el cliente a la misma ventana (max_len del arm, en tokens del propio
tokenizador). Normalizacion L2 del lado cliente (Infinity no renormaliza, ver 1.8).
"""
import json
import pathlib
import sys
import time
import urllib.request

import numpy as np

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parent))
import harness  # noqa: E402


def snapshot(repo):
    base = harness.RAIZ / ".cache" / "hf" / ("models--" + repo.replace("/", "--")) / "snapshots"
    return next(base.iterdir())


class Cliente:
    def __init__(self, arm, motor, base, modelo):
        from transformers import AutoTokenizer

        self.cfg = harness.ARMS[arm]
        self.ruta = base.rstrip("/") + ("/v1/embeddings" if motor == "tei" else "/embeddings")
        self.modelo = modelo
        snap = snapshot(self.cfg["repo"])
        self.tok = AutoTokenizer.from_pretrained(str(snap))
        st = snap / "config_sentence_transformers.json"
        self.prompts = json.loads(st.read_text(encoding="utf-8")).get("prompts", {}) if st.exists() else {}

    def prefijo(self, tipo):
        nombre = self.cfg.get(f"{tipo}_name")
        return self.prompts.get(nombre) if nombre in self.prompts else (self.cfg.get(tipo) or "")

    def recortar(self, texto, prefijo):
        # El prompt cuenta dentro de la ventana, igual que en sentence-transformers.
        ids = self.tok(prefijo + texto, add_special_tokens=False)["input_ids"]
        limite = self.cfg["max_len"] - 2
        return prefijo + texto if len(ids) <= limite else self.tok.decode(ids[:limite])

    def embeber(self, textos, tipo, lote=16):
        pre = self.prefijo(tipo)
        entradas = [self.recortar(t, pre) for t in textos]
        salida = []
        for i in range(0, len(entradas), lote):
            cuerpo = json.dumps({"input": entradas[i:i + lote], "model": self.modelo}).encode()
            req = urllib.request.Request(self.ruta, data=cuerpo, headers={"Content-Type": "application/json"})
            with urllib.request.urlopen(req, timeout=1800) as r:
                datos = sorted(json.loads(r.read())["data"], key=lambda d: d["index"])
            salida.extend(d["embedding"] for d in datos)
        return harness.normalizar(salida)


def medir(arm, motor, base, modelo):
    cli = Cliente(arm, motor, base, modelo)
    ref, textos = [], []
    for col in harness.COLECCIONES:
        npy = harness.CACHE / "emb" / arm / f"{col}.docs.npy"
        if not npy.exists():
            continue
        chunks = harness.cargar_chunks(col)
        st = np.load(npy)
        for i in range(0, len(chunks), 19):  # misma muestra que harness.muestra_costo
            textos.append(chunks[i]["enriched_content"])
            ref.append(st[i])
    cli.embeber(textos[:16], "d")  # calentamiento
    orden = sorted(range(len(textos)), key=lambda i: len(textos[i]))
    t = time.perf_counter()
    vec = cli.embeber([textos[i] for i in orden], "d")
    s = time.perf_counter() - t
    sc = np.empty_like(vec)
    sc[orden] = vec
    cos = np.sum(sc * np.asarray(ref, dtype=np.float32), axis=1)
    t = time.perf_counter()
    for _ in range(20):
        cli.embeber(["¿Qué se necesita para cerrar un ticket?"], "q")
    res = {"arm": arm, "motor": motor, "modelo": modelo, "textos": len(textos), "chunks_por_s": round(len(textos) / s, 2),
           "ms_consulta": round((time.perf_counter() - t) * 50, 1),
           "paridad_cos_min_p01_p50": [round(float(x), 5) for x in (cos.min(), np.percentile(cos, 1), np.median(cos))],
           "paridad_cos_lt_0_999": int((cos < 0.999).sum()),
           "peores_tokens_cos": [[len(cli.tok(textos[i])["input_ids"]), round(float(cos[i]), 4)] for i in np.argsort(cos)[:8]]}
    print(json.dumps(res), flush=True)
    harness.SALIDA.mkdir(parents=True, exist_ok=True)
    (harness.SALIDA / f"sidecar-{motor}-{arm}.json").write_text(json.dumps(res, indent=1), encoding="utf-8")


def embeber_colecciones(arm, motor, base, modelo, colecciones):
    """Completa los vectores de un arm via sidecar (solo si `medir` dio paridad ~1 con la referencia)."""
    cli = Cliente(arm, motor, base, modelo)
    dest = harness.CACHE / "emb" / arm
    meta_p = dest / "meta.json"
    meta = json.loads(meta_p.read_text(encoding="utf-8")) if meta_p.exists() else {"arm": arm, "colecciones": {}}
    for col in colecciones:
        chunks = harness.cargar_chunks(col)
        items, _ = harness.cargar_preguntas(col)
        orden = sorted(range(len(chunks)), key=lambda i: len(chunks[i]["enriched_content"]))
        t = time.perf_counter()
        docs = cli.embeber([chunks[i]["enriched_content"] for i in orden], "d")
        doc_s = time.perf_counter() - t
        matriz = np.empty_like(docs)
        matriz[orden] = docs
        qs = cli.embeber([i["Question"] for i in items], "q")
        np.save(dest / f"{col}.docs.npy", matriz)
        np.save(dest / f"{col}.queries.npy", qs)
        meta["colecciones"][col] = {"chunks": len(chunks), "doc_s": round(doc_s, 1), "chunks_por_s": round(len(chunks) / doc_s, 2),
                                    "preguntas": len(items), "via": f"sidecar {motor} ({modelo})"}
        print(arm, col, meta["colecciones"][col], flush=True)
    meta_p.write_text(json.dumps(meta, indent=2, ensure_ascii=False), encoding="utf-8")


if __name__ == "__main__":
    cmd, arm, motor, base, modelo, *cols = sys.argv[1:]
    if cmd == "medir":
        medir(arm, motor, base, modelo)
    else:
        embeber_colecciones(arm, motor, base, modelo, cols)
