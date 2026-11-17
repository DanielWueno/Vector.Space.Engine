# Solo el contenedor efimero dedicado; nunca una instancia servida en los puertos del compose.
[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$nombre = 'vse-it-1-21-4-qdrant'
$rest = 'http://127.0.0.1:26333'
$test = Join-Path $repo 'tests\Reyma.VectorSpace.Engine.Microservice.Tests'
$directorio = Join-Path ([IO.Path]::GetTempPath()) ('vse-1-21-4-' + [guid]::NewGuid().ToString('N'))

try {
    $estado = & docker inspect $nombre 2>$null
    if ($LASTEXITCODE -ne 0) { throw 'Falta el contenedor efimero dedicado.' }
    $c = @($estado | ConvertFrom-Json)[0]
    if (-not $c.State.Running -or $c.Config.Image -cne 'qdrant/qdrant:v1.19.2' -or
        $c.Config.Labels.'vse.item' -cne '1.21.4' -or -not $c.HostConfig.AutoRemove) {
        throw 'El contenedor no cumple la procedencia efimera esperada.'
    }
    foreach ($puerto in @(@('6333/tcp', '26333'), @('6334/tcp', '26334'))) {
        $binding = @($c.NetworkSettings.Ports.($puerto[0]))
        if ($binding.Count -ne 1 -or $binding[0].HostIp -cne '127.0.0.1' -or $binding[0].HostPort -cne $puerto[1]) {
            throw 'Los puertos no pertenecen al banco local dedicado.'
        }
    }
    $v = Invoke-RestMethod "$rest/" -TimeoutSec 5
    if ($v.version -cne '1.19.2') { throw 'Version distinta de Qdrant 1.19.2; detener, no cambiar el oraculo.' }
} catch {
    Write-Host "ACEPTACION 1.21.4: infraestructura ausente o incompatible. $($_.Exception.Message)"
    exit 3
}

function Ejecutar([string]$filtro, [string]$archivo, [string[]]$requeridos) {
    & dotnet test $test --no-restore --nologo --filter $filtro --logger "trx;LogFileName=$archivo" --results-directory $directorio -v minimal
    if ($LASTEXITCODE -ne 0) { throw "dotnet test fallo: $archivo" }
    $trx = Join-Path $directorio $archivo
    if (-not (Test-Path $trx)) { throw "No hay TRX: $archivo" }
    [xml]$x = Get-Content -Raw $trx
    $ns = @{t = 'http://microsoft.com/schemas/VisualStudio/TeamTest/2010'}
    $counters = (Select-Xml -Xml $x -XPath '//t:ResultSummary/t:Counters' -Namespace $ns).Node
    $resultados = @(Select-Xml -Xml $x -XPath '//t:UnitTestResult' -Namespace $ns | ForEach-Object Node)
    if ([int]$counters.total -le 0 -or [int]$counters.executed -ne [int]$counters.total -or
        [int]$counters.passed -ne [int]$counters.total -or [int]$counters.notExecuted -ne 0) {
        throw 'Cero tests, fallo u omision: no hay aceptacion.'
    }
    foreach ($r in $resultados) { if ($r.outcome -cne 'Passed') { throw "No paso: $($r.testName)" } }
    foreach ($req in $requeridos) {
        if (@($resultados | Where-Object { $_.testName.EndsWith(".$req", [StringComparison]::Ordinal) }).Count -ne 1) {
            throw "Caso requerido ausente o duplicado: $req"
        }
    }
    Write-Host "${archivo}: $($counters.passed)/$($counters.total), sin omisiones."
}

$previo = [Environment]::GetEnvironmentVariable('VSE_QDRANT_TEXTUAL')
try {
    # El lector congelado solo cambia nombres; evita actualizar accidentalmente el control para favorecer el resultado.
    Push-Location $repo
    try {
        $base = (& git --no-pager show da1b3d0570ab2d9cf38701b7471e17d4d631f780:src/Reyma.VectorSpace.Engine.Microservice.Infrastructure/VectorStore/QdrantSemanticRetriever.cs) -join "`n"
        if ($LASTEXITCODE -ne 0) { throw 'Falta el commit del lector anterior.' }
    } finally { Pop-Location }
    $esperado = '// Frozen reader from da1b3d0570ab2d9cf38701b7471e17d4d631f780; only type names changed.' + "`n" +
        $base.Replace('QdrantSemanticRetriever', 'LectorAnterior1_21_4').Replace('SearchHit', 'HitAnterior1_21_4') + "`n"
    $actual = [IO.File]::ReadAllText((Join-Path $test 'Motor\LectorAnterior1_21_4.cs')).Replace("`r`n", "`n")
    if ($actual -cne $esperado) { throw 'El lector anterior no coincide con la fuente fijada.' }

    $env:VSE_QDRANT_TEXTUAL = '1'
    # Puerta historica: si discrepa, NO ejecutar la matriz ni acomodar expectativas.
    Ejecutar 'FullyQualifiedName~FiltrosTextualesIntegracionTests.ReferenciaHistoricaCoincideSinModificarOraculo' 'historia.trx' @(
        'FiltrosTextualesIntegracionTests.ReferenciaHistoricaCoincideSinModificarOraculo')
    Ejecutar 'FullyQualifiedName~FiltrosTextuales|FullyQualifiedName~FiltroDeModuloContratoTests|FullyQualifiedName~EspacioDeBusquedaTests|FullyQualifiedName~ColeccionesTests.CuerpoInvalidoDevuelve400SinLlegarAlMotor|FullyQualifiedName~ColeccionesTests.CredencialInvalidaDevuelve401EnTodoElContratoSinLlegarAlMotor|FullyQualifiedName~ColeccionesTests.BusquedaSinTopKUsaElValorPorDefecto|FullyQualifiedName~LimiteDeIngestasTests' 'filtros.trx' @(
        'FiltrosTextualesContratoTests.LenguajesCanonicosYNamespaceLiteralLleganAlMotor',
        'FiltrosTextualesContratoTests.AusentesNullYNamespaceBlancoSonAdmitidos',
        'FiltrosTextualesContratoTests.NamespaceConFormaDeFechaONumeroSigueSiendoTextoLiteral',
        'FiltrosTextualesContratoTests.ValoresYTiposInvalidosResponden400AntesDelMotor',
        'FiltrosTextualesContratoTests.LosFiltrosNoRetiranAutenticacionHttp',
        'FiltrosTextualesIntegracionTests.ReferenciaHistoricaCoincideSinModificarOraculo',
        'FiltrosTextualesIntegracionTests.DensoAplicaTextoAntesDelCorteYAndConCadaModuloYAnySimbolos',
        'FiltrosTextualesIntegracionTests.FusionFiltraDensoYSparseAntesDelPool',
        'FiltrosTextualesIntegracionTests.ResumenAplicaMismoFiltroAntesDelCorteEnAmbosModos',
        'FiltrosTextualesIntegracionTests.SinFiltrosNuevosConservaIdsOrdenYScoresDelLectorAnterior')
    $restos = @((Invoke-RestMethod "$rest/collections" -TimeoutSec 5).result.collections | Where-Object { $_.name -like 'it-1-21-4-*' })
    if ($restos.Count -ne 0) { throw 'Quedaron colecciones propias sin limpiar.' }
    Write-Host 'ACEPTACION 1.21.4: OK (datos sinteticos, no recall ni paridad entre versiones).'
} catch {
    Write-Host "ACEPTACION 1.21.4: FALLA. $($_.Exception.Message)"
    exit 1
} finally {
    [Environment]::SetEnvironmentVariable('VSE_QDRANT_TEXTUAL', $previo)
    if (Test-Path $directorio) { Remove-Item -LiteralPath $directorio -Recurse -Force }
}
