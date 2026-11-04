# Contrato HTTP de embeddings/ingesta (MVP)

Definido en el item `1.3-definir-contrato-api-y-autenticacion` del ledger. Rutas relativas a la
ruta base del servicio (`/reyma/vectorspace/engine` desplegado; vacia en local) y al prefijo global
`api/` de la plantilla. Swagger (solo Development) publica el mismo contrato.

Guia de uso: [filtros de busqueda y ejemplos](filtros.md). Distingue los filtros
HTTP disponibles de las capacidades aun pendientes del cliente de Rag.
Lenguaje y namespace estan disponibles desde 1.21.4; el adaptador de Rag sigue pendiente.

| Metodo | Ruta | Operacion | Respuesta 200 |
|---|---|---|---|
| GET | `api/sesion` | — (solo login) | Usuario en sesion desde los claims |
| POST | `api/colecciones/{coleccion}/embeddings` | `Consultar` | Vectores densos de textos sueltos |
| POST | `api/colecciones/{coleccion}/busquedas` | `Consultar` | Resultados de busqueda en la coleccion |
| POST | `api/colecciones/{coleccion}/ingestas` | `Ingestar` | Resultado de la ingesta (origen `documentos`, sincrona) |
| POST | `api/colecciones/{coleccion}/ingestas/proyecto` | `Ingestar` | **202**: ingesta admitida (origen `proyecto` desde `.zip`, asincrona, item 1.7.5) |
| POST | `api/colecciones/{coleccion}/ingestas/git` | `Ingestar` | **202**: ingesta admitida (origen `proyecto` desde URL Git, asincrona, item 1.7.6) |
| GET | `api/colecciones/{coleccion}/ingestas/{ingestaId}` | `Consultar` | Estado de una ingesta de proyecto (items 1.7.5/1.7.6) |
| GET | `api/colecciones/ingestas` | `Consultar` | Lista las ingestas de proyecto, en cualquier estado; `?coleccion=` opcional filtra por una (items 1.15/1.16) |
| GET | `api/colecciones` | `Consultar` | Lista las colecciones existentes en el vector store, con su perfil de embeddings si lo tiene (item 1.16) |
| GET | `api/colecciones/{coleccion}/documentos` | `Consultar` | Lista los documentos indexados en la coleccion: ruta, huella y cantidad de puntos (item 1.19) |

Codigos comunes: **401** sin token valido (firma, emisor, audiencia y vigencia se validan como en
la plantilla); **400** cuerpo invalido, incluido pedir un `perfil` de embeddings no registrado en el servicio o un modelo que el servidor remoto
no tiene cargado para esa clave (1.7.4: nunca 503, siempre con los perfiles disponibles), o un `.zip`
invalido o malicioso en `ingestas/proyecto` (1.7.5: limites de tamano/entradas/ratio, Zip Slip, enlaces
simbolicos o sin documentos admitidos por el perfil de escaneo);
**404** (solo `ingestas/proyecto/{ingestaId}`) ese identificador no existe o no es de esta coleccion;
**409** la coleccion ya existe con vectores de otra dimension, **o** con el mismo numero de dimensiones
pero de otro perfil de embeddings (1.7.4: el perfil, no solo la dimension, identifica si dos vectores son
comparables), **o** se pide `resumen: true` sobre una coleccion existente que no tiene el vector
`dense-resumen` (1.7.7: el tercer vector no se puede agregar despues de crear la coleccion), **o** se busca con
`espacio: "resumen"` en una coleccion sin `dense-resumen` o cuyo resumen se genero con otro perfil o dimension
(1.20), **o** se busca con `modulos` en una coleccion existente sin preparacion completa del filtro de modulo, o se
ingesta en una coleccion con un relleno de modulo en curso o interrumpido (1.21.1); **413**
(`ingestas` e `ingestas/proyecto`) el cuerpo de la solicitud supera el limite de la accion (1.7.1/1.7.5);
**429** (`ingestas`, `ingestas/proyecto` e `ingestas/git`) hay demasiadas ingestas en curso o en espera,
reintentar despues de `Retry-After` (1.7); **501** parte del contrato aun sin implementado (origen
`ruta`); **503** Qdrant, el sidecar de embeddings o el perfil de LLM del resumen no respondieron (el
mensaje no dice a que host ni puerto se intento conectar). `ingestas/git` responde **400** si la URL no
esta en la lista blanca de origenes Git permitidos, sin conexion al destino (item 1.7.6); tambien **400**
si se pide `resumen: true` (ingestas) o `espacio: "resumen"` (busquedas, 1.20) y esta instancia no tiene el
resumen de negocio configurado (`Motor:Resumen:PerfilEmbeddings` vacio, item 1.7.7).

