# 1.21.4 - Filtros textuales de lenguaje y namespace

Fecha: 2026-11-03. Base anterior: `da1b3d0570ab2d9cf38701b7471e17d4d631f780`.
Implementacion de servidor, no activacion del cliente Rag, permisos ni evaluacion de recall.
Contrato normativo y ejemplos: [contrato-api.md](../../servicio/contrato-api.md) y
[filtros.md](../../servicio/filtros.md).

## Referencia y oraculo fijados antes de medir

- Qdrant **1.19.2**, imagen `qdrant/qdrant:v1.19.2`, ID local
  `sha256:b7b0444c4c351c970b98e90a6f89c2ee4287c65b44e52b4cb503fa5b2aa927ad`.
  Contenedor propio con `--rm`, sin volumen compartido, REST/gRPC solo loopback
  `26333/26334`. Nunca se conecto al compose servido ni a servicios corporativos.
- Colecciones `it-1-21-4-*`, 139 puntos sinteticos fijos y vectores de 4 dimensiones,
  creados por el fixture. Incluyen 120 negativos de mayor score que superan el pool
  minimo de 40; un punto sin resumen y otro elegible bajo el umbral 0.25.
- `language` y `namespace` sin indices (comprobacion de esquema antes/despues).
  Solo los indices existentes creados por el escritor actual para simbolos/modulo;
  no se agregaron indices al producto ni se cambio el escritor.
- Elegibilidad textual: `POST /collections/{nombre}/points/scroll` por REST,
  construyendo directamente `match: {text: valor}` sobre `language`/`namespace`.
  No llama al constructor de filtros bajo prueba. Campos/valores y tratamiento
  de blancos contrastados con `RetrievalOptions`, `BuildFilter` y `SearchCommand`
  de Rag (solo lectura). La referencia y el nuevo lector usan los mismos puntos,
  payload, vectores, version y esquema.
- Ranking independiente sobre vectores guardados: dot con e0 y umbral para denso/
  resumen; dot sparse, pool `max(4K,40)`, RRF k=2 en f32 para fusion. Desempate ordinal
  por id. Modulos evaluados con el predicado original de namespace/ruta, no sus prefijos;
  simbolos por ANY exacto. Pruebas con K=3 y K=100.
- Control sin filtros nuevos: lector congelado de `da1b3d0` en
  `tests/Reyma.VectorSpace.Engine.Microservice.Tests/Motor/LectorAnterior1_21_4.cs`.
  Solo cambian nombres de tipos; el script comprueba su fuente contra ese commit.
  Los helpers de ranking/consulta determinista compartidos no se modificaron.

## Casos y resultados

| Grupo | Evidencia |
|---|---|
| Contrato HTTP | Todos los nombres de SourceLanguage admitidos; null/ausente sin lenguaje; vacio/blanco/nombre ajeno/mayuscula no canonica rechazados. Ambos campos rechazan numero, booleano, arreglo y objeto antes del motor. |
| Namespace literal | Null/vacio/blanco no filtra; mayusculas, espacios y separadores conservados. Incluso cadenas con forma de fecha, numero o booleano siguen siendo cadenas. |
| Positivos/negativos | CSharp/alpha incluye el punto CSharp+alpha; excluye Markdown, language ausente, beta y namespace ausente aunque la ruta contenga alpha. Payload `csharp` coincide por Match.Text, distinguiendolo de igualdad keyword. |
| Tokens/subcadenas | `alpha`, `ALPHA`, `alp`, ` Alpha `, `alpha.Foo`, `alpha..Foo`, `alpha/beta`, otros lenguajes y combinaciones comparados con referencia directa. |
| Historia 1.2 | `Reyma.Ventas` y `reyma.ventas`: solo `Reyma.Ventas.Api`, no `Reyma.VentasExtra.Api` ni `xReyma.Ventas`. Puerta inicial separada: una discrepancia detiene la matriz. No hubo discrepancia. |
| AND y riesgo | Cada modulo AND ambos textos AND ANY de S1/S2, conjuncion imposible vacia y K elegibles pese a negativos de mayor score. Denso, ambas ramas de fusion y resumen en ambos modos. |
| Control anterior | 24 comparaciones pareadas (2 modos x 2 espacios x 3 combinaciones de filtros anteriores/blancos x 2 K), IDs/orden/scores exactamente iguales. |
| Sin escrituras | Igualdad estructural de puntos completos antes/despues: payload, huellas y vectores; esquema de indices intacto. 0 llamadas a LLM; embeddings sinteticos inyectados. |
| Autenticacion | 401 sin token, sin alcanzar el motor; no se retiro JWT ni se agregaron permisos. |

