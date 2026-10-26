# Contrato HTTP de embeddings/ingesta (MVP)

Definido en el item `1.3-definir-contrato-api-y-autenticacion` del ledger. Rutas relativas a la
ruta base del servicio (`/reyma/vectorspace/engine` desplegado; vacia en local) y al prefijo global
`api/` de la plantilla. Swagger (solo Development) publica el mismo contrato.

| Metodo | Ruta | Operacion | Respuesta 200 |
|---|---|---|---|
| GET | `api/sesion` | — (solo login) | Usuario en sesion desde los claims |
| POST | `api/colecciones/{coleccion}/embeddings` | `Consultar` | Vectores densos de textos sueltos |
| POST | `api/colecciones/{coleccion}/busquedas` | `Consultar` | Resultados de busqueda en la coleccion |
| POST | `api/colecciones/{coleccion}/ingestas` | `Ingestar` | Resultado de la ingesta (origen `documentos`, sincrona) |
| POST | `api/colecciones/{coleccion}/ingestas/proyecto` | `Ingestar` | **202**: ingesta admitida (origen `proyecto` desde `.zip`, asincrona, item 1.7.5) |
| POST | `api/colecciones/{coleccion}/ingestas/git` | `Ingestar` | **202**: ingesta admitida (origen `proyecto` desde URL Git, asincrona, item 1.7.6) |
| GET | `api/colecciones/{coleccion}/ingestas/{ingestaId}` | `Consultar` | Estado de una ingesta de proyecto (items 1.7.5/1.7.6) |

Codigos comunes: **401** sin token valido (firma, emisor, audiencia y vigencia se validan como en
la plantilla); **400** cuerpo invalido, incluido pedir un `perfil` de embeddings no registrado en el servicio o un modelo que el servidor remoto
no tiene cargado para esa clave (1.7.4: nunca 503, siempre con los perfiles disponibles), o un `.zip`
invalido o malicioso en `ingestas/proyecto` (1.7.5: limites de tamano/entradas/ratio, Zip Slip, enlaces
simbolicos o sin documentos admitidos por el perfil de escaneo);
**404** (solo `ingestas/proyecto/{ingestaId}`) ese identificador no existe o no es de esta coleccion;
**409** la coleccion ya existe con vectores de otra dimension, **o** con el mismo numero de dimensiones
pero de otro perfil de embeddings (1.7.4: el perfil, no solo la dimension, identifica si dos vectores son
comparables), **o** se pide `resumen: true` sobre una coleccion existente que no tiene el vector
`dense-resumen` (1.7.7: el tercer vector no se puede agregar despues de crear la coleccion); **413**
(`ingestas` e `ingestas/proyecto`) el cuerpo de la solicitud supera el limite de la accion (1.7.1/1.7.5);
**429** (`ingestas`, `ingestas/proyecto` e `ingestas/git`) hay demasiadas ingestas en curso o en espera,
reintentar despues de `Retry-After` (1.7); **501** parte del contrato aun sin implementado (origen
`ruta`); **503** Qdrant, el sidecar de embeddings o el perfil de LLM del resumen no respondieron (el
mensaje no dice a que host ni puerto se intento conectar). `ingestas/git` responde **400** si la URL no
esta en la lista blanca de origenes Git permitidos, sin conexion al destino (item 1.7.6); tambien **400**
si se pide `resumen: true` y esta instancia no tiene el resumen de negocio configurado
(`Motor:Resumen:PerfilEmbeddings` vacio, item 1.7.7).

## Motor (item 1.4)

El motor es el de Rag.Context.Engine portado (ver
`src/Reyma.VectorSpace.Engine.Microservice.Infrastructure/PORTE.md`): mismo chunking, mismos Id y
`content_hash`, mismo esquema de coleccion en Qdrant. El vector denso lo calcula el sidecar (TEI) con
`snowflake-arctic-embed-m-v2.0` a 256 dimensiones (1.6), normalizado L2; el vector `sparse-code` es el
`SparseTokenizer` propio.