## Motor (item 1.4)

El motor es el de Rag.Context.Engine portado (ver
`src/Reyma.VectorSpace.Engine.Microservice.Infrastructure/PORTE.md`): mismo chunking, mismos Id y
`content_hash`, mismo esquema de coleccion en Qdrant. El vector denso lo calcula el sidecar (TEI) con
`snowflake-arctic-embed-m-v2.0` a 256 dimensiones (1.6), normalizado L2; el vector `sparse-code` es el
`SparseTokenizer` propio.

- **embeddings**: vectores de *documento* (sin el prefijo `query: `), los mismos que guardaria una ingesta
  de esos textos. No escribe nada.
- **busquedas**: rama densa exacta (coseno >= 0,10, desempate por id), sin fusion con el sparse (1.10) salvo que
  `Motor:Busqueda:Modo` este en `FusionNativa` (RRF nativa denso + sparse-code). `puntuacion` es el coseno en modo
  denso, el score RRF en fusion. Una coleccion autorizada que aun no tiene ingestas responde 200 sin resultados.
  Cada resultado trae ademas los metadatos de chunk ya guardados en el payload y admite un filtro exacto opcional
  por simbolos definidos (item 1.18, ver seccion dedicada abajo); igual en ambos modos de busqueda. Con
  `espacio: "resumen"` consulta el vector `dense-resumen` en vez del del codigo (item 1.20, ver seccion dedicada).
  `modulos` acota por namespace/ruta antes del corte en todas las ramas (item 1.21.1, ver seccion dedicada).
  `lenguaje` y `namespace` agregan filtros textuales antes del corte sobre sus campos de payload (1.21.4).
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

## Perfil de ingesta compatible opt-in (1.12)

`perfilIngesta: "rag-local-1.12"` y `repositorioLogico` se admiten juntos en el
JSON de documentos/Git y como campos del formulario ZIP. Ambos ausentes conservan
el perfil nativo (1024 tokens/configuracion existente, repositorio=coleccion).
No son el campo `perfil` de embeddings ni cambian el modo de busqueda.
Un perfil desconocido, tipo no textual, nombre blanco, de mas de128 caracteres
o con separadores/caracteres de control responde400; un nombre sin perfil tambien.

El opt-in reutiliza los chunkers/admisiones del servidor con512/64 tokens,
lote32, contexto padre5 y SplitWithOverlap, sin declaraciones cortas. La cabecera
de ruta usa `\` como el control Windows; la identidad normaliza separadores.
El nombre logico, no la coleccion fisica, identifica repositorio/documentos/chunks.
El cliente sigue enviando documentos completos, nunca fragmentos preparados.

La coleccion H2 se crea con `perfil_ingesta` y `repositorio_logico` atomicos en su
metadata. Una coleccion nativa/legacy, incluso vacia, no se adopta como H2.
Omitir/cambiar el perfil o nombre de una H2 responde409 antes de embeddings;
ZIP/Git verifican el binding antes de admitir el job y al procesarlo.
Los campos se propagan a respuestas/estado de ingesta y al catalogo de colecciones.
El listado y barrido de documentos usan el nombre logico; no se renombra el indice.
Los jobs siguen siendo memoria de instancia: un404 tras reinicio no es completitud.

**Implementado, no aceptado como sustituto del control:** la igualdad exhaustiva
de representacion paso, pero el diagnostico H2 perdio any/full@10 en tickets014.
Ver [preparacion y resultado de1.12](../evaluaciones/1.12/preparacion.md).
Defaults nativos, autenticacion, filtros y resumenes no cambian.
Revertir codigo o desactivar el opt-in no revierte puntos existentes.

## Presupuesto opt-in de busqueda (H3, 1.12)

`prefetchPorRama` es opcional, entero JSON estricto entre **40 y 1200**, solo
para `codigo` con `Motor:Busqueda:Modo=FusionNativa`. Tipos/rangos invalidos,
modo `Denso` o espacio `resumen` responden **400 antes de consultar colecciones
o consumir embeddings**. Omitido o `null` conserva exactamente
`max(4*topK,40)`; no cambia los defaults nativos.

El presupuesto corta cada rama elegible; `topK` (1..100) limita por separado
los resultados fusionados. Los filtros siguen aplicandose antes de cada corte,
con piso denso 0.1, RRF f32 k=2, orden denso-luego-sparse y desempate UUID ordinal.
Dos prefijos de40 ofrecen **como maximo80 candidatos distintos**, menos si hay
solapamiento o menor elegibilidad: pedir100 no garantiza100. No hay relleno.
Las consultas exactas pueden examinar mas puntos para resolver empates completos;
ese trabajo no amplia el prefijo elegible solicitado.

El cliente experimental activa `VectorSpace:UseRetrievalBudget` (defaultfalse)
solo para su busqueda primaria: `max(4*B,40)`, con `B=TopK` o `3*TopK` si hay
reranking. Resumen y saltos por simbolos no envian override. El ranking puede
cambiar con el opt-in; implementar este contrato no acredita calidad ni cierra1.12.

## Cuerpos

```jsonc
// POST .../embeddings   (1 a 64 textos, cada uno no vacio y de hasta 8192 caracteres)
{ "textos": ["texto 1", "texto 2"], "perfil": "altheia-bge-m3-256" }   // "perfil" opcional, ver abajo
// 200: { "coleccion", "modelo", "dimension", "vectores": [[...], [...]] }

