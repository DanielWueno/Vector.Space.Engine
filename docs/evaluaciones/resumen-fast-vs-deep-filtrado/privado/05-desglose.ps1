# Desglose descriptivo posterior al descifrado (no cambia la decision): utilidad y preferencia por categoria de centinela
# y por negocio_visible segun cada juez. Ver ../informe.md.
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$S = 'SIN_CONTENIDO_DE_NEGOCIO'
$mapa = Get-Content "$PSScriptRoot\mapa.json" -Raw -Encoding utf8 | ConvertFrom-Json -AsHashtable
$pares = @{}; foreach ($x in (Get-Content "$root\jueces\pares-ciegos.json" -Raw -Encoding utf8 | ConvertFrom-Json)) { $pares[$x.par] = $x }
$juicios = @{}
foreach ($juez in 'j5', 'j6') { $juicios[$juez] = @{}; foreach ($e in (Get-Content "$root\jueces\juicio-$juez.json" -Raw -Encoding utf8 | ConvertFrom-Json)) { $juicios[$juez][$e.par] = $e } }

$filas = foreach ($par in ($mapa.Keys | Sort-Object)) {
  $lf = if ($mapa[$par].A -eq 'fast') { 'A' } else { 'B' }; $ld = if ($lf -eq 'A') { 'B' } else { 'A' }
  $tf = $pares[$par].$lf; $td = $pares[$par].$ld
  $cat = if ($tf -ceq $S -and $td -ceq $S) { 'ambos centinela' } elseif ($tf -ceq $S) { 'solo fast centinela' } elseif ($td -ceq $S) { 'solo deep centinela' } else { 'ambos resumen' }
  foreach ($juez in 'j5', 'j6') {
    $e = $juicios[$juez][$par]; $pl = ([string]$e.preferido).Trim().ToUpperInvariant()
    $g = if ($pl -eq $lf) { 'fast' } elseif ($pl -eq $ld) { 'deep' } else { 'empate' }
    [pscustomobject]@{ par = $par; juez = $juez; cat = $cat; negocio = [bool]$e.negocio_visible
      uf = [double]$e.$lf.puntos.utilidad_negocio; ud = [double]$e.$ld.puntos.utilidad_negocio; g = $g; ruta = $pares[$par].ruta }
  }
}
function Linea($nombre, $a) { '{0,-34} n={1,3}  utilidad fast={2,4} deep={3,4}  preferido fast/deep={4}/{5}' -f $nombre, @($a).Count, ($a | Measure-Object uf -Sum).Sum, ($a | Measure-Object ud -Sum).Sum, @($a | Where-Object g -eq 'fast').Count, @($a | Where-Object g -eq 'deep').Count }
'== Por categoria de centinela'
foreach ($g in ($filas | Group-Object juez, cat | Sort-Object Name)) { Linea $g.Name $g.Group }
'== Por negocio_visible (segun el juez)'
foreach ($g in ($filas | Group-Object juez, negocio | Sort-Object Name)) { Linea $g.Name $g.Group }
'== Pares sin negocio visible para ambos jueces'
$filas | Where-Object { -not $_.negocio } | Group-Object par | Where-Object Count -eq 2 | ForEach-Object { '{0} [{1}] {2}' -f $_.Name, $_.Group[0].cat, $_.Group[0].ruta }
'== Solo fast centinela'
$filas | Where-Object cat -eq 'solo fast centinela' | ForEach-Object { '{0} {1} negocio={2} utilidad fast={3} deep={4} preferido={5} {6}' -f $_.par, $_.juez, $_.negocio, $_.uf, $_.ud, $_.g, $_.ruta }
