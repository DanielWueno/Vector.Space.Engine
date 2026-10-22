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
  detalle de la excepcion; el detalle va al log. Conflictos de estado (p. ej. dimension distinta) como
  `ConflictException` (409).
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

## Codigo portado

`Chunking/`, `Utilities/`, `Model/` y `Vectorization/SparseTokenizer.cs` vienen de Rag.Context.Engine y deben producir
los mismos Ids y `content_hash` que su origen. Antes de tocarlos, leer `PORTE.md` del proyecto: el golden master
portado (`tests/.../Motor/ChunkingGoldenMasterTests.cs`) no se regenera para hacerlo pasar.

## Pruebas

- Unitarias sin dependencias (golden master, sidecar simulado con `HttpMessageHandler`).
- Integracion con el motor real en `MotorIntegracionTests`: solo con `VSE_MOTOR_LOCAL=1` y `docker-compose.motor.yml`
  levantado; puertos fijos de ese compose, nunca un Qdrant servido.