- **embeddings**: vectores de *documento* (sin el prefijo `query: `), los mismos que guardaria una ingesta
  de esos textos. No escribe nada.
- **busquedas**: rama densa exacta (coseno >= 0,10, desempate por id), sin fusion con el sparse (1.10).
  `puntuacion` es el coseno. Una coleccion autorizada que aun no tiene ingestas responde 200 sin resultados.
- **ingestas**: cada documento se trocea por su extension con el perfil del CLI de Rag.Context.Engine
  (`.cs .ts .tsx .js .jsx .xaml .sql .md .txt .json .xml .csproj`, hasta 512 KB en UTF-8), con
  `MaxTokensPerChunk` 1024 (1.11). La coleccion hace de repositorio: el Id de un chunk sale de
  `coleccion/ruta` (separadores normalizados), linea inicial y hash. Por documento:
  - `documentosOmitidos`: extension fuera del perfil, mas de 512 KB, o el chunker no pudo procesarlo
    (sus puntos anteriores se conservan).
  - `documentosSinCambios`: ya estaba indexado con el mismo contenido (finales de linea aparte), la misma
    ruta y la misma configuracion de chunking y modelo; no se vuelve a vectorizar.
  - `documentosIndexados`: se vectorizo y se borraron los puntos que el documento ya no genera.
  Dos rutas que solo difieren en el separador (`a/b.cs`, `a\b.cs`) en la misma solicitud responden 400.
  La primera ingesta crea la coleccion. Las ingestas sobre una misma coleccion se serializan.
  Nota historica (item TransactionPipelineBehavior, resuelta): el mediador corporativo
  (`AddReymaMediatorDefault`) envolvia cada solicitud en una transaccion que expiraba a los 10 minutos,
  lo que podia responder 500 en una ingesta grande aunque los puntos ya hubieran quedado guardados. El
  servicio no tiene ningun recurso transaccional de .NET (Qdrant y el sidecar de embeddings no participan
  de un `TransactionScope`), asi que se compone el mediador con `AddReymaMediator` (sin ese behavior); ya
  no hay un limite de tiempo por solicitud impuesto por el mediador. Con TEI en CPU (~1 chunk/s con chunks
  largos) una ingesta grande en una sola solicitud puede tardar minutos u horas; eso sigue siendo un costo
  real del hardware, no un limite del contrato.
- **Limite de admision (1.7)**: una ingesta en curso por instancia, entre todas las colecciones, y hasta
  cuatro en espera (`LimiteDeIngestas:MaximoSimultaneas` 1, `MaximoEnCola` 4). La siguiente responde
  **429** con `Retry-After: 60` (`SegundosParaReintentar`) sin llegar al motor. Va despues de validar el
  cuerpo: un 400 no ocupa lugar en la cola. `embeddings` y `busquedas` no pasan por el limite. Es por
  instancia: varias replicas no comparten cola. Medido en `docs/evaluaciones/1.7/decision.md`: dos ingestas
  a la vez no terminan antes que una tras otra con TEI en CPU.
- **Cuerpo (1.7.1)**: el limite de cuerpo de `ingestas` es 125 000 000 bytes (~119,2 MiB), fijado con
  `[RequestSizeLimit]` solo en esa accion (el resto del servicio sigue en el default de Kestrel, 30 000 000
  bytes). El numero sale de 100 documentos x 1 MiB (`Limites.MaxDocumentosPorIngesta` x
  `Limites.MaxCaracteresPorDocumento` = 104 857 600 bytes de contenido) mas ~20% de margen para la
  estructura JSON alrededor (claves, comillas, escapes de ruta y contenido). Al rebasarlo responde **413**
  con un mensaje claro (antes respondia 500 con el mensaje interno de Kestrel).
