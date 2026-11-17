# Genera los resumenes de un brazo (fast | deep-thinking) con la peticion de LlmResumenBrain. Reanudable.
# La clave solo vive en memoria y en archivos temporales de cabecera que se borran al terminar cada llamada.
# Ver ../protocolo.md.
param([ValidateSet('fast', 'deep-thinking')][string]$Brazo, [int]$Paralelo = 4)
$ErrorActionPreference = 'Stop'
$utf8 = [Text.UTF8Encoding]::new($false)
$out = "$PSScriptRoot\resultados-$Brazo.jsonl"

$key = $env:ALTHEIA_KEY; if (-not $key) { $key = [Environment]::GetEnvironmentVariable('ALTHEIA_KEY', 'User') }
$cert = $env:ALTHEIA_CERT; if (-not $cert) { $cert = [Environment]::GetEnvironmentVariable('ALTHEIA_CERT', 'User') }
if (-not $key -or -not $cert -or -not (Test-Path $cert)) { throw 'Falta ALTHEIA_KEY o ALTHEIA_CERT' }

# Prompt del sistema: el literal de 02-generar.ps1 del experimento anterior, verificado otra vez contra el motor.
$prev = [IO.File]::ReadAllText("$PSScriptRoot\..\..\resumen-fast-vs-deep\privado\02-generar.ps1", $utf8)
$system = Invoke-Expression ([regex]::Match($prev, '(?m)^\$system = (".*")$').Groups[1].Value)
$src = [IO.File]::ReadAllText("$PSScriptRoot\..\..\..\..\src\Reyma.VectorSpace.Engine.Microservice.Infrastructure\Resumen\LlmResumenBrain.cs", $utf8).ReplaceLineEndings("`n")
$i0 = $src.IndexOf('SystemPrompt = """') + 'SystemPrompt = """'.Length + 1
$i1 = $src.IndexOf('        """;', $i0)
$motor = (($src.Substring($i0, $i1 - $i0).TrimEnd("`n") -split "`n") | ForEach-Object { if ($_.Length -ge 8) { $_.Substring(8) } else { $_.TrimStart() } }) -join "`n"
if ($motor -cne $system) { throw 'El prompt del sistema no coincide con el literal del motor' }

$muestra = Get-Content "$PSScriptRoot\muestra.json" -Raw -Encoding utf8 | ConvertFrom-Json
$done = @{}
if (Test-Path $out) { foreach ($l in [IO.File]::ReadAllLines($out, $utf8)) { if ($l) { $r = $l | ConvertFrom-Json; if ($r.final) { $done[$r.id] = $true } } } }
$pendientes = @($muestra | Where-Object { -not $done[$_.id] })
Write-Host "$Brazo : pendientes $($pendientes.Count) de $($muestra.Count)"

$pendientes | ForEach-Object -ThrottleLimit $Paralelo -Parallel {
  $chunk = $_; $brazo = $using:Brazo; $key = $using:key; $cert = $using:cert; $system = $using:system
  $utf8 = [Text.UTF8Encoding]::new($false)
  $req = [ordered]@{ model = if ($brazo -eq 'fast') { 'qwen3.8-27b-fast' } else { 'qwen3.8-27b-deep' }
    messages = @([ordered]@{ role = 'system'; content = $system }, [ordered]@{ role = 'user'; content = "[Contexto: Lenguaje: $($chunk.language), Ruta: $($chunk.relative_path)]`n`n$($chunk.content)" })
    temperature = 0.15; top_p = 0.95; max_tokens = if ($brazo -eq 'fast') { 160 } else { 4096 } }
  if ($brazo -ne 'fast') { $req.chat_template_kwargs = [ordered]@{ enable_thinking = $true } }
  $lineas = @()
  for ($intento = 1; $intento -le 2; $intento++) {
    $hdr = New-TemporaryFile; $bodyFile = New-TemporaryFile; $respFile = New-TemporaryFile
    try {
      [IO.File]::WriteAllText($hdr.FullName, "Authorization: Bearer $key", $utf8)
      [IO.File]::WriteAllText($bodyFile.FullName, ($req | ConvertTo-Json -Depth 6 -Compress), $utf8)
      $sw = [Diagnostics.Stopwatch]::StartNew()
      $code = curl.exe -s --max-time 600 --cacert $cert -H "@$($hdr.FullName)" -H 'Content-Type: application/json' `
        --data-binary "@$($bodyFile.FullName)" -o $respFile.FullName -w '%{http_code}' https://altheia/v1/chat/completions
      $sw.Stop()
      $raw = [IO.File]::ReadAllText($respFile.FullName, $utf8)
      $rec = [ordered]@{ id = $chunk.id; modelo = $brazo; http = [int]$code; ms = $sw.ElapsedMilliseconds
        prompt_tokens = $null; completion_tokens = $null; total_tokens = $null; finish_reason = $null
        texto = $null; reasoning_chars = $null; error = $null }
      try {
        $j = $raw | ConvertFrom-Json; $msg = $j.choices[0].message
        $rec.prompt_tokens = $j.usage.prompt_tokens; $rec.completion_tokens = $j.usage.completion_tokens; $rec.total_tokens = $j.usage.total_tokens
        $rec.finish_reason = $j.choices[0].finish_reason
        $rec.texto = if ($null -ne $msg.content) { ([string]$msg.content).Trim() } else { '' }
        if ($msg.PSObject.Properties['reasoning'] -and $null -ne $msg.reasoning) { $rec.reasoning_chars = ([string]$msg.reasoning).Length }
        if ($j.error) { $rec.error = [string]$j.error.message }
      } catch { $rec.error = "respuesta no JSON (longitud $($raw.Length))" }
      $ok = $rec.http -eq 200
      $rec.intento = $intento; $rec.final = ($ok -or $intento -eq 2)
      $lineas += ($rec | ConvertTo-Json -Compress -Depth 3)
      if ($ok) { break }
    } finally { Remove-Item $hdr.FullName, $bodyFile.FullName, $respFile.FullName -Force -ErrorAction SilentlyContinue }
  }
  $lineas
} | ForEach-Object { [IO.File]::AppendAllText($out, $_ + "`n", $utf8) }

$fin = @([IO.File]::ReadAllLines($out, $utf8) | Where-Object { $_ } | ForEach-Object { $_ | ConvertFrom-Json } | Where-Object final)
Write-Host "$Brazo : finales $($fin.Count); http 200 $(@($fin | Where-Object http -eq 200).Count); tokens $(($fin | Measure-Object total_tokens -Sum).Sum)"
