# 1.6: calcula el denso de cada arm, uno tras otro; un fallo no detiene la cola.
# Orden por prioridad. granite-*-r2 fuera: ModernBERT en CPU rindio 3,3 textos/s (ver decision.md).
# Lista por argumento: separada por comas en un solo parametro (pwsh -File no parte arrays).
# Arms con codigo remoto antiguo van en -Legacy (transformers 4.x, ver py.ps1).
# Presupuesto de 3 h (decision del usuario): gemma2 y nomic-v2-moe no se miden.
param([string]$Arms = "qwen3-0.6b,bge-m3", [string]$Legacy = "arctic-m-v2,jina-v2-code", [int]$EsperarPid = 0)
if ($EsperarPid) { Wait-Process -Id $EsperarPid -ErrorAction SilentlyContinue }
$py = Join-Path $PSScriptRoot "py.ps1"
$log = Join-Path (Split-Path -Parent (Split-Path -Parent $PSScriptRoot)) ".cache\embeddings-eval\cola.log"
$cola = @($Arms -split "," | Where-Object { $_ } | ForEach-Object { @{ arm = $_; legacy = "0" } }) +
        @($Legacy -split "," | Where-Object { $_ } | ForEach-Object { @{ arm = $_; legacy = "1" } })
foreach ($item in $cola) {
    $a = $item.arm
    $env:VSE_LEGACY = $item.legacy
    $t = [Diagnostics.Stopwatch]::StartNew()
    "== $a $(Get-Date -Format HH:mm:ss)" | Out-File -Append -Encoding utf8 $log
    & $py embeber $a 2>&1 | Select-String -NotMatch "Warning: You are sending|warnings.warn|Loading weights|FutureWarning|get_sentence_embedding" |
        Select-Object -Last 12 | Out-File -Append -Encoding utf8 $log
    "== $a exit=$LASTEXITCODE en $([int]$t.Elapsed.TotalSeconds) s" | Out-File -Append -Encoding utf8 $log
}
"== FIN $(Get-Date -Format HH:mm:ss)" | Out-File -Append -Encoding utf8 $log
