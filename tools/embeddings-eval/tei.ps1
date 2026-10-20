# 1.6: levanta TEI (CPU) con un modelo del cache local, offline, en 127.0.0.1:18080.
# Uso: tei.ps1 <repo HF> [max-batch-tokens]
# Rollback: docker rm -f vse-16-tei
param([Parameter(Mandatory)][string]$Repo, [int]$MaxBatchTokens = 4096)
$ErrorActionPreference = "Stop"
$cache = Join-Path (Split-Path -Parent (Split-Path -Parent $PSScriptRoot)) ".cache\hf"
$snap = Get-ChildItem (Join-Path $cache ("models--" + $Repo.Replace("/", "--") + "\snapshots")) | Select-Object -First 1
$rel = "models--" + $Repo.Replace("/", "--") + "/snapshots/" + $snap.Name
docker rm -f vse-16-tei 2>$null | Out-Null
docker run -d --name vse-16-tei -p 127.0.0.1:18080:80 -v "${cache}:/data" `
    -e HF_HUB_OFFLINE=1 -e HF_HUB_CACHE=/data `
    ghcr.io/huggingface/text-embeddings-inference:cpu-1.8 `
    --model-id "/data/$rel" --max-client-batch-size 64 --max-batch-tokens $MaxBatchTokens --auto-truncate | Out-Null
for ($i = 0; $i -lt 120; $i++) {
    Start-Sleep 5
    try { Invoke-RestMethod http://127.0.0.1:18080/health -TimeoutSec 5 | Out-Null; "TEI listo: $Repo"; exit 0 } catch {}
    if (-not (docker ps -q -f name=vse-16-tei)) { docker logs --tail 20 vse-16-tei; "TEI murio: $Repo"; exit 1 }
}
docker logs --tail 20 vse-16-tei; "TEI no respondio en 10 min: $Repo"; exit 1
