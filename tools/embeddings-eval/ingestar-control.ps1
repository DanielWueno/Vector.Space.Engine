<#
.SYNOPSIS
    1.6: ingesta de control con el CLI real de Rag.Context.Engine (MiniLM ONNX fp32 x64) en un
    Qdrant EFIMERO aparte, y su `rag eval` de dos bandas. De aqui salen los chunks (enriched_content,
    content, relative_path, sparse) sobre los que el harness re-embebe cada candidato.

.DESCRIPTION
    No toca rag-qdrant ni ninguna coleccion servida: exige el contenedor vse-16-qdrant en
    127.0.0.1:16333/16334 (ver README.md). Sin --con-resumen (decision de 1.6: sin banda resumen).
    Los comandos de ingesta son los de Rag.Context.Engine/replicate-env/scripts/04-ingest-collections.sh.
    Rollback: docker rm -f vse-16-qdrant; borrar .cache/embeddings-eval.
#>
param(
    [string]$RagRoot = "D:\Git\Rag.Context.Engine",
    [string]$Fuentes = "D:\Git",
    [string]$ModelsDir = "$env:USERPROFILE\models",
    [string[]]$Colecciones = @("bsuite-auditorias-test", "innovapp-docs", "micro-repo"),
    # 1.11: con -Sufijo la ingesta va a "<coleccion><Sufijo>" con Refragmentar/ (mismo pipeline que el
    # CLI, que fija MaxTokensPerChunk=512 en codigo) y sin `rag eval`. -Sufijo -r512 es la prueba de
    # fidelidad: debe reproducir los chunks de la ingesta del CLI.
    [string]$Sufijo = "",
    [int]$MaxTokensPerChunk = 512
)
$ErrorActionPreference = "Stop"
$repo = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$out = Join-Path $repo ".cache\embeddings-eval"
New-Item -ItemType Directory -Force (Join-Path $out "logs") | Out-Null

# El Qdrant efimero es requisito; nunca se cae al 6333/6334 de rag-qdrant.
$info = Invoke-RestMethod "http://127.0.0.1:16333/" -TimeoutSec 5
"Qdrant efimero: $($info.version)"

$env:RAG_MODELS_DIR = $ModelsDir
$env:OnnxBrain__ModelPath = Join-Path $ModelsDir "paraphrase-multilingual-MiniLM-L12-v2\model.onnx"
$env:OnnxBrain__VocabPath = Join-Path $ModelsDir "paraphrase-multilingual-MiniLM-L12-v2\sentencepiece.bpe.model"
$env:Qdrant__Host = "127.0.0.1"
$env:Qdrant__GrpcPort = "16334"
$env:Qdrant__HttpPort = "16333"
$env:Ingestion__IndexShortTypeDeclarations = "false"
$env:RAG_LOGS_DIR = Join-Path $out "logs"
$cli = Join-Path $RagRoot "src\RagEngine.Cli\bin\Release\net10.0\RagEngine.Cli.dll"
if (-not (Test-Path $cli)) { throw "Falta $cli (dotnet build -c Release de RagEngine.Cli)" }

$refrag = Join-Path $PSScriptRoot "Refragmentar\bin\Release\net10.0\Refragmentar.dll"
if ($Sufijo) {
    if (-not (Test-Path $refrag)) { throw "Falta $refrag (dotnet build -c Release de Refragmentar)" }
    $env:RAG_CLI_BIN = Split-Path -Parent $cli
}

function Ingest([string]$ruta, [string]$col, [string[]]$extra) {
    $t = [Diagnostics.Stopwatch]::StartNew()
    if ($Sufijo) {
        $repoName = if ($extra -contains "--repo-name") { $extra[[array]::IndexOf($extra, "--repo-name") + 1] } else { "my-repo" }
        & dotnet $refrag $ruta "$col$Sufijo" $MaxTokensPerChunk ($extra -contains "--force") $repoName 2>&1 |
            Out-File -Append -Encoding utf8 (Join-Path $out "ingest-$col$Sufijo.log")
    } else {
        & dotnet $cli ingest $ruta --collection $col @extra --yes 2>&1 | Out-File -Append -Encoding utf8 (Join-Path $out "ingest-$col.log")
    }
    if ($LASTEXITCODE -ne 0) { throw "ingest $col$Sufijo ($ruta) salio con $LASTEXITCODE" }
    "{0}: {1} en {2:n1} s" -f $col, $ruta, $t.Elapsed.TotalSeconds
}

$evalSets = @{
    "bsuite-auditorias-test" = "docs/eval/bsuite-auditorias.eval-set.json"
    "innovapp-docs"          = "docs/eval/innovapp-docs.eval-set.json"
    "micro-repo"             = "docs/eval/tickets-microservice.eval-set.json"
}

foreach ($col in $Colecciones) {
    switch ($col) {
        "bsuite-auditorias-test" {
            $base = Join-Path $Fuentes "BusinessSuite.Xaf\src\Modules"
            Ingest (Join-Path $base "REYMA.XAFR1PV.Compras\Auditorias") $col @("--repo-name", "BusinessSuite.Xaf", "--force")
            Ingest (Join-Path $base "REYMA.XAFR1PV.Compras\Compras\Utils") $col @("--repo-name", "BusinessSuite.Xaf")
            Ingest (Join-Path $base "REYMA.XAFR1PV.Base") $col @("--repo-name", "BusinessSuite.Xaf")
        }
        "innovapp-docs" { Ingest (Join-Path $Fuentes "docs-bsute-innovapp-plan") $col @("--force") }
        "micro-repo"    { Ingest (Join-Path $Fuentes "Reyma.TI.Tickets.Microservice") $col @("--force") }
    }
    if ($Sufijo) {
        $n = (Invoke-RestMethod "http://127.0.0.1:16333/collections/$col$Sufijo" -TimeoutSec 10).result.points_count
        "{0}{1}: {2} puntos (MaxTokensPerChunk {3})" -f $col, $Sufijo, $n, $MaxTokensPerChunk
        continue
    }
    # Eval de control con el retriever real (dos bandas, fusion nativa): sirve de ancla para el harness.
    Push-Location $RagRoot
    try {
        & dotnet $cli eval --eval-set $evalSets[$col] -c $col -k 10 --json --dump-hits 2>$null |
            Out-File -Encoding utf8 (Join-Path $out "rag-eval-$col.json")
        if ($LASTEXITCODE -ne 0) { throw "eval $col salio con $LASTEXITCODE" }
    } finally { Pop-Location }
    $n = (Invoke-RestMethod "http://127.0.0.1:16333/collections/$col" -TimeoutSec 10).result.points_count
    "{0}: {1} puntos" -f $col, $n
}
