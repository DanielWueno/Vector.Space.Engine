"""1.24: resumen de negocio anadido al chunk (un solo vector) frente a espacio resumen separado fusionado con RRF.

Protocolo fijado antes de generar: docs/evaluaciones/1.24/protocolo.md.

Subcomandos:
  generar              resumenes con qwen3.8-27b-fast (peticion de LlmResumenBrain) para los chunks c1024 de los tres
                       corpus; reanudable; tope duro de total_tokens (VSE_TOPE_TOKENS, 4 000 000 por defecto)
  embeber              arctic@256 (TEI local) de los resumenes solos para el brazo B y del texto con resumen para C;
                       control de paridad TEI frente a los vectores de A. Enmienda del 2026-11-12 (protocolo.md): B usaba
                       bge-m3, descartado por diseno en 1.6/1.7.3; ahora usa el mismo modelo y consultas que A
  evaluar              brazos A, B, C y D: hit@10 pareado, tokens del top-10, proporcion de chunks tecnicos

Credenciales: ALTHEIA_KEY y ALTHEIA_CERT solo desde el entorno; la clave no se imprime ni se guarda, y del cuerpo de
error del servidor solo se guarda el codigo (su mensaje puede citar la clave). Los resumenes (texto de negocio de
codigo privado) quedan en .cache/embeddings-eval/resumenes/ (ignorado por git); al repo solo van metricas.
"""
import concurrent.futures as cf
import hashlib
import json
import os
import pathlib
import re
import ssl
import sys
import threading
import time
import urllib.error
import urllib.request

import numpy as np

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parent))
import harness  # noqa: E402

SUF = "-c1024"
RES_DIR = harness.CACHE / "resumenes"
EMB_B = harness.CACHE / "emb" / "arctic-resumen-solo-1.24"
EMB_A = harness.CACHE / "emb" / "arctic-c1024-w8192"
EMB_Q = harness.CACHE / "emb" / "arctic-m-v2"
EMB_C = harness.CACHE / "emb" / "arctic-c1024-resumen-1.24"
SALIDA = harness.RAIZ / "docs" / "evaluaciones" / "1.24" / "resultados"
FUENTE_PROMPT = harness.RAIZ / "src" / "Reyma.VectorSpace.Engine.Microservice.Infrastructure" / "Resumen" / "LlmResumenBrain.cs"

ALTHEIA = os.environ.get("ALTHEIA_URL", "https://altheia/v1")
TEI = os.environ.get("VSE_TEI", "http://127.0.0.1:18080")
MODELO, MAX_TOKENS, TEMPERATURA, TOP_P, ENABLE_THINKING = "qwen3.8-27b-fast", 160, 0.15, 0.95, False
CENTINELA = "SIN_CONTENIDO_DE_NEGOCIO"
TOPE = int(os.environ.get("VSE_TOPE_TOKENS", "4000000"))
DIMS, POOL, RRF_K = 256, 100, 60
LENGUAJES = {".cs": "CSharp", ".ts": "TypeScript", ".tsx": "TypeScript", ".js": "JavaScript", ".jsx": "JavaScript",
             ".xaml": "Xaml", ".sql": "Sql", ".md": "Markdown", ".txt": "PlainText", ".json": "PlainText",
             ".xml": "PlainText", ".csproj": "PlainText"}

# Filtro previo por defecto de 1.27 (FiltroPrevioOptions), para el brazo D y la metrica de ruido.
RUTAS_TECNICAS = [re.compile(p) for p in (
    r"(?i)\.designer\.cs$", r"(?i)\.g\.cs$", r"(?i)\.generated\.cs$", r"(?i)(^|/)AssemblyInfo\.cs$", r"(?i)\.csproj$",
    r"(?i)\.json$", r"(?i)\.xml$", r"(?i)(^|/)RepocitorioReportes/Rpt[^/]*(?<!\.aspx)(?<!\.aspx\.designer)\.cs$")]
ACCESORES = re.compile(r"\{\s*get\s*;\s*((public|private|protected|internal)\s+)*((set|init)\s*;\s*)?\}")


# ---------------------------------------------------------------------------------------------- comunes

def prompt_sistema():
    """Literal de LlmResumenBrain.SystemPrompt con LF y la sangria comun quitada (como el experimento del 2026-11-11)."""
    src = FUENTE_PROMPT.read_text(encoding="utf-8").replace("\r\n", "\n")
    i0 = src.index('SystemPrompt = """') + len('SystemPrompt = """') + 1
    i1 = src.index('        """;', i0)
    lineas = src[i0:i1].rstrip("\n").split("\n")
    return "\n".join(l[8:] if len(l) >= 8 else l.lstrip() for l in lineas)


