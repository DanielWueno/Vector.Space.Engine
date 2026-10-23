# 1.7.3 — Ingesta de proyecto completo (URL Git o .zip) y selección de modelo

Fecha: 2026-10-23. Ficha: `1.7.3-decidir-ingesta-de-proyecto-completo-url-git-o-zip-y-seleccion-de-modelo`.
Entorno: host Windows (git 2.53, .NET 10.0.12) y contenedor Linux `mcr.microsoft.com/dotnet/sdk:10.0` (Ubuntu
24.04, sin red) para las pruebas de zip y enlaces; servidor Git de prueba en loopback (`git-http-backend` real) con
copias bare de este repo y de Rag.Context.Engine; API del servicio en Kestrel real con identidad de prueba; Qdrant
1.19.2 y TEI cpu-1.8 (arctic@256) de `docker-compose.motor.yml`; AltheIA real con el cliente HTTP de 1.13.
Herramienta: [`tools/ingesta-proyecto/`](../../../tools/ingesta-proyecto/README.md). Evidencia cruda:
`resultados/*.json`.

## Alcance confirmado al arrancar

Reentrada del 2026-10-23 (`_reentrada_resuelta_2026_10_23` en el ledger): tiempo de máquina sin tope; repo Git
permitido para la prueba: este repo o Rag.Context.Engine (de autoría propia), servidos desde un Git local como host
de la lista blanca, sin credenciales de Azure DevOps; resumen LLM sobre una muestra de uno de esos repos guardada en
disco (`.cache/ingesta-proyecto-1.7.3/resumen-muestra.json`). Durante el trabajo el usuario fijó además: conservar
`git` para remotos **y** `ruta` para cuando el repo ya esté en el servidor; cada proyecto puede traer su
`.ragignore`; arctic (Matryoshka) es el modelo por diseño y bge-m3 se descartó.

## Decisión

1. **Un origen nuevo, `proyecto`, con dos fuentes: `git` (URL) y `zip` (subida).** Las dos producen la misma lista de
   documentos `(ruta, contenido)` que hoy recibe `documentos` y pasan por el mismo pipeline, con las mismas
   identidades: el `.zip` del mismo commit ingerido después del clone da **todo «sin cambios»** (ver (3)). `ruta`
   no se toca y queda reservado (6). `git` y `ruta` comparten el escáner portado: clonar es «traer el repo a un
   directorio temporal y hacer lo que haría `ruta`».
2. **`git`**: lista blanca exacta de orígenes (`https`, host, puerto y prefijo de la organización de Azure DevOps,
   en configuración, sin comodines); `git clone` superficial y endurecido con el git del sistema; identidad **del
   servicio** (cuenta no personal: PAT o Entra ID) inyectada por entorno, nunca la del usuario final; plazo y tope
   de tamaño, con un tope **duro** en el sistema de archivos (el de proceso resultó blando, ver (1)); checkout
   temporal borrado al terminar.
3. **`zip`**: subida de hasta 100 MiB, leída **a documentos en memoria, sin extraer a disco**, con límites de
   entradas, de tamaño real por entrada y total, y de ratio; `carpetaRaiz` explícita para el envoltorio
   `repo-rama/` de las descargas.
4. **Escáner**: se porta `FileSystemIngestionScanner` (66 líneas) más `IgnoreRules` (156) con dos cambios: no
   sigue symlinks ni junctions, y corrige un **bug del origen** por el que las reglas de la raíz que empiezan por
   punto (`.cache/`, `.mcp.json`, `.claude/settings.local.json`) nunca se aplicaban.
5. **Selección de modelo por perfiles registrados, no por nombre libre.** El servicio declara perfiles de embeddings
   (modelo, dimensión, prefijo de consulta, servidor) y de LLM (modelo, `max_tokens`, razonamiento). La colección
   fija su perfil de embeddings **al crearse** y lo guarda en la metadata de Qdrant; una solicitud con otro perfil
   sobre una colección existente da 409, **aunque coincida la dimensión** (hoy no: ver (5)). El LLM del resumen
   es un perfil por defecto de la colección que la solicitud puede sustituir.
6. **Opciones del CLI**: el resumen de negocio se decide al crear la colección (el tercer vector no se puede
   agregar después), con un perfil de LLM que funcione (no `deep` tal cual) y como fase reanudable, porque un repo
   mediano supera la cuota diaria de AltheIA. Perfil y lenguajes son atributos de la colección. El tenant **no** se
   revive como simple etiqueta: la unidad de aislamiento del servicio es la colección (1.3).