// POST .../busquedas    (consulta de hasta 2048 caracteres; topK 1..100, por defecto 10)
{ "consulta": "como se configura X", "topK": 10, "perfil": "altheia-bge-m3-256",
  "simbolosDefinidos": ["MiMetodo"],    // "perfil" y "simbolosDefinidos" opcionales, ver seccion "Metadatos de chunk..." abajo
  "modulos": ["Empresa.Ventas"],        // opcional, 1..50; AND entre modulos y con simbolosDefinidos (item 1.21.1)
  "lenguaje": "CSharp",                 // opcional, nombre exacto del enum; Match.Text sobre language (1.21.4)
  "namespace": "Empresa.Ventas",        // opcional; Match.Text SOLO sobre namespace, sin rutas ni normalizacion
  "espacio": "codigo" }                 // opcional, "codigo" (por defecto) | "resumen"; con "resumen", sin "perfil" (item 1.20)
// 200: { "coleccion", "resultados": [{ "id", "puntuacion", "ruta", "texto",
//        "lineaInicio", "lineaFin", "lenguaje", "tipoChunk", "contentHash",      // item 1.18
//        "namespace", "clase", "metodo",                                        // item 1.18, null si el chunk no aplica
//        "simbolosDefinidos", "simbolosConsumidos",                             // item 1.18, lista vacia, nunca null
//        "resumen" }] }                                                         // item 1.20, null si el chunk no lo tiene

// H3/1.12, solo codigo y Motor:Busqueda:Modo=FusionNativa:
// campo opcional "prefetchPorRama": 40..1200, entero JSON estricto (no "40",40.0,4e1,true).
// Omitido/null conserva max(4*topK,40); topK sigue limitando resultados, no cada rama.

// POST .../ingestas     origen 'documentos': 1 a 100 documentos, ruta unica y contenido <= 1 MiB c/u
{ "origen": { "tipo": "documentos", "documentos": [{ "ruta": "docs/a.md", "contenido": "..." }] },
  "perfil": "altheia-bge-m3-256",   // "perfil" opcional, ver abajo
  "resumen": false }                // "resumen" opcional, item 1.7.7, ver seccion "Resumen de negocio" abajo
// origen 'ruta': reservado para el item 1.7, hoy responde 501
{ "origen": { "tipo": "ruta", "ruta": "/ruta/accesible/al/servicio" } }
// 200: { "ingestaId", "coleccion", "estado": "completada", "documentosRecibidos",
//        "documentosIndexados", "documentosSinCambios", "documentosOmitidos", "documentosEliminados",
//        "resumenesGenerados", "resumenesPendientes", "resumenTokensConsumidos",
//        "perfilIngesta", "repositorioLogico" }  // binding1.12; null para nativo

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
//      los conteos (documentosIndexados/SinCambios/Omitidos/Eliminados/Resumenes*) solo son definitivos en "completada";
//      "mensajeError" (item 1.15) solo tiene valor cuando "estado" es "error" (el mensaje de la excepcion que corto
//      el procesamiento en segundo plano), null en los demas casos

// GET api/colecciones/ingestas?coleccion=X   lista las ingestas de proyecto, en cualquier estado (items 1.15/1.16)
// 200: [ { "ingestaId", "coleccion", "estado", "documentosRecibidos", ..., "mensajeError" }, ... ]
//      mismo cuerpo por elemento que GET .../ingestas/{ingestaId}; "coleccion" en la query string es opcional:
//      ausente trae las ingestas de cualquier coleccion, presente filtra a esa; lista vacia sin coincidencias
//      (nunca 404); sin orden garantizado

