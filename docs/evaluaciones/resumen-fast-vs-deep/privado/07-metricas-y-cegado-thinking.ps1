# Metricas de coste/forma fast vs deep-thinking y pares ciegos Q001..Q100 (Random(11)). Ver regla-thinking.md.
$ErrorActionPreference = 'Stop'
$root = 'C:\Users\dsoft03.corp\Experimentos\resumen-fast-vs-deep'
$utf8 = [Text.UTF8Encoding]::new($false)
$S = 'SIN_CONTENIDO_DE_NEGOCIO'
$muestra = Get-Content "$root\privado\muestra.json" -Raw -Encoding utf8 | ConvertFrom-Json
function Leer($f) { [IO.File]::ReadAllLines($f, $utf8) | Where-Object { $_ } | ForEach-Object { $_ | ConvertFrom-Json } }
$fastTodas = @(Leer "$root\privado\resultados.jsonl" | Where-Object modelo -eq 'fast')
$thkTodas = @(Leer "$root\privado\resultados-thinking.jsonl")
$final = @{}
foreach ($r in $fastTodas) { if ($r.final) { $final["$($r.id)|fast"] = $r } }
foreach ($r in $thkTodas) { if ($r.final) { $final["$($r.id)|deep"] = $r } }

function Pct($xs, $p) { $s = @($xs | Sort-Object); if (-not $s.Count) { return $null }; $s[[math]::Min($s.Count - 1, [math]::Ceiling($p * $s.Count) - 1)] }
function Median($xs) { $s = @($xs | Sort-Object); $n = $s.Count; if (-not $n) { return $null }; if ($n % 2) { $s[($n - 1) / 2] } else { ($s[$n / 2 - 1] + $s[$n / 2]) / 2 } }
function Media($xs) { $a = @($xs); if (-not $a.Count) { return $null }; [math]::Round(($a | Measure-Object -Average).Average, 1) }

