# Runbook de despliegue - Reyma.VectorSpace.Engine.Microservice

Item de ledger `1.5-preparar-acceso-ssh-y-despliegue-offline`. Este documento prepara el
despliegue (imagen, configuracion, orden de arranque, healthchecks) para cuando exista acceso
SSH y aprobacion de TI al servidor destino. **Nada de lo que describe este runbook se ha
ejecutado contra un servidor real.** Todo lo verificado aqui corrio en Docker local, en esta
maquina de desarrollo, contra `docker-compose.motor.yml` (Qdrant + TEI) y el overlay
`docker-compose.api.yml` (item 1.5). Ver "Servidor destino" en el `README.md` de la raiz: el
acceso SSH esta anunciado, no otorgado.

No inventa hostnames, puertos de produccion ni credenciales: donde faltan, este documento usa
placeholders explicitos (`<...>`) y variables sin valor.

## 1. Cuando exista acceso y aprobacion: orden de arranque

1. **Confirmar el alcance aprobado**: namespace/host destino, imagen y tag a desplegar, y quien
   autorizo el cambio. No reutilizar una aprobacion de otro entorno o proyecto.
2. **Qdrant** (vector store). Debe estar arriba y respondiendo antes que la API; la API no
   arranca con `Motor:Qdrant` incoherente, pero si Qdrant no responde, `/health/ready` queda
   `Unhealthy` indefinidamente (no bloquea el arranque del proceso, si el liveness).
3. **Sidecar de embeddings (TEI)**. Igual que Qdrant: sin `Motor:Embeddings:UrlBase` valido el
   servicio **no arranca** (falla `ValidateOnStart`, ver `MotorServiceCollectionExtensions.cs`).
   Si arranca pero TEI no responde, `/health/ready` lo reporta `Unhealthy`.
4. **API** (`Reyma.VectorSpace.Engine.Microservice.Api`), una vez Qdrant y TEI esten listos.
5. **Servicios externos compartidos** (IdentityServer, PermissionsValidator, IntegrityService):
   no los levanta este servicio ni este item; deben existir ya en el entorno destino. Sin
   `IdentityServerAuthenticationOptions:Authority` configurado, Swagger omite el login OAuth
   pero los endpoints protegidos seguiran exigiendo JWT igualmente.

## 2. Construir la imagen

**Credenciales del feed Gr-Api: usuario/contrasena, no PAT.** La empresa no usa PAT para esto
(vencen al ano y hay demasiados proyectos para rotarlos a tiempo). `NuGet.Config` declara
`packageSourceCredentials` para `Gr-Api` con los placeholders `%FEED_USERNAME%` /
`%FEED_PASSWORD%` (NuGet los expande desde variables de entorno en cualquier plataforma; no
hace falta el credential provider de Azure Artifacts). Esas dos variables, y
`CORP_CA_CERT_PATH` si aplica, nunca van en un archivo versionado: se entregan como
variables de entorno de usuario persistentes (`[Environment]::SetEnvironmentVariable("FEED_USERNAME",
"...", "User")` o `setx`), una terminal **nueva** las hereda automaticamente. `.env.example`
en la raiz documenta los nombres esperados (sin valores reales); copialo a `.env`
(`.gitignore`) solo si prefieres ese mecanismo a variables de usuario.

```bash
docker build \
  --secret id=feed-username,env=FEED_USERNAME \
  --secret id=feed-password,env=FEED_PASSWORD \
  --secret id=corp-ca-cert,src=<ruta-local-a-tu-cert>  # opcional, ver abajo \
  -t reyma-vectorspace-engine:<tag> .
```

**Certificado CA corporativo (opcional pero necesario en este entorno): Netskope.** La
inspeccion TLS de Netskope rompe conexiones salientes a `pkgs.dev.azure.com` y `aka.ms` desde
dentro de un contenedor Linux (el host Windows ya confia en el cert de Netskope; el
contenedor no hereda ese almacen). Export root + intermedio desde los almacenes de
certificados de Windows (son publicos por diseno, no secretos):

```powershell
Get-ChildItem Cert:\LocalMachine\Root, Cert:\LocalMachine\CA |
  Where-Object { $_.Subject -match "Netskope" -or $_.Issuer -match "Netskope" }
# exportar cada uno (Base-64 X.509) y concatenarlos en un solo .pem
```

El `Dockerfile` monta `corp-ca-cert` como secreto de BuildKit y solo instala el certificado si
el archivo montado no esta vacio; sin `--secret id=corp-ca-cert` el build se comporta igual
que antes (red sin inspeccion TLS). El certificado real nunca se commitea:
`certs/corp-ca-placeholder.crt` (vacio, versionado) es el valor por defecto en
`docker-compose.api.yml` via `CORP_CA_CERT_PATH`; exporta esa variable con la ruta local a tu
`.pem` para usarlo. Ver `certs/README.md`.

