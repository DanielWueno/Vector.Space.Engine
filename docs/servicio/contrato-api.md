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
**400** cuerpo invalido (solo despues de autorizar: sin acceso es 403 aunque el cuerpo sea invalido);
**409** la coleccion ya existe con vectores de otra dimension; **501** parte del contrato aun sin
implementar (origen `ruta`); **503** Qdrant o el sidecar de embeddings no respondieron (el mensaje no
dice a que host ni puerto se intento conectar).

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

## Cuerpos

```jsonc
// POST .../embeddings   (1 a 64 textos, cada uno no vacio y de hasta 8192 caracteres)
{ "textos": ["texto 1", "texto 2"] }
// 200: { "coleccion", "modelo", "dimension", "vectores": [[...], [...]] }

// POST .../busquedas    (consulta de hasta 2048 caracteres; topK 1..100, por defecto 10)
{ "consulta": "como se configura X", "topK": 10 }
// 200: { "coleccion", "resultados": [{ "id", "puntuacion", "ruta", "texto" }] }

// POST .../ingestas     origen 'documentos': 1 a 100 documentos, ruta unica y contenido <= 1 MiB c/u
{ "origen": { "tipo": "documentos", "documentos": [{ "ruta": "docs/a.md", "contenido": "..." }] } }
// origen 'ruta': reservado para el item 1.7, hoy responde 501
{ "origen": { "tipo": "ruta", "ruta": "/ruta/accesible/al/servicio" } }
// 200: { "ingestaId", "coleccion", "estado": "completada", "documentosRecibidos",
//        "documentosIndexados", "documentosSinCambios", "documentosOmitidos" }
```

Decisiones que dejan abierto el item 1.7 (modelo de consumo de ingesta):

- Calcular embeddings de un texto suelto y la ingesta son **superficies separadas**; la primera no
  escribe en la coleccion.
- La ingesta declara su `origen`: el contenido en el cuerpo es solo uno de los tipos. Disparar sobre
  una ruta ya accesible al servicio cabe en el mismo contrato sin romperlo; queda en 501 porque leer
  rutas del servidor exige antes fijar raices permitidas por coleccion.
- La respuesta de ingesta ya trae `ingestaId` y `estado`, para que una ingesta en cola pueda
  responder 202 con la misma forma.

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
