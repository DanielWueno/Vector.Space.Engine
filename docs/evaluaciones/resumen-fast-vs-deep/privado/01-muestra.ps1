# Muestra estratificada determinista (N=100) de reyoneweb. Solo lectura sobre Qdrant.
$ErrorActionPreference = 'Stop'
$root = 'C:\Users\dsoft03.corp\Experimentos\resumen-fast-vs-deep'
$N = 100; $MIN = 3; $SEED = 20261111
$all = Get-Content "$root\privado\coleccion-payload.json" -Raw | ConvertFrom-Json

$strata = @($all | Group-Object { '{0}|{1}' -f ($_.relative_path -split '/')[1], $_.language } |
  ForEach-Object { [pscustomobject]@{ Key = $_.Name; Size = $_.Count; Ids = @($_.Group.id) } } | Sort-Object Key)

# Reparto: estratos con >=MIN puntos son elegibles. Se fijan en MIN los que tendrian cuota < MIN
# (iterativo); el resto de N se reparte proporcionalmente con resto mayor (Hamilton).
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

# Seleccion: por estrato (orden ordinal de clave), ids en orden ordinal, Random(SEED) nuevo por
# estrato, Fisher-Yates parcial tomando los primeros k.
$chosen = [System.Collections.Generic.List[object]]::new()
$report = foreach ($s in ($strata | Sort-Object Key)) {
  $k = [int]$alloc[$s.Key]
  $ids = [string[]]$s.Ids; [Array]::Sort($ids, [StringComparer]::Ordinal)
  $rng = [System.Random]::new($SEED)
  for ($i = 0; $i -lt $k; $i++) { $j = $rng.Next($i, $ids.Length); $t = $ids[$i]; $ids[$i] = $ids[$j]; $ids[$j] = $t; $chosen.Add([pscustomobject]@{ id = $ids[$i]; estrato = $s.Key }) }
  [pscustomobject]@{ estrato = $s.Key; tamano = $s.Size; asignados = $k }
}
$report | Format-Table -AutoSize | Out-String | Write-Host
"total elegidos: $($chosen.Count)" | Write-Host
$report | ConvertTo-Json | Set-Content -Encoding utf8NoBOM "$root\privado\reparto.json"

# Recuperar payload completo de los elegidos (points get por ids).
$body = @{ ids = @($chosen.id); with_payload = @{ include = @('content','language','relative_path','chunk_type','class_name','method_name','start_line','end_line') }; with_vector = $false } | ConvertTo-Json -Depth 4
$r = Invoke-RestMethod -Method Post -Uri 'http://localhost:6333/collections/reyoneweb/points' -ContentType 'application/json; charset=utf-8' -Body ([Text.Encoding]::UTF8.GetBytes($body))
$byId = @{}; foreach ($p in $r.result) { $byId[[string]$p.id] = $p.payload }
$muestra = foreach ($c in $chosen) {
  $p = $byId[$c.id]; if (-not $p) { throw "falta payload $($c.id)" }
  [ordered]@{ id = $c.id; estrato = $c.estrato; language = $p.language; relative_path = $p.relative_path; chunk_type = $p.chunk_type;
    class_name = $p.class_name; method_name = $p.method_name; start_line = $p.start_line; end_line = $p.end_line; content = $p.content }
}
@($muestra) | ConvertTo-Json -Depth 4 | Set-Content -Encoding utf8NoBOM "$root\privado\muestra.json"
"muestra.json: $(@($muestra).Count) chunks; content vacio: $(@($muestra | Where-Object { -not $_.content }).Count)" | Write-Host
