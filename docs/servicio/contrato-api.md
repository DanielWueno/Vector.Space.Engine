# Contrato HTTP de embeddings/ingesta (MVP)

Definido en el item `1.3-definir-contrato-api-y-autenticacion` del ledger. Rutas relativas a la
ruta base del servicio (`/reyma/vectorspace/engine` desplegado; vacia en local) y al prefijo global
`api/` de la plantilla. Swagger (solo Development) publica el mismo contrato.

| Metodo | Ruta | Operacion | Respuesta 200 |
|---|---|---|---|
| GET | `api/sesion` | — (solo login) | Usuario en sesion desde los claims |
| GET | `api/colecciones` | — (solo login) | Colecciones que alcanza el usuario y sus operaciones |
| POST | `api/colecciones/{coleccion}/embeddings` | `Consultar` | Vectores densos de textos sueltos |
| POST | `api/colecciones/{coleccion}/busquedas` | `Consultar` | Resultados de busqueda en la coleccion |
| POST | `api/colecciones/{coleccion}/ingestas` | `Ingestar` | Resultado de la ingesta (origen `documentos`, sincrona) |
| POST | `api/colecciones/{coleccion}/ingestas/proyecto` | `Ingestar` | **202**: ingesta admitida (origen `proyecto` desde `.zip`, asincrona, item 1.7.5) |
| GET | `api/colecciones/{coleccion}/ingestas/{ingestaId}` | `Consultar` | Estado de una ingesta de proyecto (item 1.7.5) |

Codigos comunes: **401** sin token valido (firma, emisor, audiencia y vigencia se validan como en
la plantilla); **403** si la tabla de acceso no otorga esa operacion sobre esa coleccion;
**400** cuerpo invalido (solo despues de autorizar: sin acceso es 403 aunque el cuerpo sea invalido),
incluido pedir un `perfil` de embeddings no registrado en el servicio o un modelo que el servidor remoto
no tiene cargado para esa clave (1.7.4: nunca 503, siempre con los perfiles disponibles), o un `.zip`
invalido o malicioso en `ingestas/proyecto` (1.7.5: limites de tamano/entradas/ratio, Zip Slip, enlaces
simbolicos o sin documentos admitidos por el perfil de escaneo);
**404** (solo `ingestas/proyecto/{ingestaId}`) ese identificador no existe o no es de esta coleccion;
**409** la coleccion ya existe con vectores de otra dimension, **o** con el mismo numero de dimensiones
pero de otro perfil de embeddings (1.7.4: el perfil, no solo la dimension, identifica si dos vectores son
comparables); **413** (`ingestas` e `ingestas/proyecto`) el cuerpo de la solicitud supera el limite de la
accion (1.7.1/1.7.5); **429** (`ingestas` e `ingestas/proyecto`) hay demasiadas ingestas en curso o en
espera, reintentar despues de `Retry-After` (1.7); **501** parte del contrato aun sin implementar (origen
`ruta`); **503** Qdrant o el sidecar de embeddings no respondieron (el mensaje no dice a que host ni
puerto se intento conectar).

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
  **429** con `Retry-After: 60` (`SegundosParaReintentar`) sin llegar al motor. Va despues de autorizar y
  validar: un 403 o un 400 no ocupan lugar. `embeddings` y `busquedas` no pasan por el limite. Es por
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
  "perfil": "altheia-bge-m3-256" }   // "perfil" opcional, ver abajo
// origen 'ruta': reservado para el item 1.7, hoy responde 501
{ "origen": { "tipo": "ruta", "ruta": "/ruta/accesible/al/servicio" } }
// 200: { "ingestaId", "coleccion", "estado": "completada", "documentosRecibidos",
//        "documentosIndexados", "documentosSinCambios", "documentosOmitidos", "documentosEliminados" }

// POST .../ingestas/proyecto   origen 'proyecto' desde .zip (item 1.7.5); multipart/form-data, no JSON:
//   zip          archivo .zip (campo de archivo), hasta 100 MiB, leido en memoria
//   carpetaRaiz  opcional: quita el envoltorio "repo-rama/" de un "descargar como zip"
//   perfil       opcional, igual que en los demas endpoints
// 202: { "ingestaId", "coleccion", "estado": "pendiente", "documentosRecibidos", ... } (conteos en 0)