7. **La ingesta de proyecto es asíncrona** (`202` + estado): un repo real tarda de minutos a horas en vectorizarse.
   `IngestaResponse` ya trae `ingestaId` y `estado`.

La implementación queda en ítems aparte (ver «Siguientes ítems»).

## (1) Seguridad de clonar una URL arbitraria

**Lista blanca antes de tocar la red** (`resultados/urls-*.json`, igual en Windows y Linux): con la organización
de Azure DevOps como único origen admitido, de 23 URLs se admiten 3 (`dev.azure.com/<org>/…`, con `%20` en el
proyecto, y `<org>.visualstudio.com`) y se rechazan 20. Entre ellas: otra organización; `/<org>/../otra`
(normalizada por `Uri`); `%2e%2e`; `dev.azure.com.atacante.example`; `dev.azure.com@atacante.example` y
`usuario:clave@…`; `atacante.example#@dev.azure.com`; `http://`; otro puerto; `github.com`; `169.254.169.254`;
`127.0.0.1:6333`; `localhost`; `file://`; `ssh://`; la forma scp `git@…:`; `ext::sh -c …`; `--upload-pack=…`;
query; y un salto de línea con `-c core.sshCommand`.

**Clonado endurecido** (`resultados/git.json`, contra el servidor de prueba):

| Caso | Resultado |
|---|---|
| Este repo, permitido | 200 → clone superficial (1 commit) en 2,1 s, 4,7 MB en disco |
| Rag.Context.Engine, permitido | 3,4 s, 55,6 MB en disco (bare completo: 24,7 MB, 246 commits); historial completo: 56,3 MB, casi igual |
| 6 URLs fuera de la lista (github, puerto interno, Qdrant REST, `localhost`, ruta fuera de prefijo, credenciales en la URL) | Rechazadas sin red; 0 peticiones al servidor interno |
| Redirección 302 desde el host permitido hacia el «interno» (otro puerto) | Endurecido (`http.followRedirects=false`): falla en 0,27 s, **0** peticiones al interno. **Git por defecto (`initial`): sigue la redirección y clona desde el interno (3 peticiones)**: es el SSRF que cierra la opción |
| Repo privado sin identidad | Falla en 0,26 s: «could not read Username … terminal prompts disabled»; no pregunta ni llama a Git Credential Manager |
| Repo privado con la identidad del servicio | Clona; la clave **no** aparece en `.git/config` ni en ningún archivo de `.git/`, ni en la línea de comandos (va por `GIT_CONFIG_KEY_0`/`VALUE_0`) |
| `credential.helper` | Por defecto en esta máquina: `manager` (gitconfig del sistema). Con el aislamiento del clonador: vacío |
| Tope de tamaño de 2 MiB (Rag.Context.Engine) | Proceso matado a los 2,5 s, **pero con 21,4 MB ya en disco**: por loopback el pack llega entre dos muestreos de 250 ms |
| Plazo de 5 s (servidor que tarda 120 s) | Matado a los 5,2 s |

Del clonador: `protocol.allow=never` salvo `https`; `core.symlinks=false`; `core.hooksPath` vacío;
`submodule.recurse=false`; `--depth 1 --single-branch --no-tags --no-recurse-submodules`; `GIT_TERMINAL_PROMPT=0`;
`GIT_CONFIG_NOSYSTEM=1` y `GIT_CONFIG_GLOBAL` vacío; `GIT_LFS_SKIP_SMUDGE=1`; `http.lowSpeedLimit/Time`; ningún
`GIT_*` heredado; la URL va después de `--`.

Consecuencias para implementar:

- El **tope de tamaño de proceso es blando**. El duro tiene que ser del sistema de archivos: checkout en un volumen
  temporal con cuota (`tmpfs` con `size=` o un volumen dedicado en el contenedor). El muestreo queda como aviso
  temprano.
- **Git en la imagen**: `aspnet:10.0` (Ubuntu 24.04) no lo trae. Con `apt-get install git`, la imagen pasa de 340 a
  445 MB (+105 MB) y trae git 2.43.0. Al clonar URLs ajenas, mantener git parcheado pasa a ser coste operativo.