- **Borrar un documento**: enviarlo con contenido vacio; sus puntos se borran.
- **Clientes**: una solicitud puede esperar en cola y luego correr (~7 min en el peor caso con lotes de 100
  documentos). Usar un timeout de al menos 10 min (el de `HttpClient` por defecto, 100 s, no alcanza),
  reintentar el 429 despues de `Retry-After` y reintentar ante fallos de red: repetir una ingesta es
  idempotente y solo vuelve a vectorizar los documentos que no quedaron completos.

## Cuerpos

```jsonc
// POST .../embeddings   (1 a 64 textos, cada uno no vacio y de hasta 8192 caracteres)
{ "textos": ["texto 1", "texto 2"], "perfil": "altheia-bge-m3-256" }   // "perfil" opcional, ver abajo
// 200: { "coleccion", "modelo", "dimension", "vectores": [[...], [...]] }

// POST .../busquedas    (consulta de hasta 2048 caracteres; topK 1..100, por defecto 10)
{ "consulta": "como se configura X", "topK": 10, "perfil": "altheia-bge-m3-256" }   // "perfil" opcional
// 200: { "coleccion", "resultados": [{ "id", "puntuacion", "ruta", "texto" }] }

// POST .../ingestas     origen 'documentos': 1 a 100 documentos, ruta unica y contenido <= 1 MiB c/u
{ "origen": { "tipo": "documentos", "documentos": [{ "ruta": "docs/a.md", "contenido": "..." }] },
  "perfil": "altheia-bge-m3-256",   // "perfil" opcional, ver abajo
  "resumen": false }                // "resumen" opcional, item 1.7.7, ver seccion "Resumen de negocio" abajo
// origen 'ruta': reservado para el item 1.7, hoy responde 501
{ "origen": { "tipo": "ruta", "ruta": "/ruta/accesible/al/servicio" } }
// 200: { "ingestaId", "coleccion", "estado": "completada", "documentosRecibidos",
//        "documentosIndexados", "documentosSinCambios", "documentosOmitidos", "documentosEliminados",
//        "resumenesGenerados", "resumenesPendientes", "resumenTokensConsumidos" }   // 1.7.7, siempre 0 sin resumen

// POST .../ingestas/proyecto   origen 'proyecto' desde .zip (item 1.7.5); multipart/form-data, no JSON:
//   zip          archivo .zip (campo de archivo), hasta 100 MiB, leido en memoria
//   carpetaRaiz  opcional: quita el envoltorio "repo-rama/" de un "descargar como zip"
//   perfil       opcional, igual que en los demas endpoints
//   resumen      opcional (1.7.7), igual semantica que en 'documentos'
// 202: { "ingestaId", "coleccion", "estado": "pendiente", "documentosRecibidos", ... } (conteos en 0)

// POST .../ingestas/git   origen 'proyecto' desde URL Git (item 1.7.6); JSON, no multipart:
{ "url": "https://dev.azure.com/<organizacion-en-lista-blanca>/<proyecto>/_git/<repo>",
  "carpetaRaiz": null, "perfil": null, "resumen": false }   // opcionales, igual que en el .zip ("resumen": 1.7.7)
// 202: { "ingestaId", "coleccion", "estado": "pendiente", "documentosRecibidos", ... } (conteos en 0)
// 400 si la URL no esta en Motor:Git:OrigenesPermitidos (esquema, host, puerto y prefijo de ruta exactos),
//     sin que el servicio abra ninguna conexion hacia ella

// GET .../ingestas/{ingestaId}   estado de una ingesta de proyecto (items 1.7.5/1.7.6/1.7.7)
// 200: mismo cuerpo que el 202 de arriba; "estado" en "pendiente" | "procesando" | "completada" | "error";
//      los conteos (documentosIndexados/SinCambios/Omitidos/Eliminados/Resumenes*) solo son definitivos en "completada"
```

## Perfiles de embeddings y metadata de coleccion (item 1.7.4)

