# Consumo de ingesta (1.7)

Mide qué cuesta ingerir un repo por el contrato HTTP del servicio y cómo se comporta con varias ingestas a la vez.
No es producto: hospeda la API en **Kestrel real** (no TestServer, para que cuenten el socket, la serialización y
el límite de cuerpo) con un emisor JWT de prueba, y el motor real contra el Qdrant y el TEI que se le indiquen.
Decisión y lectura de los números: [`docs/evaluaciones/1.7/decision.md`](../../docs/evaluaciones/1.7/decision.md).

Destino por variables (por defecto, el túnel SSH a corpreyiaprod de 1.7):

| Variable | Defecto |
|---|---|
| `VSE_QDRANT_HOST` | `127.0.0.1` |
| `VSE_QDRANT_GRPC` | `6334` |
| `VSE_TEI_URL` | `http://127.0.0.1:8081` |

Solo crea colecciones `it-1-7-*`, las anota en `.cache/consumo-1.7/creadas.txt` y `limpiar` borra esas y solo esas.
No lista ni toca ninguna otra colección.

| Comando | Qué hace |
|---|---|
| `corpus <raiz>` | Documentos del escáner real de Rag.Context.Engine, tamaño, tiempo de escanear y leer del disco, cuerpos JSON en lotes de 100. |
| `transferencia <raiz> <lote> <seg>` | El repo completo por `POST ingestas` en lotes de 100. El lote `<lote>` se corta a los `<seg>` segundos y se repite entero. Después, segunda pasada («sin cambios»). Bytes, tiempo, working set y tráfico aproximado del túnel. |
| `cuerpo` | Cuerpos de 7,6 / 26,7 / 38,2 MB con documentos `.bin` (fuera de perfil, no vectoriza): límite efectivo del cuerpo HTTP. |
| `concurrencia <raiz> [caracteres]` | Dos subconjuntos disjuntos, primero uno tras otro y luego a la vez, con la latencia de `busquedas` y `embeddings` mientras tanto. Al final, el mismo lote dos veces a la vez sobre la misma colección. Requiere `transferencia` antes. |
| `saturacion <raiz> <n>` | `n` ingestas simultáneas con el límite por defecto del servicio: cuántas 200 y cuántas 429. |
| `limpiar` | Borra las colecciones del registro. |

```powershell
dotnet build tools/consumo-ingesta -c Release
$d = "tools/consumo-ingesta/bin/Release/net10.0/ConsumoIngesta.dll"
dotnet $d corpus D:\Git\Reyma.TI.Tickets.Microservice
dotnet $d transferencia D:\Git\Reyma.TI.Tickets.Microservice 0 30
dotnet $d cuerpo
dotnet $d concurrencia D:\Git\Reyma.TI.Tickets.Microservice 400000
dotnet $d saturacion D:\Git\Reyma.TI.Tickets.Microservice 8
dotnet $d limpiar
```

Salida en `.cache/consumo-1.7/` (ignorado por git); la evidencia de 1.7 está copiada en
`docs/evaluaciones/1.7/resultados/`.