// GET api/colecciones   lista las colecciones existentes en el vector store (item 1.16)
// 200: [ { "coleccion", "perfilEmbeddings", "modelo", "dimension", "perfilIngesta", "repositorioLogico" }, ... ]
//      perfilEmbeddings/modelo/dimension salen de la metadata de la coleccion (item 1.7.4); los tres null si la
//      coleccion no la tiene (creada antes de 1.7.4 y nunca tocada desde entonces); lista vacia si no hay ninguna
//      coleccion (nunca 404)

// GET api/colecciones/{coleccion}/documentos   documentos indexados en la coleccion (item 1.19)
// 200: [ { "ruta", "huella", "puntos" }, ... ]
//      agrupados por su clave de identidad (file_path, sin el prefijo "{coleccion}/"); "huella" es el
//      document_fingerprint compartido por todos los puntos de ese documento, null si no coinciden (mismo criterio
//      de "cambiado" que la ingesta); "puntos" es cuantos chunks quedaron indexados para ese documento; coleccion
//      inexistente responde con una lista vacia (nunca 404)
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

## Metadatos de chunk en busquedas y filtro por simbolos definidos (item 1.18)

`POST .../busquedas` solo devolvia `id`, `puntuacion`, `ruta` y `texto`; el payload de cada punto en Qdrant ya
guardaba el resto desde la ingesta (`QdrantVectorStore.UpsertBatchAsync`). 1.18 expone esos campos y agrega un
filtro exacto opcional por `defined_symbols`, sin ningun salto de simbolo ni fusion/reranking (eso sigue siendo
responsabilidad del consumidor, no de este servicio).

- **Campos nuevos por resultado**: `lineaInicio`, `lineaFin`, `lenguaje`, `tipoChunk`, `contentHash` (la ingesta los
  escribe en todo punto; null solo en un punto de una ingesta anterior a este item que no los tenga);
  `namespace`/`clase`/`metodo` (null cuando el chunk no aplica, p. ej. un chunk de Markdown); `simbolosDefinidos`/
  `simbolosConsumidos` (lista vacia, **nunca** null, cuando el chunk no define o no consume ningun simbolo, o la
  estrategia de chunking no los extrae). Mismos campos y mismo comportamiento en modo denso y en modo `FusionNativa`
  (1.10): la construccion del resultado es un unico paso posterior a elegir la rama de busqueda.
- **`simbolosDefinidos` en el cuerpo de la peticion** (opcional): 1 a 50 cadenas no vacias; fuera de ese rango,
  **400**. Acota la busqueda a puntos cuyo `defined_symbols` contenga alguno de esos nombres (coincidencia exacta
  sobre el indice keyword ya existente, `Conditions.Match` por lista = coincide con cualquiera), con el mismo
  umbral de similitud y el mismo desempate deterministico por id que ya usaba la busqueda. Ausente (el caso de
  siempre): la busqueda no cambia respecto a antes de este item (mismos ids, orden y puntuaciones).
- **Fuera de alcance, a proposito**: filtrar por `class_name`/`method_name` (ese campo no tiene indice y crear uno
  nuevo sobre colecciones servidas es otra decision); cualquier salto de simbolo, tope de semillas, peso de
  refusion o reranking (responsabilidad del consumidor). El texto resumido por hit lo agrego despues el item 1.20
  (campo `resumen`, ver "Busqueda en el espacio resumen" abajo); `contentHash` no lo sustituye.

## Busqueda en el espacio resumen (item 1.20)

Las colecciones creadas con resumen de negocio (1.7.7) guardan un tercer vector, `dense-resumen`, que hasta 1.20 no
se podia consultar. `espacio` en el cuerpo de `POST .../busquedas` elige que vector guardado se consulta; el
servicio no fusiona espacios, no los pondera ni decide cuando usar cada uno (eso es del consumidor; en el origen era
una RRF ponderada de tres ramas calibrada para MiniLM, que 1.10 mostro que no se traslada a arctic).

- **`espacio`** (opcional): `"codigo"` (por defecto, y lo mismo que ausente) o `"resumen"`; cualquier otro valor
  (incluido `"Resumen"` con mayuscula o la cadena vacia), **400**. `"codigo"` no cambia nada respecto a antes de 1.20:
  mismos ids, orden y puntuaciones (medido pareado contra el codigo anterior sobre la misma coleccion).
