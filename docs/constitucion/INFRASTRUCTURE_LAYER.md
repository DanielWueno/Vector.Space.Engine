# Capa Infrastructure

Proyecto `Reyma.VectorSpace.Engine.Microservice.Infrastructure` (item 1.4). Implementa los puertos que declara
Business (`Business/Motor/IMotor.cs`) con dependencias externas: Qdrant (vector store), el sidecar de embeddings (TEI)
y el chunking de Roslyn. Business no conoce Qdrant ni HTTP; Api solo registra (`AddMotorVectorial`).

```
Api -> Business -> Domain
Api -> Infrastructure -> Business
```

## Reglas

- Un puerto nuevo se declara en Business y se implementa aqui. Los handlers de Business no referencian Infrastructure.
- Codigo de infraestructura en ingles; configuracion y mensajes al cliente en espanol (seccion `Motor`).
- Los errores de una dependencia salen como `MotorNoDisponibleException` (503) con un mensaje sin host, puerto ni
  detalle de la excepcion; el detalle va al log. Conflictos de estado (dimension distinta, o perfil de embeddings
  distinto al guardado en la metadata de la coleccion aunque la dimension coincida, item 1.7.4) como
  `ConflictException` (409). Un perfil de embeddings no registrado en esta instancia, o un modelo que el servidor
  remoto rechaza para la clave configurada, como `BadRequestException` (400), nunca 503.
- Opciones validadas al arrancar (`ValidateOnStart`): una configuracion incoherente no deja arrancar el servicio.
- Credenciales de dependencias (p. ej. `Motor__Qdrant__ApiKey`, `Motor__Embeddings__ApiKey`) solo por `.env`
  local/user-secrets en desarrollo, o el gestor de secretos del entorno de despliegue real cuando exista (ninguno
  implementado hoy). Nunca en logs ni mensajes de error (`TeiVectorizationBrain` nunca registra ni expone la clave).
- El readiness (`/health/ready`) comprueba cada dependencia real con un limite de tiempo y sin exponer su direccion.

## Perfiles del sidecar de embeddings (item 1.13)

`EmbeddingsOptions` admite dos perfiles contra el mismo puerto `IVectorizationBrain` (`TeiVectorizationBrain`), sin
cambiar codigo, solo configuracion de la seccion `Motor:Embeddings`:

| | TEI local (por defecto) | AltheIA (servidor de IA corporativo) |
|---|---|---|
| `UrlBase` | `http://127.0.0.1:18080` (docker-compose.motor.yml) | `https://altheia` (sin `/v1`: ver `Ruta`) |
| `Ruta` | `v1/embeddings` | `v1/embeddings` (vLLM expone la misma ruta OpenAI-compatible) |
| `Modelo` | `Snowflake/snowflake-arctic-embed-m-v2.0` | `bge-m3` |
| `Dimension` | `256` (Matryoshka, recorta y renormaliza) | `1024` (dimension nativa de `bge-m3`: **no** se recorta cuando `Dimension` coincide con lo que devuelve el sidecar) |
| `PrefijoConsulta` | `"query: "` | `""` (configurable a vacio; `bge-m3` no necesita prefijo de consulta) |
| `ApiKey` | vacio (TEI local no tiene auth) | clave Bearer de AltheIA, **solo** por `Motor__Embeddings__ApiKey` (variable de entorno o user-secrets; nunca en `appsettings*.json` versionado) |
| `RutaCertificadoCA` | vacio (sin proxy TLS intermedio) | ruta local al certificado CA corporativo (p. ej. Netskope, ver item 1.5) si AltheIA queda detras de el; **solo** por `Motor__Embeddings__RutaCertificadoCA` |

Detalles de implementacion (`MotorServiceCollectionExtensions.AddMotorVectorial`):

- Si `ApiKey` no esta vacia, el `HttpClient` nombrado `TeiVectorizationBrain.HttpClientName` se registra con cabecera
  `Authorization: Bearer <clave>` fija (`DefaultRequestHeaders.Authorization`). Vacia (perfil TEI) = sin cabecera,
  exactamente el comportamiento de hoy.
