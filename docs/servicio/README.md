# Servicio generado con `reyma-api`

Este `README.md` se genera junto con el servicio: los cuatro parametros
del generador (`-n`/`--name`, `--base-path`, `--api-name`,
`--permission-prefix`) ya quedaron sustituidos en todo el contenido y en
los nombres de archivos y carpetas. No hace falta renombrar ni editar
namespaces, referencias o la composicion comun; lo unico que falta
completar es la configuracion de entorno que se describe abajo.

## Arquitectura y reglas de trabajo

Esta API usa C#/.NET 10, ASP.NET Core, MediatR, AutoMapper, FluentValidation,
JWT, Swagger y paquetes corporativos Reyma. Los `.csproj` son la referencia
de versiones y dependencias por capa. La solucion separa Domain, Business,
Infrastructure (motor de embeddings/ingesta, item 1.4) y Api, con un proyecto
Tests de xUnit.

Este servicio no usa PostgreSQL/EF Core: guarda vectores en Qdrant y calcula el
vector denso con un sidecar de embeddings (TEI). El item 1.9 del ledger retiro
el proyecto `Persistence` que traia la plantilla (`ApplicationDbContext` no
llego a declarar ningun `DbSet`), junto con `AddReymaPersistence`/`AddNpgSql`,
el chequeo de Postgres en `/health/ready` y la exigencia de `CONNECTION_STRING`.
Si en el futuro se nombra un uso real (por ejemplo, el registro de ingestas
de 1.7), se reintroduce la capa de persistencia con ese alcance concreto, no
como trabajo especulativo.

La constitucion esta incluida. Antes de modificar el servicio, consulta
[su indice y forma de trabajo](../constitucion/README.md) y las guias de
las capas afectadas. Las reglas comunes viven en
[.github/copilot-instructions.md](../../.github/copilot-instructions.md);
[CLAUDE.md](../../CLAUDE.md) y [AGENTS.md](../../AGENTS.md) son entradas a esas mismas reglas.

El contrato HTTP de embeddings/ingesta y su control de acceso por coleccion
estan en [contrato-api.md](contrato-api.md).

## Identificadores del servicio

| Identificador | Valor |
|---|---|
| Nombre | `Reyma.VectorSpace.Engine` |
| Ruta base | `/reyma/vectorspace/engine` |
| Nombre del API | `rym.vectorspace.engine.api` |
| Prefijo de permisos | `VECTORSPACE.ENGINE` (p. ej. `VECTORSPACE.ENGINE.API.COLECCIONES.CONSULTAR`) |

## Requisitos

- SDK de .NET 10.
- Acceso al feed `Gr-Api` (paquetes `Reyma.*`), configurado en
  `NuGet.Config`. La primera restauracion local pide iniciar sesion:

  ```bash
  dotnet restore --interactive
  ```

- Para ejecutar en contenedor: Docker con BuildKit y el token del feed (ver
  "Imagen de contenedor").

## Ejecutar en local

```bash
docker compose -f docker-compose.motor.yml up -d   # Qdrant 1.19.2 (127.0.0.1:16334) y TEI (127.0.0.1:18080)
dotnet build
dotnet run --project src/Reyma.VectorSpace.Engine.Microservice.Api
```

`docker-compose.motor.yml` levanta las dependencias del motor (ver su cabecera: el modelo lo descarga el
host a `.cache/hf`); `appsettings.Development.json` ya apunta a esos puertos. `rag-qdrant` de
Rag.Context.Engine (6333/6334) no se toca. Rollback: `docker compose -f docker-compose.motor.yml down -v`.

`/health/self` responde 200 sin dependencias. `/health/ready` comprueba Qdrant y el sidecar (5 s cada uno)
y responde 503 `Unhealthy` si alguno no contesta.

Pruebas del motor real (opcionales): con el compose levantado, `$env:VSE_MOTOR_LOCAL = "1"; dotnet test`
corre tambien `MotorIntegracionTests`; sin la variable se omiten.

## Configuración por entorno

Los archivos versionados (`appsettings.json`, `appsettings.Development.json`,
`appsettings.Production.json`, `api.yaml`, `azure-pipelines.yml`) traen la
estructura (claves) con valores vacios, placeholders o no sensibles (en
Development, la cadena de conexion del dev general; ver abajo). Los valores
reales de infraestructura se entregan por `.env` local (item 1.5, ver
`.env.example` en la raiz) o user-secrets en desarrollo, o por el grupo de
variables del pipeline/Secret del entorno destino fuera de Development; el
gestor de secretos de ese entorno real todavia no esta decidido (no hay
Doppler ni ningun otro implementado hoy). Nunca como opciones adicionales de
`dotnet new`.

### Obligatoria para arrancar

| Variable | Notas |
|---|---|
| `ASPNETCORE_ENVIRONMENT` | La imagen ya trae `Production`. |
| `Motor__Embeddings__UrlBase` | URL del sidecar de embeddings (TEI). Sin ella el servicio no arranca. |

No usa PostgreSQL (ver nota de arquitectura arriba) y por eso no exige
`CONNECTION_STRING` en ningun entorno.

### Motor (seccion `Motor`)

| Variable | Defecto | Notas |
|---|---|---|
| `Motor__Qdrant__Host` / `Motor__Qdrant__PuertoGrpc` | `localhost` / `6334` | Qdrant por gRPC. |
| `Motor__Qdrant__Https` / `Motor__Qdrant__ApiKey` | `false` / vacia | La clave solo por variable de entorno o gestor de secretos. |
| `Motor__Embeddings__Modelo` / `Motor__Embeddings__Dimension` | arctic-embed-m-v2.0 / `256` | Decision de 1.6; cambiarlos exige reingestar. |
| `Motor__Ingesta__MaxTokensPorChunk` | `1024` | Decision de 1.11. |
| `Motor__Busqueda__Modo` | `Denso` | `FusionNativa` reactiva la RRF denso+sparse de Rag.Context.Engine (1.10). |

