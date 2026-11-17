# Descifra los juicios con mapa.json y aplica la regla de decision fijada de antemano (seccion 8).
# Parametros con los valores de la corrida principal; la comparacion con pensamiento usa
# -Jueces jueces2 -Nombres j3,j4 -ArchivoMapa mapa-thinking.json -Salida resultado-juicio-thinking.json
# (alli la etiqueta 'deep' es deep-thinking; misma regla, ver regla-thinking.md).
param([string]$Jueces = 'jueces', [string[]]$Nombres = @('j1', 'j2'), [string]$ArchivoMapa = 'mapa.json', [string]$Salida = 'resultado-juicio.json', [string]$EtiquetaDeep = 'qwen3.8-27b-deep', [string]$Raiz = 'C:\Users\dsoft03.corp\Experimentos\resumen-fast-vs-deep')
# -Raiz se anadio el 2026-11-17 para la re-prueba con filtro (../../resumen-fast-vs-deep-filtrado); el valor por
# defecto es el de las corridas originales.
$ErrorActionPreference = 'Stop'
$root = $Raiz
$mapa = Get-Content "$root\privado\$ArchivoMapa" -Raw -Encoding utf8 | ConvertFrom-Json -AsHashtable
$pares = Get-Content "$root\$Jueces\pares-ciegos.json" -Raw -Encoding utf8 | ConvertFrom-Json
$ruta = @{}; foreach ($p in $pares) { $ruta[$p.par] = $p.ruta }
$CLASES = 'fragmento', 'codigo', 'inventada', 'contradice'
$PUNTOS = 'fidelidad', 'cobertura', 'utilidad_negocio', 'formato'

function Norm([string]$s) { if ($null -eq $s) { return '' }; $s.Trim().ToLowerInvariant().Normalize([Text.NormalizationForm]::FormD) -replace '\p{Mn}', '' }
function Binom-Cola([int]$n, [int]$k) { # P(X >= k), X ~ Bin(n, 0.5)
  if ($n -eq 0) { return 1.0 }; $p = 0.0; $c = 1.0
  for ($i = 0; $i -le $n; $i++) { if ($i -gt 0) { $c = $c * ($n - $i + 1) / $i }; if ($i -ge $k) { $p += $c } }
  return $p / [math]::Pow(2, $n)
}

$res = [ordered]@{}; $prefPorJuez = @{}
$ja, $jb = $Nombres
foreach ($j in $Nombres) {
  $juicio = Get-Content "$root\$Jueces\juicio-$j.json" -Raw -Encoding utf8 | ConvertFrom-Json
  $ids = @($juicio.par); $faltan = @($mapa.Keys | Where-Object { $_ -notin $ids })
  if ($faltan.Count -or $ids.Count -ne $mapa.Count) { throw "$j : pares faltantes o duplicados ($($faltan -join ','))" }
  $acc = @{}
  foreach ($m in 'fast', 'deep') { $acc[$m] = [ordered]@{ afirmaciones = [ordered]@{}; clase_desconocida = 0; omisiones = 0; centinela_ok_false = 0; puntos = [ordered]@{} }
    foreach ($c in $CLASES) { $acc[$m].afirmaciones[$c] = 0 }; foreach ($q in $PUNTOS) { $acc[$m].puntos[$q] = 0 } }
  $pref = @{ fast = 0; deep = 0; empate = 0 }; $prefPorJuez[$j] = @{}
  foreach ($e in $juicio) {
    foreach ($letra in 'A', 'B') {
      $m = $mapa[$e.par][$letra]; $x = $e.$letra
      foreach ($af in @($x.afirmaciones)) { if (-not $af) { continue }; $c = Norm $af.clase; if ($c -in $CLASES) { $acc[$m].afirmaciones[$c]++ } else { $acc[$m].clase_desconocida++ } }
      $acc[$m].omisiones += @($x.omisiones | Where-Object { $_ }).Count
      if ($x.centinela_ok -eq $false) { $acc[$m].centinela_ok_false++ }
      foreach ($q in $PUNTOS) { $acc[$m].puntos[$q] += [double]$x.puntos.$q }
    }
    $pl = (Norm $e.preferido).ToUpperInvariant()
    $g = if ($pl -eq 'A' -or $pl -eq 'B') { $mapa[$e.par][$pl] } else { 'empate' }
    $pref[$g]++; $prefPorJuez[$j][$e.par] = $g
  }
  $n = $pref.fast + $pref.deep
  $res[$j] = [ordered]@{ fast = $acc.fast; deep = $acc.deep
    preferido = [ordered]@{ fast = $pref.fast; deep = $pref.deep; empate = $pref.empate }
    pares_con_ganador = $n
    deep_pct_entre_ganadores = if ($n) { [math]::Round(100.0 * $pref.deep / $n, 1) } else { $null }
    signos_p_unilateral_deep_mejor = [math]::Round((Binom-Cola $n $pref.deep), 5)
    signos_p_unilateral_fast_mejor = [math]::Round((Binom-Cola $n $pref.fast), 5) }
}

