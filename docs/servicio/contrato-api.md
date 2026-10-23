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
| POST | `api/colecciones/{coleccion}/ingestas` | `Ingestar` | Resultado de la ingesta |

Codigos comunes: **401** sin token valido (firma, emisor, audiencia y vigencia se validan como en
la plantilla); **403** si la tabla de acceso no otorga esa operacion sobre esa coleccion;
**400** cuerpo invalido (solo despues de autorizar: sin acceso es 403 aunque el cuerpo sea invalido),
incluido pedir un `perfil` de embeddings no registrado en el servicio o un modelo que el servidor remoto
no tiene cargado para esa clave (1.7.4: nunca 503, siempre con los perfiles disponibles);
**409** la coleccion ya existe con vectores de otra dimension, **o** con el mismo numero de dimensiones
pero de otro perfil de embeddings (1.7.4: el perfil, no solo la dimension, identifica si dos vectores son
comparables); **413** (solo `ingestas`) el cuerpo de la solicitud supera el limite de la accion (1.7.1);
**429** (solo `ingestas`) hay demasiadas ingestas en curso o en espera, reintentar despues de
`Retry-After` (1.7); **501** parte del contrato aun sin implementar (origen `ruta`); **503** Qdrant o el
sidecar de embeddings no respondieron (el mensaje no dice a que host ni puerto se intento conectar).

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
//        "documentosIndexados", "documentosSinCambios", "documentosOmitidos" }
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

## Proyecto completo (items 1.7.3/1.7.5-1.7.8): decidido, no implementado

Decidido en `docs/evaluaciones/1.7.3/decision.md`. La seleccion de perfil de embeddings y la metadata de
coleccion (criterio 5 de esa decision) ya estan implementadas, ver la seccion anterior. **El resto no
existe todavia en la API**; los items 1.7.5-1.7.8 lo implementan. Forma prevista:

- Un origen nuevo `proyecto`, aparte de `documentos` y de `ruta` (que sigue en 501), con dos fuentes:
  URL de un repo Git (solo hosts de una lista blanca de configuracion: la organizacion de Azure DevOps;
  el servicio clona con su propia identidad, nunca con la del usuario) o `.zip` subido (hasta 100 MiB,
  leido en memoria, con `carpetaRaiz` opcional para el envoltorio `repo-rama/`). Respeta el
  `.gitignore` y el `.ragignore` del proyecto. Es **asincrona**: 202 con `ingestaId` y consulta de estado.
- Perfil de LLM para el resumen de negocio (por defecto de la coleccion, sustituible por solicitud) como
  fase reanudable con cache y tope de cuota (1.7.7).
- Resumen de negocio, perfil de escaneo y lenguajes: atributos de la coleccion, fijados al crearla.

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