Valores incoherentes (URL invalida, dimension o lotes no positivos) impiden arrancar.

### Para operar o desplegar

| Variable | Notas |
|---|---|
| `IdentityServerAuthenticationOptions__Authority` | Autoridad OAuth/OIDC. Vacia, Swagger omite la definicion OAuth. |
| `PermissionsValidatorOptions__ApiUri` | Servicio de permisos. |
| `IntegrityServiceOptions__ApiUri` | Servicio de integridad. |
| `ASPNETCORE_BASEPATH` | La imagen ya trae el valor generado para este servicio. |
| `AllowedOrigins` | CORS; vacio deshabilita el origen cruzado sin romper el arranque. |
| `Serilog__WriteTo__1__Name` / `Serilog__WriteTo__1__Args__serverUrl` | Activan el sumidero Seq (ponerlos ambos a `Seq` y a la URL del servidor). |
| Variables del grupo del pipeline y de `api.yaml` | Ver "Pipeline y despliegue". |

El secreto OAuth de Swagger no esta versionado (`SwaggerConstants` trae un
marcador). Para iniciar sesion desde Swagger en local, darlo por variable de
entorno `SwaggerOptions__ClientSecret` en la terminal que lanza el servicio.

Los endpoints de ejemplo de la plantilla se retiraron; queda `GET sesion`
(datos del login desde los claims, igual que el antiguo `ejemplo/usuario`).
Los endpoints de negocio se describen en [contrato-api.md](contrato-api.md).

## Imagen de contenedor

El gestor de secretos debe inyectar `FEED_ACCESSTOKEN` en el entorno del
proceso de build; no escribir su valor en comandos, archivos ni argumentos
de Docker. BuildKit lo entrega solo durante `restore`, con las caches de
credenciales desactivadas. No se guarda como `ARG` ni `ENV` de la imagen.

```bash
docker build \
  --build-arg FEED_URL=<url-del-feed> \
  --secret id=feed-token,env=FEED_ACCESSTOKEN \
  -t reyma-vectorspace-engine:local .

docker run -p 8080:8080 \
  reyma-vectorspace-engine:local
```

La imagen expone el puerto `8080` (`ASPNETCORE_HTTP_PORTS`), corre con un
usuario no root (`USER app`), trae la zona horaria `America/Mexico_City`
y ya incluye `ASPNETCORE_ENVIRONMENT` y `ASPNETCORE_BASEPATH` generados;
`FEED_URL` es un argumento no secreto y `FEED_ACCESSTOKEN` se recibe solo
como secreto BuildKit. El frontend Dockerfile debe soportar secretos
montados como variables de entorno (1.10 o posterior).

## Pipeline y despliegue

`azure-pipelines.yml` compila, prueba, construye y publica la imagen, y
aplica el manifiesto, con dos ambientes:

| Ambiente | Rama | Imagen | Manifiesto | Grupo de variables | Ruta base |
|---|---|---|---|---|---|
| dev | `develop` | `Dockerfile.develop` (Debug, `Development`) | `api.dev.yaml` | `reyma-vectorspace-engine-dev` | `/reyma/vectorspace/engine/dev` |
| prod | `main` | `Dockerfile` (Release, `Production`) | `api.yaml` | `reyma-vectorspace-engine` | `/reyma/vectorspace/engine` |

El ambiente que se configura de inicio es **dev**: crear el grupo
`reyma-vectorspace-engine-dev`, el Secret `reyma-vectorspace-engine-dev-env` y las conexiones de
servicio. **Produccion ya viene completa** (pipeline, `Dockerfile`, `api.yaml`) y
no requiere editar ni regenerar nada: solo se configura (grupo `reyma-vectorspace-engine`,
Secret `reyma-vectorspace-engine-env` y conexiones de prod) cuando se haga el primer
despliegue desde `main`.

Los identificadores del servicio ya estan sustituidos;
los valores de infraestructura llegan como marcadores de despliegue que
resuelve el pipeline desde el grupo de variables del ambiente o desde
conexiones de servicio, nunca como texto en el YAML:

| Marcador | Que es |
|---|---|
| `${K8S_NAMESPACE}` | Namespace de destino en el cluster. |
| `${IMAGE_REGISTRY}` | Registry de imagenes. |
| `${IMAGE_TAG}` | Etiqueta de la imagen (por defecto, el numero de build). |
| `${INGRESS_HOST}` | Host publico del Ingress. |
| `${ASPNETCORE_ENVIRONMENT}` | Entorno del despliegue (solo `api.yaml`; dev usa `Development` fijo). |

El resto de la configuracion por entorno llega al pod por `envFrom` de un
Secret `reyma-vectorspace-engine-env` que crea quien opere ese entorno (el
gestor de secretos concreto todavia no esta decidido); `api.yaml` solo lo
nombra, no lo crea. El pipeline toma el
token del feed de `System.AccessToken` mediante el entorno de la tarea
de build, nunca por interpolacion en sus argumentos, y las conexiones
de registry/cluster de variables del grupo, sin valores reales en el
repositorio. Ni `api.yaml` ni el pipeline quedan listos para aplicarse a
un cluster real sin crear antes ese grupo de variables y ese Secret.
Build y push son tareas separadas: `Docker@2` no admite `arguments` con
`buildAndPush`. El token de build necesita permiso de lectura sobre el feed.

Las sondas de Kubernetes usan `/health/self` (liveness) y `/health/ready`
(readiness), sin la ruta base del servicio.
