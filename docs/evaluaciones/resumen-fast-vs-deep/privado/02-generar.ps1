# Genera resumenes con fast y deep usando la peticion exacta de LlmResumenBrain. Reanudable.
# La clave solo vive en memoria y en un archivo temporal de cabecera que se borra en finally.
param([int]$Limite = 100)
$ErrorActionPreference = 'Stop'
$root = 'C:\Users\dsoft03.corp\Experimentos\resumen-fast-vs-deep'
$out = "$root\privado\resultados.jsonl"
$utf8 = [Text.UTF8Encoding]::new($false)

$key = $env:ALTHEIA_KEY; if (-not $key) { $key = [Environment]::GetEnvironmentVariable('ALTHEIA_KEY', 'User') }
$cert = $env:ALTHEIA_CERT; if (-not $cert) { $cert = [Environment]::GetEnvironmentVariable('ALTHEIA_CERT', 'User') }
if (-not $key -or -not $cert -or -not (Test-Path $cert)) { throw 'Falta ALTHEIA_KEY o ALTHEIA_CERT' }

$system = "Eres un analista de negocio que traduce fragmentos de código — en cualquier lenguaje de`nprogramación — a descripciones de comportamiento para usuarios sin conocimiento técnico.`n`nNo expliques sintaxis, nombres de frameworks ni construcciones del lenguaje. A partir de tu`npropio conocimiento de cómo funciona el lenguaje indicado en el contexto, identifica QUÉ hace`neste fragmento, QUÉ dato o acción involucra, y QUÉ regla o condición aplica — en el lenguaje`nque usaría alguien que jamás vio código.`n`nResponde en español, en 1 a 3 oraciones (máximo 60 palabras). Empieza nombrando la entidad,`npantalla o archivo, seguido de dos puntos — sin frases como `"este componente`" o `"esta clase`nrepresenta`". No inventes campos, reglas o comportamientos que no estén explícitos en el`nfragmento: si no está en el código, no existe.`n`nSi el fragmento es puramente técnico sin significado de negocio visible (imports,`nconfiguración, boilerplate, getters/setters triviales), responde exactamente:`nSIN_CONTENIDO_DE_NEGOCIO"
# Comprobacion: mismo hash que el literal del motor (archivo con LF, sangria comun de 8 espacios eliminada).
$src = [IO.File]::ReadAllText('D:\git\Vector.Space.Engine\src\Reyma.VectorSpace.Engine.Microservice.Infrastructure\Resumen\LlmResumenBrain.cs', $utf8)
$i0 = $src.IndexOf('SystemPrompt = """') + 'SystemPrompt = """'.Length + 1
$i1 = $src.IndexOf('        """;', $i0)
$motor = (($src.Substring($i0, $i1 - $i0).TrimEnd("`n") -split "`n") | ForEach-Object { if ($_.Length -ge 8) { $_.Substring(8) } else { $_.TrimStart() } }) -join "`n"
if ($motor -cne $system) { throw 'El prompt del sistema no coincide con el literal del motor' }

$muestra = Get-Content "$root\privado\muestra.json" -Raw -Encoding utf8 | ConvertFrom-Json
$done = @{}
if (Test-Path $out) {
  foreach ($l in [IO.File]::ReadAllLines($out, $utf8)) { $r = $l | ConvertFrom-Json; if ($r.final) { $done["$($r.id)|$($r.modelo)"] = $true } }
}

function Invoke-Llm($chunk, $modelo, $hdr) {
  $user = "[Contexto: Lenguaje: $($chunk.language), Ruta: $($chunk.relative_path)]`n`n$($chunk.content)"
  $req = [ordered]@{
    model = "qwen3.8-27b-$modelo"
    messages = @([ordered]@{ role = 'system'; content = $system }, [ordered]@{ role = 'user'; content = $user })
    temperature = 0.15; top_p = 0.95; max_tokens = 160
  }
  if ($modelo -eq 'deep') { $req.chat_template_kwargs = [ordered]@{ enable_thinking = $false } }
  $bodyFile = New-TemporaryFile; $respFile = New-TemporaryFile
  try {
    [IO.File]::WriteAllText($bodyFile.FullName, ($req | ConvertTo-Json -Depth 6 -Compress), $utf8)
    $sw = [Diagnostics.Stopwatch]::StartNew()
    $code = curl.exe -s --max-time 180 --cacert $cert -H "@$hdr" -H 'Content-Type: application/json' `
      --data-binary "@$($bodyFile.FullName)" -o $respFile.FullName -w '%{http_code}' https://altheia/v1/chat/completions
    $sw.Stop()
    $raw = [IO.File]::ReadAllText($respFile.FullName, $utf8)
    $rec = [ordered]@{ id = $chunk.id; modelo = $modelo; http = [int]$code; ms = $sw.ElapsedMilliseconds;
      total_tokens = $null; completion_tokens = $null; prompt_tokens = $null; finish_reason = $null; texto = $null; error = $null }
    try {
      $j = $raw | ConvertFrom-Json
      $rec.total_tokens = $j.usage.total_tokens; $rec.completion_tokens = $j.usage.completion_tokens; $rec.prompt_tokens = $j.usage.prompt_tokens
      $rec.finish_reason = $j.choices[0].finish_reason
      $c = $j.choices[0].message.content; $rec.texto = if ($null -ne $c) { $c.Trim() } else { '' }
      if ($j.error) { $rec.error = [string]$j.error.message }
    } catch { $rec.error = "respuesta no JSON (longitud $($raw.Length))" }
    return $rec
  } finally { Remove-Item $bodyFile.FullName, $respFile.FullName -Force -ErrorAction SilentlyContinue }
}

$hdrFile = New-TemporaryFile
try {
  [IO.File]::WriteAllText($hdrFile.FullName, "Authorization: Bearer $key", $utf8)
  $n = 0
  for ($k = 0; $k -lt [math]::Min($Limite, $muestra.Count); $k++) {
    $chunk = $muestra[$k]
    $orden = if ($k % 2 -eq 0) { @('fast', 'deep') } else { @('deep', 'fast') }
    foreach ($modelo in $orden) {
      if ($done["$($chunk.id)|$modelo"]) { continue }
      for ($intento = 1; $intento -le 2; $intento++) {
        $rec = Invoke-Llm $chunk $modelo $hdrFile.FullName
        $ok = $rec.http -eq 200
        $rec.intento = $intento; $rec.orden = [array]::IndexOf($orden, $modelo) + 1; $rec.final = ($ok -or $intento -eq 2)
        [IO.File]::AppendAllText($out, ($rec | ConvertTo-Json -Compress -Depth 3) + "`n", $utf8)
        $n++
        if ($ok) { break }
      }
    }
    if (($k + 1) % 10 -eq 0) { Write-Host "chunks $($k + 1)/$($muestra.Count), llamadas en esta corrida: $n" }
  }
  Write-Host "fin; llamadas en esta corrida: $n"
} finally { Remove-Item $hdrFile.FullName -Force -ErrorAction SilentlyContinue }