- **`"resumen"`** consulta solo `dense-resumen`, siempre por la rama densa exacta (tambien con
  `Motor:Busqueda:Modo=FusionNativa`: no hay sparse del resumen con que fusionar), con el mismo desempate
  deterministico por id. La consulta se vectoriza con el perfil de `Motor:Resumen:PerfilEmbeddings` (y su prefijo de
  consulta), no con el perfil de la coleccion: por eso `perfil` en el cuerpo junto con `"resumen"` responde **400**
  en vez de ignorarse. `simbolosDefinidos` (1.18) se aplica igual que en el espacio de codigo.
- **Solo devuelve chunks con `dense-resumen`**: los "sin contenido de negocio" (sentinel, 1.7.7) y los que siguen
  `resumen_pending` (la fase 2 no los alcanzo por el tope de tokens) no tienen ese vector y no aparecen en este
  espacio; si aparecen en el de codigo.
- **Umbral**: `Motor:Busqueda:UmbralSimilitudResumen`, **0 por defecto = sin umbral** (devuelve el top-K). El 0,10 de
  `UmbralSimilitud` se calibro para el vector del codigo (1.10) y no se aplica a este espacio. Cada despliegue puede
  fijarlo si lo calibra; `puntuacion` es el coseno contra `dense-resumen`, asi que el consumidor tambien puede cortar.
  No se midio recall en este item.
- **Errores, en este orden y antes de llamar al sidecar de embeddings**: instancia sin
  `Motor:Resumen:PerfilEmbeddings`: **400**, sin tocar Qdrant. Coleccion inexistente: **200** sin resultados (igual que
  el espacio de codigo). Coleccion sin `dense-resumen` en su esquema real de Qdrant (no en la metadata): **409**.
  `dense-resumen` de otra dimension que el perfil de resumen de la instancia: **409**. Misma dimension pero otro
  perfil de resumen (ver `perfil_resumen` abajo): **409**.
- **`perfil_resumen` en la metadata de la coleccion**: desde 1.20, crear una coleccion con resumen guarda tambien el
  nombre del perfil de embeddings del resumen. Cierra, para la busqueda, el mismo hueco que 1.7.4 cerro para
  `dense`: sin el, cambiar `Motor:Resumen:PerfilEmbeddings` a otro modelo de la misma dimension haria que la
  busqueda comparara vectores no comparables sin error visible. Una coleccion creada antes de 1.20 no tiene ese campo: solo se comprueba la
  dimension. La fase 2 de resumen (escritura) no lo verifica todavia; si se cambia el perfil de resumen de una
  instancia, los chunks nuevos de una coleccion vieja se vectorizan con el perfil nuevo.
- **`resumen` en cada resultado, en cualquier espacio**: el texto del resumen de negocio del chunk, persistido desde
  1.20 en el payload (`resumen_texto`) junto con `dense-resumen`. **null** si la coleccion no tiene resumen, si el
  chunk sigue pendiente, si es "sin contenido de negocio", o **si se resumio antes de 1.20**: hasta 1.20 el texto
  solo vivia en una cache en memoria por instancia y no se recupera sin volver a llamar al LLM. 1.20 no rellena esos
  chunks (0 tokens); hoy no existe un mecanismo para volver a marcar como pendiente un chunk sin cambios, asi que el
  texto aparece cuando el documento cambia y se vuelve a resumir, o al reingestar en una coleccion nueva.

## Filtro exacto de modulo en busquedas (item 1.21.1)

Decision en `docs/evaluaciones/1.21/decision.md`: `modulos` es un filtro opcional para acotar consultas y pruebas.
**No es autorizacion** (1.14 sigue retirada: no lee claims ni restringe a nadie), no hace reranking ni salto por
simbolo y no promete mejora de recall.

- **`modulos` en el cuerpo de `POST .../busquedas`** (opcional): ausente o `null` = la busqueda no cambia (mismos
  ids, orden y puntuaciones que antes de este item, medido pareado sobre la misma coleccion). Presente: arreglo de 1
  a 50 cadenas no blancas; arreglo vacio, mas de 50, elemento null/vacio/solo espacios o cualquier tipo que no sea un
  arreglo de cadenas JSON (numero, booleano, objeto, cadena suelta, elemento numerico) → **400** antes del motor. No
  se recorta, no se pasa a minusculas ni se normaliza nada: `" alpha"` es otro modulo.