// GET .../ingestas/{ingestaId}   estado de una ingesta de proyecto (item 1.7.5)
// 200: mismo cuerpo que el 202 de arriba; "estado" en "pendiente" | "procesando" | "completada" | "error";
//      los conteos (documentosIndexados/SinCambios/Omitidos/Eliminados) solo son definitivos en "completada"
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
- Perfil de LLM (resumen de negocio) y seleccion por proyecto completo (origen `git`/`zip`): siguen sin
  implementar (items 1.7.5-1.7.8); ver la seccion siguiente.

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
- **Pendiente** (items 1.7.6-1.7.8, no implementados): origen `proyecto` desde URL de un repo Git (bloqueado
  por la identidad del servicio en Azure DevOps); resumen de negocio via LLM como fase reanudable con cache
  y tope de cuota; perfil de escaneo y lenguajes como atributos de la coleccion (hoy siempre
  `DotNetEnterprise`); el bug de `IgnoreRules` en Rag.Context.Engine (item 1.7.8, otro repo).

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

## Control de acceso por coleccion (multi-tenencia)

Tabla en configuracion, seccion `ControlDeAcceso`. **Sin concesiones no se alcanza ninguna
coleccion.** Cada concesion otorga operaciones sobre una coleccion a quien coincida con al menos
uno de sus sujetos:

```json
"ControlDeAcceso": {
  "Concesiones": [
    {
      "Coleccion": "rag-engine",
      "Operaciones": [ "Consultar", "Ingestar" ],
      "Usuarios": [ "nombre.usuario", "a0000000-0000-0000-0000-00000000000a" ],
      "Clientes": [ ],
      "Roles": [ "rol-exacto" ]
    }
  ]
}
```

| Sujeto | Claim (via `Reyma.IdentityService.Extensions`) | Comparacion |
|---|---|---|
| `Usuarios` | `sub` (GUID) o `preferred_username` | GUID exacto; nombre sin distinguir mayusculas |
| `Clientes` | `cli_id` organizacional Reyma (no el `client_id` OAuth) | GUID exacto |
| `Roles` | `role` | exacta |

- Nombres de coleccion: `^[a-z0-9][a-z0-9_-]{0,62}\z`. No se normalizan: `PROPIA`, `propia ` o
  `propia%2F..%2Fajena` no coinciden con `propia` y responden 403.
- El servicio no arranca con una tabla que otorgaria mas de lo escrito: nombre invalido, concesion
  sin operaciones o sin sujetos, sujetos vacios o GUID vacio (coincidiria con tokens sin ese claim).
  Tampoco arranca con concesiones escritas como texto (`"Concesiones": ["coleccion"]`): el binder
  las descartaria en silencio y todo responderia 403 sin explicacion.
- `appsettings.Development.json` trae dos concesiones de prueba local (`prueba-propia`, `prueba-ajena`).
- El control vive en un behavior de MediatR (`AutorizacionColeccionBehavior`) sobre toda solicitud
  que implemente `ISolicitudDeColeccion`, antes de la validacion y del handler; un test falla si una
  solicitud con propiedad `Coleccion` no implementa la interfaz.
- `GET api/sesion` devuelve los valores con los que el token coincidira en la tabla.

En variables de entorno (por ejemplo, para una prueba local):

```powershell
$env:ControlDeAcceso__Concesiones__0__Coleccion = "prueba-propia"
$env:ControlDeAcceso__Concesiones__0__Operaciones__0 = "Consultar"
$env:ControlDeAcceso__Concesiones__0__Usuarios__0 = "<tu preferred_username>"
```

### Permisos corporativos: previstos, no aplicados

`ApiSecurityConstants` declara `VECTORSPACE.ENGINE.API.COLECCIONES.CONSULTAR` e `...INGESTAR`, pero
las acciones no los exigen todavia. El validador corporativo consulta al servicio de permisos en
cada llamada y, mientras esos permisos no esten registrados ahi, toda llamada real responderia 403.
Cuando existan, se pueden exigir ademas de la tabla (la tabla sigue decidiendo la coleccion).