**Limite de plataforma confirmado (2026-10-22), pendiente de decision aparte:** con el
certificado de Netskope resuelto, el build llega limpio hasta el `dotnet restore`, pero
Azure DevOps Services (`dev.azure.com`, la nube) responde `401 Unauthorized` a usuario/
contrasena de dominio por Basic Auth contra el feed `Gr-Api` -- esa plataforma solo acepta un
PAT (como "password", usuario arbitrario) o un token OAuth/Entra ID; una contrasena de AD
literal no es un metodo soportado ahi, sea cual sea el `Dockerfile` o `NuGet.Config`. La
Service Connection `Gr-Maui` del pipeline (`azure-pipelines.yml`) resuelve esto en CI de otra
forma (token de la propia organizacion o PAT detras de la Service Connection, no verificable
desde aqui). Decidir como autenticar builds Docker locales/fuera del pipeline contra feeds de
Azure DevOps Services es una decision de infraestructura que afecta a todos los proyectos de
la empresa, no solo a este servicio: queda fuera del alcance de este item. Mientras no se
resuelva, el build de imagen completo solo es reproducible dentro del pipeline real.

Usar `Dockerfile` (Release/Production) para el ambiente `prod` y `Dockerfile.develop`
(Debug/Development, ruta base `/reyma/vectorspace/engine/dev`) para `dev`, segun
`azure-pipelines.yml` (ver `docs/servicio/README.md`, "Pipeline y despliegue").

`.dockerignore` excluye `.cache/` (modelos del sidecar descargados por el host, varios GB),
`docs/`, `tests/`, `tools/` y los YAML de pipeline/manifiesto: nada de eso hace falta para
compilar ni correr la API, y antes de este item `.cache/` no estaba excluido (hallazgo de 1.4
corregido aqui).

## 3. Variables de entorno

