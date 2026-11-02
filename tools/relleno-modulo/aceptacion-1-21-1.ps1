# Aceptacion del item 1.21.1 (filtro exacto de modulo). Solo Qdrant local efimero y colecciones it-1-21-*,
# con embeddings/LLM sinteticos inyectados por las pruebas. Sale distinto de 0 si falta Qdrant, si no se ejecuta
# ningun test, si alguno falla o queda omitido, si falta un caso requerido o si el smoke de la herramienta falla.
#
#   docker run -d --rm --name vse-it-1-21-qdrant -p 127.0.0.1:16333:6333 -p 127.0.0.1:16334:6334 qdrant/qdrant:v1.19.2
#   pwsh -File tools\relleno-modulo\aceptacion-1-21-1.ps1
[CmdletBinding()]
param(
    [string]$QdrantHost = '127.0.0.1',
    [int]$PuertoRest = 16333,
    [int]$PuertoGrpc = 16334
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$rest = "http://${QdrantHost}:$PuertoRest"
$fallos = [System.Collections.Generic.List[string]]::new()

function Fallar([string]$m) { $script:fallos.Add($m); Write-Host "FALLO: $m" -ForegroundColor Red }

# Las pruebas usan 127.0.0.1:16333/16334 fijos; no apuntar el script a otro Qdrant.
if ($QdrantHost -ne '127.0.0.1' -or $PuertoRest -ne 16333 -or $PuertoGrpc -ne 16334) {
    Write-Host 'Solo se admite el Qdrant local efimero en 127.0.0.1:16333/16334.' -ForegroundColor Red; exit 2
}

# 0) Infraestructura: sin Qdrant no hay aceptacion (nunca omitir y dar exito).
try { $v = Invoke-RestMethod -Uri "$rest/" -TimeoutSec 5; Write-Host "Qdrant $($v.version) en $rest" }
catch { Write-Host "Qdrant local no responde en ${rest}: $($_.Exception.Message)" -ForegroundColor Red; exit 3 }

# 1) Pruebas de contrato e integracion del item.
$resultados = Join-Path ([IO.Path]::GetTempPath()) ("vse-aceptacion-1-21-1-" + [guid]::NewGuid().ToString('N'))
$env:VSE_QDRANT_LOCAL = '1'
try {
    & dotnet test (Join-Path $repo 'tests\Reyma.VectorSpace.Engine.Microservice.Tests') --nologo `
        --filter 'FullyQualifiedName~FiltroDeModulo' --logger 'trx;LogFileName=filtro-modulo.trx' --results-directory $resultados
    $codigoTest = $LASTEXITCODE
} finally { Remove-Item Env:\VSE_QDRANT_LOCAL -ErrorAction SilentlyContinue }
if ($codigoTest -ne 0) { Fallar "dotnet test salio con $codigoTest" }

$trx = Join-Path $resultados 'filtro-modulo.trx'
if (-not (Test-Path $trx)) { Fallar 'no se genero el TRX'; $resultadosTrx = @() }
else {
    [xml]$x = Get-Content -Raw $trx
    $ns = @{ t = 'http://microsoft.com/schemas/VisualStudio/TeamTest/2010' }
    $c = (Select-Xml -Xml $x -XPath '//t:ResultSummary/t:Counters' -Namespace $ns).Node
    Write-Host "TRX: total=$($c.total) ejecutados=$($c.executed) pasados=$($c.passed) fallidos=$($c.failed) no_ejecutados=$($c.notExecuted)"
    if ([int]$c.total -le 0) { Fallar 'cero tests seleccionados' }
    if ([int]$c.passed -ne [int]$c.total) { Fallar "pasados $($c.passed) de $($c.total)" }
    if ([int]$c.notExecuted -ne 0) { Fallar "$($c.notExecuted) tests no ejecutados u omitidos" }
    $resultadosTrx = @(Select-Xml -Xml $x -XPath '//t:UnitTestResult' -Namespace $ns | ForEach-Object { $_.Node })
    foreach ($r in $resultadosTrx) { if ($r.outcome -ne 'Passed') { Fallar "$($r.testName): $($r.outcome)" } }
}
Remove-Item -Recurse -Force $resultados -ErrorAction SilentlyContinue

$requeridos = @(
    'FiltroDeModuloContratoTests.ModulosInvalidosResponden400SinLlegarAlMotor',
    'FiltroDeModuloContratoTests.ModulosValidosLleganAlMotorSinCambiosYAusentesSonNull',
    'FiltroDeModuloContratoTests.LosPrefijosDerivadosEquivalenAlPredicadoOriginal',
    'FiltroDeModuloContratoTests.LosCasosLiteralesDeLaFichaSeComportanComoDice',
    'FiltroDeModuloIntegracionTests.DensoFiltraAntesDelCorteConFronterasExactasYCoincideConElOraculo',
    'FiltroDeModuloIntegracionTests.RellenoConNamespacesArbitrariosYSeparadoresRepetidosCoincideConElOraculoYEmpatesPorId',
    'FiltroDeModuloIntegracionTests.FusionNativaFiltraEnAmbasRamasAntesDelCorte',
    'FiltroDeModuloIntegracionTests.EspacioResumenFiltraAntesDelCorteYCoincideConElOraculo',
    'FiltroDeModuloIntegracionTests.ColeccionSinPrepararResponde409AntesDeVectorizarYSinModulosNoCambia',
    'FiltroDeModuloIntegracionTests.RellenoPreservaTodoEsIdempotenteYNoVectorizaNiLlamaAlLlm',
    'FiltroDeModuloIntegracionTests.RellenoInterrumpidoNoMarcaCompletoBloqueaIngestasYSeReanuda',
    'FiltroDeModuloIntegracionTests.ExclusionDeRellenosYEscritoresAntiguos'
)
foreach ($req in $requeridos) {
    $pasados = @($resultadosTrx | Where-Object {
        ($_.testName -eq $req -or $_.testName.EndsWith(".$req") -or $_.testName -like "*.$req(*") -and $_.outcome -eq 'Passed' })
    if ($pasados.Count -eq 0) { Fallar "caso requerido sin ejecutar o sin pasar: $req" }
}

# 2) Smoke de la herramienta sobre una coleccion cruda "anterior a 1.21.1" (sin prefijos), borrada al terminar.
function DerivarPrefijos($namespace, $ruta) {
    # Derivacion independiente del codigo C#: valor completo y cada prefijo justo antes de un separador (i > 0).
    $set = [System.Collections.Generic.SortedSet[string]]::new([StringComparer]::Ordinal)
    foreach ($par in @(@($namespace, '.'), @($ruta, '/'))) {
        $valor = [string]$par[0]; if ([string]::IsNullOrEmpty($valor)) { continue }
        for ($i = 1; $i -lt $valor.Length; $i++) { if ($valor[$i] -eq [char]$par[1]) { [void]$set.Add($valor.Substring(0, $i)) } }
        [void]$set.Add($valor)
    }
    return @($set)
}

$coleccion = 'it-1-21-smoke-' + [guid]::NewGuid().ToString('N').Substring(0, 8)
$herramienta = Join-Path $repo 'tools\relleno-modulo'
$puntos = @(
    @{ id = 1; vector = @{ dense = @(1.0, 0.0, 0.0, 0.0) }; payload = @{ namespace = 'alpha.Foo'; relative_path = 'alpha/child/Foo.cs'; content_hash = 'h1'; text = 't1' } },
    @{ id = 2; vector = @{ dense = @(0.0, 1.0, 0.0, 0.0) }; payload = @{ namespace = 'alpha..Bar'; relative_path = 'x//y.cs'; content_hash = 'h2'; text = 't2' } },
    @{ id = 3; vector = @{ dense = @(0.0, 0.0, 1.0, 0.0) }; payload = @{ relative_path = '/alpha/x.md'; content_hash = 'h3'; text = 't3' } },
    @{ id = 4; vector = @{ dense = @(0.0, 0.0, 0.0, 1.0) }; payload = @{ content_hash = 'h4'; text = 't4' } }
)
function Leer() {
    $cuerpo = @{ limit = 100; with_payload = $true; with_vector = $true } | ConvertTo-Json
    return (Invoke-RestMethod -Method Post -Uri "$rest/collections/$coleccion/points/scroll" -Body $cuerpo -ContentType 'application/json').result.points
}
function Json($objeto, [string[]]$excluir) {
    # Claves ordenadas (Qdrant no garantiza el orden de las claves del payload).
    $orden = [ordered]@{}
    foreach ($pr in ($objeto.PSObject.Properties | Sort-Object Name -CaseSensitive)) { if ($excluir -notcontains $pr.Name) { $orden[$pr.Name] = $pr.Value } }
    return ($orden | ConvertTo-Json -Compress -Depth 8)
}
function Herramienta([string[]]$extra) {
    $ErrorActionPreference = 'Continue'
    $salida = & dotnet run --no-build --project $herramienta -- --coleccion $coleccion --host 127.0.0.1 `
        --puerto-grpc $PuertoGrpc --puerto-rest $PuertoRest @extra 2>&1 | Out-String
    return @{ Codigo = $LASTEXITCODE; Salida = $salida.Trim() }
}

& dotnet build $herramienta --nologo -v q | Out-Null
if ($LASTEXITCODE -ne 0) { Fallar 'no compila tools\relleno-modulo' }
else {
    try {
        Invoke-RestMethod -Method Put -Uri "$rest/collections/$coleccion" -ContentType 'application/json' `
            -Body (@{ vectors = @{ dense = @{ size = 4; distance = 'Cosine' } } } | ConvertTo-Json -Depth 5) | Out-Null
        Invoke-RestMethod -Method Put -Uri "$rest/collections/$coleccion/points?wait=true" -ContentType 'application/json' `
            -Body (@{ points = $puntos } | ConvertTo-Json -Depth 8) | Out-Null
        $antes = Leer

        $r = Herramienta @('--verificar'); Write-Host "verificar (antes): $($r.Codigo) $($r.Salida)"
        if ($r.Codigo -ne 1) { Fallar "--verificar antes del relleno debia salir 1, salio $($r.Codigo)" }

        $r = Herramienta @(); Write-Host "relleno: $($r.Codigo) $($r.Salida)"
        if ($r.Codigo -ne 0 -or $r.Salida -notmatch 'puntos_actualizados=4\b') { Fallar "relleno: codigo $($r.Codigo)" }

        $r = Herramienta @('--verificar'); Write-Host "verificar (despues): $($r.Codigo) $($r.Salida)"
        if ($r.Codigo -ne 0) { Fallar "--verificar despues del relleno debia salir 0, salio $($r.Codigo)" }

        $despues = Leer
        foreach ($p in $despues) {
            $a = $antes | Where-Object { $_.id -eq $p.id }
            $ns = if ($a.payload.PSObject.Properties['namespace']) { $a.payload.namespace } else { $null }
            $ruta = if ($a.payload.PSObject.Properties['relative_path']) { $a.payload.relative_path } else { $null }
            $esperados = DerivarPrefijos $ns $ruta
            if (-not $p.payload.PSObject.Properties['module_prefixes']) { Fallar "punto $($p.id): sin module_prefixes"; continue }
            [string[]]$obtenidos = @($p.payload.module_prefixes)
            [Array]::Sort($obtenidos, [StringComparer]::Ordinal)
            if (($esperados -join "`n") -cne ($obtenidos -join "`n")) {
                Fallar "punto $($p.id): prefijos [$($obtenidos -join ', ')] esperados [$($esperados -join ', ')]"
            }
            if ($p.payload.module_prefixes_version -ne 1) { Fallar "punto $($p.id): module_prefixes_version" }
            if ((Json $p.vector @()) -cne (Json $a.vector @())) { Fallar "punto $($p.id): vector cambiado" }
            if ((Json $p.payload @('module_prefixes', 'module_prefixes_version')) -cne (Json $a.payload @())) { Fallar "punto $($p.id): payload original cambiado" }
        }
        if (@($despues).Count -ne $puntos.Count) { Fallar 'cambio el numero de puntos' }

        $r = Herramienta @(); Write-Host "relleno (segunda vez): $($r.Codigo) $($r.Salida)"
        if ($r.Codigo -ne 0 -or $r.Salida -notmatch 'ya_completa=True' -or $r.Salida -notmatch 'puntos_actualizados=0\b') { Fallar 'la segunda ejecucion no fue un no-op' }
    }
    catch { Fallar "smoke: $($_.Exception.Message)" }
    finally { try { Invoke-RestMethod -Method Delete -Uri "$rest/collections/$coleccion" | Out-Null } catch { } }
}

$restos = @((Invoke-RestMethod -Uri "$rest/collections").result.collections | Where-Object { $_.name -like 'it-1-21-*' })
if ($restos.Count -gt 0) { Fallar "quedaron colecciones de prueba: $(($restos | ForEach-Object name) -join ', ')" }

if ($fallos.Count -gt 0) { Write-Host "ACEPTACION 1.21.1: FALLA ($($fallos.Count))" -ForegroundColor Red; exit 1 }
Write-Host 'ACEPTACION 1.21.1: OK' -ForegroundColor Green
exit 0