- La **identidad del servicio en ADO** es el mismo hueco que 1.5 y 1.7: Basic con contraseña de dominio da 401;
  hace falta un PAT de una cuenta de servicio (vence) o un token de Entra ID. **No verificado contra ADO**: el host
  de la prueba es local. En Windows, aislar del gitconfig del sistema también quita `http.sslBackend=schannel`; en
  el contenedor Linux no aplica (OpenSSL con el almacén del sistema).
- Superficial ahorra poco cuando el historial es chico (Rag.Context.Engine: 55,6 frente a 56,3 MB): lo que ocupa es
  el checkout, no el historial.

## (2) Seguridad de un .zip subido

`resultados/zip-windows.json` y `zip-linux.json` (mismos resultados en los dos):

| Caso | Lector del prototipo (en memoria) | Extracción ingenua `Path.Combine(destino, entrada)` | `ZipFile.ExtractToDirectory` |
|---|---|---|---|
| Zip válido (`git archive` de este repo, 0,98 MB, 313 entradas) | 208 documentos, 41 fuera de perfil, 4,2 MB leídos en 0,02–0,08 s | — | — |
| Zip Slip: `../../`, `docs/../../../`, `..\..\`, `/tmp/…`, `C:/…`, `archivo.md:flujo` | Las 6 rechazadas; queda la legítima | **Escapa**: 3 archivos fuera del destino en Windows (2 en Linux, donde `..\` es un nombre literal); las absolutas caerían en `/tmp` y en `C:\` (no se escribieron) | Lanza `IOException` y **aborta a medias** (la legítima ya escrita) |
| Bomba: 1 GiB de ceros en una entrada (1 MB comprimido) | Declara más de 512 KB: fuera de perfil sin abrirla | — | — |
| Bomba chica: 500 000 ceros (628 B) | Rechazada por ratio 1000:1 sin descomprimir | — | — |
| 1000 entradas de 500 000 ceros (0,6 MB → 477 MiB) | Las 1000 rechazadas por ratio; **sin** el chequeo de ratio la para el tope total (256 MiB) a los 0,5 s | — | — |
| Cabeceras que declaran 100 bytes para 50 MiB reales | .NET corta el flujo en el tamaño declarado: 100 bytes leídos | — | — |
| 30 000 entradas vacías | Rechazado (máximo 20 000) | — | — |
| Entrada `S_IFLNK` apuntando fuera | Rechazada como enlace | — | Se extrae como archivo normal (30 bytes), no como enlace, en Windows y en Linux |
| 100 MiB + 1 byte | Rechazado por tamaño | — | — |

Leer a memoria elimina el Zip Slip por construcción: el nombre de la entrada solo se convierte en la `ruta` del
documento, nunca en una ruta de disco. Los nombres se validan igual, porque esa ruta va al payload y al encabezado
del chunk. Sin `carpetaRaiz`, el envoltorio de `git archive --prefix` / «Descargar zip» queda en todas las rutas
(`Vector.Space.Engine/README.md`): adivinarlo confundía un único `docs/` con el envoltorio, así que es un parámetro
explícito. La subida necesita además `[RequestSizeLimit]` en la acción (mismo patrón que 1.7.1) y bufferizar a un
archivo temporal, porque `ZipArchive` necesita un flujo con búsqueda.

**`.ragignore` del proyecto** (`resultados/ragignore-*.json`, Windows y Linux): el mismo árbol como checkout y como
zip, con `.gitignore` (`bin/`, `*.log`) y `.ragignore` (`docs/privado/`, `!docs/privado/publico.md`, `*.sql`,
`.env.json`, `src/Generado/**`), da en los dos los 3 documentos esperados: `docs/guia.md`,
`docs/privado/publico.md` y `src/App.cs`.

## (3) Coste de portar el escáner

`resultados/escaner.json`:

- Portado contra original, sobre los clones: **mismas rutas y mismo orden** en los dos repos (208 y 320
  documentos), en 0,05–0,09 s. Lo portado son 66 líneas de escáner (frente a 137 del origen, que es async y
  registra) más 156 de `IgnoreRules`, y reutiliza `ScanProfile`, `RawArtifact` y `DocumentArtifacts`, que ya están
  en Infrastructure.
- **Enlaces**: con una junction dentro de la raíz, el escáner original lee `enlace-dir/secreto.md`, que vive fuera;
  el portado lo omite (Windows: junction; Linux: symlink de directorio y de archivo).
- **Bug de `IgnoreRules` en el origen**: `IsIgnored` hace `TrimStart('.', '/')`, que además de `./` se come el punto
  de `.cache`. En el árbol de trabajo de este repo el original lee **1018 archivos de `.cache/`** pese al `.cache/`
  del `.gitignore`, y en Rag.Context.Engine lee `.claude/settings.local.json`. A un clone no le afecta (lo ignorado
  no se versiona), pero sí a `ruta` y a un zip hecho desde una carpeta local. Corregido en el prototipo; el origen
  queda como ítem.
- Clone frente a árbol de trabajo: en el clone solo hay lo versionado (Rag.Context.Engine: 320 frente a 324 del
  árbol, por `.mcp.json`, `.claude/settings.local.json` y dos borradores sin versionar).
- Zip del mismo commit frente al clone: mismas 208 rutas; las 208 difieren **solo en el fin de línea** (`git
  archive` con `autocrlf` del sistema da CRLF; el clone aislado, LF). El pipeline lo absorbe (ver ingesta).

**Ingesta de extremo a extremo** (`resultados/ingesta-rce.json`): URL Git de Rag.Context.Engine → clone → escáner
portado → `POST ingestas` en lotes de 100, contra el servicio real con TEI local (arctic@256):

| Fase | Resultado |
|---|---|
| Clonar (superficial, servidor de prueba) | 3,8 s, 55,6 MB |
| Escanear y leer | 320 documentos, 2,78 MB, 0,57 s |
| Primera pasada | 4 solicitudes, **2104 s (35 min)**, 308 indexados y 12 sin chunks indexables, **2368 puntos** (los mismos que da el chunker sin vectorizar, `chunks-rce.json`), working set máximo 179 MB |
| Segunda pasada | 0,4 s, 320 «sin cambios», mismos 2368 puntos |
| `.zip` del mismo commit (`git archive` con el git del sistema, CRLF), a la **misma** colección | Leído en 0,1 s (18 MB); 0,3 s, **320 «sin cambios»**, mismos 2368 puntos |
| Checkout temporal | Borrado al terminar |

Las dos fuentes producen las mismas identidades: la ruta y el contenido normalizado (el fin de línea no cuenta, como
ya probó 1.4) son la huella. El coste vuelve a ser el de 1.7: los embeddings (~1,1 chunks/s con TEI en CPU), no
clonar ni escanear (menos de 5 s juntos). Por eso la ingesta de proyecto es asíncrona: 35 minutos no caben en una
solicitud HTTP razonable.

Primero se intentó con este repo (`resultados/ingesta-vse-parcial.json`): 2908 chunks, de los que 2219 son JSON
de resultados de evaluaciones (números que se tokenizan casi a un token por carácter, chunks mucho más largos en
tokens reales de lo que estima el chunker, como ya vio 1.11). Llevaba 864 puntos en 21 min y bajando de ritmo, y se
cortó a propósito. Un corpus así se excluye en la práctica con un `.ragignore`.

## (4) Resumen de negocio, lenguaje/perfil y tenant

**Resumen** (`resultados/resumen.json`): 30 chunks de Rag.Context.Engine (`src/**/*.cs`, 1024 tokens, ≥ 400
caracteres, uno de cada k), con el prompt y los parámetros del origen (`OllamaBusinessSummaryGenerator`:
temperatura 0,15, `max_tokens` 160), por el cliente HTTP de 1.13 contra AltheIA:

| Perfil de LLM | Resúmenes | «Sin negocio» | Cortados por longitud | Mediana | Tokens entrada / salida |
|---|---|---|---|---|---|
| `qwen3.8-27b-fast`, 160 | 30/30 | 1 | 0 | 1,88 s | 18 630 / 1 564 |
| `qwen3.8-27b-deep`, 160 (por defecto) | **0/30: todos vacíos** | — | **30** | 4,97 s | 19 710 / 4 800 |
| `qwen3.8-27b-deep`, 160, `enable_thinking=false` | 30/30 | 1 | 0 | 1,76 s | 18 630 / 1 629 |

- `deep` razona por defecto: gasta los 160 tokens exactos (4800 / 30) sin llegar a escribir el resumen. El `deep`
  que 1.13 fijó para el LLM **no sirve tal cual** con el límite del origen. Por eso el perfil de LLM incluye
  `max_tokens` y si razona, no solo el nombre del modelo.
- **Tercer vector**: colección nueva con `dense` + `dense-resumen` (bge-m3 1024d, perfil AltheIA): 29 puntos con
  los dos vectores (el «sin negocio» no lleva resumen), guardados y leídos. Buscar por la banda de resumen con «¿cómo
  se decide qué colecciones puede consultar un usuario?» devuelve primero `CollectionAuthorizationService.cs`
  (0,64). **Agregar `dense-resumen` a una colección que no lo tiene da 400** «Not existing vector name»: el resumen
  se decide al crear la colección, como ya exigía el origen con `--force`.
- **Coste**: unos 670 tokens por chunk (entrada ~620, de los que ~250 son el prompt de sistema, más ~52 de salida) y
  ~1,8 s por chunk en serie. Micro-repo (1734 chunks, 1.7) serían ~1,17 M tokens, **más que el tope diario de 1 M** de
  AltheIA, y ~52 min en serie. El resumen tiene que ser una fase aparte y reanudable (como `resumen_pending` del
  origen), con la caché por `content_hash` + versión de prompt (que incluye el modelo): reingerir sin cambios cuesta
  0 tokens. Gasto de esta prueba: ~65 k tokens de LLM, que son los que cuentan contra la cuota. Según el usuario
  (2026-10-23), los embeddings de AltheIA no consumen cuota (~22 k tokens aquí); no se verificó, y corrige lo
  anotado en 1.7 el 2026-10-22, que decía que sí contaban.

**Lenguaje y perfil**:

- `--lang` del CLI **nunca funcionó**: `IngestCommand.Settings.LanguageFilter` se declara y no se pasa a
  `IngestionRequest` (1 sola aparición). No hay nada que portar; es nuevo.
- Coste: un filtro de extensiones sobre la salida del escáner o del lector de zip (trivial). Efecto medido: en
  Rag.Context.Engine, C# son 222 de 320 documentos, Markdown 52, texto plano 44 y SQL y XAML 1 cada uno; en este
  repo, C# 103 de 208, y el texto plano (81 documentos, 3,3 MB de JSON de resultados) es la mayor parte del
  contenido.
- **Atributo de la colección, no de la solicitud**: el origen `proyecto` conoce el árbol completo, así que puede
  barrer los documentos que ya no existen (lo que `documentos` resuelve con contenido vacío). Con un filtro distinto
  en cada solicitud, el barrido borraría los documentos de los otros lenguajes. Perfil y lenguajes se guardan en la
  metadata de la colección y el barrido se acota a ese alcance. El barrido no se midió (estimado: un scroll por
  `file_path`, que ya está indexado, y un borrado).

**Tenant** (estimado con el código del origen): en Rag.Context.Engine es una clave `tenant` (keyword) en el payload,
omitida si viene vacía (`QdrantVectorStore`), más un filtro `Match.Keyword` en el retriever que sale del claim
`tenant` del actor (`QdrantSemanticRetriever.BuildTenantFilter`, `RagEngine.Api/Program.cs`): unas 30 líneas. En
este servicio el aislamiento ya es la colección (tabla de acceso de 1.3). Un tenant **en la ingesta sin filtro en
`busquedas`** es una etiqueta que cualquiera con acceso a la colección atraviesa. Recomendación: un tenant = una
colección (ya soportado). Revivirlo dentro de una colección compartida exige decidir antes el claim y la regla del
filtro de búsqueda, y queda para el responsable.

## (5) Selección de modelo

`resultados/modelos.json` y `mezcla-*.json`:

- `GET /v1/models` (AltheIA real): `bge-m3` (7372), `qwen3.8-27b-deep` y `qwen3.8-27b-fast` (117 964). Solo trae
  `id`, `object`, `owned_by` y `max_model_len`: **no dice qué sabe hacer cada modelo**. Responde en 3–6 ms (284 ms
  la primera vez): validar contra él al arrancar y cachearlo es barato.
- Modelo que no existe: **403** `model_not_allowed` («API key '<alias>' is not allowed to use model…»), igual en
  embeddings y en chat. AltheIA limita los modelos **por clave**, así que la lista válida es la de la clave del
  servicio. Modelo de otro tipo: **400** `model_capability_mismatch` (chat como embeddings y al revés). El servicio
  debe traducirlos a un 400 propio con los perfiles disponibles, no a un 503 de dependencia caída.
- **Hueco medido en la regla de 409 de 1.4**: en la misma colección, arctic@256 (TEI) indexa `a.md` → 200;
  después, **bge-m3 truncado a 256 (AltheIA) indexa `b.md` → 200** (bge-m3 no es Matryoshka); bge-m3@1024 → 409.
  Una búsqueda con arctic con el texto de `b.md` devuelve **0 resultados**: el documento queda inalcanzable sin
  error. La colección no recuerda su modelo; solo se compara la dimensión.
- **Metadata de colección en Qdrant 1.19.2**: crear con `metadata` `{perfil_embeddings, modelo, dimension,
  resumen}` → 200 y se lee igual; `PATCH` con otra clave la **fusiona** sin borrar las anteriores. Es el sitio
  natural para el perfil, y sirve para anotar las colecciones existentes sin metadata (hoy todas: se asumen del
  perfil por defecto, arctic@256, y se les escribe la metadata).

Diseño resultante:

| | Embeddings | LLM del resumen |
|---|---|---|
| Qué se elige | Un **perfil** registrado: modelo, dimensión, prefijo de consulta, si admite truncar, servidor y su clave | Un **perfil** registrado: modelo, `max_tokens`, razonamiento (`enable_thinking`) |
| Dónde | Al **crear** la colección (primera ingesta); queda en su metadata | Por defecto de la colección (metadata), sustituible por solicitud |
| Si cambia | 409 aunque coincida la dimensión; cambiar de modelo = colección nueva | Se permite: cada resumen guarda `resumen_modelo` y la caché lo distingue |
| Si no está disponible | Validado contra `/v1/models` (caché) y la lista de perfiles → 400 con los disponibles | Igual |

No hay nombres de modelo libres en la API. Así, «bge-m3 truncado a 256» no existe como opción, y arctic@256 sigue
siendo el perfil por defecto (1.6).

## (6) `ruta` no se toca

`OrigenIngestaRequest.TipoRuta` sigue en 501 y no se reemplaza. El origen `proyecto` es otro tipo. El escáner que
se porta sirve después a `ruta` sin cambios; lo que le falta a `ruta` (raíces permitidas por colección, 1.7) no
cambia con esta decisión. 1.7 midió que leer en local solo ahorra la transferencia (menos de 0,5 s por repo
completo): `ruta` es más práctico de operar cuando el repo ya está en el servidor, pero no notablemente más rápido.

## Siguientes ítems

Registrados en el ledger (`1.7.6` bloqueado por la identidad en ADO; el resto, `pendiente`):

- `1.7.4`: perfiles de embeddings y de LLM y metadata de colección (cierra el hueco de misma dimensión). Es la base
  de los demás.
- `1.7.5`: origen `proyecto` desde `.zip` (asíncrono, límites de (2), `.ragignore`, barrido de ausentes).
- `1.7.6`: origen `proyecto` desde URL Git (necesita de TI la identidad del servicio en ADO y la organización
  permitida; git en la imagen; volumen temporal con cuota).
- `1.7.7`: resumen de negocio como fase reanudable con caché y tope de cuota.
- `1.7.8`: el bug de `IgnoreRules` (punto inicial) en Rag.Context.Engine, que hoy afecta a su CLI sobre árboles de
  trabajo.

## No medido / límites

- Nada contra Azure DevOps real: ni la identidad del servicio, ni la red, ni el tamaño real de los repos de la
  empresa. El host permitido de la prueba es local, por http sobre loopback; producción sería https.
- Un solo repo de extremo a extremo (este) y una muestra de 30 chunks para el resumen. Las cifras de cuota son
  extrapolaciones.
- El barrido de documentos ausentes del origen `proyecto`: estimado, no implementado.
- Zip: probado con el lector del prototipo, no con un endpoint de subida (el límite de cuerpo ya se probó en 1.7.1).
- Limitación del banco: una segunda `WebApplicationFactory` en el mismo proceso responde 503 a todo; las pruebas de
  mezcla corren un paso por proceso (ver el README de la herramienta).

## Rollback

Documental y de herramienta: revertir el commit. No se expuso ningún origen nuevo y el servicio no cambió. Se
borraron las colecciones `it-1-7-3-*` del Qdrant local (`limpiar`, verificadas inexistentes) y la imagen de prueba
`vse-prueba-git:1.7.3`; `docker-compose.motor.yml` se detuvo sin `-v`, sin tocar otras colecciones de ese volumen.
Las copias bare, los clones y la muestra del resumen viven en `.cache/ingesta-proyecto-1.7.3/` (ignorado por git).
