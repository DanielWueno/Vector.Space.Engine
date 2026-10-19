"""Descarga los modelos de prueba de 1.8 al cache compartido (.cache/hf) desde el host.

Los contenedores no validan la CA de la inspeccion TLS corporativa; el host si (almacen de
Windows via truststore). No se desactiva la validacion TLS en ningun sitio: los contenedores
corren con HF_HUB_OFFLINE=1 sobre lo que se descarga aqui.
"""
import pathlib
import sys

import truststore

truststore.inject_into_ssl()

from huggingface_hub import snapshot_download  # noqa: E402

CACHE = pathlib.Path(__file__).resolve().parents[2] / ".cache" / "hf"

MODELOS = {
    "BAAI/bge-m3": ["onnx/*", "imgs/*", "*.jpg"],
    "nomic-ai/nomic-embed-text-v1.5": ["onnx/*"],
    # Codigo remoto que nomic-embed-text-v1.5 declara en auto_map (lo necesita trust_remote_code).
    "nomic-ai/nomic-bert-2048": ["*.safetensors", "*.bin", "onnx/*"],
}

for repo in (sys.argv[1:] or MODELOS):
    ruta = snapshot_download(repo, cache_dir=CACHE, ignore_patterns=MODELOS.get(repo, ["onnx/*"]))
    print(repo, "->", ruta)