- **Predicado por modulo `m`**, comparacion ordinal: el punto cumple si `namespace == m` o empieza por `m + "."`,
  **o** si `relative_path == m` o empieza por `m + "/"`. Pidiendo `alpha`: cumplen `alpha`, `alpha.Foo`, `alpha..Bar`,
  `alpha.`, `alpha/child/Foo.cs`, `alpha//x.md`; no cumplen `alpha-private`, `alpha_private`, `Alpha`,
  `Company.alpha`, `.alpha`, `src/alpha/Foo.cs`, `/alpha/x.md`, `alphabet/x.md`, ni un punto sin namespace cuya ruta no
  case. Varios modulos: **AND** (cada uno puede cumplirse por campos distintos); con `simbolosDefinidos`: AND con el
  ANY interno de simbolos. Una conjuncion imposible devuelve 200 sin resultados.
- **Antes del corte, en todas las ramas**: el predicado va como filtro de Qdrant dentro de la consulta densa exacta,
  de cada rama de `FusionNativa` (denso y sparse, antes del pool y de la RRF) y del espacio `resumen`. Si hay K
  elegibles por encima del umbral, se devuelven K aunque haya falsos candidatos de mayor score. Mismo umbral,
  mismas escalas y mismo desempate por id ordinal.
- **Indice**: cada punto lleva `module_prefixes` (keyword, indexado): la union ordinal de el namespace completo y
  cada prefijo anterior a un `.`, y la ruta completa y cada prefijo anterior a un `/` (separadores repetidos
  incluidos; nunca la cadena vacia), y `module_prefixes_version` (entero, indexado). La consulta es una condicion
  keyword exacta por modulo, no `Match.Text`. Las ingestas nuevas siempre los escriben; los Ids, vectores,
  `content_hash`, huella, resumenes y `ChunkingContract.Version` no cambian.
- **Completitud, en este orden y antes de vectorizar la consulta**: coleccion inexistente → **200** sin resultados.
  Coleccion existente con `modulos` → **409** si la metadata no tiene `module_prefixes: "completo:1"`, si falta o es
  incompatible cualquiera de los dos indices, o si **algun** punto no tiene `module_prefixes_version = 1` (conteo
  exacto); nunca una lista vacia de exito sobre un indice incompleto. Sin `modulos` no se exige preparacion.
  Las colecciones creadas desde este item nacen completas.
- **Colecciones anteriores**: se preparan con la herramienta explicita `tools/relleno-modulo` (ver su README):
  SetPayload paginado, sin revectorizar ni llamar al LLM, idempotente y reanudable, con marca
  `en_curso:<run>:<expira>` y lease. Mientras la marca este en curso (o interrumpida), las **ingestas** de este
  servicio en esa coleccion responden **409** y la busqueda con `modulos` tambien; relanzar el relleno la completa.
  Una reingesta de documentos sin cambios los omite y **no** repara sus puntos antiguos: hace falta el relleno.
- **Escritores antiguos**: un proceso con el codigo anterior a este item no conoce la marca ni escribe
  `module_prefixes`. El procedimiento exige detenerlo o actualizarlo antes del relleno; si aun asi escribe, el relleno
  no marca completo y, si escribe despues, la busqueda con `modulos` vuelve a responder 409 (por conteo) hasta
  relanzar el relleno. Qdrant no ofrece compare-and-set de metadata: la exclusion entre dos rellenos es por lease
  verificado en cada pagina y sus escrituras son deterministas e idempotentes.
- **Rollback**: revertir el commit restaura contrato, escritor y lector anteriores. `module_prefixes`,
  `module_prefixes_version`, sus indices y la marca son aditivos y quedan inertes: el lector anterior los ignora
  (comprobado con el lector de 538cb96 sobre una coleccion de prueba preparada: mismos ids, orden y puntuaciones).
  Si hiciera falta retirarlos de una coleccion real, solo con alcance autorizado y respaldo verificado (ver el README
  de la herramienta); nunca borrar colecciones como rollback.

## Filtros textuales de lenguaje y namespace (item 1.21.4)

Disponibles en `POST .../busquedas`, sin migracion ni nuevos indices. No son permisos:
la autenticacion HTTP sigue obligatoria y tenant/Authorized no se implementan aqui.

- **`lenguaje`**: ausente/null no filtra. Solo admite cadenas con uno de los nombres exactos
  `CSharp`, `TypeScript`, `JavaScript`, `Xaml`, `Sql`, `Markdown`, `PlainText`, `Unknown`.
  Vacio/blanco, otro nombre (incluido `"csharp"`), numero, booleano, arreglo u objeto: **400 antes del motor**.
  El nombre valido viaja literal en `Match.Text` sobre `language`, no como igualdad keyword.
