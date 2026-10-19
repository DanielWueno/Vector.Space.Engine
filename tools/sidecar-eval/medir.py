"""Latencia/throughput en CPU de un motor ya levantado: 3 rondas de un lote de 32 textos (~120 palabras).

Uso: python medir.py <tei|infinity> <base_url> <model_name>
Mide tiempo de pared del lado cliente; un calentamiento previo no se cuenta.
"""
import json
import statistics
import sys
import time
import urllib.request

MOTOR, BASE, MODELO = sys.argv[1], sys.argv[2].rstrip("/"), sys.argv[3]
RUTA = "/v1/embeddings" if MOTOR == "tei" else "/embeddings"

BASE_TXT = ("El servicio de facturacion valida el RFC del receptor, calcula impuestos trasladados y "
            "retenidos, genera el XML del CFDI y lo envia al proveedor de timbrado. Si el proveedor "
            "responde con error de conexion se reintenta con espera exponencial hasta tres veces; "
            "si el error es de validacion se registra en la bitacora con el folio y no se reintenta. ")
TEXTOS = [f"[{i}] " + BASE_TXT * 2 for i in range(32)]  # ~120 palabras cada uno


def lote():
    req = urllib.request.Request(BASE + RUTA, data=json.dumps({"input": TEXTOS, "model": MODELO}).encode(),
                                 headers={"Content-Type": "application/json"})
    t0 = time.perf_counter()
    with urllib.request.urlopen(req, timeout=600) as r:
        n = len(json.loads(r.read())["data"])
    return time.perf_counter() - t0, n


lote()  # calentamiento
tiempos = [lote()[0] for _ in range(3)]
med = statistics.median(tiempos)
print(json.dumps({"motor": MOTOR, "lote": len(TEXTOS), "ms_por_lote": [round(t * 1000) for t in tiempos],
                  "mediana_ms": round(med * 1000), "textos_por_s": round(len(TEXTOS) / med, 1)}))