El 409 de dimension del item 1.4 solo comparaba el numero de dimensiones: 1.7.3 midio que un modelo
distinto con la misma dimension (bge-m3 truncado a 256 contra una coleccion `arctic@256`) entraba con
200 y dejaba sus documentos inalcanzables por busqueda. 1.7.4 cierra ese hueco con perfiles registrados:

- El servicio declara perfiles de embeddings nombrados en configuracion (`Motor:Embeddings` describe el
  perfil por defecto, **`tei-arctic-256`**: `snowflake-arctic-embed-m-v2.0` a 256d por TEI local, 1.6;
  `Motor:Embeddings:Perfiles:<nombre>` declara perfiles adicionales, por ejemplo `altheia-bge-m3-256`
  contra AltheIA, 1.13). Cada uno define modelo, dimension, prefijo de consulta, servidor y su clave.
- `perfil` en el cuerpo es opcional en `embeddings`, `busquedas` e `ingestas`; ausente/vacio usa el perfil
  por defecto o, si la coleccion ya existe, el que tenga guardado en su metadata.
- **Al crear una coleccion** (primera ingesta que la toca), el perfil usado queda en su metadata de Qdrant
  (`perfil_embeddings`, `modelo`, `dimension`), vista con `GET /collections/{coleccion}` en Qdrant.
- **Perfil distinto sobre una coleccion existente**: **409**, aunque coincida la dimension numerica; no
  escribe nada (ni metadata ni puntos). Aplica a `ingestas` (no se vectoriza) y a `busquedas`/`embeddings`
  (no se consulta con el modelo equivocado).
- **Coleccion creada antes de 1.7.4** (sin metadata de perfil): se asume del perfil por defecto
  (`tei-arctic-256`); si la solicitud coincide, se le escribe la metadata en ese momento (no rompe
  colecciones ya en uso); si pide otro perfil, 409 igual que si el perfil ya estuviera guardado.
- **Perfil no registrado en esta instancia**, o modelo que el servidor remoto (AltheIA) no tiene cargado
  para esta clave (`model_not_allowed`/`model_capability_mismatch`, medido en 1.7.3 contra AltheIA real):
  **400** con la lista de perfiles disponibles, nunca 503 (un 503 sigue siendo solo para una dependencia
  caida o que no respondio).
- Perfil de LLM (resumen de negocio): implementado en 1.7.7, ver la seccion "Resumen de negocio reanudable" abajo.

## Modelo de consumo (item 1.7)

Decidido en `docs/evaluaciones/1.7/decision.md`: **hibrido, con el envio por API como mecanismo de
produccion**. Un pipeline de Azure DevOps (al hacer push), que ya tiene el repo descargado, envia por
`ingestas` (origen `documentos`) los archivos cambiados, y los borrados con contenido vacio (item 1.7.2).
La transferencia no es el coste: un repo de 567 documentos (1,5 MB de JSON) que no cambio se reenvia
entero en 0,4 s; lo que cuesta es calcular los embeddings.

- Calcular embeddings de un texto suelto y la ingesta son **superficies separadas**; la primera no
  escribe en la coleccion ni pasa por el limite de ingestas.
- La ingesta declara su `origen`. `ruta` (disparar sobre un repo ya accesible al servicio) sigue
  reservado en 501: solo ahorraria la transferencia, y exige credenciales de Azure DevOps en el servidor,
  un aviso de cambios que llegue al servidor (o *polling*) y raices permitidas por coleccion. Se retoma
  si un repo deja de caber razonablemente en envios por lotes.
- La respuesta de ingesta ya trae `ingestaId` y `estado`, para que una ingesta en cola pueda
  responder 202 con la misma forma si mas adelante se vuelve asincrona.

## Proyecto completo desde .zip (item 1.7.5)