- Si `RutaCertificadoCA` no esta vacia, el `HttpMessageHandler` primario del mismo cliente valida la cadena TLS
  normalmente y, solo cuando el unico problema es una cadena no confiable (`SslPolicyErrors.RemoteCertificateChainErrors`),
  la reconstruye confiando ademas en ese CA (`X509ChainTrustMode.CustomRootTrust` + `chain.Build(cert)`). Cualquier
  otro error (host que no coincide, certificado vencido, etc.) se sigue rechazando igual que sin CA adicional. Nunca se
  desactiva la validacion TLS ni se acepta un certificado sin reconstruir y validar la cadena.
- `ValidateOnStart` exige que, si `RutaCertificadoCA` esta definida, el archivo exista en disco; si no, el servicio no
  arranca (falla rapido, igual estilo que las demas validaciones de `Motor`).
- `/health/ready` sigue funcionando igual en ambos perfiles: usa el mismo `HttpClient` (con la cabecera y el CA ya
  configurados) contra `health` relativo a `UrlBase`. Confirmado contra el AltheIA real (2026-10-22, ver
  `MotorAltheiaIntegracionTests.HealthReadyComprueboAltheia`): su vLLM expone `/health` en la raiz igual que TEI,
  responde 200.
- TEI local **no se borra ni cambia**: sigue siendo el perfil por defecto (`ApiKey` y `RutaCertificadoCA` vacios).
- Verificado contra AltheIA real (bge-m3, 1024d) en `tests/.../Motor/MotorAltheiaIntegracionTests.cs`, gateado por
  `VSE_ALTHEIA_LOCAL=1` + `ALTHEIA_KEY`/`ALTHEIA_CERT` (variables de usuario) + Qdrant de `docker-compose.motor.yml`:
  ingesta minima en coleccion aislada y descartable -> 200, puntos de 1024d sin truncar en Qdrant, busqueda con
  resultados, segunda pasada "sin cambios", coleccion existente de otra dimension (256) sigue en 409 sin mezclar
  modelos. Documento de prueba minimo a proposito: AltheIA cuenta contra su cuota diaria compartida (1.7).

## Perfiles de embeddings por nombre y metadata de coleccion (item 1.7.4)

1.13 dejaba un solo perfil activo por instancia (se cambiaba de TEI a AltheIA reconfigurando
`Motor:Embeddings` entero). 1.7.4 agrega perfiles **adicionales, simultaneos, seleccionables por nombre**
en una misma instancia, sin tocar el comportamiento de 1.13 cuando no se usan:

- `Motor:Embeddings` sigue describiendo el perfil por defecto, con nombre fijo
  `MotorOptions.PerfilEmbeddingsPorDefecto` (`"tei-arctic-256"`). `Motor:Embeddings:Perfiles:<nombre>`
  (`PerfilEmbeddingsOptions`, implementa el mismo `IPerfilEmbeddingsConfig` que `EmbeddingsOptions`) declara
  cada perfil adicional (p. ej. `altheia-bge-m3-256`): su propio `UrlBase`, `Modelo`, `Dimension`,
  `PrefijoConsulta`, `ApiKey` y `RutaCertificadoCA`, validados igual que el perfil por defecto
  (`ValidateOnStart`); no puede redefinir el nombre del perfil por defecto.
- `AddMotorVectorial` registra un `HttpClient` nombrado por perfil adicional
  (`VectorizationBrainFactory.NombreCliente`, `"motor-embeddings:<nombre>"`), con el mismo patron de
  cabecera `Authorization: Bearer` y CA adicional que el `HttpClient` del perfil por defecto
  (`TeiVectorizationBrain.HttpClientName`).
- `IVectorizationBrainFactory` (`Vectorization/IVectorizationBrainFactory.cs`) resuelve un nombre de perfil
  a un `IVectorizationBrain`: el por defecto reutiliza el `IVectorizationBrain` singleton de siempre; uno
  adicional crea un `TeiVectorizationBrain` con su `HttpClient` propio. Un nombre no registrado lanza
  `BadRequestException` con la lista de perfiles disponibles (400, nunca 503) -- `MotorVectorial`,
  `DocumentIngestionPipeline` y `QdrantSemanticRetriever` reciben el `perfil` pedido (puede ser `null`) y
  resuelven ahi, no antes.
