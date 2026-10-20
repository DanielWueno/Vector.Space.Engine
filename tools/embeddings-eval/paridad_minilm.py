"""1.6: paridad numerica entre el ONNX propio de Rag.Context.Engine y MiniLM de referencia
(sentence-transformers) sobre los mismos textos. Es la medida que 1.4 exige antes de conservar
colecciones MiniLM 384d sin reingestar."""
import json
import sys

import numpy as np

sys.path.insert(0, __file__.rsplit("\\", 1)[0])
import harness  # noqa: E402

salida = {}
for col in harness.COLECCIONES:
    chunks = harness.cargar_chunks(col)
    onnx = harness.normalizar([c["onnx_dense"] for c in chunks])
    st = np.load(harness.CACHE / "emb" / "minilm-st" / f"{col}.docs.npy")
    cos = np.sum(onnx * st, axis=1)
    _, qv = harness.cargar_preguntas(col)
    q_onnx = harness.normalizar([v["onnx_dense"] for v in qv])
    q_st = np.load(harness.CACHE / "emb" / "minilm-st" / f"{col}.queries.npy")
    qcos = np.sum(q_onnx * q_st, axis=1)
    largo = np.array([len(c["enriched_content"]) for c in chunks])
    salida[col] = {
        "chunks_cos_p01_p50_p99": [round(float(x), 4) for x in np.percentile(cos, [1, 50, 99])],
        "chunks_cos_min": round(float(cos.min()), 4),
        "chunks_cos_lt_0_99": int((cos < 0.99).sum()), "chunks": len(chunks),
        "preguntas_cos_min_mediana": [round(float(qcos.min()), 4), round(float(np.median(qcos)), 4)],
        "largo_mediano_chars_cos_lt_0_99": int(np.median(largo[cos < 0.99])) if (cos < 0.99).any() else None,
        "largo_mediano_chars_cos_ge_0_99": int(np.median(largo[cos >= 0.99])),
    }
print(json.dumps(salida, indent=1))
(harness.SALIDA / "paridad-minilm-onnx-vs-st.json").write_text(json.dumps(salida, indent=1), encoding="utf-8")