Decidido en `docs/evaluaciones/1.7.3/decision.md`, criterio 2/3; implementado en 1.7.5. Origen nuevo
`proyecto`, aparte de `documentos` y de `ruta` (que sigue en 501): `POST .../ingestas/proyecto` recibe un
`.zip` subido como `multipart/form-data` (nunca JSON, por el tamano del archivo) y responde **202**
de inmediato; el trabajo pesado (chunking, vectorizacion, barrido de documentos ausentes) corre en
segundo plano, fuera del ciclo de vida de esa solicitud HTTP (cancelar o cerrar la conexion del cliente no
lo aborta). El cliente consulta `GET .../ingestas/{ingestaId}` hasta ver `"estado": "completada"` (o
`"error"`).

- **Lectura del .zip**: en memoria, sin extraer nada a disco — el nombre de cada entrada solo se convierte
  en la `ruta` del documento (su identidad en la coleccion), nunca en una ruta del sistema de archivos, asi
  que un intento de Zip Slip (`../`, rutas absolutas, `C:/...`) no tiene donde escribir; se rechaza igual
  por validacion de nombre. Limites (medidos en 1.7.3, `tools/ingesta-proyecto/`): 100 MiB de subida,
  20 000 entradas, 256 MiB descomprimidos en total, ratio declarado/real de hasta 100:1 por entrada
  (verificado antes de descomprimir), y el tope por archivo del perfil de escaneo (512 KiB). Una entrada
  marcada como enlace simbolico Unix (`S_IFLNK`) se rechaza sin abrirla. Una cabecera que miente sobre el
  tamano descomprimido no logra amplificar nada: .NET corta la lectura en el tamano que la cabecera
  declara, no en el real (medido en 1.7.3).
- **Si el .zip es invalido, excede un limite, o no trae ningun documento admitido por el perfil de
  escaneo** (`.cs .ts .tsx .js .jsx .xaml .sql .md .txt .json .xml .csproj`, igual que `documentos`):
  **400**, sin tocar el limite de admision de ingestas ni crear ningun registro de estado.
- **`carpetaRaiz`** (opcional): quita el envoltorio `repo-rama/` que agrega un "descargar como zip" de
  GitHub/Azure DevOps. Si alguna entrada no esta bajo esa carpeta, **400** (no se adivina el envoltorio:
  cambiar la ruta de un documento revectoriza todo).
- **`.gitignore`/`.ragignore` del proyecto**: si el `.zip` trae esos archivos en su raiz, sus reglas se
  aplican igual que al escanear un checkout (mismo `IgnoreRules`, mismas dos fuentes, `.ragignore` puede
  re-incluir con `!patron` algo que `.gitignore` excluye).
- **Barrido de documentos ausentes**: a diferencia del origen `documentos` (que solo ve lo que el cliente
  decide enviar y borra un documento puntual enviandolo con contenido vacio, item 1.7.2), el origen
  `proyecto` conoce el arbol completo: tras indexar, borra los puntos de cualquier documento que ya estaba
  en la coleccion pero no reaparecio en este `.zip`. El conteo queda en `documentosEliminados` del estado
  final.
- **Limite de admision (1.7)**: antes de admitir, compite por el mismo cupo global que el origen
  `documentos` (`LimiteDeIngestas`); saturado, **429** con `Retry-After`, igual que `ingestas`, sin crear
  registro de estado.
- **Estado en memoria, por instancia**: igual filosofia que `LimiteDeIngestas` — no persiste entre
  reinicios ni se comparte entre replicas; una ingesta en curso cuando el proceso se reinicia pierde su
  `ingestaId` consultable (el trabajo ya escrito en Qdrant no se deshace).
- **Resumen de negocio**: implementado en 1.7.7, ver la seccion dedicada abajo.
- **Pendiente** (item 1.7.8, no implementado, otro repo): perfil de escaneo y lenguajes como atributos de la
  coleccion (hoy siempre `DotNetEnterprise`); el bug de `IgnoreRules` en Rag.Context.Engine.

## Proyecto completo desde URL Git (item 1.7.6)