- **`namespace`**: ausente/null/vacio/blanco no filtra. Cualquier cadena no blanca se conserva literalmente
  (espacios, mayusculas y separadores incluidos); los tipos JSON distintos de cadena/null responden **400**.
  Se envia como `Match.Text` SOLO sobre `namespace`: no es `StartsWith`, keyword, modulo ni OR con ruta.
- **Combinacion**: lenguaje AND namespace AND cada modulo AND el ANY interno de `simbolosDefinidos`.
  El filtro se aplica dentro de la rama densa, la sparse de `FusionNativa` y la densa de resumen,
  antes del pool/corte. No cambia los umbrales, el desempate por id ni las escalas coseno/RRF.
- **Campos ausentes**: un punto sin `language` o sin `namespace` no satisface el filtro activo de ese campo.
  Sin ese filtro, la ausencia no lo excluye. Una ruta coincidente nunca suple un namespace ausente/distinto.
  El filtro textual no exige el relleno de modulo; si ademas se piden `modulos`, se conserva su puerta 409.
- **Version y esquema**: comprobado con Qdrant **1.19.2**, sin indices full-text de estos campos, contra
  consultas directas `Match.Text` independientes sobre los mismos puntos. En esa referencia
  `Reyma.Ventas` y `reyma.ventas` coinciden con `Reyma.Ventas.Api`, no con
  `Reyma.VentasExtra.Api` ni `xReyma.Ventas`. El operador depende de version/tokenizacion/indices:
  no se promete paridad con 1.14.1 ni con otra instancia no comprobada.
- **Sin filtros nuevos**: IDs, orden y scores identicos al lector de `da1b3d0` en el banco pareado,
  incluidos los filtros anteriores y ambos espacios/modos. Payload, vectores, huellas e indices intactos.
  No es evidencia de mejora de recall ni activa el cliente remoto de Rag.

Ejemplo, con ambos filtros y los existentes:

```json
{
  "consulta": "confirmar pedido",
  "topK": 5,
  "lenguaje": "CSharp",
  "namespace": "Empresa.Ventas",
  "modulos": ["Empresa", "pruebas"],
  "simbolosDefinidos": ["ConfirmarPedido", "CancelarPedido"]
}
```

Para resumen se agrega `"espacio": "resumen"` sin `perfil`, con el resumen configurado y su vector presente.
Reproduccion, evidencia y limites: [banco 1.21.4](../evaluaciones/1.21/filtros-textuales.md).
Rollback: revertir el commit de 1.21.4; no hay escrituras/migracion de datos que revertir.
Un cliente que requiera filtros no debe reintentar sin ellos contra un servidor anterior.

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
  `ingestaId` consultable (el trabajo ya escrito en Qdrant no se deshace). El listado de
  `GET api/colecciones/ingestas` (items 1.15/1.16) tiene el mismo limite: solo muestra lo que paso desde el
  ultimo arranque de esta instancia.
- **Resumen de negocio**: implementado en 1.7.7, ver la seccion dedicada abajo.
- **Listado de ingestas, mensaje de error y catalogo de colecciones**: implementados en 1.15/1.16, ver las
  secciones dedicadas abajo.
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

## Listado de ingestas y mensaje de error (item 1.15, filtro opcional desde 1.16)

Hallazgo: el `ingestaId` de `POST .../ingestas/proyecto`/`.../ingestas/git` solo era visible en el momento de esa
respuesta 202; sin ningun endpoint para recuperarlo despues, si varias personas del equipo ingestan a la misma
coleccion, cada una solo podia consultar la suya propia. `GET api/colecciones/ingestas` cierra ese hueco.

- **`GET api/colecciones/ingestas?coleccion=X`**: mismo cuerpo por elemento que `GET .../ingestas/{ingestaId}`.
  `coleccion` es un filtro opcional por query string (item 1.16, el endpoint original de 1.15 lo exigia en la ruta):
  ausente trae las ingestas de cualquier coleccion registradas en esta instancia; presente, solo las de esa
  coleccion. Nunca 404: sin coincidencias responde **200** con una lista vacia. Sin orden garantizado. No reemplaza
  la consulta puntual (`GET .../ingestas/{ingestaId}`), que sigue disponible.
- **`mensajeError`**: hasta 1.15, `MarcarError` guardaba el mensaje de la excepcion solo en el log del servicio y lo
  descartaba para el estado expuesto al cliente. Ahora queda en `IngestaResponse.MensajeError`: null salvo que
  `estado` sea `"error"`, donde trae el mensaje de la excepcion que corto el procesamiento en segundo plano. Aplica
  a los dos origenes asincronos (`.zip` y Git) y se ve tanto en la consulta puntual como en el listado.
