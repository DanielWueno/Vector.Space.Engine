# Sonda: deep con el pensamiento por defecto / activado y max_tokens amplio. Mide, no infiere.
# Misma peticion que LlmResumenBrain salvo chat_template_kwargs y max_tokens. 5 llamadas.
$ErrorActionPreference = 'Stop'
$root = 'C:\Users\dsoft03.corp\Experimentos\resumen-fast-vs-deep'
$out = "$root\privado\sonda-thinking.jsonl"
$utf8 = [Text.UTF8Encoding]::new($false)
$key = $env:ALTHEIA_KEY; if (-not $key) { $key = [Environment]::GetEnvironmentVariable('ALTHEIA_KEY', 'User') }
$cert = $env:ALTHEIA_CERT; if (-not $cert) { $cert = [Environment]::GetEnvironmentVariable('ALTHEIA_CERT', 'User') }

# Mismo prompt de sistema que 02-generar.ps1 (alli verificado contra el literal del motor).
$gen = [IO.File]::ReadAllText("$root\privado\02-generar.ps1", $utf8)
$system = Invoke-Expression ([regex]::Match($gen, '(?m)^\$system = (".*")$').Groups[1].Value)
if (-not $system.EndsWith('SIN_CONTENIDO_DE_NEGOCIO')) { throw 'prompt no cargado' }

$muestra = Get-Content "$root\privado\muestra.json" -Raw -Encoding utf8 | ConvertFrom-Json
$porLong = @($muestra | Sort-Object { $_.content.Length }, id)
$corto = $porLong[0]; $medio = $porLong[[int]($porLong.Count / 2)]; $largo = $porLong[-1]

# (etiqueta, chunk, kwargs: $null = no se envia, max_tokens)
$casos = @(
  @('A-defecto-160', $medio, $null, 160),
  @('B-defecto-4096', $medio, $null, 4096),
  @('C-thinking-4096', $corto, $true, 4096),
  @('D-thinking-4096', $medio, $true, 4096),
  @('E-thinking-4096', $largo, $true, 4096)
)

$hdr = New-TemporaryFile
try {
  [IO.File]::WriteAllText($hdr.FullName, "Authorization: Bearer $key", $utf8)
  foreach ($c in $casos) {
    $chunk = $c[1]
    $req = [ordered]@{ model = 'qwen3.8-27b-deep'
      messages = @([ordered]@{ role = 'system'; content = $system }, [ordered]@{ role = 'user'; content = "[Contexto: Lenguaje: $($chunk.language), Ruta: $($chunk.relative_path)]`n`n$($chunk.content)" })
      temperature = 0.15; top_p = 0.95; max_tokens = $c[3] }
    if ($null -ne $c[2]) { $req.chat_template_kwargs = [ordered]@{ enable_thinking = $c[2] } }
    $bodyFile = New-TemporaryFile; $respFile = New-TemporaryFile
    try {
      [IO.File]::WriteAllText($bodyFile.FullName, ($req | ConvertTo-Json -Depth 6 -Compress), $utf8)
      $sw = [Diagnostics.Stopwatch]::StartNew()
      $code = curl.exe -s --max-time 600 --cacert $cert -H "@$($hdr.FullName)" -H 'Content-Type: application/json' `
        --data-binary "@$($bodyFile.FullName)" -o $respFile.FullName -w '%{http_code}' https://altheia/v1/chat/completions
      $sw.Stop()
      $raw = [IO.File]::ReadAllText($respFile.FullName, $utf8)
      $rec = [ordered]@{ caso = $c[0]; id = $chunk.id; contenido_chars = $chunk.content.Length; http = [int]$code; ms = $sw.ElapsedMilliseconds }
      try {
        $j = $raw | ConvertFrom-Json
        $msg = $j.choices[0].message
        $content = [string]$msg.content
        $reason = if ($msg.PSObject.Properties['reasoning_content']) { [string]$msg.reasoning_content } elseif ($msg.PSObject.Properties['reasoning']) { [string]$msg.reasoning } else { $null }
        $rec.finish_reason = $j.choices[0].finish_reason
        $rec.prompt_tokens = $j.usage.prompt_tokens; $rec.completion_tokens = $j.usage.completion_tokens; $rec.total_tokens = $j.usage.total_tokens
        $rec.reasoning_tokens_reportados = $j.usage.completion_tokens_details.reasoning_tokens
        $rec.campos_message = ($msg.PSObject.Properties.Name -join ',')
        $rec.content_chars = $content.Length
        $rec.content_tiene_think = $content.Contains('<think>') -or $content.Contains('</think>')
        $rec.reasoning_chars = if ($null -ne $reason) { $reason.Length } else { $null }
        $rec.texto = $content.Trim()
        $rec.razonamiento = $reason
        if ($j.error) { $rec.error = [string]$j.error.message }
      } catch { $rec.error = "respuesta no JSON (longitud $($raw.Length)): " + $raw.Substring(0, [math]::Min(300, $raw.Length)) }
      [IO.File]::AppendAllText($out, ($rec | ConvertTo-Json -Compress -Depth 3) + "`n", $utf8)
      $show = [ordered]@{}; foreach ($k in $rec.Keys) { if ($k -ne 'razonamiento') { $show[$k] = $rec[$k] } }
      [pscustomobject]$show | Format-List | Out-String | Write-Host
    } finally { Remove-Item $bodyFile.FullName, $respFile.FullName -Force -ErrorAction SilentlyContinue }
  }
} finally { Remove-Item $hdr.FullName -Force -ErrorAction SilentlyContinue }