Decidido en `docs/evaluaciones/1.7.3/decision.md`, criterio 1; implementado en 1.7.6, reusando el mismo
patron asincrono que el `.zip` de 1.7.5 (mismo `IRegistroDeIngestasProyecto`, mismo `LimiteDeIngestas`,
mismo `GET .../ingestas/{ingestaId}`, mismo barrido de documentos ausentes). Lo que cambia es el origen del
arbol de documentos: en vez de leer un `.zip` subido, el servicio clona el repositorio por su cuenta.

- **Lista blanca (`Motor:Git:OrigenesPermitidos`)**: una URL se valida contra una lista EXACTA de
  esquema+host+puerto (y opcionalmente un prefijo de ruta, p. ej. la organizacion de Azure DevOps) **antes
  de tocar la red**: sin ningun origen configurado, toda URL se rechaza. Rechaza tambien, sin conexion:
  esquemas distintos de `https`/`http` (`file://`, `ssh://`, `scp://`, `ext::`), `localhost`, IPs internas o
  el endpoint de metadata de nube (`169.254.169.254`), usuario/contrasena embebidos en la URL (el servicio
  usa su propia identidad, nunca la de quien llama), query/fragmento, puntos/barras/nulos codificados
  (`%2e`/`%2f`/`%5c`/`%00`), saltos de linea o espacios, y un puerto que no sea el configurado. Mismo
  criterio que el banco de 1.7.3 (`docs/evaluaciones/1.7.3/resultados/urls-*.json`).
- **Clonado endurecido (`ClonadorGit`)**: `git clone --depth` superficial, una sola rama, sin tags ni
  submodulos, sin hooks, sin seguir enlaces simbolicos, y **sin seguir redirecciones**
  (`http.followRedirects=false`): 1.7.3 midio que el valor por defecto de git (`initial`) sigue una
  redireccion del host permitido hacia un host interno y clona desde ahi (SSRF); por eso nunca se sigue
  ninguna. Tope de tamano del checkout (`Motor:Git:MaxBytesCheckout`, vigilado mientras clona, el proceso se
  mata al rebasarlo) y plazo (`Motor:Git:PlazoSegundos`, el proceso se mata al rebasarlo). El tope de tamano
  es un limite logico de proceso, no una cuota real de filesystem (ver limitacion abajo).
- **Identidad del servicio**: PAT de una cuenta de servicio (nunca la del usuario final) leido de la
  variable de entorno `Motor:Git:NombreVariableIdentidad` (por defecto `VSE_GIT_PAT`; nunca en
  `appsettings*.json`), convertido en la cabecera `Authorization: Basic ...` que Azure DevOps acepta para
  autenticacion basica con PAT. Viaja por `GIT_CONFIG_KEY/VALUE` en el entorno del proceso de `git`, nunca
  en la linea de comandos ni en `.git/config` del checkout. Sin esa variable configurada, el clonado **no**
  aisla la configuracion/credenciales del host y deja que su `credential.helper` ya configurado resuelva la
  autenticacion: esto es valido solo para la verificacion puntual de 1.7.6 (credencial personal del usuario
  ya cacheada en el host, sin PAT de cuenta de servicio todavia) y exige correr el proceso en un host con ese
  credential helper (nunca en el contenedor Linux de produccion). Produccion siempre configura la variable.
- **Escaneo del checkout (`EscanerDeCheckout`)**: mismo perfil de escaneo (`ScanProfile.DotNetEnterprise`) y
  mismas reglas `.gitignore`/`.ragignore` (`IgnoreRules`) que el origen `.zip`; a diferencia del origen
  `.zip` (que lee en memoria sin tocar disco), aqui el documento ya esta en disco (el checkout), asi que no
  se siguen enlaces simbolicos ni junctions al recorrerlo (un enlace dentro de un repo ajeno podria apuntar
  fuera de la raiz del checkout).
