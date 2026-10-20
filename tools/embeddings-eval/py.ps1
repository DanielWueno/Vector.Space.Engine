# 1.6: corre harness.py en un entorno efimero de uv (nada instalado globalmente).
# --system-certs: uv valida TLS con el almacen de Windows (inspeccion TLS corporativa), sin desactivar TLS.
# $env:VSE_LEGACY=1: transformers 4.x para modelos con codigo remoto antiguo (trust_remote_code), que
# en transformers 5.x dejan sin inicializar sus buffers de posicion (arctic-m-v2: IndexError en rope).
$env:HF_HUB_DISABLE_SYMLINKS_WARNING = "1"
$env:HF_HUB_DISABLE_XET = "1"
# El proxy corporativo corta lecturas largas con el timeout por defecto (10 s).
$env:HF_HUB_DOWNLOAD_TIMEOUT = "300"
$env:HF_HUB_ETAG_TIMEOUT = "60"
$versiones = if ($env:VSE_LEGACY -eq "1") {
    @("--with", "sentence-transformers==5.1.2", "--with", "transformers==4.57.6")
} else {
    @("--with", "sentence-transformers==6.1.0", "--with", "transformers==5.20.2")
}
# Primer argumento *.py: otro script de esta carpeta (p. ej. sidecar.py); si no, harness.py.
$script = "harness.py"
if ($args.Count -gt 0 -and $args[0] -like "*.py") { $script, $args = $args }
uv run --system-certs --no-project --python 3.12 @versiones --with "torch==2.14.1" `
    --with einops --with psutil --with numpy --with truststore `
    python (Join-Path $PSScriptRoot $script) @args
exit $LASTEXITCODE