$metricas = [ordered]@{}
foreach ($pair in @(@('fast', $fastTodas), @('deep-thinking', $thkTodas))) {
  $m = $pair[0]; $llamadas = @($pair[1]); $clave = if ($m -eq 'fast') { 'fast' } else { 'deep' }
  $f = @($muestra | ForEach-Object { $final["$($_.id)|$clave"] } | Where-Object { $_ })
  $ok = @($f | Where-Object http -eq 200)
  $txt = @($ok | Where-Object { $_.texto })
  $neg = @($txt | Where-Object { $_.texto -cne $S })
  $pal = @($neg | ForEach-Object { @($_.texto -split '\s+' | Where-Object { $_ }).Count })
  $ora = @($neg | ForEach-Object { @([regex]::Split($_.texto.Trim(), '(?<=[.!?])\s+') | Where-Object { $_ }).Count })
  $metricas[$m] = [ordered]@{
    chunks_con_registro_final = $f.Count; llamadas_totales = $llamadas.Count
    reintentos = @($llamadas | Where-Object intento -eq 2).Count
    http_200 = $ok.Count; vacios = @($ok | Where-Object { -not $_.texto }).Count
    finish_length = @($ok | Where-Object finish_reason -eq 'length').Count
    centinela_exacto = @($txt | Where-Object { $_.texto -ceq $S }).Count
    centinela_prefijo_no_exacto = @($txt | Where-Object { $_.texto.StartsWith($S) -and $_.texto -cne $S }).Count
    resumenes_con_negocio = $neg.Count
    palabras_media = Media $pal; palabras_max = if ($pal.Count) { ($pal | Measure-Object -Maximum).Maximum } else { $null }
    palabras_mas_de_60 = @($pal | Where-Object { $_ -gt 60 }).Count
    oraciones_mas_de_3 = @($ora | Where-Object { $_ -gt 3 }).Count
    empieza_entidad_dos_puntos = @($neg | Where-Object { $_.texto -match '^[^:\n]{1,80}:' }).Count
    latencia_ms_media = Media ($ok.ms); latencia_ms_mediana = Median ($ok.ms); latencia_ms_p90 = Pct ($ok.ms) 0.9
    latencia_ms_max = ($ok.ms | Measure-Object -Maximum).Maximum
    latencia_s_total = [math]::Round((($llamadas.ms | Measure-Object -Sum).Sum) / 1000.0, 1)
    completion_tokens_media = Media ($ok.completion_tokens); completion_tokens_mediana = Median ($ok.completion_tokens)
    completion_tokens_p90 = Pct ($ok.completion_tokens) 0.9; completion_tokens_max = ($ok.completion_tokens | Measure-Object -Maximum).Maximum
    prompt_tokens_totales = ($llamadas | Measure-Object prompt_tokens -Sum).Sum
    completion_tokens_totales = ($llamadas | Measure-Object completion_tokens -Sum).Sum
    total_tokens_totales = ($llamadas | Measure-Object total_tokens -Sum).Sum
  }
  if ($m -ne 'fast') {
    $metricas[$m].reasoning_chars_media = Media (@($ok | Where-Object { $null -ne $_.reasoning_chars }).reasoning_chars)
    $metricas[$m].sin_campo_reasoning = @($ok | Where-Object { $null -eq $_.reasoning_chars }).Count
    $cen = @($ok | Where-Object { $_.texto -ceq $S }); $res = @($ok | Where-Object { $_.texto -and $_.texto -cne $S })
    $metricas[$m].completion_tokens_mediana_centinela = Median ($cen.completion_tokens)
    $metricas[$m].completion_tokens_mediana_resumen = Median ($res.completion_tokens)
    $metricas[$m].latencia_ms_mediana_centinela = Median ($cen.ms)
    $metricas[$m].latencia_ms_mediana_resumen = Median ($res.ms)
  }
}
$ids = @($muestra.id)
$metricas['comun'] = [ordered]@{
  textos_identicos = @($ids | Where-Object { $a = $final["$_|fast"].texto; $b = $final["$_|deep"].texto; $a -and ($a -ceq $b) }).Count
  ambos_centinela = @($ids | Where-Object { $final["$_|fast"].texto -ceq $S -and $final["$_|deep"].texto -ceq $S }).Count
  solo_fast_centinela = @($ids | Where-Object { $final["$_|fast"].texto -ceq $S -and $final["$_|deep"].texto -cne $S }).Count
  solo_deep_thinking_centinela = @($ids | Where-Object { $final["$_|deep"].texto -ceq $S -and $final["$_|fast"].texto -cne $S }).Count }
$metricas | ConvertTo-Json -Depth 4 | Set-Content -Encoding utf8NoBOM "$root\privado\metricas-thinking.json"
$metricas | ConvertTo-Json -Depth 4 | Write-Host

# Cegado
New-Item -ItemType Directory -Force "$root\jueces2" | Out-Null
$rng = [System.Random]::new(11)
$pares = [System.Collections.Generic.List[object]]::new(); $mapa = [ordered]@{}
for ($k = 0; $k -lt $muestra.Count; $k++) {
  $c = $muestra[$k]; $par = 'Q{0:D3}' -f ($k + 1)
  $aEsFast = $rng.Next(2) -eq 0
  $tf = $final["$($c.id)|fast"].texto; $td = $final["$($c.id)|deep"].texto
  if (-not $tf) { $tf = '[sin respuesta]' }; if (-not $td) { $td = '[sin respuesta]' }
  $pares.Add([ordered]@{ par = $par; ruta = $c.relative_path; lenguaje = $c.language; fragmento = $c.content
      A = if ($aEsFast) { $tf } else { $td }; B = if ($aEsFast) { $td } else { $tf } })
  $mapa[$par] = [ordered]@{ id = $c.id; A = if ($aEsFast) { 'fast' } else { 'deep' }; B = if ($aEsFast) { 'deep' } else { 'fast' } }
}
$pares | ConvertTo-Json -Depth 4 | Set-Content -Encoding utf8NoBOM "$root\jueces2\pares-ciegos.json"
$mapa | ConvertTo-Json -Depth 4 | Set-Content -Encoding utf8NoBOM "$root\privado\mapa-thinking.json"
Write-Host "pares: $($pares.Count); A=fast en $(@($mapa.Values | Where-Object A -eq 'fast').Count)"
