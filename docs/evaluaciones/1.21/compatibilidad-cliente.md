# 1.21.2 - Compatibilidad de filtros del futuro cliente

Fecha: 2026-11-02. **Bloqueada: falta respuesta del responsable; preparacion parcial.**
Base VectorSpace: `9d4c2f1736c90918a949e1fedfbdd63db2ffdf78`.
Base Rag.Context.Engine (solo lectura): `aae1de8d2afde69bfab88248cdd31768bded2443`.
`V\` y `R\` identifican esos repositorios; las referencias de codigo son de esas bases.

No se ejecuta 1.12 ni se reabren 1.14/1.21.1. No se modifican contratos productivos,
indices, datos o permisos. La declaracion del responsable en 1.21 ("no uso module",
"esta bien que se quede por lo mismo pruebas") solo decide conservar modulo opcional;
no demuestra nada sobre uso de otros filtros o de autorizacion.

## Evidencia real de 1.21.1

El commit `9d4c2f1` contiene codigo, pruebas, contrato, herramienta y el resultado del
ledger con estado `hecho`; no es solo la propuesta de `decision.md`. Esta ultima
describe el momento anterior a la implementacion y se conserva como historico.

| Fuente | Comprobacion por lectura en esta ficha |
|---|---|
| `V\src\Reyma.VectorSpace.Engine.Microservice.Business\Models\Requests\ColeccionesRequests.cs:18-58`, `Queries\Colecciones\BuscarEnColeccionQuery.cs:19-79` | `modulos` opcional se propaga al puerto; convertidor de cadenas estricto y validacion 1..50 no blancas. Ausente/null no filtra; lista vacia o elemento blanco/invalido produce 400, sin normalizar valores no blancos. |
| `V\src\Reyma.VectorSpace.Engine.Microservice.Infrastructure\VectorStore\ModulePrefixes.cs:33-50`, `QdrantVectorStore.cs:475-495` | Se derivan/escriben prefijos ordinales por `.` del namespace y `/` de la ruta; union keyword y version 1 en cada upsert. No se reutiliza full-text. |
| `V\src\Reyma.VectorSpace.Engine.Microservice.Infrastructure\VectorStore\QdrantSemanticRetriever.cs:79-173` | Una condicion Must por modulo, AND con ANY de simbolos; mismo filtro en denso, sparse de FusionNativa y resumen, antes del corte. No hay logica de identidad. |
| `V\src\Reyma.VectorSpace.Engine.Microservice.Infrastructure\VectorStore\QdrantVectorStore.cs:184-205`, `ModulePrefixes.cs:107-131` | Exige marca completa, indices keyword/integer y conteo exacto sin puntos pendientes; 409 antes de vectorizar si falta preparacion. Coleccion inexistente: 200 vacio; sin modulos no exige relleno. |
| `V\src\Reyma.VectorSpace.Engine.Microservice.Infrastructure\VectorStore\RellenoDePrefijosDeModulo.cs:50-192`, `tools\relleno-modulo\README.md` | SetPayload paginado, comparacion de ID/version/namespace/ruta, reanudacion e idempotencia; no embeddings/LLM. Lease best-effort, no CAS de metadata; exige detener/actualizar escritores antiguos. La preparacion no es una barrera de seguridad contra escritores externos. |
| `V\tests\Reyma.VectorSpace.Engine.Microservice.Tests\FiltroDeModuloContratoTests.cs:26-144` | Casos HTTP validos/invalidos, no trim/lowercase y comparacion de prefijos con predicado independiente sobre namespace/ruta. |
| `V\tests\Reyma.VectorSpace.Engine.Microservice.Tests\Motor\FiltroDeModuloIntegracionTests.cs:270-740` | Oraculo con falsos candidatos de mayor score, intersecciones por el mismo/distinto campo, conjuncion imposible, simbolos, ramas, 409, preservacion del payload/vectores, reanudacion y escritor antiguo. |
| `V\tools\relleno-modulo\aceptacion-1-21-1.ps1:20-76` y `resultado` de 1.21.1 | El script exige Qdrant, TRX con todos pasados y 12 casos requeridos, mas smoke. El resultado historico registra 44/44, negativo sin Qdrant, mutacion de post-filtro y rollback con lector anterior. No se vuelve a ejecutar aqui: conectaria servicios. El script elimina su TRX temporal; no se encontro TRX persistido en este checkout. Los conteos son evidencia registrada por 1.21.1, no una corrida nueva de 1.21.2. |

Contrato revisado: `V\docs\servicio\contrato-api.md:190-299` (1.18, 1.20, 1.21.1).
1.18 aporta metadatos y ANY keyword de simbolos; 1.20 aporta espacio resumen y texto
opcional (historicos sin relleno pueden tener null). No aportan filtros de
lenguaje/namespace/tenant ni fusion de tres ramas, reranking o two-hop del consumidor.
Exponer datos en un hit no equivale a poder filtrarlos antes del corte.

Dos comentarios historicos del motor dicen "la coleccion ya es el tenant" y
"prefijo de texto sin fronteras" para el origen. No se toman como contrato vigente:
el origen SI tiene post-filtro de frontera para modulo, y despues de 1.14 el nombre
de una coleccion NO prueba tenencia ni permiso. No se corrigen comentarios de
producto en esta ficha documental.

## Matriz de compatibilidad

API = `/api/search`, `/api/ask` y `/api/ask/stream` de Rag. Su cuerpo
`RagQueryRequest` no expone los cuatro filtros; CLI = `search`; puerto =
`RetrievalOptions` con `Context` obligatorio. No inferir paridad de CLI/puerto a
partir de la menor superficie del cuerpo HTTP.

En las filas L/N/T se indica el comportamiento en **Local y Authorized**: los
filtros explicitos se aplican en ambos. Las filas C-L/C-A distinguen los contextos.
Las alternativas pendientes NO son decisiones consentidas ni autorizan ejecucion.

| Fila/capacidad | Entrada actual API / CLI / puerto | Semantica real de Rag | Dato/contrato disponible en VectorSpace | Tratamiento futuro concreto y brecha |
|---|---|---|---|---|
| L - lenguaje (Local/Authorized) | API: no en body ni resuelto por claims. CLI: `--language`/`-l`, `SourceLanguage?`. Puerto: `FilterByLanguage`. | Si HasValue: `Match.Text(language, enum.ToString())`, no keyword. Null no filtra. Enum compartido: CSharp, TypeScript, JavaScript, Xaml, Sql, Markdown, PlainText, Unknown. No hay una entrada de texto blanco en el puerto tipado. | Payload `language` y hit `lenguaje`; enum identico. Request/puerto de busqueda NO lo aceptan. | Pendiente: preservar Match.Text con version/esquema de referencia fijados; o cambiar con consentimiento a igualdad ordinal del enum; o diferir y rechazar todo valor activo antes de enviar HTTP. Preservar/cambiar exige ampliacion de filtro de servidor; diferir exige guardia del adaptador. Sin valor: omitir, nunca inventar lenguaje. |
| N - namespace (Local/Authorized) | API: no en body ni claims. CLI: `--namespace`/`-n`. Puerto: `FilterByNamespace`. | No blanco: `Match.Text(namespace, valor)` sin post-filtro de frontera. Null/vacio/blanco no filtra. Texto no blanco se pasa literal, sin trim/lowercase. El comentario "prefix" NO convierte el codigo en StartsWith. | Payload y hit `namespace` opcionales. No hay parametro de namespace. `module_prefixes` mezcla namespace y ruta, por tanto NO lo representa. | Pendiente: preservar full-text con version/esquema fijados; o consentir namespace exacto/descendiente por `.` SOLO en namespace, con mecanismo separado; o diferir rechazando valor activo. No traducir namespace a `modulos`: admitiria rutas ajenas al namespace y cambiaria pertenencia. |
| T-E - tenant explicito (Local/Authorized) | API: no en body. CLI: `--tenant`. Puerto: `FilterByTenant`. | No blanco: keyword exacto ordinal en payload `tenant`; ausente/blanco no restringe e incluye puntos sin tenant. Con valor, punto sin tenant NO coincide. AND con tenant del contexto Authorized, nunca reemplazo/union. | No hay campo de tenant en request de ingesta/documento, busqueda o hit; el escritor no lo guarda. Coleccion y `repository_name` no son sustitutos para una coleccion mixta. | Pendiente: preservar con fuente de asignacion por documento/punto e historicos, campo/indice y filtro AND; o diferir rechazando todo tenant explicito activo, aun en Local. No deducirlo del nombre/ruta, ni buscar sin tenant y post-filtrar un top-K. Reentrada de preservacion: mapa con procedencia y politica para puntos sin asignacion. |
| T-A - tenant autorizado | API: claim `tenant` de identidad autenticada unica; no texto del body. CLI search: no crea Authorized. Puerto: `Context.Tenant` si Mode=Authorized. | Keyword exacto adicional si no blanco. `FilterByTenant=A` AND `Context.Tenant=B` no puede ampliar resultados; con payload escalar A != B da vacio. Un administrador puede superar ACL de coleccion, pero sus filtros de contexto siguen aplicando. | Mismas carencias de T-E; JWT valido de VectorSpace no resuelve el tenant del actor de Rag. | Depende de T-E y C-A. Preservar exige transportar AMBAS restricciones; si se difiere, rechazar la solicitud antes de buscar, no suprimir el claim. Nunca prometer aislamiento en acceso directo a VectorSpace. |
| M-E/M-A - modulo explicito y autorizado | API: solo claim `module` autenticado para contexto, no filtro en body. CLI: `--module` con Local. Puerto: `FilterByModule` y `Context.Module` en Authorized. | Cada activo exige namespace exacto/descendiente `.` O ruta exacta/descendiente `/`, ordinal. AND entre ambos. Prefiltro Match.Text aproximado, comprobacion exacta tras corte y antes/despues de two-hop; puede devolver menos de K. | `modulos` 1..50, AND, keyword de prefijos y guardia de completitud de 1.21.1. Namespace/ruta ausentes no inventan coincidencia. | Preservacion del predicado YA decidida en 1.21: lista de los activos, no ANY. Sin ampliar el conjunto elegible; puede recuperar mas elegibles que el antiguo post-filtro, no garantiza los mismos top-K/scores. No requiere otra implementacion de servidor para modulo; SI traduccion y pruebas del adaptador. La admision de Authorized sigue pendiente en C-A. |
| C-L - Local explicito | API: `AuthorizationMode.Local` -> actor administrador local, sin consultar identidad. CLI search siempre construye `RetrievalContext.Local`. Puerto: Mode=Local obligatorio. | No activa `Context.Tenant/Module`, aunque alguien construya un Local con esos campos; SI aplica los filtros explicitos. Local no es fallback de un fallo de identidad. | VectorSpace siempre exige su autenticacion HTTP, aunque el contexto de Rag sea Local. No entiende Local/Authorized. | Conservar seleccion Local explicita, sin convertirla en promesa de acceso anonimo al servicio. Credencial de servicio se resuelve fuera de esta ficha. L/N/T-E deben preservarse o rechazarse segun decision, M-E se transmite. No hace falta modo Local en VectorSpace; hace falta guardia/composicion en adaptador. |
| C-A - Authorized / autorizacion | API: modo de configuracion `Empresarial`; primero identidad/ACL de coleccion, despues `ForAuthorized(actor.Tenant, actor.Module)`. Puerto recibe contexto YA resuelto; no autoriza por crear ese record. | Exactamente una identidad autenticada; mas de un tenant o modulo distinto invalida identidad (duplicados ordinalmente iguales no). ACL por scopes/tenants del manifiesto, administrador como excepcion. Manifiesto ausente/no publicado: solo administrador. Tenant/module ausentes/blancos no activan filtro de payload; no eximen de ACL. | No hay ACL/manifiesto de Rag ni decision por tenant/modulo: 1.14 retirada, cualquier autenticado alcanza cualquier coleccion. Metadata de perfil del modelo no es ACL. | Pendiente: preservar autorizacion en Rag con fuente ACL independiente de su Qdrant al pasar a cliente, incluyendo politica explicita de claims ausentes/blancos; o diferir TODO Authorized con rechazo explicito, aun sin tenant/module. Nunca degradar a Local. Nuevos permisos de VectorSpace requeririan reabrir 1.14 y quedan fuera de este trabajo. |

### Fuentes de la matriz y limites de evidencia

- `R\src\RagEngine.Core\Domain\RetrievalResult.cs:44-90`,
  `RetrievalContext.cs:8-33`, `SourceLanguage.cs:7-17`;
  `R\src\RagEngine.Cli\Commands\SearchCommand.cs:37-48,84-95`.
- `R\src\RagEngine.Core\Infrastructure\VectorStore\QdrantSemanticRetriever.cs:105-206,668-785`:
  filtros AND y MustNot del punto manifiesto, prefiltros de todas las ramas,
  comprobacion de modulo antes/despues del salto; el filtro original tambien se
  conserva en la expansion por simbolos (`:446-460` y metodo ExpandBySymbolAsync).
- `R\src\RagEngine.Api\Contracts.cs:26-33`, `Program.cs:369-409,443-460`:
  cuerpo, identidad, contexto y ACL; search/ask/stream usan esa misma autorizacion
  (`:714-722,816-824,943-952`).
- `R\src\RagEngine.Core\Infrastructure\Authorization\CollectionActorResolver.cs:10-23`,
  `CollectionAuthorizationService.cs:8-23`, `Domain\CollectionManifest.cs:22-62`.
- `R\tests\RagEngine.Core.Tests\RetrievalAuthorizationContextHttpHarnessTests.cs:164-367`:
  denso/sparse/resumen, dos tenants, AND de modulos, colisiones, two-hop y exclusiones
  antes del reranker. `CollectionAuthorizationServiceTests.cs:37-181`: scopes,
  tenants, administrador, Local, identidad ausente/no autenticada, manifiesto
  ausente y fallo de identidad sin fallback.
- `V\src\Reyma.VectorSpace.Engine.Microservice.Infrastructure\VectorStore\QdrantVectorStore.cs:475-515`,
  `QdrantSemanticRetriever.cs:88-105,176-194`; requests y contrato citados arriba.
  `V\docs\servicio\contrato-api.md:528-548` y `ColeccionesController.cs:12-26`:
  excepcion explicita vigente a las guias genericas de politicas, por decision 1.14.

Match.Text depende de version e indice/tokenizador. El hallazgo de 1.2 (historico,
`docs\evaluaciones\1.2\decision.md`, citado por 1.21) compara subcadena sensible a
mayusculas en Qdrant 1.14.1 frente a tokens insensibles en 1.19.2 sin indice full-text.
No es una medicion nueva ni prueba de la version efectiva usada por Rag hoy.
No se inspeccionaron configuraciones sensibles, procesos, identidades ni logs.
Los harness usan identidades sinteticas; no prueban login ni utilizacion real.
No se encontro cobertura especifica de lenguaje/namespace en los tests de filtros
revisados; el codigo permite describir su semantica, no certificar paridad remota.

## Traduccion futura de modulo: tabla literal

Definicion: activo = `!string.IsNullOrWhiteSpace(valor)`. Ningun valor activo se
recorta ni cambia de mayusculas. P(m,p) = frontera namespace por `.` O ruta por `/`.
El destino debe satisfacer todos los P de la lista; no seleccionar solo uno.

| Contexto de origen | FilterByModule | Context.Module | Cuerpo futuro `modulos` |
|---|---|---|---|
| Local explicito | ausente/vacio/blanco | cualquiera | Ausente/null; el contexto Local no aporta restriccion en el origen. |
| Local explicito | `alpha` | cualquiera | `["alpha"]`. |
| Authorized admitido por politica/ACL | ausente/vacio/blanco | `alpha` | `["alpha"]`; nunca omitir el autorizado por faltar el explicito. |
| Authorized admitido por politica/ACL | `alpha` | ausente/vacio/blanco | `["alpha"]`, SOLO si se consiente mantener opcionales los claims y la ACL permite la consulta. |
| Authorized admitido por politica/ACL | `alpha` | `alpha.Foo` | `["alpha","alpha.Foo"]`, AND; tambien puede cumplirse por campos diferentes. |
| Authorized admitido por politica/ACL | `alpha` | `beta` | `["alpha","beta"]`; no deducir incompatibilidad solo de los textos: un punto puede tener namespace alpha y ruta beta/x. |
| Authorized admitido por politica/ACL | `alpha` | `alpha` | `["alpha","alpha"]` es valido e idempotente; no requiere normalizacion. |
| Authorized admitido por politica/ACL | ambos ausentes/vacios/blancos | ambos ausentes/vacios/blancos | Ausente/null SOLO si la politica de C-A preserva opcionalidad y la ACL permite; en cualquier otra politica, rechazo. |
| Cualquier contexto admitido | `" alpha"` o `"alpha "` | otro activo | Conservar cadenas literalmente; no convertir en `"alpha"`. |
| Contexto null/desconocido, identidad/ACL no resuelta, o Authorized diferido | cualquiera | cualquiera | Error explicito antes de HTTP; no lista vacia, ni Local, ni reintento sin filtros. |

Una lista calculada sin activos se serializa ausente/null, NO `[]` (400). El
tratamiento de blancos del origen no autoriza descartar un claim de seguridad
malformado: la politica de C-A debe decidir su admision antes de esta traduccion.
Los faltantes de namespace/ruta en un punto no permiten quitar una restriccion.

Para preservar tenant, la misma lista logica de restricciones activas es AND,
pero sobre keyword de tenant, no `module_prefixes`. No asumir un payload multivalor
ni simplificar A AND B como A. Un rechazo por capacidad ausente o un 400/409/401/403/
503 remoto debe ser visible; no devolver exito vacio ni invocar el pipeline local.
Los filtros deben viajar en codigo, resumen y cada peticion por simbolos; ninguna
semilla, reranker, contexto LLM, fuente o respuesta puede consumir un resultado
fuera del contexto. No basta filtrar la respuesta final.

## Decisiones solicitadas y trabajo dependiente

Se consulto al responsable con la herramienta de preguntas, DESPUES de esta matriz,
solamente:

1. L: preservar full-text, consentir igualdad ordinal de lenguaje o diferir con rechazo.
2. N: preservar full-text, consentir frontera solo de namespace o diferir con rechazo.
3. T-E/T-A: preservar tenant con fuente de asignacion/historicos por definir, o
   diferir rechazando tenant activo; no usar coleccion como equivalencia implicita.
4. C-A: preservar autorizacion en Rag (no en VectorSpace), con opcionalidad original
   o endurecimiento consentido de claims; o diferir todo Authorized con rechazo.

La herramienta informo que el responsable no estaba disponible. **No hubo respuesta,
consentimiento ni seleccion de opciones.** No se eligio preservacion, cambio o
diferimiento para L/N/T/C-A. El mandato de no ampliar resultados es una restriccion
de trabajo, no una decision de eliminar capacidades del primer cliente.

Reentrada concreta de 1.21.2: recibir las cuatro decisiones sobre esta matriz y
registrar sus motivos; si se elige preservacion, concretar la referencia full-text
de L/N y las fuentes/ausencias de tenant y ACL que correspondan. Si falta esa
informacion, preparar una ficha bloqueada de decision con responsable y oraculo,
no dar por lista una implementacion ambigua. No hace falta volver a decidir modulo.

Se registra **1.21.3-implementar-guardias-y-traduccion-de-filtros-del-cliente**,
bloqueada por 1.21.2: componente y pruebas del futuro adaptador en Rag, sin cambiar
la composicion ni ejecutar 1.12. Es trabajo necesario cualquiera que sea la politica:
VectorSpace ya entiende AND de modulos pero Rag todavia no traduce su contexto a
ese contrato ni rechaza capacidades incompatibles antes del transporte remoto.
Presupuesto provisional 0.6 h, rollback de codigo sin datos, oraculos en su ficha.
Rag permanece solo lectura EN ESTA conversacion; la ficha nueva no autoriza iniciar
trabajo alli sin resolver su propio ledger y la politica de compatibilidad.

Por fila: M y C-L no necesitan nuevos campos/permisos de VectorSpace, pero si el
componente de 1.21.3. L/N requieren filtro de servidor si se preservan/cambian;
si se difieren, guardia de rechazo. T necesita asignacion, payload, filtro y eventual
migracion si se preserva; si se difiere, rechazo. C-A preservado necesita fuente ACL
en Rag y oraculos de identidad/consumo; diferido necesita rechazo integral. Esas
implementaciones/fuentes adicionales NO se declaran innecesarias ni listas sin
respuesta. Al retomar 1.21.2, crear las fichas concretas con presupuesto/rollback y
encadenarlas ANTES de 1.21.3/1.12 segun sus requisitos; no basta una nota `_...`.

Oraculos minimos para el futuro adaptador, no ejecutados en esta ficha: traduccion
de todas las filas de modulo, no perdida de restricciones, AND con simbolos,
Local explicito, rechazo de capacidades no soportadas antes de HTTP, mismo contexto
en todas las ramas/saltos y cero consumo de resultados ajenos por reranker/LLM.
Para L/N, un corpus sintetico debe separar tokens/subcadenas, mayusculas, fronteras,
ruta coincidente pero namespace distinto y campos ausentes; fijar version/esquema
y expectativas ANTES de medir. Para tenant, positivo propio, negativo ajeno/sin
tenant y riesgo de contexto A con filtro B y two-hop. Para ACL, cuatro actores
(admin, autorizado, ajeno, no autenticado), manifiesto ausente/no publicado,
claims multiples/blancos y error de identidad sin fallback.

**Cadena literal: 1.12 -> 1.21.3 -> 1.21.2 -> 1.21.1 (hecha).** Las tres primeras
estan bloqueadas. No hay siguiente item ejecutable: el prerrequisito inmediato es
la respuesta del responsable para retomar 1.21.2, no ejecutar 1.21.3 ni 1.12.
El baseline/eval-set pareados de 1.12 no se ejecutan ni se satisfacen con esta
preparacion; el objetivo sigue siendo cliente completo de ingesta/busqueda con
orquestacion/generacion en Rag, no solo sustitucion del brain. 1.7.2 conserva su
bloqueo externo y no se ejecuta.

## Rollback y comprobacion documental

Revertir el commit documental restituye esta preparacion y los bloqueos anteriores,
sin tocar codigo, contratos, datos ni permisos. `decision.md` y los resultados
historicos de 1.14/1.21/1.21.1 no se reescriben.

La aceptacion de esta ficha es revision de evidencia, matriz, respuestas y fichas
dependientes, mas `arnes validar`; validar el JSON no prueba filtros ni recall.
Si falta respuesta, el resultado es parcial y la ficha debe quedar bloqueada con
condicion concreta de reentrada, aunque el arnes salga 0.

Comprobaciones ejecutadas manualmente, en invocaciones separadas de la edicion:
`arnes.cmd validar` valido 35 items (30 hechos, 4 bloqueados, 1 descartado);
`arnes.cmd validar --al-cerrar 1.21.2-preparar-compatibilidad-de-filtros-del-cliente`
devolvio 0 e informo expresamente **bloqueado, no cerro**. Arnes 1.17.4 (017e7d9);
su wrapper emite errores de comentarios de cmd antes de ejecutar, ya registrados
en 1.21; no se modifico esa herramienta ajena.

Revision manual de todas las filas y limites, mas comprobacion del JSON: IDs unicos,
dependencias existentes sin ciclos, cadena bloqueada y objetos historicos intactos
(solo cambian la ficha actual y las dependencias de 1.12; 1.21.3 es nueva).
`appsettings.json` de VectorSpace conserva el SHA256 inicial sin leer su contenido;
Rag conserva su estado local ajeno. No se ejecutaron pruebas de producto,
servicios ni ingestas; no hay aceptacion funcional nueva de 1.21.1 o 1.12.
