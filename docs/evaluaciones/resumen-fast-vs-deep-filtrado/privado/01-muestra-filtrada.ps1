# Muestra estratificada (N=100) de reyoneweb entre los puntos que el filtro previo de 1.27 deja pasar, sin los 100
# de la muestra del 2026-11-11. Solo lectura sobre Qdrant. Ver ../protocolo.md.
param([string]$Volcado = (Join-Path $env:TEMP 'vse-fvd-filtrado-volcado.json'))
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$N = 100; $MIN = 3; $SEED = 20261117
$utf8 = [Text.UTF8Encoding]::new($false)
$campos = @('content', 'language', 'relative_path', 'chunk_type', 'class_name', 'method_name', 'start_line', 'end_line')

# 1. Volcado completo por scroll (fuera del repo: contiene todo el codigo de la coleccion).
$todos = [System.Collections.Generic.List[object]]::new(); $offset = $null; $paginas = 0
do {
  $req = [ordered]@{ limit = 1000; with_payload = @{ include = $campos }; with_vector = $false }
  if ($null -ne $offset) { $req.offset = $offset }
  $r = Invoke-RestMethod -Method Post -Uri 'http://localhost:6333/collections/reyoneweb/points/scroll' -ContentType 'application/json; charset=utf-8' `
    -Body ([Text.Encoding]::UTF8.GetBytes(($req | ConvertTo-Json -Depth 4 -Compress)))
  foreach ($p in $r.result.points) {
    $x = [ordered]@{ id = [string]$p.id }; foreach ($c in $campos) { $x[$c] = $p.payload.$c }; $todos.Add($x)
  }
  $offset = $r.result.next_page_offset; $paginas++
} while ($null -ne $offset)
[IO.File]::WriteAllText($Volcado, ($todos | ConvertTo-Json -Depth 3 -Compress), $utf8)
Write-Host "volcado: $($todos.Count) puntos en $paginas paginas"

# 2. Filtro previo real (FiltroPrevioDeResumen, configuracion por defecto).
$motivos = Join-Path $env:TEMP 'vse-fvd-filtrado-motivos.json'
dotnet run --project "$PSScriptRoot\FiltroPrevio" -c Release -- $Volcado $motivos | Write-Host
if ($LASTEXITCODE) { throw 'fallo el filtro previo' }
$motivoDe = @{}; foreach ($m in (Get-Content $motivos -Raw | ConvertFrom-Json)) { $motivoDe[$m.id] = $m.motivo }

# 3. Exclusiones: filtro y muestra anterior.
$previa = @{}; foreach ($p in (Get-Content "$root\..\resumen-fast-vs-deep\privado\muestra.json" -Raw -Encoding utf8 | ConvertFrom-Json)) { $previa[[string]$p.id] = $true }
$pob = @($todos | Where-Object { -not $motivoDe[$_.id] -and -not $previa[$_.id] })
$poblacion = [ordered]@{
  puntos = $todos.Count
  excluidos_por_filtro = @($todos | Where-Object { $motivoDe[$_.id] }).Count
  excluidos_por_motivo = [ordered]@{}
  muestra_anterior_que_pasa_el_filtro = @($todos | Where-Object { $previa[$_.id] -and -not $motivoDe[$_.id] }).Count
  poblacion = $pob.Count }
foreach ($g in ($todos | Where-Object { $motivoDe[$_.id] } | Group-Object { $motivoDe[$_.id] } | Sort-Object Name)) { $poblacion.excluidos_por_motivo[$g.Name] = $g.Count }
$poblacion | ConvertTo-Json | Set-Content -Encoding utf8NoBOM "$PSScriptRoot\poblacion.json"
$poblacion | ConvertTo-Json | Write-Host

# 4. Reparto y seleccion, identicos a ../resumen-fast-vs-deep/privado/01-muestra.ps1.
$strata = @($pob | Group-Object { '{0}|{1}' -f ($_.relative_path -split '/')[1], $_.language } |
  ForEach-Object { [pscustomobject]@{ Key = $_.Name; Size = $_.Count; Ids = @($_.Group.id) } } | Sort-Object Key)
$elig = @($strata | Where-Object Size -ge $MIN)
$fixed = @{}
do {
  $changed = $false
  $free = @($elig | Where-Object { -not $fixed.ContainsKey($_.Key) })
  $R = $N - $MIN * $fixed.Count; $T = ($free | Measure-Object Size -Sum).Sum
  foreach ($s in $free) { if ($R * $s.Size / $T -lt $MIN) { $fixed[$s.Key] = $true; $changed = $true } }
} while ($changed)
$alloc = @{}
foreach ($s in $strata) { $alloc[$s.Key] = 0 }
foreach ($k in $fixed.Keys) { $alloc[$k] = $MIN }
$free = @($elig | Where-Object { -not $fixed.ContainsKey($_.Key) })
$R = $N - $MIN * $fixed.Count; $T = ($free | Measure-Object Size -Sum).Sum
$rem = foreach ($s in $free) { $q = $R * $s.Size / $T; $alloc[$s.Key] = [math]::Floor($q); [pscustomobject]@{ Key = $s.Key; Frac = $q - [math]::Floor($q) } }
$left = $R - ($free | ForEach-Object { $alloc[$_.Key] } | Measure-Object -Sum).Sum
$rem | Sort-Object @{e='Frac';Descending=$true}, @{e='Key';Descending=$false} | Select-Object -First $left | ForEach-Object { $alloc[$_.Key]++ }

$porId = @{}; foreach ($p in $pob) { $porId[$p.id] = $p }
$chosen = [System.Collections.Generic.List[object]]::new()
$report = foreach ($s in ($strata | Sort-Object Key)) {
  $k = [int]$alloc[$s.Key]
  $ids = [string[]]$s.Ids; [Array]::Sort($ids, [StringComparer]::Ordinal)
  $rng = [System.Random]::new($SEED)
  for ($i = 0; $i -lt $k; $i++) { $j = $rng.Next($i, $ids.Length); $t = $ids[$i]; $ids[$i] = $ids[$j]; $ids[$j] = $t; $chosen.Add([pscustomobject]@{ id = $ids[$i]; estrato = $s.Key }) }
  [pscustomobject]@{ estrato = $s.Key; tamano = $s.Size; asignados = $k }
}
$report | Format-Table -AutoSize | Out-String | Write-Host
$report | ConvertTo-Json | Set-Content -Encoding utf8NoBOM "$PSScriptRoot\reparto.json"
$muestra = foreach ($c in $chosen) {
  $p = $porId[$c.id]
  [ordered]@{ id = $c.id; estrato = $c.estrato; language = $p.language; relative_path = $p.relative_path; chunk_type = $p.chunk_type
    class_name = $p.class_name; method_name = $p.method_name; start_line = $p.start_line; end_line = $p.end_line; content = $p.content }
}
@($muestra) | ConvertTo-Json -Depth 4 | Set-Content -Encoding utf8NoBOM "$PSScriptRoot\muestra.json"
"muestra.json: $(@($muestra).Count) chunks; content vacio: $(@($muestra | Where-Object { -not $_.content }).Count)" | Write-Host