- **`carpetaRaiz`** (opcional): mismo proposito que en el `.zip` (quitar un envoltorio), validada contra
  recorrido de directorio (`..`, rutas absolutas, `:` de unidad) antes de resolverla dentro del checkout.
- El checkout temporal se borra siempre al terminar (exito o error), fuera de cualquier volumen persistente.
- **Limitacion conocida (documentada, no resuelta en 1.7.6)**: `Motor:Git:MaxBytesCheckout` es un limite
  logico de proceso (el proceso de `git` se mata al medir mas bytes de los permitidos), no una cuota real de
  filesystem: en un entorno sin volumen con cuota dedicado, un clonado matado a tiempo puede dejar varios
  MiB ya escritos en disco (medido en 1.7.3: ~21 MB con un tope de 2 MiB). La mitigacion completa es un
  volumen temporal con cuota real de filesystem para `Motor:Git:DirectorioTemporal`, pendiente de
  infraestructura (ver `docs/despliegue/runbook.md`).

## Resumen de negocio reanudable (item 1.7.7)

Decidido en `docs/evaluaciones/1.7.3/decision.md`, criterio 4; implementado en 1.7.7. Revive el resumen de negocio
de Rag.Context.Engine (`OllamaBusinessSummaryGenerator`) como un tercer vector opcional, `dense-resumen`, generado
por un LLM a partir del contenido de cada chunk, en una fase 2 que corre DESPUES de la ingesta normal (fase 1:
chunking + `dense` + `sparse-code`, sin cambios).

- **Opt-in solo al crear la coleccion**: `resumen: true` en el cuerpo de la primera ingesta que crea la coleccion
  agrega el vector `dense-resumen` (ademas de `dense`); una ingesta posterior no necesita repetirlo (es un
  atributo de la coleccion, igual que el perfil de embeddings). Pedir `resumen: true` sobre una coleccion
  EXISTENTE que no tiene ese vector responde **409** con un mensaje claro ("no se puede activar el resumen sobre
  una coleccion existente"), nunca 500: Qdrant en si respondia 400 "Not existing vector name" al intentar
  agregarlo (medido en 1.7.3), y el servicio lo anticipa antes de llegar a Qdrant.
- **Perfil de LLM** (`Motor:Llm`): modelo, `max_tokens` (160, igual que el origen), temperatura/top_p y, si el
  modelo razona por defecto, `chat_template_kwargs.enable_thinking=false`. 1.7.3 midio contra AltheIA real:
  `qwen3.8-27b-fast` responde 30/30 sin razonar; `qwen3.8-27b-deep` (el perfil de LLM de 1.13) gasta los 160
  tokens razonando y responde VACIO en 0/30 si no se fuerza `enable_thinking=false` (con el, igual que `fast`).
  El perfil por defecto de este servicio es `qwen3.8-27b-fast`.
- **Perfil de embeddings del resumen** (`Motor:Resumen:PerfilEmbeddings`): un nombre de perfil YA declarado en
  `Motor:Embeddings` (el por defecto o uno de `Motor:Embeddings:Perfiles`, 1.7.4). 1.7.3: bge-m3 1024d via
  AltheIA, **sin truncar** (bge-m3 no es Matryoshka, a diferencia del perfil por defecto arctic@256). Vacio
  (por defecto) = el resumen de negocio no esta disponible en esta instancia; pedirlo responde **400** con un
  mensaje claro ("Motor:Resumen:PerfilEmbeddings"), nunca 500.
- **Cache por `content_hash` + version de prompt + modelo**: antes de llamar al LLM, se busca en una cache en
  memoria (por instancia, no persiste entre reinicios, igual filosofia que `LimiteDeIngestas`) por esa clave
  compuesta; un acierto reutiliza el resumen sin gastar tokens. Ademas, y es la garantia principal de "reingerir
  sin cambios gasta 0 tokens": un documento sin cambios ni siquiera entra a la fase de resumen (la fase 1 ya lo
  trata como "sin cambios" y no lo reencola).