def prompt_version(sistema):
    entrada = "|".join(["v2", MODELO, str(ENABLE_THINKING).lower(), str(MAX_TOKENS), repr(TEMPERATURA), repr(TOP_P), sistema])
    return hashlib.sha256(entrada.encode("utf-8")).hexdigest()[:8]


def lenguaje(ruta):
    return LENGUAJES.get(os.path.splitext(ruta)[1].lower(), "PlainText")


def tipo_de_chunk(c):
    enr, ruta = c["enriched_content"] or "", c["relative_path"] or ""
    if ruta.lower().endswith(".md"):
        return "DocumentSection"
    if not ruta.lower().endswith(".cs"):
        return "PlainTextWindow"
    for marca, tipo in (("\n// constructor:", "Constructor"), ("\n// method:", "Method"), ("\n// Properties", "Property")):
        if marca in enr:
            return tipo
    return "Class"


def tecnico(c):
    """Motivo del filtro previo de 1.27 (por defecto) o None."""
    ruta = (c["relative_path"] or "").replace("\\", "/")
    if any(p.search(ruta) for p in RUTAS_TECNICAS):
        return "ruta"
    tipo = tipo_de_chunk(c)
    if tipo == "Constructor":
        return "tipo"
    if tipo == "Property":
        codigo = [l.strip() for l in c["content"].split("\n") if l.strip() and not l.strip().startswith(("//", "["))][:2]
        if len(codigo) == 1 and "=>" not in codigo[0] and ACCESORES.search(codigo[0]):
            return "propiedad-automatica"
    if any("<auto-generated" in l.lower() for l in c["content"].split("\n")[:5]):
        return "auto-generado"
    return None


def credenciales():
    clave, cert = os.environ.get("ALTHEIA_KEY"), os.environ.get("ALTHEIA_CERT")
    if not clave or not cert or not os.path.isfile(cert):
        sys.exit("Faltan ALTHEIA_KEY o ALTHEIA_CERT en el entorno.")
    return clave, ssl.create_default_context(cafile=cert)


def post_json(url, cuerpo, cabeceras, contexto=None, timeout=180):
    req = urllib.request.Request(url, data=json.dumps(cuerpo).encode("utf-8"),
                                 headers={"Content-Type": "application/json", **cabeceras})
    try:
        with urllib.request.urlopen(req, timeout=timeout, context=contexto) as r:
            return r.status, json.loads(r.read())
    except urllib.error.HTTPError as e:
        try:
            cuerpo_error = json.loads(e.read())
            codigo = (cuerpo_error.get("error") or {}).get("code") or (cuerpo_error.get("error") or {}).get("type")
        except Exception:
            codigo = None
        return e.code, {"_codigo_error": codigo}


def cargar_resumenes(col):
    ruta = RES_DIR / f"resumenes-{col}.jsonl"
    filas = {}
    if ruta.exists():
        for l in ruta.read_text(encoding="utf-8").splitlines():
            r = json.loads(l)
            if r.get("final"):
                filas[r["id"]] = r
    return filas


def resumen_util(r):
    t = (r or {}).get("texto") or ""
    return t if t and r.get("http") == 200 and not t.startswith(CENTINELA) else None


# ---------------------------------------------------------------------------------------------- generar