# Combinado
$comb = [ordered]@{}
foreach ($m in 'fast', 'deep') {
  $comb[$m] = [ordered]@{ afirmaciones = [ordered]@{}; puntos = [ordered]@{}
    inventada_mas_contradice = 0; codigo = 0; centinela_ok_false = $res[$ja].$m.centinela_ok_false + $res[$jb].$m.centinela_ok_false
    omisiones = $res[$ja].$m.omisiones + $res[$jb].$m.omisiones }
  foreach ($c in $CLASES) { $comb[$m].afirmaciones[$c] = $res[$ja].$m.afirmaciones[$c] + $res[$jb].$m.afirmaciones[$c] }
  foreach ($q in $PUNTOS) { $comb[$m].puntos[$q] = $res[$ja].$m.puntos[$q] + $res[$jb].$m.puntos[$q] }
  $comb[$m].inventada_mas_contradice = $comb[$m].afirmaciones.inventada + $comb[$m].afirmaciones.contradice
  $comb[$m].codigo = $comb[$m].afirmaciones.codigo
}
$res['combinado'] = $comb

# Acuerdo entre jueces
$disc = @($mapa.Keys | Sort-Object | Where-Object { $prefPorJuez[$ja][$_] -ne $prefPorJuez[$jb][$_] } |
  ForEach-Object { [ordered]@{ par = $_; ruta = $ruta[$_]; $ja = $prefPorJuez[$ja][$_]; $jb = $prefPorJuez[$jb][$_] } })
$res['acuerdo'] = [ordered]@{ pares = $mapa.Count; coinciden = $mapa.Count - $disc.Count
  pct = [math]::Round(100.0 * ($mapa.Count - $disc.Count) / $mapa.Count, 1)
  opuestos_fast_vs_deep = @($disc | Where-Object { $_[$ja] -ne 'empate' -and $_[$jb] -ne 'empate' }).Count
  discrepancias = $disc }

# Regla de decision (seccion 8): deep solo si se cumplen las tres.
$c1 = ($res[$ja].pares_con_ganador -gt 0 -and $res[$ja].deep_pct_entre_ganadores -ge 60) -and ($res[$jb].pares_con_ganador -gt 0 -and $res[$jb].deep_pct_entre_ganadores -ge 60)
$c2 = $comb.deep.inventada_mas_contradice -lt $comb.fast.inventada_mas_contradice
$c3 = $comb.deep.puntos.utilidad_negocio -ge $comb.fast.puntos.utilidad_negocio
$res['decision'] = [ordered]@{
  c1_deep_preferido_60pct_ambos = $c1; c2_menos_inventada_contradice = $c2; c3_utilidad_no_menor = $c3
  modelo = if ($c1 -and $c2 -and $c3) { $EtiquetaDeep } else { 'qwen3.8-27b-fast' } }
$res | ConvertTo-Json -Depth 6 | Set-Content -Encoding utf8NoBOM "$root\privado\$Salida"
$res | ConvertTo-Json -Depth 6 | Write-Host