- `QdrantVectorStore` guarda el perfil, el modelo y la dimension en la metadata de la coleccion en Qdrant
  (`EnsureCollectionAsync`/`VerificarPerfilParaConsultaAsync`), por **REST** (`QdrantVectorStore.RestHttpClientName`,
  `Motor:Qdrant:PuertoRest`): el cliente gRPC de Qdrant.Client 1.19 no expone el campo `metadata` de la
  coleccion. Una coleccion sin esa metadata (creada antes de 1.7.4) se trata como del perfil por defecto y
  se le escribe la metadata la primera vez que coincide; si no coincide, 409 igual que si ya la tuviera.
- `TeiVectorizationBrain` traduce los errores de AltheIA por un modelo no permitido para la clave
  (`model_not_allowed`, 403) o de otra capacidad (`model_capability_mismatch`, 400; medido en 1.7.3 contra
  AltheIA real) a `BadRequestException` en vez del `MotorNoDisponibleException` (503) generico de cualquier
  otro error del sidecar.
- Verificado contra Qdrant y TEI reales de `docker-compose.motor.yml` en
  `tests/.../Motor/PerfilesEmbeddingsIntegracionTests.cs` (gateado por `VSE_MOTOR_LOCAL=1`): metadata
  guardada al crear, 409 sin escribir al mezclar perfiles de la misma dimension, compatibilidad retroactiva
  de una coleccion sin metadata, y 400 con los perfiles disponibles al pedir uno no registrado. La
  traduccion de errores de AltheIA se verifica con un sidecar simulado en
  `tests/.../Motor/TeiVectorizationBrainTests.cs` (sin AltheIA real).

## Origen 'proyecto' desde URL Git (item 1.7.6)

`Git/ListaBlancaGit.cs` y `Git/ClonadorGit.cs` vienen de `tools/ingesta-proyecto/Prototipo/` (1.7.3), con una
diferencia deliberada en `ClonadorGit`: el prototipo siempre aislaba la configuracion/credenciales del host; esta
version solo lo hace cuando hay una identidad explicita que inyectar (`Motor:Git:NombreVariableIdentidad`), y si no
la hay, deja que el `credential.helper` ya configurado en el host resuelva la autenticacion (decision puntual del
usuario para la verificacion de 1.7.6, no el diseno de produccion). `Scanning/EscanerDeCheckout.cs` reusa
`ScanProfile` e `IgnoreRules` ya portados para el `.zip` (1.7.5), leyendo el contenido del checkout en disco en vez
de rutas en memoria; a diferencia de `LectorZip` (que lee sin tocar disco), aqui no se siguen enlaces simbolicos ni
junctions al recorrer el checkout. `MotorVectorial` implementa `IMotorDeIngestaProyectoGit` igual que
`IMotorDeIngestaProyecto`: clona a un directorio temporal propio (`Motor:Git:DirectorioTemporal`), escanea,
ejecuta el mismo `DocumentIngestionPipeline.IngestAsync(..., barrerDocumentosAusentes: true)` que el `.zip`, y borra
el checkout siempre al terminar (exito o error). Los limites del clonado (`Motor:Git:MaxBytesCheckout`,
`Motor:Git:PlazoSegundos`) y la lista blanca (`Motor:Git:OrigenesPermitidos`) se validan al arrancar.

## Resumen de negocio reanudable (item 1.7.7)

Revive `OllamaBusinessSummaryGenerator`/`IBusinessSummaryGenerator` de Rag.Context.Engine como una fase 2 que corre
DESPUES de `DocumentIngestionPipeline` (fase 1, sin cambios): prompt, sentinel `SIN_CONTENIDO_DE_NEGOCIO` y
parametros de muestreo (`temperatura 0.15`, `max_tokens 160`, `top_p 0.95`) portados literal. El transporte NO es
Semantic Kernel (como en el origen): es un `HttpClient` nombrado contra la ruta `chat/completions` OpenAI-compatible
de AltheIA, mismo patron que `TeiVectorizationBrain`.