- **Tope de tokens por corrida** (`Motor:Resumen:TopeTokensPorCorrida`, configuracion, nunca fijo en codigo): la
  fase 2 corre despues de cada ingesta (sincrona en el origen `documentos`, en segundo plano en `proyecto`) y
  gasta como maximo este tope de tokens de LLM (`usage.total_tokens` de la respuesta); lo que no alcanza a
  procesar queda marcado `resumen_pending=true` en Qdrant, reanudable por la siguiente ingesta sobre la misma
  coleccion (no repite los chunks ya resumidos). `resumenesPendientes` en la respuesta indica cuanto falta.
- **Sentinel `SIN_CONTENIDO_DE_NEGOCIO`**: un chunk puramente tecnico (imports, boilerplate) no lleva vector de
  resumen (como el origen); su `resumen_pending` igual se apaga (ya se proceso, no vuelve a intentarse).
- **Nombres literales en Qdrant** (fijados en el ledger): vector `dense-resumen`; payload `resumen_pending`
  (bool), `resumen_modelo` (string) y `resumen_prompt_version` (string, hash de prompt+modelo).
- **Aislamiento de fallos**: una respuesta vacia del LLM para UN chunk se salta (aislado, recuperable en la
  siguiente corrida, igual que el origen); un perfil de LLM mal configurado (modelo no permitido o de otra
  capacidad) responde 400 sin tocar el tope de tokens; cualquier otro fallo (conexion) corta la corrida completa
  dejando el resto pendiente, en vez de insistir contra una dependencia caida chunk a chunk.

## Identidad

La de la plantilla, sin cambios de proveedor: JWT del IdentityServer corporativo, mismas
credenciales que el resto de APIs Reyma. En Development se valida la audiencia compartida de prueba
(`permissions.service.api`); produccion usa `rym.vectorspace.engine.api` y necesita el recurso
registrado en Identity.

Unico ajuste a la identidad heredada: `MapInboundClaims = false` en
`IdentityServiceExtensions`. Las extensiones corporativas (`GetUserId`, `GetRoles`) leen los claims
crudos `sub` y `role`; con el mapeo por defecto de JwtBearer se renombran y devuelven vacio (medido
con tokens firmados que pasan por el pipeline real, `SesionTests`). La plantilla de origen tiene el
mismo comportamiento en su ejemplo `usuario`.

## Control de acceso por coleccion (retirado en 1.14)

El item `1.14-retirar-control-de-acceso-por-coleccion` del ledger retiro por completo la tabla
`ControlDeAcceso` y el behavior que la aplicaba (`AutorizacionColeccionBehavior`). Hoy **no hay
autorizacion por coleccion**: `[Authorize]` basta (cualquier usuario autenticado alcanza cualquier
coleccion). Motivo: la tabla vivia en `appsettings` (sin base de datos desde el item `1.9`, que
retiro Postgres) y cada alta de coleccion o usuario exigia redeploy, lo cual no escala para el uso
real del equipo. La multi-tenencia por coleccion queda diferida: una alternativa evaluada y no
adoptada todavia es expresarla como permiso del IdP corporativo por recurso (ver mas abajo), que
requiere resolver primero que ese validador sea de grano fino (por coleccion, no solo por
operacion) y registrar los permisos en un sistema externo al servicio.

### Permisos corporativos: previstos, no aplicados

`ApiSecurityConstants` declara `VECTORSPACE.ENGINE.API.COLECCIONES.CONSULTAR` e `...INGESTAR`, pero
las acciones no los exigen todavia. El validador corporativo consulta al servicio de permisos en
cada llamada y, mientras esos permisos no esten registrados ahi, toda llamada real responderia 403.
Son de grano grueso (la operacion, no la coleccion): no resuelven por si solos la multi-tenencia por
coleccion que tenia `ControlDeAcceso` sin una decision adicional sobre como expresar el recurso.