El primer ensayo detecto un problema del instrumento de preservacion:
comparar `RetrievedPoint.ToString()` dependia del orden no garantizado de los mapas
protobuf. Se sustituyo por igualdad estructural y orden de claves del esquema,
sin cambiar datos, umbrales, expectativas textuales ni ranking; ambos brazos se
volvieron a medir con el mismo fixture. No fue una discrepancia de la referencia 1.2.

Un caso adicional detecto que Newtonsoft interpretaba cadenas ISO como fechas antes
del convertidor estricto. Se reprodujo el 400 incorrecto y se corrigio desactivando
ese parseo solo al leer `BuscarRequest`; no se normaliza ni reserializa la cadena.

## Aceptacion reproducible

Requiere Docker y dependencias .NET ya restauradas. Desde la raiz del repositorio,
sin reutilizar una instancia existente en esos puertos:

```powershell
docker run -d --rm --name vse-it-1-21-4-qdrant --label vse.item=1.21.4 -p 127.0.0.1:26333:6333 -p 127.0.0.1:26334:6334 qdrant/qdrant:v1.19.2
pwsh -NoProfile -File tools\filtros-textuales\aceptacion-1-21-4.ps1
# Al terminar, detener solo el ID devuelto por docker run; --rm elimina sus recursos anonimos.
docker stop <ID-del-contenedor-propio>
```

El script verifica contenedor/imagen/etiqueta/puertos/AutoRemove/version antes de
conectar. Ejecuta primero la sonda historica y luego la seleccion de contrato/
integracion y regresiones de modulo/espacio, validacion del cuerpo, JWT, TopK y
limitador (doble de motor actualizado). Exige TRX, total > 0, todos ejecutados
y pasados, cero omisiones y todos los metodos requeridos presentes. Comprueba el
lector anterior y que no queden colecciones propias; elimina solo su directorio TRX
temporal. Las pruebas de Qdrant se habilitan con `VSE_QDRANT_TEXTUAL=1` solo en ese
script; ejecutarlas omitidas en una suite ordinaria NO acredita aceptacion.

Comandos ejecutados manualmente en invocaciones separadas de la edicion:

- Script sin contenedor: **exit 3 esperado**, no aceptacion por infraestructura ausente.
- Script final con el banco propio: **exit 0**, sonda **1/1**, seleccion **87/87**, cero
  fallos/omisiones, todos los casos requeridos presentes, cero colecciones sobrantes.
  La seleccion compila las capas y los tests sin errores ni advertencias.
- Limpieza final: cero colecciones en el contenedor propio; `docker stop` sobre su
  ID exacto y comprobacion de retirada automatica. Imagen preexistente conservada.
- `arnes.cmd validar`: exit 0, 38 items (32 hechos, 5 bloqueados, 1 descartado).
  `arnes.cmd validar --al-cerrar 1.21.4-implementar-filtros-textuales-de-lenguaje-y-namespace`:
  exit 0, cierre con rastro. Persisten los avisos conocidos del wrapper cmd; no se modifico.
  Son controles del ledger, no ejecucion de pruebas funcionales.
- `arnes.cmd --solo-anunciar`: exit 1, no hay siguiente pendiente. 1.21.3 sigue
  bloqueada; su dependencia de servidor esta satisfecha, pero su reentrada exige
  nueva conversacion con alcance concreto en Rag y comprobar/resolver su ledger y
  cualquier trabajo en curso sin mezclarlo. Ningun otro item se ejecuto.
- Revision documental: solo cambio la ficha 1.21.4, ocho cuerpos JSON validos;
  `appsettings.json` ajeno conserva SHA256 inicial sin leer su contenido.

Los conteos describen esta corrida, no una constante para futuras pruebas.
El ledger registra tambien los controles del arnes; estos no ejecutan la aceptacion
funcional. No se conectaron embeddings/LLM reales ni se midio recall, rendimiento de
produccion, Qdrant 1.14.1 o paridad entre versiones. La semantica de Match.Text puede
cambiar con version/indices; no se extrapola a instancias no comprobadas.

## Rollback

Revertir el commit de request/propagacion/filtros/pruebas/documentacion. No hay
migracion ni payload/indices productivos que deshacer. No activar el adaptador de
Rag hasta sus items propios; un cliente que necesite estos filtros debe rechazar
un servidor incompatible, no reintentar sin restricciones. Retirar solo el
contenedor y las colecciones sinteticas propios.
