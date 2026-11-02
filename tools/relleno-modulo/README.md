# relleno-modulo (item 1.21.1)

Herramienta de operacion, no parte del servicio. Prepara una coleccion creada **antes** de 1.21.1 para el filtro
`modulos` de `POST .../busquedas` (ver `docs/servicio/contrato-api.md`, "Filtro exacto de modulo en busquedas"), o
verifica su estado sin escribir. Las colecciones creadas desde 1.21.1 ya nacen completas y no la necesitan.

Que escribe, y nada mas:

- En cada punto sin `module_prefixes_version = 1`: `module_prefixes` (derivado de su `namespace` y `relative_path`
  guardados) y `module_prefixes_version = 1`, con `SetPayload` (fusion de claves; el resto del payload no se toca).
- Los indices `module_prefixes` (keyword) y `module_prefixes_version` (integer), si faltan.
- La marca de metadata de la coleccion `module_prefixes`: `en_curso:<run>:<expiraUnixMs>` mientras trabaja y
  `completo:1` al terminar.

No vectoriza, no llama a ningun LLM ni sidecar y no cambia Ids, vectores, `content_hash`, huellas, resumenes ni
`ChunkingContract.Version`.

## Uso

```powershell
# Solo lectura: marca, indices y puntos sin preparar (conteo exacto). Sale 0 si esta completa, 1 si no.
dotnet run --project tools\relleno-modulo -- --coleccion NOMBRE --verificar --host 127.0.0.1 --puerto-grpc 6334 --puerto-rest 6333

# Relleno (paginas de 256 puntos por defecto)
dotnet run --project tools\relleno-modulo -- --coleccion NOMBRE --host 127.0.0.1 --puerto-grpc 6334 --puerto-rest 6333 [--lote 256]

# Qdrant con api-key: pasar el NOMBRE de una variable de entorno; la clave nunca se imprime ni va en la linea de comandos.
dotnet run --project tools\relleno-modulo -- --coleccion NOMBRE --https --api-key-env QDRANT_API_KEY ...
```

Codigos de salida: `0` completo (o verificado completo); `1` verificacion: incompleta; `2` argumentos o variable de
api-key ausente; `3` Qdrant no disponible o error de comunicacion; `4` error inesperado; `11` coleccion inexistente; `12` otro relleno con
lease vivo; `13` lease perdido (otro proceso lo tomo); `14` indice existente de otro tipo; `15` incompleto (quedan
o aparecieron puntos sin preparar); `16` sin progreso; `17` `namespace`/`relative_path` de un tipo inesperado;
`130` cancelado.

## Procedimiento sobre una coleccion real

Una migracion sobre datos servidos **no** queda autorizada por este item: requiere aprobacion aparte, sobre un
alcance concreto, con respaldo recuperable y exclusion de escritores verificada.

1. **Detener o actualizar todos los escritores antiguos** de esa coleccion (cualquier instancia del servicio o
   herramienta con codigo anterior a 1.21.1): no conocen la marca y escribirian puntos sin `module_prefixes`. Las
   instancias con 1.21.1 si la respetan: mientras este en curso o interrumpida, sus ingestas responden 409.
2. **Respaldo**: snapshot de la coleccion (`POST /collections/{nombre}/snapshots`) descargado fuera del nodo y
   comprobado que se puede restaurar en una coleccion aparte.
3. **Ejecutar** la herramienta. Es idempotente: cada pagina toma solo puntos sin la version actual y cada escritura
   lleva un filtro de comparacion (Id + sin version + mismo namespace/ruta leidos).
4. **Verificar**: `--verificar` sale 0 (`completa=True`, `puntos_sin_preparar=0`), y una busqueda con `modulos`
   responde 200; sin `modulos`, mismos ids, orden y puntuaciones que antes.
5. **Si se interrumpe** (error, cancelacion, caida): la marca queda `en_curso` con el lease expirado, **nunca**
   `completo`; ingestas y busquedas con `modulos` responden 409. Relanzar la herramienta continua donde quedo.
   Si el lease de otra ejecucion sigue vivo (salida 12), esperar a que expire (5 min) o confirmar que ese proceso
   termino.
6. **Reactivar** los escritores, ya con 1.21.1 o posterior.

Limites: Qdrant no tiene compare-and-set de metadata, asi que la exclusion entre dos rellenos es un lease que cada
pagina verifica y renueva; si dos se pisan, sus escrituras son las mismas (deterministas e idempotentes) y el que
pierde el lease se detiene. Un escritor antiguo que siga activo no se puede bloquear desde aqui: el relleno no marca
completo si lo detecta en el conteo final y, si escribe despues, el conteo exacto de la busqueda vuelve a responder
409 hasta relanzar el relleno.

## Aceptacion

Aceptacion del item (Qdrant local efimero en 127.0.0.1:16333/16334, colecciones `it-1-21-*`, embeddings/LLM
sinteticos): `pwsh -File tools\relleno-modulo\aceptacion-1-21-1.ps1`. Falla si falta Qdrant, si no se ejecuta
ningun test, si alguno falla o se omite, si falta un caso requerido o si el smoke de esta herramienta falla.

## Rollback

- **Codigo**: revertir el commit de 1.21.1. El lector y el escritor anteriores ignoran `module_prefixes`,
  `module_prefixes_version`, sus indices y la marca (aditivos, inertes); Ids y vectores no cambiaron.
- **Datos** (solo si hiciera falta retirar los campos de una coleccion real, con alcance autorizado y el respaldo del
  paso 2 verificado): `POST /collections/{nombre}/points/payload/delete` con
  `{"keys": ["module_prefixes", "module_prefixes_version"], "filter": {}}`, `DELETE /collections/{nombre}/index/module_prefixes`,
  `DELETE /collections/{nombre}/index/module_prefixes_version` y `PATCH /collections/{nombre}` con
  `{"metadata": {"module_prefixes": null}}`. Despues, comprobar por lectura que el resto del payload y los vectores
  siguen iguales; si no puede probarse, restaurar el snapshot. Nunca borrar la coleccion como rollback.