- `Resumen/LlmResumenBrain.cs` (`ILlmResumenBrain`): un chunk por llamada (`ResumirAsync`), devuelve
  `ResumenLlmResultado?` (null si el modelo respondio vacio, aislado a ese chunk). Traduce `model_not_allowed`/
  `model_capability_mismatch` a `BadRequestException` (400), igual que `TeiVectorizationBrain`; cualquier otro
  error a `MotorNoDisponibleException` (503). **Resolucion perezosa del `HttpClient`** (campo `Http =>
  _http ??= _httpClientFactory.CreateClient(...)`, nunca en el constructor): `Motor:Llm:UrlBase` puede estar vacio
  cuando el resumen no esta configurado (opt-in), y `MotorVectorial` (que inyecta `ResumenPipeline` -> `ILlmResumenBrain`)
  se construye en TODA solicitud de ingesta/busqueda/embeddings, use o no resumen. Si el `HttpClient` se creara en
  el constructor, una instancia sin resumen configurado fallaria al resolver el motor en cualquier operacion.
- `Resumen/CacheDeResumenes.cs` (`ICacheDeResumenes`): cache en memoria por `(content_hash, prompt_version,
  modelo)`, misma filosofia que `LimiteDeIngestas` (por instancia, sin persistencia). Cubre contenido identico que
  aparece en mas de un punto; la garantia principal de "reingerir sin cambios gasta 0 tokens" la da
  `DocumentIngestionPipeline` al no reencolar un documento sin cambios para resumen (ver abajo), no esta cache.
- `Resumen/ResumenPipeline.cs`: procesa los puntos `resumen_pending=true` de una coleccion hasta agotar
  `Motor:Resumen:TopeTokensPorCorrida` (tokens de LLM, `usage.total_tokens`) o los pendientes. Una respuesta vacia
  del LLM para un chunk se salta (aislado); un `BadRequestException` (perfil mal configurado) se propaga; cualquier
  otro fallo corta la corrida ENTERA dejando el resto pendiente (no insiste contra una dependencia caida chunk a
  chunk). El vector de resumen usa un perfil de embeddings YA declarado (`Motor:Resumen:PerfilEmbeddings`,
  resuelto via `IVectorizationBrainFactory`, 1.7.4): 1.7.3 midio bge-m3 1024d via AltheIA sin truncar.
- `QdrantVectorStore`: `EnsureCollectionAsync` ahora decide ademas si la coleccion tiene (o debe crear) el vector
  `ResumenDenseVectorName` ("dense-resumen"). La verdad de si una coleccion tiene resumen es el ESQUEMA real de
  Qdrant (`GetEsquemaAsync`, si el vector existe), no la metadata (que es solo informativa): pedir `resumen` sobre
  una coleccion sin ese vector es `ConflictException` (409) antes de tocar nada. Puntos nuevos/cambiados de una
  coleccion con resumen habilitado se escriben con `ResumenPendingPayloadKey` ("resumen_pending") = true;
  `ListarPendientesDeResumenAsync`/`ContarPendientesDeResumenAsync` filtran por ese campo (scroll/count con
  `Conditions.Match(..., bool)`); `ActualizarResumenAsync` escribe el vector (`UpdateVectorsAsync`, solo si el
  chunk tiene contenido de negocio) y apaga `resumen_pending` (`SetPayloadAsync`) en un solo punto a la vez.
- `DocumentIngestionPipeline.IngestAsync` gana el parametro `resumenSolicitado`: resuelve el perfil de embeddings
  del resumen ANTES de `EnsureCollectionAsync` (para saber la dimension si hay que crear el vector), y marca
  `resumen_pending=true` en el upsert solo si la coleccion termina con el resumen habilitado de verdad (devuelto
  por `EnsureCollectionAsync`, no por lo que pidio esta solicitud: es un atributo de la coleccion). Un documento
  "sin cambios" (fingerprint igual) ni siquiera entra a esta fase: no se reencola para resumen, que es como se
  logra "reingerir sin cambios gasta 0 tokens" sin depender solo de la cache.