def generar():
    sistema = prompt_sistema()
    pv = prompt_version(sistema)
    clave, contexto = credenciales()
    cab = {"Authorization": f"Bearer {clave}"}
    RES_DIR.mkdir(parents=True, exist_ok=True)
    candado = threading.Lock()
    estado = {"tokens": 0, "llamadas": 0, "vacias": 0, "parar": None}
    for col in harness.COLECCIONES:
        estado["tokens"] += sum(r.get("total_tokens") or 0 for r in cargar_resumenes(col).values())
    print(f"PromptVersion v2 {pv}; tokens ya gastados en corridas previas: {estado['tokens']}; tope {TOPE}", flush=True)

    def llamar(col, c, salida):
        for intento in (1, 2):
            with candado:
                if estado["parar"] or estado["tokens"] >= TOPE:
                    estado["parar"] = estado["parar"] or "tope de tokens"
                    return
            usuario = f"[Contexto: Lenguaje: {lenguaje(c['relative_path'])}, Ruta: {c['relative_path']}]\n\n{c['content']}"
            cuerpo = {"model": MODELO, "messages": [{"role": "system", "content": sistema}, {"role": "user", "content": usuario}],
                      "temperature": TEMPERATURA, "top_p": TOP_P, "max_tokens": MAX_TOKENS,
                      "chat_template_kwargs": {"enable_thinking": ENABLE_THINKING}}
            t0 = time.perf_counter()
            try:
                http, j = post_json(f"{ALTHEIA}/chat/completions", cuerpo, cab, contexto)
            except Exception as ex:  # red o tiempo: sin detalle (no hay cabeceras en el mensaje, pero se acota igual)
                http, j = 0, {"_codigo_error": type(ex).__name__}
            ms = int((time.perf_counter() - t0) * 1000)
            uso = j.get("usage") or {}
            texto = ((j.get("choices") or [{}])[0].get("message") or {}).get("content") if http == 200 else None
            texto = (texto or "").strip()
            ok = http == 200 and texto != ""
            fila = {"id": c["id"], "modelo": MODELO, "prompt_version": pv, "http": http, "ms": ms,
                    "prompt_tokens": uso.get("prompt_tokens"), "completion_tokens": uso.get("completion_tokens"),
                    "total_tokens": uso.get("total_tokens") or 0,
                    "finish_reason": ((j.get("choices") or [{}])[0]).get("finish_reason") if http == 200 else None,
                    "texto": texto if http == 200 else None, "error": j.get("_codigo_error"),
                    "intento": intento, "final": ok or intento == 2}
            with candado:
                estado["tokens"] += fila["total_tokens"]
                estado["llamadas"] += 1
                estado["vacias"] += http == 200 and texto == ""
                salida.write(json.dumps(fila, ensure_ascii=False) + "\n")
                salida.flush()
                if estado["llamadas"] >= 20 and estado["vacias"] * 2 > estado["llamadas"]:
                    estado["parar"] = "mas de la mitad de respuestas vacias"
                if http in (401, 403):
                    estado["parar"] = f"HTTP {http} ({fila['error']})"
            if ok:
                return
            if http == 429:
                time.sleep(10)

    for col in harness.COLECCIONES:
        hechos = cargar_resumenes(col)
        pendientes = [c for c in harness.cargar_chunks(col + SUF) if c["id"] not in hechos]
        print(col, "pendientes", len(pendientes), "de", len(hechos) + len(pendientes), flush=True)
        t0 = time.perf_counter()
        with open(RES_DIR / f"resumenes-{col}.jsonl", "a", encoding="utf-8") as salida, cf.ThreadPoolExecutor(4) as pool:
            futuros = [pool.submit(llamar, col, c, salida) for c in pendientes]
            for k, f in enumerate(cf.as_completed(futuros), start=1):
                f.result()
                if k % 200 == 0:
                    print(f"  {col}: {k}/{len(pendientes)}  tokens acumulados {estado['tokens']}  "
                          f"{time.perf_counter() - t0:.0f} s", flush=True)
        if estado["parar"]:
            break
    print("fin:", estado, flush=True)


# ---------------------------------------------------------------------------------------------- embeber

def arctic_tei(textos, parcial=None):
    """Lotes de VSE_TEI_LOTE textos (4 por defecto: la memoria de TEI crece con textos largos). Con `parcial`, guarda un
    punto de control cada 64 textos y retoma desde el si el proceso se corto (el orden de `textos` es determinista)."""
    lote = int(os.environ.get("VSE_TEI_LOTE", "4"))
    vecs = list(np.load(parcial)) if parcial is not None and parcial.exists() else []
    if vecs:
        print(f"  retoma {parcial.name}: {len(vecs)}/{len(textos)}", flush=True)
    for i in range(len(vecs), len(textos), lote):
        http, j = post_json(f"{TEI}/embed", {"inputs": textos[i:i + lote], "truncate": True, "normalize": True}, {}, timeout=600)
        if http != 200:
            sys.exit(f"TEI HTTP {http}")
        vecs.extend(np.asarray(j, dtype=np.float32)[:, :DIMS])
        if parcial is not None and (len(vecs) % 64 < lote or len(vecs) == len(textos)):
            np.save(parcial, np.asarray(vecs, dtype=np.float32))
    return harness.normalizar(np.asarray(vecs, dtype=np.float32)[:, :DIMS])


