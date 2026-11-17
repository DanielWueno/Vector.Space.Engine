# Metricas automaticas por modelo y construccion de pares ciegos (Random(7)).
$ErrorActionPreference = 'Stop'
$root = 'C:\Users\dsoft03.corp\Experimentos\resumen-fast-vs-deep'
$utf8 = [Text.UTF8Encoding]::new($false)
$S = 'SIN_CONTENIDO_DE_NEGOCIO'
$muestra = Get-Content "$root\privado\muestra.json" -Raw -Encoding utf8 | ConvertFrom-Json
$todas = [IO.File]::ReadAllLines("$root\privado\resultados.jsonl", $utf8) | Where-Object { $_ } | ForEach-Object { $_ | ConvertFrom-Json }
$final = @{}; foreach ($r in $todas) { if ($r.final) { $final["$($r.id)|$($r.modelo)"] = $r } }

function Pct($xs, $p) { $s = @($xs | Sort-Object); if (-not $s.Count) { return $null }; $s[[math]::Min($s.Count - 1, [math]::Ceiling($p * $s.Count) - 1)] }
function Median($xs) { $s = @($xs | Sort-Object); $n = $s.Count; if (-not $n) { return $null }; if ($n % 2) { $s[($n - 1) / 2] } else { ($s[$n / 2 - 1] + $s[$n / 2]) / 2 } }

$metricas = [ordered]@{}
foreach ($m in 'fast', 'deep') {
  $llamadas = @($todas | Where-Object modelo -eq $m)
  $f = @($muestra | ForEach-Object { $final["$($_.id)|$m"] } | Where-Object { $_ })
  $ok = @($f | Where-Object http -eq 200)
  $txt = @($ok | Where-Object { $_.texto })
  $neg = @($txt | Where-Object { $_.texto -cne $S })
  $pal = @($neg | ForEach-Object { @($_.texto -split '\s+' | Where-Object { $_ }).Count })
  $ora = @($neg | ForEach-Object { @([regex]::Split($_.texto.Trim(), '(?<=[.!?])\s+') | Where-Object { $_ }).Count })
  $metricas[$m] = [ordered]@{
    chunks_con_registro_final = $f.Count
    llamadas_totales = $llamadas.Count
    reintentos = @($llamadas | Where-Object intento -eq 2).Count
    http_200 = $ok.Count
    vacios = @($ok | Where-Object { -not $_.texto }).Count
    finish_length = @($ok | Where-Object finish_reason -eq 'length').Count
    centinela_exacto = @($txt | Where-Object { $_.texto -ceq $S }).Count
    centinela_prefijo_no_exacto = @($txt | Where-Object { $_.texto.StartsWith($S) -and $_.texto -cne $S }).Count
    resumenes_con_negocio = $neg.Count
    palabras_media = if ($pal.Count) { [math]::Round(($pal | Measure-Object -Average).Average, 1) } else { $null }
    palabras_max = if ($pal.Count) { ($pal | Measure-Object -Maximum).Maximum } else { $null }
    palabras_mas_de_60 = @($pal | Where-Object { $_ -gt 60 }).Count
    oraciones_mas_de_3 = @($ora | Where-Object { $_ -gt 3 }).Count
    empieza_entidad_dos_puntos = @($neg | Where-Object { $_.texto -match '^[^:\n]{1,80}:' }).Count
    latencia_ms_mediana = Median ($ok.ms)
    latencia_ms_p90 = Pct ($ok.ms) 0.9
    completion_tokens_medio = if ($ok.Count) { [math]::Round(($ok.completion_tokens | Measure-Object -Average).Average, 1) } else { $null }
    tokens_totales_todas_las_llamadas = ($llamadas | Measure-Object total_tokens -Sum).Sum
    prompt_tokens_totales = ($llamadas | Measure-Object prompt_tokens -Sum).Sum
    completion_tokens_totales = ($llamadas | Measure-Object completion_tokens -Sum).Sum
  }
}
$identicos = @($muestra | Where-Object { $a = $final["$($_.id)|fast"]; $b = $final["$($_.id)|deep"]; $a -and $b -and $a.texto -and ($a.texto -ceq $b.texto) }).Count
$ambosCentinela = @($muestra | Where-Object { $final["$($_.id)|fast"].texto -ceq $S -and $final["$($_.id)|deep"].texto -ceq $S }).Count
$metricas['comun'] = [ordered]@{ textos_identicos_fast_deep = $identicos; ambos_centinela = $ambosCentinela
  solo_fast_centinela = @($muestra | Where-Object { $final["$($_.id)|fast"].texto -ceq $S -and $final["$($_.id)|deep"].texto -cne $S }).Count
  solo_deep_centinela = @($muestra | Where-Object { $final["$($_.id)|deep"].texto -ceq $S -and $final["$($_.id)|fast"].texto -cne $S }).Count }
$metricas | ConvertTo-Json -Depth 4 | Set-Content -Encoding utf8NoBOM "$root\privado\metricas.json"
$metricas | ConvertTo-Json -Depth 4 | Write-Host

# Cegado: un Random(7) en el orden de muestra.json; Next(2)==0 => A=fast.
$rng = [System.Random]::new(7)
$pares = [System.Collections.Generic.List[object]]::new(); $mapa = [ordered]@{}
for ($k = 0; $k -lt $muestra.Count; $k++) {
  $c = $muestra[$k]; $par = 'P{0:D3}' -f ($k + 1)
  $aEsFast = $rng.Next(2) -eq 0
  $tf = $final["$($c.id)|fast"].texto; $td = $final["$($c.id)|deep"].texto
  if (-not $tf) { $tf = '[sin respuesta]' }; if (-not $td) { $td = '[sin respuesta]' }
  $pares.Add([ordered]@{ par = $par; ruta = $c.relative_path; lenguaje = $c.language; fragmento = $c.content
      A = if ($aEsFast) { $tf } else { $td }; B = if ($aEsFast) { $td } else { $tf } })
  $mapa[$par] = [ordered]@{ id = $c.id; A = if ($aEsFast) { 'fast' } else { 'deep' }; B = if ($aEsFast) { 'deep' } else { 'fast' } }
}
$pares | ConvertTo-Json -Depth 4 | Set-Content -Encoding utf8NoBOM "$root\jueces\pares-ciegos.json"
$mapa | ConvertTo-Json -Depth 4 | Set-Content -Encoding utf8NoBOM "$root\privado\mapa.json"
Write-Host "pares: $($pares.Count); A=fast en $(@($mapa.Values | Where-Object A -eq 'fast').Count)"