- `MotorVectorial` dispara `ResumenPipeline.ProcesarPendientesAsync` despues de cada ingesta (`IngerirAsync`,
  `ProcesarAsync` del .zip, `ProcesarGitAsync`) solo si la coleccion quedo con el resumen habilitado; en el origen
  `documentos` (sincrono) esto corre inline antes de responder, respetando el tope de tokens por invocacion; en
  `proyecto` (asincrono) corre dentro de la misma tarea en segundo plano. `IngestaResponse`/`ResultadoIngestaProyecto`
  ganan `resumenesGenerados`/`resumenesPendientes`/`resumenTokensConsumidos` (siempre 0 sin resumen habilitado).

## Catalogo de colecciones (item 1.16)

Nuevo puerto `IMotorDeColecciones` (`Business/Motor/IMotor.cs`), junto a los demas `IMotorDe*`: resuelve el hallazgo
de que ninguna superficie anterior permitia saber que colecciones existen sin acceso directo a Qdrant.

- `QdrantVectorStore.ListarColeccionesAsync`: nombres de `_client.ListCollectionsAsync` (gRPC, igual que
  `GetEsquemaAsync`: las excepciones las traduce `MotorVectorial.ConQdrantAsync` del llamador, no un try/catch local),
  mas una llamada a `LeerPerfilAsync` (REST) por coleccion para su metadata de perfil (1.7.4). Devuelve tuplas
  `(Nombre, PerfilColeccion?)`, no un response de Business: la composicion a `ColeccionInfoResponse` vive en
  `MotorVectorial.ListarAsync` (`IMotorDeColecciones`), igual separacion que el resto del motor (`QdrantVectorStore`
  no conoce los tipos de `Business.Models.Responses`).
- Una llamada REST por coleccion es aceptable para un catalogo de descubrimiento (no es una ruta caliente); no se
  optimizo a una sola llamada porque el cliente gRPC 1.19 no expone `metadata` en `ListCollectionsAsync`.
- Registro DI igual que los demas puertos: `services.AddSingleton<IMotorDeColecciones>(sp =>
  sp.GetRequiredService<MotorVectorial>())` en `MotorServiceCollectionExtensions.AddMotorVectorial`.
- Verificado contra Qdrant y TEI reales de `docker-compose.motor.yml` en `tests/.../Motor/ResumenIntegracionTests.cs`
  (gateado por `VSE_MOTOR_LOCAL=1`, con un `ILlmResumenBrain` de prueba sustituido por DI: no hay ninguna llamada
  real a AltheIA en esas pruebas): vector `dense-resumen` al crear, 409 al activarlo sobre una coleccion existente
  sin el, chunk sin contenido de negocio sin vector, cache por content_hash (dos chunks identicos = una sola
  llamada), corte por tope de tokens y reanudacion sin repetir resumenes, reingesta sin cambios en 0 tokens. La
  traduccion de errores y el formato del prompt se verifican con un sidecar simulado en
  `tests/.../Motor/LlmResumenBrainTests.cs` (sin AltheIA real). Evidencia contra AltheIA real (modelo, coste,
  `enable_thinking`) en `tests/.../Motor/ResumenAltheiaIntegracionTests.cs`, gateada por `RequiereAltheiaFact`
  igual que `MotorAltheiaIntegracionTests`.

## Documentos indexados por coleccion (item 1.19)

Hallazgo al revisar `QdrantVectorStore`: `ListarRutasDocumentadasAsync` (barrido de ausentes del origen 'proyecto',
1.7.5) recorre toda la coleccion sin filtro pero solo trae `file_path` e ids; `GetDocumentPointsAsync` trae la
huella, pero solo de rutas pedidas explicitamente (lo usa la ingesta para decidir si un documento cambio). Ninguno
de los dos expone un listado legible por coleccion sin acceso directo a Qdrant. No es el "manifiesto" que el item
1.4 declaro no portado (ese era un registro propio de Rag.Context.Engine con otro proposito); es una capacidad
nueva, deliberadamente mas simple.

