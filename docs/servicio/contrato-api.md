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
**501** parte del contrato aun sin implementar.

Hasta el item 1.4 el motor no esta portado: las tres operaciones POST autorizadas y validas
responden **501**. El contrato, la autenticacion y el control de acceso ya son los definitivos del MVP.

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
//        "documentosIndexados", "documentosSinCambios" }
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