- **Limitacion conocida (no resuelta, a proposito fuera de alcance)**: el registro sigue en memoria, por instancia
  (igual filosofia que `LimiteDeIngestas`, ver arriba): el listado solo muestra lo que paso desde el ultimo arranque
  de esta instancia, y no se comparte entre replicas. Resolverlo implicaria persistencia real, lo que recrearia la
  capa `Persistence` retirada en el item 1.9; esa decision queda fuera de alcance aqui.

## Catalogo de colecciones existentes (item 1.16)

Hallazgo del usuario al revisar 1.15: ni el listado de ingestas (en memoria, por instancia) ni ninguna otra
superficie del contrato permitian saber que colecciones existen en el vector store sin acceso directo a Qdrant --
hacia falta conocer el nombre de la coleccion de antemano para cualquier operacion. El endpoint historico
`GET api/colecciones` (retirado en el item 1.14 junto con `ControlDeAcceso`) resolvia un problema distinto: listaba
la tabla de tenencia por usuario, no las colecciones reales de Qdrant, asi que no es lo mismo que se reintroduce aqui.

- **`GET api/colecciones`**: `{ "coleccion", "perfilEmbeddings", "modelo", "dimension" }` por cada coleccion
  existente, leido directo de Qdrant (`ListCollectionsAsync` del cliente gRPC, mas una lectura REST de metadata por
  coleccion, item 1.7.4). Nunca 404: sin colecciones responde **200** con una lista vacia. **503** si Qdrant no
  responde (mismo criterio que el resto del contrato, sin exponer host ni puerto).
  `perfilEmbeddings`/`modelo`/`dimension` son null si la coleccion no tiene esa metadata guardada (creada antes de
  1.7.4 y nunca tocada desde entonces).
- No esta protegido por tenencia (item 1.14 retiro el control de acceso por coleccion): `[Authorize]` basta, igual
  que el resto del contrato.

## Documentos indexados en una coleccion (item 1.19)

Hallazgo al revisar el motor existente: `ListarRutasDocumentadasAsync` (barrido de ausentes del origen 'proyecto',
1.7.5) recorre toda la coleccion pero solo trae `file_path` e ids, y solo lo usa ese barrido; la busqueda de puntos
existentes de la ingesta (`GetDocumentPointsAsync`) trae la huella, pero solo de rutas pedidas explicitamente.
Ninguna de las dos superficies expone un listado legible por coleccion sin acceso directo a Qdrant. Esto **no** es
el "manifiesto" que el item 1.4 declaro no portado (ese era un registro propio de Rag.Context.Engine con otro
proposito); es una capacidad nueva, mas simple: un listado de solo lectura.

- **`GET api/colecciones/{coleccion}/documentos`**: `{ "ruta", "huella", "puntos" }` por cada documento indexado,
  agrupado por su clave de identidad (`file_path`, ver `ChunkBuilder.BuildIdentityKey`): `"ruta"` es ese valor sin
  el prefijo `"{coleccion}/"` (la ruta normalizada con separadores `/`, no la ruta literal de `relative_path`).
  `"huella"` es el `document_fingerprint` con que se escribieron sus puntos; **null** si los puntos de ese
  documento no comparten una sola huella (mismo criterio de "cambiado" que usa la ingesta para decidir si
  revectoriza un documento, `DocumentIngestionPipeline.IsUnchanged`). `"puntos"` es cuantos chunks quedaron
  indexados para ese documento. Nunca 404: coleccion inexistente responde **200** con una lista vacia (mismo
  criterio que `GET api/colecciones`). **503** si Qdrant no responde, sin exponer host ni puerto.
- **Catalogo de descubrimiento, no ruta caliente**: un solo scroll sin filtro sobre toda la coleccion (comparte la
  mecanica de paginacion con `ListarRutasDocumentadasAsync`, sin cambiar su firma ni su semantica), sin paginar la
  respuesta: el offset del scroll de Qdrant es por punto, no por documento, igual limitacion que el catalogo de
  colecciones (1.16).
- **Fuera de alcance, a proposito**: hash de contenido puro por documento (eso es un dato distinto del
  `document_fingerprint`, que tambien incluye la configuracion de chunking y el modelo); paginar por documento.

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
  (bool), `resumen_modelo` (string) y `resumen_prompt_version` (string, hash de prompt+modelo); desde 1.20 tambien
  `resumen_texto` (payload, solo en chunks con `dense-resumen`) y `perfil_resumen` (metadata de la coleccion).
- **Buscar sobre el resumen**: item 1.20, ver "Busqueda en el espacio resumen" arriba.
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