- **`QdrantVectorStore.ScrollAllAsync`** (privado, nuevo): extrae la mecanica de paginacion por scroll (1024 puntos
  por pagina, por offset de Id de punto) que antes estaba solo dentro de `ListarRutasDocumentadasAsync`, parametrizada
  por los campos de payload a traer. La comparten `ListarRutasDocumentadasAsync` (ahora reducido a recorrer y
  agrupar por `file_path`) y la nueva `ListarDocumentosConHuellaAsync` (agrupa por `file_path`, con
  `document_fingerprint` ademas). Ninguno de los dos cambio su firma ni su semantica externa: mismo filtro (ninguno),
  mismo criterio de "coleccion inexistente -> vacio", mismo contrato para el barrido de ausentes de 1.7.5.
- **`IMotorDeColecciones.ListarDocumentosAsync`** (nuevo metodo en el puerto existente de 1.16, no un puerto nuevo):
  implementado en `MotorVectorial.ListarDocumentosAsync`. Compone `ColeccionInfoResponse`-style: pide
  `ListarDocumentosConHuellaAsync` al store, quita el prefijo `"{coleccion}/"` de cada `file_path` (la coleccion es
  siempre el `repositoryName` con que `ChunkBuilder.BuildIdentityKey` arma esa clave, asi que el prefijo a quitar es
  exacto), y calcula la huella comun: si los `Fingerprint` de los puntos de un documento no son todos iguales
  (`Distinct` sobre esa columna da mas de un valor), la huella expuesta es `null` en vez de un valor arbitrario --
  mismo criterio que `DocumentIngestionPipeline.IsUnchanged` usa para decidir si un documento "cambio".
- **Respuesta**: `DocumentoIndexadoResponse(Ruta, Huella, Puntos)` en `Business/Models/Responses/ColeccionesResponses.cs`;
  `GetDocumentosQuery` en `Business/Queries/Colecciones/`; `GET {coleccion}/documentos` en `ColeccionesController`,
  `[Authorize]` sin politica especifica (igual que el resto del contrato desde 1.14).
- **Catalogo de descubrimiento, no ruta caliente**, igual criterio que `ListarAsync` (1.16): sin paginar la
  respuesta (el offset del scroll de Qdrant es por punto, no por documento); aceptable para inspeccionar una
  coleccion, no para servir en el camino caliente de una aplicacion.
- **Fuera de alcance, a proposito**: hash de contenido puro por documento (distinto de `document_fingerprint`, que
  tambien depende de la configuracion de chunking y el modelo); paginar por documento; cambiar `ListarRutasDocumentadasAsync`
  (lo usa el barrido de ausentes de 1.7.5, verificado sin cambios en `tests/.../Motor/IngestaProyectoZipIntegracionTests.cs`).
- Verificado contra Qdrant y TEI reales de `docker-compose.motor.yml` en
  `tests/.../Motor/DocumentosIndexadosIntegracionTests.cs` (gateado por `VSE_MOTOR_LOCAL=1`): N documentos listados
  con el mismo conteo de puntos que un scroll directo, la huella cambia (y solo ella) al editar y reingerir un
  documento, y un documento "borrado" (contenido vacio, mismo patron que el resto del contrato) desaparece del
  listado sin afectar a los demas.

## Codigo portado

`Chunking/`, `Utilities/`, `Model/` y `Vectorization/SparseTokenizer.cs` vienen de Rag.Context.Engine y deben producir
los mismos Ids y `content_hash` que su origen. Antes de tocarlos, leer `PORTE.md` del proyecto: el golden master
portado (`tests/.../Motor/ChunkingGoldenMasterTests.cs`) no se regenera para hacerlo pasar.

## Pruebas

- Unitarias sin dependencias (golden master, sidecar simulado con `HttpMessageHandler`).
- Integracion con el motor real en `MotorIntegracionTests`: solo con `VSE_MOTOR_LOCAL=1` y `docker-compose.motor.yml`
  levantado; puertos fijos de ese compose, nunca un Qdrant servido.
