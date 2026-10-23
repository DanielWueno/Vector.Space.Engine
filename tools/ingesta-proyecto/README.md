# Ingesta de proyecto completo (1.7.3)

Banco de medición para decidir los orígenes nuevos de ingesta (URL de un repo Git y `.zip` subido) y la selección de
modelo. **No es producto**: nada de esto está expuesto en el servicio. Decisión y lectura de los números:
[`docs/evaluaciones/1.7.3/decision.md`](../../docs/evaluaciones/1.7.3/decision.md).

## Prototipo (`Prototipo/`, solo BCL)

Los cuatro componentes que el servicio necesitaría, aislados para medirlos. Sin paquetes, así que también corre en
Linux (`mcr.microsoft.com/dotnet/sdk:10.0`, `--network none`), que es donde se despliega el servicio.

| Archivo | Qué es |
|---|---|
| `ListaBlancaGit.cs` | Validación de la URL antes de tocar la red (esquema, host y puerto exactos, prefijo de ruta, sin credenciales en la URL ni escapes de punto/barra). |
| `ClonadorGit.cs` | `git clone` superficial y endurecido: solo https, sin redirecciones ni hooks ni symlinks, aislado de la configuración y las credenciales de quien corre el proceso, identidad del servicio por entorno, tope de tamaño y de plazo. |
| `LectorZip.cs` | `.zip` → documentos **en memoria** (no extrae a disco): nombres validados, solo entradas que el perfil admite, bytes reales con tope por entrada y total, ratio, número de entradas, symlinks. |
| `EscanerDeCheckout.cs` + `IgnoreRules.cs` | `FileSystemIngestionScanner` de Rag.Context.Engine que no sigue symlinks ni junctions; `IgnoreRules` copiado con la normalización corregida (ver decisión). |

```powershell
dotnet build tools/ingesta-proyecto/Prototipo -c Release
$p = "tools/ingesta-proyecto/Prototipo/bin/Release/net10.0/IngestaProyecto.Prototipo.dll"
$t = ".cache/ingesta-proyecto-1.7.3"
git archive --format=zip --prefix=Vector.Space.Engine/ -o "$t/valido-vse.zip" HEAD
dotnet $p urls                      # lista blanca, sin red
dotnet $p zip "$t/zip-windows" "$t/valido-vse.zip"
dotnet $p enlaces "$t/enlaces-windows"
dotnet $p ragignore "$t/ragignore-windows"
# Lo mismo en Linux, sin red:
docker run --rm --network none -v "${PWD}/tools/ingesta-proyecto/Prototipo:/fuente:ro" -v "${PWD}/${t}:/trabajo" `
  mcr.microsoft.com/dotnet/sdk:10.0 bash -c "mkdir -p /tmp/p && cp /fuente/*.cs /fuente/*.csproj /tmp/p/ && cd /tmp/p && dotnet build -c Release -o /tmp/out && dotnet /tmp/out/IngestaProyecto.Prototipo.dll zip /tmp/zip /trabajo/valido-vse.zip > /trabajo/zip-linux.json"
```

## Banco (`Banco/`)

Usa el prototipo contra cosas reales:

- **Servidor Git de prueba** (`ServidorGitDePrueba.cs`): `git-http-backend` (el CGI del propio git, protocolo smart HTTP)
  sobre loopback, servido por Kestrel, con copias bare de este repo y de Rag.Context.Engine. Hace de host de la lista
  blanca sin credenciales de Azure DevOps. También tiene rutas `/privado/` (identidad del servicio), `/redirige/` (302
  a un segundo servidor "interno" que cuenta las peticiones que recibe) y `/lento/`.
- La **API del servicio** en Kestrel real con identidad de prueba y el motor real contra `docker-compose.motor.yml`
  (Qdrant 16333/16334, TEI 18080).
- El **escáner real** de Rag.Context.Engine (checkout hermano, solo lectura) para compararlo con el portado.
- El **cliente HTTP de 1.13** (`motor-embeddings`, sacado del contenedor DI del servicio) contra AltheIA, para el
  resumen y la selección de modelo. `ALTHEIA_KEY` / `ALTHEIA_CERT` solo por nombre; nunca se imprimen.

Preparar las copias bare (una vez):

```powershell
git clone --bare D:\Git\Vector.Space.Engine .cache/ingesta-proyecto-1.7.3/git-raiz/vse.git
git clone --bare D:\Git\Rag.Context.Engine  .cache/ingesta-proyecto-1.7.3/git-raiz/rce.git
docker compose -f docker-compose.motor.yml up -d
dotnet build tools/ingesta-proyecto/Banco -c Release
$b = "tools/ingesta-proyecto/Banco/bin/Release/net10.0/IngestaProyecto.Banco.dll"
```

| Comando | Qué mide |
|---|---|
| `git` | Clonado permitido (este repo y Rag.Context.Engine), historial completo frente a superficial, rechazos por lista blanca, redirección (endurecido frente a git por defecto), repo privado sin y con identidad del servicio, `credential.helper` con y sin aislamiento, topes de tamaño y de plazo. Deja `clones/vse` y `clones/rce`. |
| `escaner` | Escáner real vs portado sobre los clones (rutas y orden), checkout vs árbol de trabajo, junction, zip del mismo commit vs clone, documentos por lenguaje, reglas de `IgnoreRules` que empiezan por punto, `--lang` del CLI. |
| `ingesta <vse\|rce>` | URL Git → clone → escáner portado → `POST ingestas` en lotes de 100 (TEI local); segunda pasada; después el `.zip` del mismo commit a la misma colección. |
| `resumen` | 30 chunks de Rag.Context.Engine resumidos con el prompt del origen por `qwen3.8-27b-fast`, `-deep` y `-deep` sin razonamiento; tercer vector (`dense-resumen`) en una colección nueva; agregarlo a una existente. Guarda la muestra en `resumen-muestra.json`. |
| `modelos` | `GET /v1/models`, modelo inexistente, modelo de otro tipo, metadata de colección en Qdrant. |
| `mezcla 1..4` | La misma colección con arctic@256 y luego bge-m3 truncado a 256 (entra), bge-m3@1024 (409) y una búsqueda con arctic. Un paso por proceso. |
| `chunks <vse\|rce>` | Chunks indexables que produce el checkout, por lenguaje, sin vectorizar. |
| `sonda` | Dos instancias seguidas del servicio en el mismo proceso: reproduce el 503 de la limitación de abajo. |
| `limpiar` | Borra las colecciones `it-1-7-3-*` del registro, y solo esas. |

Limitación del banco: una segunda `WebApplicationFactory` en el mismo proceso, aunque la primera ya se haya
desechado, responde `503 - Service unavailable.` a todo. Por eso `mezcla` corre un paso por proceso. No se
investigó más: no afecta al servicio, que es una sola instancia por proceso.

Salida en `.cache/ingesta-proyecto-1.7.3/` (ignorado por git); la evidencia de 1.7.3 está copiada en
`docs/evaluaciones/1.7.3/resultados/`.
