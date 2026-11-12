# deep con enable_thinking=true y max_tokens=4096 sobre los 100 chunks. Reanudable. Ver regla-thinking.md.
param([int]$Limite = 100)
$ErrorActionPreference = 'Stop'
$root = 'C:\Users\dsoft03.corp\Experimentos\resumen-fast-vs-deep'
$out = "$root\privado\resultados-thinking.jsonl"
$utf8 = [Text.UTF8Encoding]::new($false)
$key = $env:ALTHEIA_KEY; if (-not $key) { $key = [Environment]::GetEnvironmentVariable('ALTHEIA_KEY', 'User') }
$cert = $env:ALTHEIA_CERT; if (-not $cert) { $cert = [Environment]::GetEnvironmentVariable('ALTHEIA_CERT', 'User') }
if (-not $key -or -not $cert -or -not (Test-Path $cert)) { throw 'Falta ALTHEIA_KEY o ALTHEIA_CERT' }

# Mismo prompt de sistema que 02-generar.ps1 (alli verificado contra el literal del motor).
$gen = [IO.File]::ReadAllText("$root\privado\02-generar.ps1", $utf8)
$system = Invoke-Expression ([regex]::Match($gen, '(?m)^\$system = (".*")$').Groups[1].Value)
if (-not $system.StartsWith('Eres un analista') -or -not $system.EndsWith('SIN_CONTENIDO_DE_NEGOCIO')) { throw 'prompt no cargado' }

$muestra = Get-Content "$root\privado\muestra.json" -Raw -Encoding utf8 | ConvertFrom-Json
$done = @{}
if (Test-Path $out) { foreach ($l in [IO.File]::ReadAllLines($out, $utf8)) { $r = $l | ConvertFrom-Json; if ($r.final) { $done[$r.id] = $true } } }

$hdr = New-TemporaryFile
try {
  [IO.File]::WriteAllText($hdr.FullName, "Authorization: Bearer $key", $utf8)
  $n = 0
  for ($k = 0; $k -lt [math]::Min($Limite, $muestra.Count); $k++) {
    $chunk = $muestra[$k]
    if ($done[$chunk.id]) { continue }
    $req = [ordered]@{ model = 'qwen3.8-27b-deep'
      messages = @([ordered]@{ role = 'system'; content = $system }, [ordered]@{ role = 'user'; content = "[Contexto: Lenguaje: $($chunk.language), Ruta: $($chunk.relative_path)]`n`n$($chunk.content)" })
      temperature = 0.15; top_p = 0.95; max_tokens = 4096; chat_template_kwargs = [ordered]@{ enable_thinking = $true } }
    for ($intento = 1; $intento -le 2; $intento++) {
      $bodyFile = New-TemporaryFile; $respFile = New-TemporaryFile
      try {
        [IO.File]::WriteAllText($bodyFile.FullName, ($req | ConvertTo-Json -Depth 6 -Compress), $utf8)
        $sw = [Diagnostics.Stopwatch]::StartNew()
        $code = curl.exe -s --max-time 600 --cacert $cert -H "@$($hdr.FullName)" -H 'Content-Type: application/json' `
          --data-binary "@$($bodyFile.FullName)" -o $respFile.FullName -w '%{http_code}' https://altheia/v1/chat/completions
        $sw.Stop()
        $raw = [IO.File]::ReadAllText($respFile.FullName, $utf8)
        $rec = [ordered]@{ id = $chunk.id; modelo = 'deep-thinking'; http = [int]$code; ms = $sw.ElapsedMilliseconds
          prompt_tokens = $null; completion_tokens = $null; total_tokens = $null; finish_reason = $null
          texto = $null; reasoning_chars = $null; razonamiento = $null; error = $null }
        try {
          $j = $raw | ConvertFrom-Json; $msg = $j.choices[0].message
          $rec.prompt_tokens = $j.usage.prompt_tokens; $rec.completion_tokens = $j.usage.completion_tokens; $rec.total_tokens = $j.usage.total_tokens
          $rec.finish_reason = $j.choices[0].finish_reason
          $rec.texto = ([string]$msg.content).Trim()
          if ($msg.PSObject.Properties['reasoning'] -and $null -ne $msg.reasoning) { $rec.razonamiento = [string]$msg.reasoning; $rec.reasoning_chars = $rec.razonamiento.Length }
          if ($j.error) { $rec.error = [string]$j.error.message }
        } catch { $rec.error = "respuesta no JSON (longitud $($raw.Length))" }
        $ok = $rec.http -eq 200
        $rec.intento = $intento; $rec.final = ($ok -or $intento -eq 2)
        [IO.File]::AppendAllText($out, ($rec | ConvertTo-Json -Compress -Depth 3) + "`n", $utf8)
        $n++
        if ($ok) { break }
      } finally { Remove-Item $bodyFile.FullName, $respFile.FullName -Force -ErrorAction SilentlyContinue }
    }
    if (($k + 1) % 10 -eq 0) { Write-Host "chunks $($k + 1)/$($muestra.Count), llamadas en esta corrida: $n" }
  }
  Write-Host "fin; llamadas en esta corrida: $n"
} finally { Remove-Item $hdr.FullName -Force -ErrorAction SilentlyContinue }
