"""Sondeo de capacidades de un motor de embeddings (TEI o Infinity) ya levantado en local.

Uso: python sondear.py <tei|infinity> <base_url> <etiqueta>
Prueba, sin suponer nada de la documentacion:
  - denso por la ruta OpenAI-compatible (/v1/embeddings o /embeddings) y su dimension;
  - parametro 'dimensions' (Matryoshka): si se acepta, si el resultado == truncar(full)+L2;
  - vector sparse: /embed_sparse (TEI) y lo que exponga Infinity;
  - simetria ES/EN: similitud coseno de pares traducidos frente a pares no relacionados.
Imprime JSON con lo observado; un fallo HTTP se registra como dato, no se oculta.
"""
import json
import math
import sys
import time
import urllib.error
import urllib.request

MOTOR, BASE, ETIQUETA = sys.argv[1], sys.argv[2].rstrip("/"), sys.argv[3]
PREFIJO = sys.argv[4] if len(sys.argv) > 4 else ""  # nomic exige 'search_query: ' / 'search_document: '

PARES = [
    ("La factura se timbra ante el SAT antes de enviarla al cliente.",
     "The invoice is stamped with the tax authority before sending it to the customer."),
    ("El servicio reintenta la conexion a la base de datos tres veces.",
     "The service retries the database connection three times."),
    ("Configura el token de acceso en la variable de entorno.",
     "Set the access token in the environment variable."),
]
NO_RELACIONADO = "Receta de pan de muerto con azahar y mantequilla."


def post(ruta, cuerpo):
    req = urllib.request.Request(BASE + ruta, data=json.dumps(cuerpo).encode(),
                                 headers={"Content-Type": "application/json"})
    t0 = time.perf_counter()
    try:
        with urllib.request.urlopen(req, timeout=120) as r:
            return r.status, json.loads(r.read()), time.perf_counter() - t0
    except urllib.error.HTTPError as e:
        return e.code, e.read().decode(errors="replace")[:300], time.perf_counter() - t0
    except Exception as e:  # conexion caida, etc.
        return None, repr(e)[:300], time.perf_counter() - t0


def norma(v):
    return math.sqrt(sum(x * x for x in v))


def cos(a, b):
    return sum(x * y for x, y in zip(a, b)) / (norma(a) * norma(b))


def l2(v):
    n = norma(v)
    return [x / n for x in v]


def openai_embed(textos, **extra):
    ruta = "/v1/embeddings" if MOTOR == "tei" else "/embeddings"
    st, cuerpo, dt = post(ruta, {"input": textos, "model": ETIQUETA if MOTOR == "tei" else
                                 sys.argv[5] if len(sys.argv) > 5 else ETIQUETA, **extra})
    if st != 200:
        return st, cuerpo, dt
    return st, [d["embedding"] for d in cuerpo["data"]], dt


out = {"motor": MOTOR, "modelo": ETIQUETA}

texto = PREFIJO + PARES[0][0]
st, full, dt = openai_embed([texto])
out["denso"] = {"status": st, "dim": len(full[0]) if st == 200 else None,
                "norma": round(norma(full[0]), 4) if st == 200 else None,
                "ms": round(dt * 1000, 1), "error": None if st == 200 else full}

if st == 200:
    mrl = {}
    for d in (256, 512):
        st2, trunc, _ = openai_embed([texto], dimensions=d)
        if st2 != 200:
            mrl[d] = {"status": st2, "error": trunc}
            continue
        esperado = l2(full[0][:d])
        mrl[d] = {"status": st2, "dim": len(trunc[0]), "norma": round(norma(trunc[0]), 4),
                  "cos_vs_truncar_y_L2": round(cos(trunc[0], esperado), 6)}
    out["dimensions_param"] = mrl

    # Simetria ES/EN a dimension completa y truncada a 256 (truncado del lado cliente).
    textos = [PREFIJO + t for par in PARES for t in par] + [PREFIJO + NO_RELACIONADO]
    st3, vs, dt3 = openai_embed(textos)
    if st3 == 200:
        sim = {}
        for d in (None, 256):
            v = [l2(x[:d]) if d else x for x in vs]
            trad = [round(cos(v[2 * i], v[2 * i + 1]), 4) for i in range(len(PARES))]
            nr = [round(cos(v[2 * i], v[-1]), 4) for i in range(len(PARES))]
            sim[str(d or "full")] = {"cos_traducciones": trad, "cos_no_relacionado": nr,
                                     "separa": min(trad) > max(nr)}
        out["simetria_es_en"] = sim
        out["lote_7_textos_ms"] = round(dt3 * 1000, 1)

# Sparse
if MOTOR == "tei":
    st4, sp, _ = post("/embed_sparse", {"inputs": PARES[0][0]})
    out["sparse"] = {"ruta": "/embed_sparse", "status": st4,
                     "n_terminos": len(sp[0]) if st4 == 200 else None,
                     "error": None if st4 == 200 else sp}
    st5, info, _ = post("/info", {}) if False else (None, None, 0)
    try:
        with urllib.request.urlopen(BASE + "/info", timeout=10) as r:
            out["info"] = {k: v for k, v in json.loads(r.read()).items()
                           if k in ("model_id", "model_type", "max_input_length", "version", "sha")}
    except Exception as e:
        out["info"] = repr(e)[:200]
else:
    try:
        with urllib.request.urlopen(BASE + "/openapi.json", timeout=10) as r:
            spec = json.loads(r.read())
        out["rutas"] = sorted(spec.get("paths", {}))
        out["version"] = spec.get("info", {}).get("version")
    except Exception as e:
        out["rutas"] = repr(e)[:200]
    try:
        with urllib.request.urlopen(BASE + "/models", timeout=10) as r:
            out["models"] = [{k: m.get(k) for k in ("id", "backend", "capabilities")}
                             for m in json.loads(r.read()).get("data", [])]
    except Exception as e:
        out["models"] = repr(e)[:200]

print(json.dumps(out, ensure_ascii=False, indent=1))