def texto_c(c, resumen):
    return c["enriched_content"] + "\n// Resumen: " + resumen


def embeber():
    EMB_B.mkdir(parents=True, exist_ok=True)
    EMB_C.mkdir(parents=True, exist_ok=True)
    meta = {"b": "resumen solo, arctic-m-v2.0 por TEI local, 256 + L2; consultas = las de A",
            "c": "enriched_content + resumen, arctic-m-v2.0 por TEI local, 256 + L2", "colecciones": {}}
    for col in harness.COLECCIONES:
        chunks = harness.cargar_chunks(col + SUF)
        res = cargar_resumenes(col)
        con = [i for i, c in enumerate(chunks) if resumen_util(res.get(c["id"]))]
        # B: espacio resumen con el modelo del perfil de diseno (arctic@256); las consultas son las mismas de A
        if not (EMB_B / f"{col}.npz").exists():
            vb = arctic_tei([resumen_util(res[chunks[i]["id"]]) for i in con], EMB_B / f"{col}.parcial.npy")
            np.savez(EMB_B / f"{col}.npz", indices=np.array(con, dtype=int), docs=vb)
        # C: texto con resumen por TEI; resto = vector de A
        info = {"chunks": len(chunks), "con_resumen": len(con)}
        if not (EMB_C / f"{col}.npz").exists():
            t0 = time.perf_counter()
            vc = arctic_tei([texto_c(chunks[i], resumen_util(res[chunks[i]["id"]])) for i in con], EMB_C / f"{col}.parcial.npy")
            info["s_tei"] = round(time.perf_counter() - t0, 1)
            np.savez(EMB_C / f"{col}.npz", indices=np.array(con, dtype=int), docs=vc)
        # paridad: 64 chunks sin cambios re-embebidos por TEI frente a A
        a = harness.normalizar(np.load(EMB_A / f"{col}.docs.npy")[:, :DIMS])
        sin = [i for i in range(len(chunks)) if i not in set(con)]
        ctrl = sin[::max(1, len(sin) // 64)][:64]
        cos = np.sum(arctic_tei([chunks[i]["enriched_content"] for i in ctrl]) * a[ctrl], axis=1)
        info["paridad_tei_cos_min"] = round(float(cos.min()), 6)
        info["paridad_tei_cos_media"] = round(float(cos.mean()), 6)
        meta["colecciones"][col] = info
        print(col, info, flush=True)
    (EMB_C / "meta.json").write_text(json.dumps(meta, indent=1), encoding="utf-8")


# ---------------------------------------------------------------------------------------------- evaluar

def evaluar():
    from tokenizers import Tokenizer

    tok = Tokenizer.from_file(str(next((harness.RAIZ / ".cache" / "hf" / "models--Snowflake--snowflake-arctic-embed-m-v2.0"
                                        / "snapshots").iterdir()) / "tokenizer.json"))
    tok.no_truncation()
    tok.no_padding()
    meta = json.loads((EMB_C / "meta.json").read_text(encoding="utf-8"))
    paridad_ok = all(v["paridad_tei_cos_min"] >= 0.999 for v in meta["colecciones"].values())
    out = {"protocolo": "docs/evaluaciones/1.24/protocolo.md", "paridad_tei_ok": paridad_ok, "embeber": meta,
           "generacion": {}, "colecciones": {}}
    for col in harness.COLECCIONES:
        chunks = harness.cargar_chunks(col + SUF)
        ids = [c["id"] for c in chunks]
        res = cargar_resumenes(col)
        filas_res = list(res.values())
        out["generacion"][col] = {
            "chunks": len(chunks), "procesados": len(res),
            "con_resumen": sum(resumen_util(r) is not None for r in filas_res),
            "centinela": sum((r.get("texto") or "").startswith(CENTINELA) for r in filas_res),
            "vacios_o_error": sum(not (r.get("texto") or "") for r in filas_res),
            "total_tokens": sum(r.get("total_tokens") or 0 for r in filas_res),
            "prompt_versions": sorted({r.get("prompt_version") for r in filas_res}),
            "ms_total": sum(r.get("ms") or 0 for r in filas_res)}
        ntok = np.array([len(e.ids) for e in tok.encode_batch([c["content"] for c in chunks], add_special_tokens=False)])
        es_tecnico = np.array([tecnico(c) is not None for c in chunks])
        a = harness.normalizar(np.load(EMB_A / f"{col}.docs.npy")[:, :DIMS])
        zc = np.load(EMB_C / f"{col}.npz")
        c_docs = a.copy()
        c_docs[zc["indices"]] = zc["docs"]
        d_docs = c_docs.copy()
        revertir = [i for i in zc["indices"] if es_tecnico[i]]
        d_docs[revertir] = a[revertir]
        zb = np.load(EMB_B / f"{col}.npz")
        out["generacion"][col]["d_revertidos_por_filtro"] = len(revertir)
        bloque = {}
        for idioma in ("es", "en"):
            os.environ["VSE_IDIOMA"] = idioma
            items, _ = harness.cargar_preguntas(col)
            qa = harness.normalizar(np.load(EMB_Q / f"{col}.queries{'-en' if idioma == 'en' else ''}.npy")[:, :DIMS])
            qb = qa  # mismo perfil de embeddings (arctic@256, prefijo 'query: ') en las dos ramas
            filas = []
            for n, it in enumerate(items):
                da = a @ qa[n]
                top = {"A": harness.ranking(da, ids, harness.TOP_K, harness.MIN_SCORE),
                       "C": harness.ranking(c_docs @ qa[n], ids, harness.TOP_K, harness.MIN_SCORE),
                       "D": harness.ranking(d_docs @ qa[n], ids, harness.TOP_K, harness.MIN_SCORE)}
                codigo = harness.ranking(da, ids, POOL, harness.MIN_SCORE)
                sb = zb["docs"] @ qb[n]
                orden_b = sorted(range(len(sb)), key=lambda k: (-float(sb[k]), ids[zb["indices"][k]]))[:POOL]
                resumen = [int(zb["indices"][k]) for k in orden_b]
                rangos = [{i: r for r, i in enumerate(codigo, start=1)}, {i: r for r, i in enumerate(resumen, start=1)}]
                candidatos = set(codigo) | set(resumen)
                puntaje = {i: sum(1.0 / (RRF_K + rg[i]) for rg in rangos if i in rg) for i in candidatos}
                top["B"] = sorted(candidatos, key=lambda i: (-puntaje[i], ids[i]))[:harness.TOP_K]
                hit = {k: harness.evaluar_item(it, v, chunks) for k, v in top.items()}
                if hit["A"] is None:
                    continue
                filas.append({"pregunta": n, "question": it["Question"],
                              **{k: {"hit10": hit[k][10], "tokens_top10": int(ntok[v].sum()),
                                     "tecnicos_top10": int(es_tecnico[v].sum()), "top_ids": [ids[i] for i in v]}
                                 for k, v in top.items()}})
            bloque[idioma] = filas
        os.environ["VSE_IDIOMA"] = "es"
        out["colecciones"][col] = bloque
    SALIDA.mkdir(parents=True, exist_ok=True)
    (SALIDA / "brazos.json").write_text(json.dumps(out, indent=1, ensure_ascii=False), encoding="utf-8")
    resumen(out)


def resumen(out):
    print("generacion:", json.dumps(out["generacion"], ensure_ascii=False))
    print("paridad TEI ok:", out["paridad_tei_ok"], {c: v["paridad_tei_cos_min"] for c, v in out["embeber"]["colecciones"].items()})
    print(f"{'idioma':6} {'corpus':24} {'brazo':5} {'n':>3} {'hit10':>5} {'gana':>4} {'pierde':>6} {'tok.top10':>9} {'tecn./top10':>11}")
    for idioma in ("es", "en"):
        for col in list(out["colecciones"]) + ["TOTAL"]:
            filas = [f for c in out["colecciones"] for f in out["colecciones"][c][idioma] if col in ("TOTAL", c)]
            for b in "ABCD":
                gana = sum(f[b]["hit10"] and not f["A"]["hit10"] for f in filas)
                pierde = sum(f["A"]["hit10"] and not f[b]["hit10"] for f in filas)
                print(f"{idioma:6} {col:24} {b:5} {len(filas):>3} {sum(f[b]['hit10'] for f in filas):>5} {gana:>4} {pierde:>6} "
                      f"{np.mean([f[b]['tokens_top10'] for f in filas]):>9.0f} "
                      f"{np.mean([f[b]['tecnicos_top10'] for f in filas]) / 10:>11.3f}")
        print()


if __name__ == "__main__":
    {"generar": generar, "embeber": embeber, "evaluar": evaluar,
     "resumen": lambda: resumen(json.loads((SALIDA / "brazos.json").read_text(encoding="utf-8")))}[sys.argv[1]]()