La tabla completa, con las ya resueltas en el repo versionado (estructura sin secretos) y las
que exige cada ambiente, vive en
[`docs/servicio/README.md`](../servicio/README.md#configuraci%C3%B3n-por-entorno). Resumen para
este runbook:

| Variable | Origen cuando exista el servidor | Obligatoria |
|---|---|---|
| `Motor__Embeddings__UrlBase` | URL del sidecar TEI en el entorno destino. **Placeholder**: `<url-sidecar-tei-destino>` | Si, el proceso no arranca sin ella. |
| `Motor__Qdrant__Host` / `Motor__Qdrant__PuertoGrpc` | Host/puerto gRPC del Qdrant destino. **Placeholder**: `<host-qdrant-destino>` / `<puerto-grpc-qdrant-destino>` | Si (hay defecto `localhost:6334`, inválido fuera de Docker local). |
| `Motor__Qdrant__ApiKey` | Si el Qdrant destino la exige. Por variable de entorno o gestor de secretos, nunca en `appsettings`. | Solo si el Qdrant destino la requiere. |
| `Motor__Embeddings__ApiKey` | Item 1.13: clave Bearer del servidor de IA corporativo AltheIA, **solo** si `UrlBase` apunta a AltheIA en vez del TEI local. Por variable de entorno o gestor de secretos, nunca en `appsettings`. Verificada contra AltheIA real (ver `docs/constitucion/INFRASTRUCTURE_LAYER.md`). | Solo si se usa el perfil AltheIA. |
| `Motor__Embeddings__RutaCertificadoCA` | Item 1.13: ruta local al certificado CA corporativo (p. ej. Netskope) si AltheIA queda detras de un proxy TLS, igual patron que `CORP_CA_CERT_PATH` de la seccion 2. Si se define y el archivo no existe, el servicio no arranca. | Solo si AltheIA esta detras de un CA corporativo. |
| `IdentityServerAuthenticationOptions__Authority` | Autoridad OAuth/OIDC del entorno destino (ya existe para otros servicios Reyma; no se inventa aqui). | Si, para validar JWT. |
| `PermissionsValidatorOptions__ApiUri` / `IntegrityServiceOptions__ApiUri` | Servicios compartidos del entorno destino. | Si. |
| `Motor__Git__OrigenesPermitidos` | Item 1.7.6: lista EXACTA de organizaciones/proyectos de Azure DevOps permitidos para el origen `proyecto` por URL Git (esquema+host+puerto+prefijo de ruta). Vacia por defecto: el origen queda deshabilitado de facto (toda URL se rechaza) hasta que se configure. **No se define aqui**: la decide quien opere el servicio para ese entorno. | Solo si se habilita el origen `proyecto` por URL Git. |
| `VSE_GIT_PAT` (nombre configurable por `Motor__Git__NombreVariableIdentidad`) | Item 1.7.6: PAT de una cuenta de servicio (no personal) de Azure DevOps para el clonado. Por variable de entorno o gestor de secretos, nunca en `appsettings`. Sin ella, el clonado deja la autenticacion al `credential.helper` del host, valido solo en la verificacion puntual de este item (ver `docs/evaluaciones/1.7.3/decision.md` y el ledger, item 1.7.6), nunca en el contenedor de produccion. | Si, antes de habilitar el origen Git en produccion. |
| `Motor__Git__MaxBytesCheckout` / `Motor__Git__PlazoSegundos` / `Motor__Git__DirectorioTemporal` | Limites del clonado superficial endurecido (`ClonadorGit`). `DirectorioTemporal` deberia apuntar a un volumen con cuota real de filesystem en el entorno destino (1.7.6 lo deja como limite logico de proceso, no cuota de filesystem: ver `docs/servicio/contrato-api.md`, seccion "Proyecto completo desde URL Git"). | Recomendado antes de habilitar el origen Git en produccion. |
| `Despliegue__Ssh__Host` / `__Puerto` / `__Usuario` / `__RutaClavePrivada` | Datos del servidor destino para quien ejecute el despliegue por SSH. Placeholders ya reservados en `appsettings.Production.json` (seccion `Despliegue:Ssh`, ver nota `_nota` en ese archivo); **no los lee la aplicacion en tiempo de ejecucion**, son solo el lugar acordado donde completar esos datos cuando exista acceso aprobado. | No aplica al proceso; informativa para el despliegue. |
| `ASPNETCORE_BASEPATH` | Ya resuelta en la imagen (`/reyma/vectorspace/engine` o `.../dev`). | Ya resuelta. |
| Registry, namespace, ingress (`${IMAGE_REGISTRY}`, `${K8S_NAMESPACE}`, `${INGRESS_HOST}`) | Marcadores de `api.yaml`/`api.dev.yaml`, resueltos por el pipeline desde el grupo de variables del ambiente. **No hay valores todavia**: pendientes del servidor real. | Segun manifiesto. |

Ningun hostname, puerto ni credencial de un servidor real aparece en este documento ni en el
repo: donde el valor depende del servidor destino, queda como placeholder entre `<...>` hasta
que exista acceso aprobado.

## 4. Healthchecks a verificar

| Endpoint | Que comprueba | Respuesta esperada |
|---|---|---|
| `GET /health/self` | El proceso esta vivo (liveness). No depende de Qdrant ni TEI. | `200 Healthy` |
| `GET /health/ready` | Qdrant y el sidecar de embeddings responden (timeout 5 s cada uno). | `200 Healthy`; `503` si alguno no contesta. |

Las sondas de Kubernetes (`api.yaml`) usan estas mismas rutas, sin la ruta base del servicio
(ver `docs/servicio/README.md`).

## 5. Verificacion local ejecutada para este item (2026-10-21/22)

Todo lo siguiente corrio en Docker Desktop local, sin tocar ningun servidor remoto:

1. **Motor local arriba** (ya levantado desde el item 1.4, reusado aqui):
   `docker compose -f docker-compose.motor.yml ps` mostro `vse-motor-qdrant-1` y
   `vse-motor-tei-1` en estado `Up` (Qdrant en `127.0.0.1:16333/16334`, TEI en
   `127.0.0.1:18080`), estable durante toda la verificacion (22+ h de uptime).

2. **Ingesta + busqueda real contra una coleccion de prueba aislada**: se corrieron las pruebas
   de integracion del motor contra Qdrant y TEI reales (`VSE_MOTOR_LOCAL=1 dotnet test --filter
   FullyQualifiedName~MotorIntegracionTests`), dos veces (2026-10-21 y, tras los cambios de este
   item, 2026-10-22 con NuGet.Config/Dockerfile ya modificados). Cada prueba genera su propia
   coleccion con un nombre aleatorio (`NuevaColeccion()` -> `it-<guid>`), nunca una coleccion
   productiva. Resultado ambas veces: **4/4 pruebas en verde** (11.9 s y 7 s), incluida
   `BusquedaYEmbeddingsUsanLosMismosVectoresQueLaIngesta` (ingesta real + busqueda real) y
   `LaIngestaIncrementalPorDocumentoDejaLosMismosPuntosQueUnaDesdeCero`.

3. **Healthchecks de la API en verde**: `dotnet run --project
   src/Reyma.VectorSpace.Engine.Microservice.Api` (Development) apuntando al motor local, dos
   veces (mismas fechas que el punto 2). `GET /health/self` -> `200 Healthy`.
   `GET /health/ready` -> `200 Healthy` (Qdrant y TEI reales, no mocks).

4. **Overlay de compose (`docker-compose.api.yml`) validado estructuralmente**:
   `docker compose -f docker-compose.motor.yml -f docker-compose.api.yml config --services`
   lista `qdrant`, `tei`, `api`: el merge de los dos archivos es valido y comparten red/nombres
   de servicio.

5. **Build de imagen Docker: diagnostico completo, resuelto hasta el limite de plataforma.**
   Tres causas encontradas y resueltas en cadena:
   - TLS de Netskope (`UntrustedRoot` / "self-signed certificate in certificate chain" contra
     `aka.ms` y `pkgs.dev.azure.com`, reproducido tambien con una imagen `curlimages/curl`
     limpia, sin cert ni token -- descartando NuGet.Config o credenciales como causa). Resuelto
     exportando root + intermedio de Netskope desde `Cert:\LocalMachine\Root`/`\CA` e
     instalandolos en el build via el secreto `corp-ca-cert` (ver seccion 2).
   - Credencial correcta: el feed usa usuario/contrasena (no PAT, decision de la empresa), asi
     que se quito el credential provider de Azure Artifacts (ya no hace falta) y se paso a
     `packageSourceCredentials` de NuGet.Config con placeholders `%FEED_USERNAME%`/
     `%FEED_PASSWORD%`, resueltos via secretos de BuildKit `feed-username`/`feed-password`.
   - **Limite real, no resuelto**: con TLS y el mecanismo de credenciales correctos, Azure
     DevOps Services responde `401 Unauthorized` a usuario/contrasena de dominio por Basic
     Auth -- esa plataforma exige PAT o token OAuth/Entra ID para sus feeds, no una contrasena
     de AD literal. Confirmado reproduciendo el mismo 401 con TLS ya resuelto. No es un bug de
     este repo: es como funciona Azure DevOps Services. Build completo de imagen Docker queda
     pendiente de una decision de infraestructura de la empresa (ver seccion 2) que excede el
     alcance de este item.
   - Hallazgo de proceso (no tecnico): un intento de build fallo porque se corrio en la misma
     consola donde se acababan de definir las variables de entorno persistentes --
     `SetEnvironmentVariable(..., "User")`/`setx` solo aplican a procesos *nuevos*; una consola
     ya abierta no las hereda hasta reabrirse.
   - Hallazgo de seguridad durante la depuracion: en un intento se escribio usuario/contrasena
     en texto plano directo en `NuGet.Config` (archivo versionado) para probar; se revirtio de
     inmediato a los placeholders `%FEED_USERNAME%`/`%FEED_PASSWORD%` antes de cualquier commit,
     y se recomendo rotar esa contrasena por haber quedado expuesta en la conversacion de la
     sesion. `credential-read-guard` bloqueo ademas un intento de exportar esas variables por
     shell (incluso por nombre, no solo por valor) -- se respeto el bloqueo sin reformular el
     comando, tal como indica su propio mensaje.

Dado el limite de plataforma (no de este repo), la verificacion real de que el servicio
funciona end-to-end se apoya en los puntos 1-4 (equivalente a correrlo desde Visual Studio:
mismo codigo, mismo restore de NuGet que ya funciona en el host, mismas dependencias Qdrant/TEI
reales), no en el build de imagen. Quien resuelva la autenticacion de Azure DevOps Services
para Docker (PAT con rotacion automatizada, token OAuth, u otro mecanismo que decida la
empresa) puede repetir `docker compose -f docker-compose.motor.yml -f docker-compose.api.yml
up -d --build` con esas credenciales y confirmar los mismos healthchecks en
`http://localhost:18081/health/self` y `/health/ready`.

## 6. Que falta para el servidor real (fuera de alcance de este item)

- Acceso SSH otorgado (hoy solo "anunciado", segun el `README.md` de la raiz) y aprobacion
  explicita de TI para conectar.
- Hostnames, puertos y credenciales reales de Qdrant, TEI, IdentityServer, PermissionsValidator
  e IntegrityService en ese entorno.
- El grupo de variables (`reyma-vectorspace-engine[-dev]`) y el Secret
  `reyma-vectorspace-engine[-dev]-env` en el pipeline, mas las conexiones de servicio de
  registry/cluster (ver `docs/servicio/README.md`, "Pipeline y despliegue").
- Decision de infraestructura (fuera de este servicio, afecta a todos los proyectos): como
  autenticar builds Docker locales/fuera del pipeline contra feeds NuGet de Azure DevOps
  Services, dado que Basic Auth con usuario/contrasena de dominio no es un metodo soportado por
  esa plataforma (ver seccion 2 y 5). Hasta entonces, el build de imagen completo solo es
  reproducible dentro de `azure-pipelines.yml`.

Ninguno de estos puntos se resuelve inventando un valor: quedan pendientes hasta que exista el
acceso y la aprobacion correspondiente.
