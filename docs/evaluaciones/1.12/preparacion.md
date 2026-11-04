# 1.12 - Preparacion del cliente completo y bifurcacion local

Fecha: 2026-11-03, continuaciones posteriores a `122755f`, `b50eb6b` y `832b31a`.
**Resultado parcial. Cliente implementado opt-in; aceptacion rechazada, 1.12 bloqueado.**
Fuente rectora: `docs\plan\ejecucion-plan.estado.json`, ficha
`1.12-decidir-si-rag-context-engine-pasa-a-cliente`. No se ejecuta otra ficha.

## H3: diagnostico causal offline y permiso nuevo pendiente

**H3-C sostiene el mecanismo de presupuesto en la cohorte congelada; H3-P no
esta implementada ni aceptada.** La consulta de permiso especifico de busqueda
no obtuvo respuesta. 1.12 queda bloqueado por ese alcance nuevo y por calidad
pendiente. H2 sigue RECHAZADA; las secciones posteriores conservan la historia.

Antes del instrumento/medicion se registro en el clon
`docs\eval\quality\1.12\protocol-h3-budget-diagnosis.json`, SHA256
`bfe40fda47953e9386ce064faa7866e0bf138f89b096d744edc67f200bb3768e`.
Se congelaron hipotesis, metodo2x2 (40/400 en ambos brazos), predicciones y
refutaciones, criterio, representacion H2, corpus/anclas/modelos y rollback.
No se busco un parametro ganador ni se retoco tickets014.

El presupuesto por rama NO equivale a union fusionada, resultados devueltos,
pool cliente o TopK publico. En H2, transporte100 deriva prefetch400 por rama;
controlK10 deriva40. Aumentar solo el corte de una lista ya ordenada no cambia
su top10; ampliar las ramas SI puede anadir contribuciones a miembros presentes.

Tras comprobar2873recibos, cuatro representaciones completas y snapshots con
schema/modelo/nombres/estado de ingesta/resumen, el contraste45preguntas,
4negativas,ambasvistas,@1/3/5/10 reproduce H2 exactamente. Las cuatro
predicciones pasan: a igual presupuesto coinciden los45top10 y todas las
mascaras. El cambio40->400 altera el orden top10 en31preguntas de cada brazo.
Con400, ambos brazos pierden014 any/full@10 y013 any/full@3; con40 el
candidato no difiere en ninguna mascara del control40 original. No se
sustituye el control de aceptacion por control400.

### Explicacion de014, igual en ambos brazos

Valores de score redondeados solo en esta tabla; el derivado conserva los
bits y contribuciones exactas f32. Son todos los predecesores del ancla
en cualquiera de los dos presupuestos, no una seleccion favorable.

| Chunk | Rango denso/sparse | Rango40->400 | Score40->400 |
|---|---|---|---|
| Guia-pruebas-envio-correos.md7-17 | 3/1 | 1->1 | 0.750000000 |
| Asignacion de permisos para usuarios APP Reyma TI.md96-112 | 1/40 | 2->2 | 0.524390221 |
| Guia-pruebas-envio-correos.md111-118 | 2/10 | 3->3 | 0.424242437 |
| MesaAyudaTicketCommandHandler.cs60-85 | 20/2 | 4->4 | 0.380952388 |
| MesaAyudaTicketCommandHandler.cs82-107 | 10/3 | 5->5 | 0.340909094 |
| Guia-pruebas-envio-correos.md62-85 | 8/5 | 6->6 | 0.277777791 |
| TicketsController.cs112-114 | 4/16 | 7->7 | 0.258823544 |
| TicketsController.cs121-123 | 5/17 | 8->8 | 0.222222224 |
| Guia-pruebas-envio-correos.md136-144 | 16/6 | 9->10 | 0.201680675 |
| TicketAsignacionExtensions.cs81-100 | 270/4 | 11->9 | 0.200000003->0.203690037 |
| **Ancla ValidarMesaAyudaTicketCommandHandler.cs73-77** | **9/9** | **10->11** | **0.200000003 sin cambio** |

El competidor `f36653e7-4135-5365-bdbb-79b925f36e8a` ya pertenece a la
union40 por sparse4: NO es un nuevo miembro. En40 empata con el ancla
`2c679ab0-cfd8-5ffb-acff-ff045f9a8a79`; UUID ordinal pone2c...antesf3....
En400 suma denso270, `f32(1/271)=0.0036900369450449944`, y adelanta
a ambos chunks en puestos9/10. El ancla conserva sus dos contribuciones
`0.10000000149011612` y score `0.20000000298023224`.

Los vectores de documentos dense/sparse son iguales en los2598puntos
docs/tickets. Aun asi, las ramas sparse guardadas difieren en score/orden
en21preguntas, orden en8 y prefijo40 en3; no cambian top10fusionado a
igual presupuesto. La causa numerica/de ejecucion residual NO se aislo.
No generalizar a todas las ramas, filtros, resumen, reranking, ask o corpus.

### Instrumento, evidencia y limites

`h3-budget-result.json` versiona resultado, hashes, comandos y consumo.
`.artifacts\1.12-h3-budget-r1`, `1.12-h3-budget-guard-r1` y
`1.12-h3-budget-r2` quedan conservados. r2 fortalece P4 de conjuntos de
preguntas a cada vista/metrica/corte, sin cambiar el protocolo: filas,
resumen y recibos de r1/r2 son identicos. Fuentes medidas conservadas.
Derivado r2 SHA `9d13b18c01fa8f84cb5b4216d43704f713779a10413610a72808ec54bace516f`;
contraste nativo `e3c9db793319b96aac69d9789ca4d81111c33dc90ae41a334663cdab5f9b8949`;
DLL `5454879735da3994a97d285a244cb027e36efd540663724773381cfbcccc9ad8`.
126binarios DLL/deps/runtimeconfig preservados sin configuraciones.

Clon persistido en `b7944b3ee2db1021159c430c2a9f8afd1d6a1ff3`:
8archivos de instrumento/protocolos/propuesta/resultado y nota de ledger
juntos; autor/committerDWeno, mensaje sin coautoria/firmas, archivos y arbol
limpio comprobados tras commit. Sin remoto/push. El commit rector contenedor
coordina solo ficha/preparacion/relevo, no aceptacion funcional.

Build aislado sin errores/advertencias;8tests focalizados incluidos
ausencia/manipulacion/cobertura incompleta/atribucion exacta;10aserciones
previas;540comparaciones con el metodo REAL de fusion servidor, IDs y bits
f32 a limites10/100/800. Invocacion completa sin `--evidence-only` sale2.
Nada de ello es aceptacion HTTP de producto ni reutilizacion de respuestas
historicas con binarios nuevos. El original y los indices no se tocaron.

Duraciones parciales r1/r2:30.7654754/31.1885262s,guarda26.9112065s.
0servicios iniciados,0red experimental,0embeddings/ingestas/LLM.
Estimaciones2h offline+4h implementacion condicional+40h aceptacion; no
limites ni consumo total. Horas globales/tokens facturados desconocidos.
Modelo efectivo gpt-6-astra; xhigh solicitado, esfuerzo no expuesto.
Sin subagentes; opus/high del plugin se conserva.

### Propuesta congelada y reentrada

`protocol-h3-search.proposed.json`, SHA
`61967d0ee7a4fd8f121cf524fdc7fa68ca3f6a64a99642fba8614663e7ad29af`:
campo opcional `prefetchPorRama`40..1200 solo codigo/FusionNativa;
ausente conserva default. Cliente opt-in calcula max(4*B,40), B=TopK
o3*TopK con rerank: presupuesto por demanda logica, no constante40.
PublicTopK1..100 y pool configurado100 permanecen; prefijos40 permiten
MAXIMO80unicos, no prometer100disponibles ni rellenar/bajarTopK.
Resumen/saltos mantienen comportamiento anterior; perfiles de ingesta
intactos. Sin framework generico, datos, despliegues ni credenciales.

Consulta especifica con impacto y rollback realizada: **usuario no disponible,
sin respuesta afirmativa; NO autorizado por silencio**. No implementar esa
ampliacion antes del permiso. No pedir de nuevo los permisos ya vigentes.
Si se autoriza: protocolo ejecutable/guardas antes de medir, cambio minimo,
pruebas negativas/filtros/escalas/riesgo, representacion/indices completos
compatibles antes de embeddings y nueva procedencia. Primero45HTTP estricto;
cualquier perdida@10 rechaza, sin retune/H4. Solo si pasa, aceptacion
original116x2/928HTTP,29extras,adjudicacion cegada y todos los riesgos.
Rollback explicito Local y desactivar opt-in, conservando indices/evidencia.

Incidencia de exclusion registrada: la primera enumeracion
`git diff --name-only` del rector incluyo la ruta protegida y avisoLF/CRLF;
se excluyo antes de diff de contenido. No contenido mostrado, hash explicito
ni build del proyecto API. Operaciones siguientes por allowlist explicita;
no afirmar exclusion total de aquella enumeracion. Cambios ajenos preservados.

## Continuacion H2 autorizada posteriormente (2026-11-03,16:35)

**Resultado: H2 implementada y RECHAZADA, ficha bloqueada por calidad.** El permiso
especifico ya no bloquea. Perfil/ranking no se retocaron tras medir y no se ejecuto
la aceptacion completa ni otra ficha.

La representacion coincide exhaustivamente por documento/chunk, raw/enriched,
lineas e ID en cuatro cohortes:2113/23221,311/2081,15/743,553/1855
documentos/chunks. Las auditorias solapan fuentes de bsuite; no son totales
de documentos unicos. El nombre fisico alternativo no modifica H2.
La ingesta real sigue ocurriendo en el servidor, no se copiaron chunks al indice.

Runner principal: identidad persistente, lock, envelopes atomicos, estado/job/
errores/misses y pares de cuatro respuestas. Probe real r2 interrumpe tras un
documento resumido de dos, conserva ese punto al reingestar, luego interrumpe
tras3/4respuestas; reentrada valida indices/cache, adquiere4/4 y confirma un
par atomico. Una tercera reentrada restaura ese par sin HTTP. No equivale a
reanudar la corrida historica r2 ni a completar la aceptacion masiva.

Diagnostico H2 completo:45preguntas,4negativas,ambas vistas,@1/3/5/10,
native400 candidato/native40 control. **tickets-microservice-014 pierde
any@10 y full@10 true->false en ambas vistas.** El mismo chunk
`2c679ab0-cfd8-5ffb-acff-ff045f9a8a79`, rawhash
`3a35bfc63a57bc47ef3dff079af25e30209605817ad66789dc08ae79c7182265`,
`ValidarMesaAyudaTicketCommandHandler.cs:73-77`, queda nativo10/control y
nativo11/candidato; denso9 y sparse9 en ambos. No seleccionar otro pool
despues de observarlo ni atribuir toda discrepancia historica a fragmentacion.

Evidencia en el clon: `docs\eval\quality\1.12\h2-result.json` y artefactos
`.artifacts\1.12-h2-representation-r2`, `1.12-h2-resume-probe-r2`,
`1.12-h2-fixture-r1`, `1.12-h2-diagnosis-r1`. Resultado diagnostico SHA256
`5a425137cb1911493aef938c6f3216c36af0ce6a7c9003d91010b7a62e9a8596`;
verificacion independiente
`7049a89910a867d151c3f60396660b14bb5a4be8213d05186387eb8628f1e751`.
Recompone45rankings/mascaras con cosenos, producto sparse y RRFf32 desde
vectores guardados; verifica todos los bytes/sellos y los cuatro indices
historicos mas los dos H2 completos e intactos. DLL medida
`f0004d65c48bc47e93bb60fc0516c86f9ed285da26f7d67906d820f2ca16c220`,
con todas sus DLL/deps/runtimeconfig conservadas, sin configuraciones.

Build aislado0warnings/0errores;80tests cliente sin skips;23aserciones checkpoint,
8inventario,10diagnostico; fixture HTTP real con12aserciones H2 mas filtros,
resumen/saltos, idempotencia/borrado,401/404/429. No llamar esto paridad.
Diagnostico156.1912939s,45embeddingsconsulta,2598puntos candidatos,0LLM;
tokens/factura totales desconocidos. Fixtures/probes pequenos:7resumenes por
brazo,2542tokens resumen candidatos reportados,7llamadas resumen control
reportadas y8respuestas ask. No se consumieron resumenes masivos.

Revision final:3negativos del verificador rechazan ausencia/manipulacion/
cobertura incompleta; modo offline verifica45y sale2 sinflag de solo evidencia.
Se cerro la entrada invalida CodeScoreFloor distinto0.1 en FusionNativa,
sin cambiar parametros medidos. Build/80tests/23+8aserciones pasan;2warnings
heredados del proyecto tests no corregidos. Binario recompilado rechaza
el sello anterior antes de red/salida; no se migraron observaciones.
Arnes validar pasa en ambos y --al-cerrar informa bloqueado/no cerro.
Clon persistido en `2d8865aead9443aa1e0a7f77b6856da31a97c613`,22archivos
solo1.12 junto con su ledger; autor/committerDWeno,mensaje sin coautoria,
arbol limpio y sin push comprobados. El commit rector contenedor coordina
servidor/contrato/dobles dependientes y resultado/ledger/relevo, no aceptacion.

Reentrada: nueva hipotesis y protocolo comparativo ANTES de cambios/medicion,
manteniendo rechazos Denso005/H1-010/H2-014 y el oraculo estricto.
No repetir H2 sin cambios ni ajustar ranking/perfil para pasar. Las autorizaciones
vigentes no se piden otra vez; solo un alcance realmente nuevo requiere permiso.
Quedan116x2/928HTTP,29extras,adjudicacion cegada y todos los riesgos para una
eventual aceptacion. La siguiente seccion conserva el prerregistro previo.

El usuario autoriza ahora el perfil opt-in servidor y nombre logico separado,
DTO/jobs/procedencia y rechazo de mezclas, mas los cambios dependientes del clon.
La consulta sin respuesta de abajo se conserva: esta aprobacion es posterior,
no retroactiva. Solo se retira el bloqueo de permiso H2; la calidad sigue sin
aceptar y las puertas tecnicas permanecen. Unicamente1.12, sin subagentes.

Contrato/oraculo registrado ANTES de producto en el clon:
`docs\eval\quality\1.12\protocol-compatibility.json`.
Campos `perfilIngesta="rag-local-1.12"` y `repositorioLogico`, juntos y con
procedencia inmutable; nativo sin cambios. Primero runner reanudable con
identidad, estado de ingesta/resumen y cuatro respuestas atomicas; luego
igualdad completa raw/enriched/lineas/IDs de todos los corpus sin embeddings,
diagnostico45 native400/controlnative40 y, solo si pasa, aceptacion completa.
No retocar ranking, corpus, anclas ni umbrales segun el resultado.

Modelo efectivo gpt-6-astra, esfuerzo no expuesto; alias opus/high conservados.
8h implementacion+40h aceptacion estimadas, no limites ni consumo medido.
Rollback Local explicito, indices/volumen/rechazos conservados. Sin API protegida,
original, otras fichas, despliegues, credenciales/permisos/tuneles o push.

## Diagnostico posterior a 355613f: H1 refutada (historia preservada)

Solo1.12, sin subagentes. Se preservan literal los resultados anteriores.
No se cambio producto del servidor, cliente ni original: solo instrumento
aislado y documentacion. 1.12 sigue **bloqueado**, no aceptado.

H1 se fijo antes de implementar/medir en `protocol-diagnosis.json`:
candidato FusionNativa, pool100/prefetch400, contra control native40.
Sobre los cuatro indices completos conservados se midieron las45 preguntas
docs/tickets, incluidas4 negativas, ambas vistas y @1/3/5/10. Un vector real
Snowflake por pregunta se compartio entre brazos. Rankings exactos completos,
RRF f32 k2, desempate UUID ordinal y umbrales originales. La recomposicion
independiente coincide con el metodo real de fusion del servidor; esto NO
certifica la composicion de su endpoint HTTP ni activa FusionNativa en cliente.

| Caso/ancla | Control | Candidato | Conclusion acotada |
|---|---|---|---|
| tickets005, validator14-21 | dense18,sparse3,native7 | dense16,sparse4,native8 | Fusion recupera el ancla sobre el mismo indice; pool40/400 no cambia ese resultado |
| tickets010, validacion de estatus | raw70-95,dense7,sparse7,native8 | raw70-93,dense8,sparse8,native11 | H1 pierde full@10 en ambas vistas; pool40 tampoco lo resuelve |

El ancla005 conserva el mismo hash crudo en ambos indices; sus hashes
enriquecidos difieren. El ancla010 sobrevive, pero el chunk crudo cambia.
No atribuir toda la perdida a sparse ni afirmar causalidad exclusiva de
fragmentacion/enriquecimiento. Denso candidato tambien pierde tickets012.
Los contrafactuales explican mecanismos, no autorizan elegir parametros
despues de observar resultados. H1 queda refutada aunque mejore005.
Los62 top10 antiguos disponibles se reprodujeron EXACTAMENTE por
ruta,lineas,contenido y orden. El rechazo original permanece intacto.

### Correccion del inventario, previa a cualquier embedding diagnostico

La primera invocacion fallo porque9/1707 hashes candidatos de tickets no
coincidian con `chunks.json`; los otros tres multisets si coincidieron.
Se prerregistro `protocol-diagnosis-inventory.json`, sin alterar H1 ni su
oraculo, para exigir doble replay sobre los mismos bytes: nombre preparado
`v112-<dataset>` contra TODO el inventario preparado y nombre UUID real
contra TODOS los puntos. Ambas igualdades pasan: cambiar el nombre reproduce
exactamente los9 hashes distintos; docs tiene0/629 diferencias. Sin reingesta.
El header afecta al presupuesto de fragmentacion. El runner anterior solo
comprobaba conteos candidatos; no retrocertificar su procedencia completa.

### Evidencia, consumo y reanudacion

Salida `.artifacts\1.12-diagnosis-r2`; resumen versionado
`docs\eval\quality\1.12\diagnosis-result.json` en el clon.
Resultado `result-ecbcc49e4c034f1a8d7cfe02020a15f4.json` SHA256
`e3f8032d45664c7a90c667ec7eebc30ec9c5c83139ad6faef7bf5fd0f3d1b035`.
Verificacion independientev2 SHA256
`fb82c14b05aa34c432156be163fc4c82b160a38ca59326de61d2f8903aa7050d`.
Comprueba45 preguntas completas, recibos/sello, mascaras, RRF y cosenos
recalculados desde vectores guardados; evidencia vacia/incompleta falla.

Corrida9.2250393s,45 embeddings de consulta,0 embeddings documentos,0 LLM.
Tokens de consulta, factura y tiempo total de sesion desconocidos. Las4h de
investigacion y40h de aceptacion siguen estimaciones, no limites.
Los31880 misses restantes no se consumieron.

El instrumento incorpora lock exclusivo, checkpoints con recibos inmutables
y sello de fuentes/inventario/protocolo/binarios/servicio/indices. El intento
real de reanudar r2 fallo ANTES de inferencia por diferencias de escape JSON
en la canonizacion. Se corrigio y se cubrio con10 aserciones, incluido roundtrip
escapado; build0warnings/0errores. La DLL medida se conserva en
`measured-binaries\Fixture.dll`, SHA256
`a34c12a71f0b4ad52b6ca3b606120016494c08ec06f74171c7b25551ec7f284c`.
El binario corregido rechaza correctamente el sello antiguo antes de inferir.
**No hubo reanudacion real r2 completa exitosa.** No sobrescribir/migrar sus
sellos ni regenerar H1 rechazada para conseguir un verde. El runner primario
largo sigue sin reanudacion: antes de usarlo faltan identidad persistente de
cohortes/colecciones, ingesta/resumen/procedencia y commits atomicos de pares
de cuatro respuestas. La cache candidata en memoria no es durable.

Comandos ejecutados manualmente en invocaciones separadas de edicion:

```powershell
# Desde D:\Git\Rag.VectorSpace.Client; diagnostico, NUNCA cierre completo.
dotnet build docs\eval\quality\1.12\Fixture\Fixture.csproj -p:VectorSpaceRoot=D:\Git\Vector.Space.Engine --no-restore --verbosity quiet
dotnet docs\eval\quality\1.12\Fixture\bin\Debug\net10.0\Fixture.dll --diagnose-self-test
# Procedencia historica del binario medido: NO repetir con el binario corregido.
dotnet docs\eval\quality\1.12\Fixture\bin\Debug\net10.0\Fixture.dll --diagnose .artifacts\1.12-20261103-r3 .artifacts\1.12-paired-r1 .artifacts\1.12-diagnosis-r2
# Salida existente inmutable: para recomprobar elegir OTRO nombre de reporte.
python docs\eval\quality\1.12\verify-diagnosis.py .artifacts\1.12-20261103-r3 .artifacts\1.12-paired-r1 .artifacts\1.12-diagnosis-r2 --report .artifacts\1.12-diagnosis-r2\independent-verification-v2.json
```

El diagnostico devuelve error por H1 refutada; aun sin perdidas devolveria
error por aceptacion completa ausente. El verificador independiente valida
la evidencia del rechazo, NO la calidad para cerrar1.12. Los110 tests y18
fallos Windows anteriores no se reejecutaron ni se modificaron.

Los cuatro indices mantuvieron hashes completos de payload/vectores/esquema
antes/despues. Qdrant propio se verifico por ID/imagen/volumen/puertos antes
de arrancar y quedo detenido, `exited`, volumen conservado. Sin escrituras de
indice, SQLite/WAL, hosts HTTP nuevos, AltheIA, credenciales o tuneles.

### H2 propuesta: permiso especifico del servidor pendiente

`protocol-compatibility.proposed.json` mantiene ranking H1 fijo y propone
perfil de ingesta servidor opt-in compatible con representacion del control,
con nombre logico independiente de coleccion. Antes de embeddings exige
igualdad completa raw/enriched/lineas/IDs; despues45 preguntas y, solo si no
pierde, toda la aceptacion original. No es garantia de mejora ni unica solucion.
Se pide aprobacion de contrato/perfil/DTOs/jobs/procedencia y pruebas aisladas,
sin despliegues ni indices servidos; defaults nativos intactos, controles
conservados y rollback Local explicito. Estimacion8h implementacion mas40h
provisionales de aceptacion. **Consulta especifica para futura reentrada:
usuario no disponible; no autorizada, implementada ni medida.**
Las aprobaciones anteriores del clon/tiempo/AltheIA siguen vigentes.

Persistencia del diagnostico en el clon:
`b14ebdbcb2a96d08ebdb680f8db6bbfe0ccbc399`,8 archivos solo1.12, incluyendo
instrumento/protocolos/propuesta/resumen y nota del ledger. Autor/committerDWeno,
mensaje y archivos comprobados, sin coautoria/firmas, arbol limpio, sin push.
El commit rector que contiene esta seccion coordina ficha/preparacion/relevo;
no representa cierre funcional. `arnes validar` sale0 en ambos y `--al-cerrar`
informa bloqueado/no cerro; solo validacion documental, con avisos heredados.

## Implementacion y fixture real anteriores (historia preservada)

Se implemento solo en `D:\Git\Rag.VectorSpace.Client`: transporte/API tipada,
retrieval remoto, perfiles/catalogo, brain para meta-intent, pipeline ZIP/estado,
resumen remoto autoritativo y composicion real API/CLI. El backend local sigue
seleccionable y predeterminado; un fallo remoto no activa fallback. No se cambio
producto del original ni de VectorSpace.

La combinacion Empresarial+VectorSpace falla desde la composicion real, antes
del consumo; se rechazan Authorized y tenant activo. L/N textuales y modulo
opcional se conservan en codigo, resumen y cada salto. Los metadatos/metricas
que el servicio no expone permanecen ausentes. El cliente no abre cache SQLite
de resumen ni estado de ingesta; el servidor usa su cache nativo en memoria.

Comprobaciones manuales, separadas de edicion:

- 110 pruebas enfocadas pasan, sin fallos ni omitidas, con comprobacion del TRX
  para rechazar cero tests. Composicion API/CLI, transporte, contexto y pipeline.
- Fixture sintetico real `1.12-live-fixture-r4`: ingesta de cuatro documentos,
  idempotencia, JWT/401, filtros en requests REALES de codigo/resumen/simbolos,
  respuestas search/ask de ambos brazos, 429/Retry-After, borrado de un documento
  de prueba, 404 de operacion desconocida y control local explicito.
- r3 y r4 completos: 18.49/18.50 segundos; 4 resumenes por brazo/corrida.
  Tokens de resumen reportados por candidato: 1391/1407. El contrato no entrega
  el total de tokens de resumen del control ni de ask: no inventar total facturado.
- Las dos ejecuciones anteriores del fixture fallaron antes de ingesta por
  registros faltantes del host de pruebas: accessor de identidad y reloj UTC.
  Se usan registros existentes y reloj Business sin SQL, identidad corporativa
  ni lectura/compilacion de la API/configuracion protegida.
- Una ampliacion de pruebas de contexto encontro 18 fallos Windows. El mismo
  conjunto de 18 se reprodujo en copia aislada de la revision original:
  CRLF en goldens y archivos SQLite abiertos. No se modificaron goldens ni 19.6.

Qdrant propio: `v112-qdrant-20261103`, volumen
`v112-qdrant-20261103-data`, imagen Qdrant 1.19.2
`sha256:b7b0444c4c351c970b98e90a6f89c2ee4287c65b44e52b4cb503fa5b2aa927ad`,
loopback 26333/26334. El Qdrant del tunel 6333 queda intacto.
Los hosts HTTP son efimeros, con JWT sintetico local validado; no cambian
credenciales reales. Conservar volumen/indices para evidencia y rollback.

Antes de la primera medicion primaria se registro protocolo v3: cohortes
completas docs, tickets, bsuite-repo y auditorias, orden inverso en ronda 2.
Si un par completo demuestra una perdida contra el criterio universal ya
congelado, detener con error y conservar el rechazo; no consumir los 31880 misses
restantes para demostrar de nuevo el incumplimiento. No cambiar documentos,
anclas, umbrales, modelos ni flags de resumen; no aprobar trabajo no medido.
`Fixture --paired` adquiere evidencia, pero NO certifica por si mismo
adjudicacion semantica, cobertura de riesgos ni aceptacion completa.

## Resultado pareado: rechazo temprano verificado

La corrida `.artifacts\1.12-paired-r1` termino con error tras **124 respuestas
HTTP 200, 31 pares completos**, todos de ronda 1: 26 docs y 5 tickets.
Se ingirieron los corpus completos de esas dos cohortes, no un subconjunto
favorable: 15/553 archivos, 743/1855 chunks del control y 629/1707 puntos
del candidato. Los resumenes estaban deshabilitados en estas cohortes desde
el protocolo original, no se omitieron para facilitar esta prueba.

Contraejemplo: **`tickets-microservice-005`**, adjuntar documento/evidencia a ticket.

| Evidencia @10 | Control | Candidato |
|---|---|---|
| Ancla `TicketDocumentoRequest request` | Rango 1 | Rango 1 |
| Ancla `RegistroTicketDocumentoValidationMessages` | Rango 7, validator lineas 14-21 | Ausente; rango 10 trae otro chunk del validator, lineas 25-29 |
| hit_any | true | true |
| hit_full | **true** | **false** |

Ocurre en ambas vistas, historica y corregida. La recomputacion independiente
normaliza los dos separadores de ruta, usa basename y comparacion ordinal
de anclas, confirma las cuatro respuestas HTTP del par y vuelve a comprobar
los hashes de **todos** los bytes del corpus congelado. No es un fallo de
infraestructura ni del nombre de archivo. No aisla causalmente sparse,
chunking o pools: no atribuirlo a uno solo sin otro experimento.

- SHA256 `evaluation.json`:
  `9acd079abed96cd43d151d31ae6586aac1f94d9ff746cd951cdd1ce21ff4b511`.
- SHA256 del protocolo medido, copiado antes de ejecutar:
  `9a682d542c74096914caff1bc1ad62b5bc7f328a6dbac08258c612c889180232`.
- `rejection-verification.json` conserva el contraejemplo independiente.
  Configuracion, esquemas, hashes binarios, respuestas, tiempos y archivos
  cegados permanecen bajo la salida de corrida, ignorada por Git.
- Duracion primaria: **888.696 s**. Ingesta candidato docs/tickets:
  44.651/101.938 s; control: 73.532/206.487 s.
  Control: 2598 textos y **712096 tokens de embeddings de documentos**,
  sin truncamiento. No es el total de tokens de consultas/generacion.
- No se generaron los **31880 misses** de resumen restantes: el criterio ya
  esta refutado. No equivale a declarar que esos brazos/cohortes pasaron.
- Quedan sin completar resto de tickets, bsuite/auditorias, ronda 2, suplemento29,
  adjudicacion semantica de ask y cobertura total de riesgos. Una lectura
  parcial de respuestas cegadas no certifica ask.

Comandos reales ejecutados en llamadas separadas de edicion:

```powershell
# Desde D:\Git\Rag.VectorSpace.Client. Evidencia parcial, NO cierre de 1.12.
dotnet test tests\RagEngine.Core.Tests\RagEngine.Core.Tests.csproj --no-restore --filter "FullyQualifiedName~VectorSpace|FullyQualifiedName~TransportProfileTests|FullyQualifiedName~CollectionAuthorizationServiceTests" --logger "trx;LogFileName=remote-real-composition-r4.trx" --results-directory .artifacts\1.12-tests --verbosity minimal
dotnet build src\RagEngine.Cli\RagEngine.Cli.csproj --no-restore --verbosity quiet
dotnet build docs\eval\quality\1.12\Fixture\Fixture.csproj -p:VectorSpaceRoot=D:\Git\Vector.Space.Engine --no-restore --verbosity quiet
dotnet run --project docs\eval\quality\1.12\Fixture\Fixture.csproj -p:VectorSpaceRoot=D:\Git\Vector.Space.Engine --no-restore -- .artifacts\1.12-live-fixture-r4
dotnet run --project docs\eval\quality\1.12\Fixture\Fixture.csproj -p:VectorSpaceRoot=D:\Git\Vector.Space.Engine --no-restore -- --paired .artifacts\1.12-20261103-r3 .artifacts\1.12-paired-r1
```

Las salidas existentes no se sobreescriben: los dos ultimos comandos son la
procedencia de lo ejecutado, no una invitacion a regenerarlo. El ultimo devolvio
error por el criterio; no se cambio para hacerlo verde. Los comandos no son
`verificacion_comando` de una aceptacion completa.

Qdrant propio **detenido y comprobado `exited`**, volumen conservado; hosts HTTP
detenidos en `finally`. Sin auxiliares de prueba que deban seguir ejecutandose.
Rollback: backend Local explicito y eventual revert del commit de composicion,
sin borrar indices ni el original. Otra medicion necesita hipotesis/protocolo
previos, conservar el rechazo y reutilizar trabajo compatible. El runner actual
exige salida nueva y no ofrece reanudacion ni certificacion de adjudicaciones:
resolver esas limitaciones antes de una futura corrida larga. No hay otra ficha
autorizada; no abrir 1.21.5/1.21.6.

## Persistencia coordinada

Cliente: `3bf19259a91756f14c27dbe500be2be98d71e0c6`, rama
`feature/vectorspace-client`: 51 archivos del item, producto/pruebas/harness
y nota del ledger juntos. Autor/committer DWeno, mensaje comprobado sin
coautoria ni firma de herramienta; arbol limpio, sin remote/push.
La medicion precede al commit y conserva sus hashes binarios/configuracion.
El commit de VectorSpace que contiene este documento persiste unicamente
la ficha 1.12, esta evidencia y el relevo; no es cierre funcional.

## Historia del preflight: servicios remotos, no inferencia local

La respuesta **"ok, realizalo"** de las 12:44 aprobo ranking distinto y nueva
referencia pareada estricta. Implementacion y tiempo ilimitado tambien estaban
aprobados. No volver a presentar esas consultas historicas como pendientes.

Antes de producto o medicion, el usuario corrigio el banco: Snowflake por el
tunel ya existente y AltheIA configurada, dejando Qdrant de pruebas aislado local.
Ambos brazos usaran los mismos modelos remotos. El control sera una adaptacion
aislada del pipeline original, NO su runtime intacto ni el baseline 5.e.
El original no se modifica. No se toca el Qdrant del tunel ni se cambia el tunel.

Comprobaciones reales, solo metadatos:

- Los puertos indicados 6333/8081 estan escuchando en loopback. Cero requests a
  Qdrant, ninguna coleccion listada, creada o modificada.
- `/info` en 8081: `Snowflake/snowflake-arctic-embed-m-v2.0`, float32,
  ventana 8192, lote 16384, TEI 1.8.3 y commit del servicio
  `78502d8e61223d2c73d4bb7aeaea46787e90d596`.
  No expone revision de pesos: ese commit NO es un hash del modelo.
- AltheIA `/v1/models`: `bge-m3`, `qwen3.8-27b-deep`, `qwen3.8-27b-fast`.
  Se selecciona `fast`, como en el ejemplo proporcionado. Esto prueba acceso al
  catalogo, no inferencia ni cuota disponible. No se selecciona bge-m3 en lugar
  del Snowflake indicado por el usuario.
- El ejemplo PowerShell se inspecciono mediante AST, sin ejecutar sus funciones
  que cambian credenciales/archivos de uso, ni mostrar valores secretos.
  El probe reutiliza el cliente HTTP/TLS del motor con las variables existentes
  `ALTHEIA_KEY`/`ALTHEIA_CERT`, sin desactivar validacion ni seguir redirecciones.
  No lee el appsettings protegido ni imprime certificados o credenciales.

Protocolo activo en el clon: `docs\eval\quality\1.12\protocol.json`.
La propuesta de inferencia local se conserva, **sustituida antes de medir**, en
`protocol-local.pre-altheia.json`. Dos rondas, mismo corpus y criterios estrictos;
no se relajan anclas/umbrales por cambiar servicios.

Preparacion real reproducible (`.artifacts\1.12-20261103-r2` y `-r3`, ignorados):

| Cohorte | Archivos admitidos | Chunks control / candidato | Preguntas con anclas ausentes |
|---|---:|---:|---:|
| bsuite-repo | 2113 | 23221 / 17628 | 0 / 0 |
| auditorias | 311 | 2081 / 1792 | 0 / 0 |
| docs | 15 | 743 / 629 | 0 / 0 |
| tickets | 553 | 1855 / 1707 | 0 / 0 |

Son 116 preguntas: 104 ancladas y 12 negativas, con cobertura verificada tras
los chunkers/reglas de admision REALES, no recall medido. Dos erratas verificadas
antes de retrieval se conservan en copia derivada: elipsis no literal de docs-020
y mayuscula inicial de tickets-008. Se deben reportar ambas vistas sobre ambos
brazos; datasets originales y cinco JSON 5.e intactos. Las 29 preguntas posteriores
de bsuite son suplemento, no sustitucion.

- SHA256 instrumento: `19b9526fcb467b6dfe0470dddfe4a818e3deccd27932346c99cbd984145cf815`.
- SHA256 manifiesto de fuentes: `32a535d48b489795b01b4810de7255c2159642315c492647b8b6371dc0863fd8`.
- Cache original abierta solo lectura; backup SQLite consistente y separado,
  hash `654bd7d7b724bc4b52fb8a1fc21a557072927580a53386fb692dbae956810c79`.
  Ningun segundo escritor sobre su WAL.
- Para `qwen3.8-27b-fast`: versiones de prompt `0879c221` / `d6778228`,
  misses **18612 / 13268**, cero hits compatibles. Los prompts solo difieren
  por CRLF/LF; no normalizar claves ni fingir reutilizacion de la cache historica.

**Actualizacion posterior:** el usuario autoriza AltheIA **sin limite** y corpus
completo si es necesario. La consulta de cuota sin respuesta queda como historia,
NO como bloqueo. Primero fixtures pequenos; solo despues aceptacion completa.
Registrar consumo y respetar limites reales, sin eludirlos. El ejemplo declara 1 M tokens diarios,
no una reserva para esta ficha ni cuota disponible verificada. La referencia de
1.7.3 (~670 tokens/chunk) extrapola **21.359.600 tokens solo para resumenes**:
unas 22 ventanas de 1 M si estuvieran completamente disponibles, mas ask,
reintentos y otras comprobaciones. Es estimacion, no consumo medido. No omitir
resumenes/control, acaparar cuota o reutilizar presupuestos de otras fichas.
Las **40 h de maquina** son provisionales y distintas del calendario por cuota;
la aprobacion de tiempo ilimitado permanece, no hay que volver a pedirla.

Herramientas de preparacion compiladas/ejecutadas manualmente en llamadas
separadas de la edicion. No hay producto implementado, servicios iniciados,
ingestas, embeddings/resumenes/generaciones ni aceptacion funcional.
El rector volvio temporalmente a `bloqueado` antes de modificar producto y
despues a `en_curso` con la autorizacion expresa de consumo sin limite.

Comandos de preparacion (NO son `verificacion_comando` de 1.12):

```powershell
# Desde D:\Git\Rag.VectorSpace.Client. Cada salida debe ser nueva.
python docs\eval\quality\1.12\prepare.py --source-parent D:\Git --output .artifacts\1.12-20261103-r3
dotnet run --project docs\eval\quality\1.12\Preparation\Preparation.csproj -p:VectorSpaceRoot=D:\Git\Vector.Space.Engine --no-restore -- .artifacts\1.12-20261103-r3 .artifacts\1.12-20261103-r2\summary-seed.sqlite3 qwen3.8-27b-fast
dotnet run --project docs\eval\quality\1.12\Preparation\Preparation.csproj -p:VectorSpaceRoot=D:\Git\Vector.Space.Engine --no-restore -- --remote-preflight .artifacts\1.12-20261103-r2\remote-preflight.json
```

`--remote-preflight` solo consulta metadatos autorizados; no ejecuta el script
del usuario ni pruebas viejas que apuntan a otra instancia. El proyecto referencia
Core/Infrastructure, NO compila la API de VectorSpace ni copia su appsettings.
Corregir la configuracion del control y sellar su procedencia antes de medir;
los hashes locales historicos no se transfieren al nuevo proveedor remoto.

## Historial hasta 832b31a: autorizaciones y repositorios

Lo que sigue conserva la preparacion anterior. Sus bloqueos/ausencias historicos
quedan actualizados por la seccion vigente y el ledger, no se reabren.

La decision TOTAL de `122755f` no se reabre: VectorSpace sera dueno de
fragmentacion, embeddings, indice y resumen indexado; el cliente conserva API/CLI,
orquestacion, fusion de espacios, salto por simbolos, reranking, contexto y generacion.
No se reduce a sustituir el brain ni se retira el pipeline local.

El usuario aprobo posteriormente **"te apruebo uso de tiempo sin limite"**, solo
para 1.12. Ya no hay bloqueo presupuestario de 4 h. La consulta historica sin
respuesta permanece intacta. La consulta inicial sobre IMPLEMENTAR y ACTIVAR
pruebas aisladas tampoco obtuvo respuesta en esa etapa.

**Actualizacion posterior a `b50eb6b`, 12:21:** el usuario respondio expresamente:

> Autorizo implementar 1.12 en D:\Git\Rag.VectorSpace.Client y ejecutar ingestas y pruebas aisladas, conservando el original y sin tocar colecciones servidas, credenciales, despliegues ni hacer push.

La autorizacion de implementacion y pruebas aisladas **ya existe**: no volver
a pedirla ni presentar la consulta historica sin respuesta como estado actual.
Sigue faltando el protocolo/oraculo previo. Se consulto la propuesta concreta
de ranking y referencia pareada de abajo; el usuario no estuvo disponible.
No se interpreta esa ausencia como aprobacion metodologica ni como revocacion
de la autorizacion de implementacion. No se modifico producto.

Durante esta conversacion el usuario pidio una bifurcacion con Git aparte para
conservar el original y luego autorizo generarla. Se creo:

| Repositorio | Funcion y estado |
|---|---|
| `D:\Git\Vector.Space.Engine` | Ledger rector; base `122755f2c9dd1e45fbac94102a92daba7d7ad07a`. Solo documentacion de 1.12. |
| `D:\git\Rag.Context.Engine` | Original conservado; base `3c3972b90b74ee3d9cd7b86945e07d4775a9c965`. Ninguna edicion ni commit. |
| `D:\Git\Rag.VectorSpace.Client` | Clon local desde esa base, rama `feature/vectorspace-client`, `.git` propio, sin hardlinks/alternates/remotes. Solo instrucciones, README y nota de procedencia en ledger. |

El nombre de carpeta es el propuesto localmente, no un nombre remoto confirmado.
Commit de preparacion del clon: `4e328bc7765033ea2f04a96d163dd1b534a5e017`;
solo AGENTS.md, README.md y nota en el ledger, autor/committer DWeno,
sin coautoria ni firma de herramienta.
Actualizacion de autorizacion y bloqueo metodologico en el clon:
`e7b1b7375a51322d99b7cc8087634cfb7a968603`, los mismos tres archivos;
autor/committer DWeno, mensaje comprobado sin coautoria/firma y arbol limpio.
La cuenta GitHub autenticada es `DanielWueno`; el mensaje escribio `DanielWeno`.
La aclaracion de propietario/destino no obtuvo respuesta. **No se creo un
repositorio remoto ni se hizo push**. No se agregaron colaboradores ni se afirma
que los permisos heredados del filesystem garanticen un unico lector/propietario.
La titularidad personal solicitada se conserva como requisito de una publicacion
futura, no como un hecho remoto ya implementado.

El clon contiene solo el estado commiteado: no los cambios ajenos ni caches/modelos
no versionados. Sus configuraciones heredadas NO autorizan iniciarlo o conectarlo
a servicios. El cambio ajeno de `appsettings.json` de VectorSpace no fue leido
(tampoco para hash), buscado, incluido en diffs ni preparado para commit.
`src\RagEngine.Api\Properties\` del original sigue fuera del trabajo.

## Dependencias y separacion de 19.6

Ambos JSON completos se cargaron/deserializaron y se reviso el inventario de
estados: 38 fichas en VectorSpace y 162 en Rag. Comprobacion directa del ledger,
no inferencia del relevo:

| ID completo | Estado | Relacion |
|---|---|---|
| `1.18-exponer-metadatos-de-chunk-en-busquedas` | hecho | adicional |
| `1.20-busqueda-explicita-sobre-banda-resumen` | hecho | adicional |
| `1.21-decidir-filtro-de-modulo-en-busquedas` | hecho | adicional |
| `1.21.1-implementar-filtro-exacto-de-modulo` | hecho | adicional |
| `1.21.2-preparar-compatibilidad-de-filtros-del-cliente` | hecho | adicional |
| `1.21.4-implementar-filtros-textuales-de-lenguaje-y-namespace` | hecho | adicional |
| `1.21.3-implementar-guardias-y-traduccion-de-filtros-del-cliente` | hecho | adicional y `bloqueado_por` literal |
| `1.22-reparar-dos-pruebas-rojas-preexistentes` | hecho | adicional |
| `1.4-portar-codigo-del-motor` | hecho | prerrequisito de motor |
| `1.19-listar-documentos-indexados-de-una-coleccion` | hecho | insumo, NO pertenece a las ocho adicionales |

El unico `en_curso` de Rag es
`19.6-ci-que-distingue-fallos-reales-de-falta-de-infraestructura`.
Su alcance es CI/categorias xUnit, aislamiento de TracingGenerationTests,
GenerationHttpHarnessTests y documentacion de CI. Sus puertas pendientes necesitan
Actions/push; no se ejecutan aqui. No se reescriben sus fallos/resultados historicos.

1.12 necesitaria composicion/puertos/cliente/CLI y pruebas propias. Se puede separar
de 19.6 en el clon: no modificar `ci.yml`, `TestCategories.cs`, los harness citados
ni su ficha. Las nuevas pruebas tendrian archivos propios, sin reemplazar el
retriever bajo prueba por un fake al probar composicion. Si posteriormente hace
falta cambiar un archivo de 19.6, parar y registrar el conflicto; no resolverlo aqui.
Todas las fichas heredadas del clon quedan intactas como historia, no como agenda
nueva. El original y su ledger permanecen sin cambios.

Las fichas y resultados completos de 1.18/1.20/1.21.1/1.21.4/1.21.3 se
contrastaron con contratos/codigo. Son evidencia historica, no nuevas corridas:
1.18 expuso metadatos y ANY de simbolos; 1.20 persistio texto/espacio resumen;
1.21.1 registro 44 casos y rollback del lector; 1.21.4 registro 87 casos mas
sonda independiente; 1.21.3 registro 48 casos simulados. Sus conteos no son
constantes de aceptacion de 1.12. No se reejecutaron esos items.

## Contrato actual y decisiones que aun faltan

`R\` denota original/clon en la base indicada; `V\`, VectorSpace.

| Componente y evidencia | Tratamiento requerido antes de implementar |
|---|---|
| `R\src\RagEngine.Core\Extensions\ServiceCollectionExtensions.cs:149,174-177,276-277` | Registra brain ONNX, admin/writer Qdrant, retriever Qdrant y pipeline local. La rama remota debe seleccionar explicitamente servicios distintos, sin resolver Qdrant/brain/resumidor local. Conservar rama local de control. |
| `R\src\RagEngine.Core\Infrastructure\VectorSpace\` y tests homonimos | Componente de `3c3972b9` sigue sin registro DI: solo estos archivos y sus tests lo referencian. Devuelve HttpResponseMessage, no implementa ISemanticRetriever ni ingesta/catalogo. Su capacidad textual es declarativa; no prueba un servidor real. |
| `R\src\RagEngine.Api\Program.cs:406-460,693-1025` | Empresarial construye Authorized; actualmente lee manifiesto antes del retrieval. Validar backend+modo desde composicion real ANTES de resolver admin, hacer HTTP, embeddings, reranker o LLM. Tambien CLI, que no dispara ValidateOnStart por construir el contenedor. |
| `R\src\RagEngine.Core\Infrastructure\VectorStore\RetrievalProfileResolver.cs` e `Abstractions\IVectorStoreAdmin.cs` | El perfil de retrieval depende del manifiesto en Qdrant. El catalogo remoto solo entrega perfil de embeddings/modelo/dimension: no es ACL ni manifiesto Rag. Hace falta definir perfil local explicito por coleccion remota y sus capacidades verificadas; no inventar hashes ONNX, fecha, salud o esquema ausentes. |
| `V\...\Models\Responses\ColeccionesResponses.cs` | El hit no trae repository_name ni last_modified exigidos por metadatos locales. Definir representacion de procedencia remota/ausencia sin fabricar valores. Campos historicos null no acreditan cobertura de citas. |
| `R\src\RagEngine.Api\Contracts.cs:54-112` y `Program.cs:561-573` | SourceDto recibe hoy resumen de ISummaryCache. Transportar texto remoto en el resultado hasta search/ask/stream, sin consulta/generacion obligatoria de cache local. Un null remoto sigue null, no se rellena con un resumen de otra procedencia. Preservar redaccion Simple/Technical/prosa. |
| `R\src\RagEngine.Cli\Commands\IngestCommand.cs`, `IngestStatusCommand.cs`, `DoctorCommand.cs` | Orquestar envio de documentos/proyecto, id y estado remoto; catalogo/documentos para descubrimiento. No conservar chunking/embeddings/escritura local en el camino remoto, ni fingir salud ONNX/Qdrant propia. |
| `V\docs\servicio\contrato-api.md` | Documentos sincronos o zip asincrono, estados pendiente/procesando/completada/error y conteos finales. Registro en memoria por instancia: 404 tras reinicio significa estado desconocido, no exito. ResumenesPendientes > 0 impide declarar aceptacion completa aunque estado sea completada. Omitidos y fallos se exponen. No activar origen Git/acceso corporativo por comodidad. |

### Rankings, escalas, pools y umbrales

El retriever local (`R\...\QdrantSemanticRetriever.cs:111-176,303-362,414-551`)
usa dos ramas independientes con RRF nativa, o tres independientes con RRF ponderada
si hay resumen. VectorSpace (`V\...\QdrantSemanticRetriever.cs:112-176`) entrega
**un ranking de codigo** (Denso o FusionNativa, configuracion de instancia) y
**otro denso de resumen**. No expone sparse independiente, modo por solicitud,
umbral por solicitud ni cursor de busqueda.

Por tanto `RRF(RRF(denso,sparse),resumen)` NO reproduce
`RRF_ponderada(denso,sparse,resumen)`. No aplicar automaticamente pesos MiniLM
1/1.3/2.5 a arctic ni etiquetar el resultado como equivalencia algoritmica.
Tambien el salto de simbolos usa codigo: si la instancia esta en FusionNativa,
no es el salto solo denso del origen. Elegir Denso preservaria esa caracteristica
del salto, pero tampoco preserva las tres ramas primarias.

Limites comprobados, no una configuracion candidata aprobada:

| Caso local | Pool calculado | Limite remoto |
|---|---|---|
| Sin rerank | base K; prefetch `max(4K,40)` | 100 resultados por peticion/espacio |
| Con rerank | base 3K; prefetch `max(12K,40)` | Con K=10 ya se requieren 120 por rama local |
| Two-hop sin rerank | final K + MaxExpansionResults; prefetch no se ensancha | K=100 + 20 excede 100; no recortar en silencio |
| Simbolos | hasta 50 nombres, ANY; AND con filtros | 1..50 por peticion; tampoco hay cursor para completar un ranking |

Pedir 100 varias veces no pagina ni recupera el resto. FusionNativa remota
ademas calcula su propio prefetch `max(4*topK,40)`: pedir un pool mayor puede
reordenar el ranking. Esto debe constar como intervencion, no como transporte neutro.

`MinimumSimilarityScore`/MinScore local NO viaja en el componente actual.
VectorSpace fija UmbralSimilitud del codigo en instancia (default 0.10) y
UmbralSimilitudResumen (default 0). No comparar un score RRF contra un coseno,
ni filtrar el ranking fusionado para simular un umbral de su rama densa.
La API local admite TopK 1..100: reducirlo, limitar pools o rechazar MinScore
variable es cambio visible que debe decidirse, no introducirse silenciosamente.

**Decision pendiente concreta:** prerregistrar modo/ramas y fusion del candidato,
pesos y escalas, manejo de umbrales, pools y contrato de entradas no representables.
O bien ampliar explicitamente el contrato necesario dentro del alcance autorizado,
o aceptar una estrategia remota distinta con rechazo visible de entradas no
soportadas y criterio de calidad acordado. La implementacion ya esta autorizada,
pero esta preparacion no elige una variante sin resolver ese oraculo,
ni ejecuta otra ficha para resolverla.

### Restricciones ya decididas

Local explicito; lenguaje/namespace por Match.Text, modulo opcional exacto.
Transmitir restricciones intactas en codigo, resumen y cada salto, antes del
corte/consumo de candidatos. Namespace no se sustituye por modulo o ruta.
Conservar blancos/ausentes segun el traductor y no normalizar valores activos.

Tenant explicito no blanco y TODO Authorized se rechazan antes del transporte.
Empresarial nunca se convierte a Local. 400/401/403/409/503, cancelacion y
fallos de red no permiten quitar filtros, devolver exito vacio ni fallback.
Se conserva JWT HTTP de VectorSpace; pruebas con identidad sintetica no acreditan
login real ni autorizan acceso corporativo.

## Inventario documental del instrumento

Los cinco JSON de `R\docs\eval\quality\5.e\` se cargaron completos. Son salidas
de recall: **no contienen respuestas de /api/ask**. Procedencia comun declarada:
`9a6e005`, `git_dirty=true`, `model_qint8_arm64.onnx`, 384d, ventana 256,
chunking contract 3, prompt `210d3e35`, K=10, sin rerank, min_score=0.1,
pesos 1/1.3/2.5 y k=60. No acreditan modelos/binarios/corpus presentes.

| Archivo 5.e | Filas | SHA256 de bytes actuales |
|---|---:|---|
| bsuite-repo.rebaseline.json | 55 | `bd40cc65d20b7f25f2d5817c94fb70db0e4d3c74871cf6f5f62a51dda89043b4` |
| bsuite-auditorias.rebaseline.json | 16 | `02c0b99f9f1d177384b5c9fe44c4f8504e373bd0ac61b81f617545ff74f116d8` |
| bsuite-auditorias.rebaseline-repro2.json | 16 | `014939ce3a49c41e2844a50afcc9977655a067f420fb39acf215d5532ea4240c` |
| innovapp-docs.rebaseline.json | 26 | `e53aa1a654cff66b9a21f63e7f711a527b02e4f1ebe516be7412d805ac950f41` |
| tickets-microservice.rebaseline.json | 19 | `506bf3885fffd84e40f3c352072c4cf86ded3bb9e28e288f1c41da9227effd0f` |

Eval-sets actuales: 145 filas en cuatro datasets; 133 con anclas textuales y
12 sin ellas. Eso es inventario del archivo, **no n efectivo con anclas validadas
contra fuente e indice**, que sigue pendiente.

| Dataset | Actual con ancla / sin ancla | Filas de 5.e | SHA256 bytes checkout |
|---|---:|---:|---|
| bsuite-repo | 76 / 8 | 55 | `c8626766ffadc8dc5f2f7816921d25c6a7d28510477b58615002318939ea4c11` |
| bsuite-auditorias | 16 / 0 | 16 | `8a322c20b68e76875aa5121ead7c6537d4483ff290be58dbc88689a2ae7b7efa` |
| innovapp-docs | 22 / 4 | 26 | `5642fe0d8a6e3785eecfaba95013015d898f3adb8540a4e8a227135dfc27e5a0` |
| tickets-microservice | 19 / 0 | 19 | `d961a14a3dc08704001ff6db2febdc34f8166e9bd8443c393c8957120b0b8d43` |

La conversion **solo en memoria** CRLF -> LF da respectivamente:

| Dataset | SHA256 LF |
|---|---|
| bsuite-repo | `cdb39feef4ee1a20a4e9fe11912bbe4caf82e1d29a12f09e93198164fb68f6d3` |
| bsuite-auditorias | `ec6ff169d1a63b65cddcd799eacea0ab17770edf42fb2ed8e58ead621cd563ba` |
| innovapp-docs | `1f9571b91552adfa1aa4eca9af92cafe2476a5c0d4d4fd0ea78719d54d85574d` |
| tickets-microservice | `2ae380bb02df6c0fb28aaa3cb3101973a126ba3c1c8b970d0802ad104709bf0c` |

Los ultimos tres prefijos coinciden con `provenance.eval_set_hash` de 5.e.
El del archivo completo bsuite-repo NO: 5.e declara `5c0b6532ac78` y
55 preguntas (47+8); el actual agrega 20 de salto y 9 salto-negativo.
La primera preparacion solo habia comprobado presencia de las preguntas
por texto, no identidad de sus anclas.

**Hallazgo posterior, comprobado sin escribir archivos:** seleccionar los
objetos completos actuales por `Question`, en el orden de `results[].question`
del baseline, recupera exactamente los primeros 55 objetos del eval-set de 84.
Serializar con `json.dumps(cohort, ensure_ascii=False, indent=2) + "\n"`,
codificar UTF-8 y calcular SHA256 produce:

`5c0b6532ac7826f4afd7cbdeefd590a94e44b0cf284614b298b017ce9bc3c08a`.

Coincide con el prefijo historico `5c0b6532ac78`; el baseline no guarda un
hash historico completo. Es evidencia de reconstruccion del instrumento de
55 objetos/anclas, no recuperacion de corpus, indice, modelos o respuestas ask.
No se sustituyeron las 55 preguntas por las 84 ni se editaron anclas/archivos.
La diferencia 55-vs-84 ya no justifica afirmar que la cohorte sea irrecuperable.

`git show 9a6e005:docs/eval/bsuite-auditorias.eval-set.json` fallo:
objeto `9a6e005` inexistente en este checkout, que NO es shallow. No se hizo fetch
ni se asumio que otro commit reproduce ese arbol sucio.

El inventory del gate nocturno ya usa 6.c para bsuite-repo; **no sustituye** la
referencia 5.e exigida por 1.12. `quality-baseline.py` usa otro bundle de preguntas,
solo llama ask y continua ante errores antes de salir 0: tampoco es la aceptacion.
`rag eval --baseline` informa regresiones/incomparabilidad sin fallar por ellas.
Estos hechos impiden registrar cualquiera de esos comandos como cierre completo.

## Protocolo previo: puertas y oraculos

Este es un protocolo de preparacion, **no un experimento congelado ejecutable**.
Faltan decisiones enumeradas abajo; no hay resultados candidatos observados
ni se han elegido umbrales despues de medir.

| Puerta | Oraculo obligatorio y evidencia que debe fallar si falta |
|---|---|
| P0, autorizacion | Implementacion e ingestas/pruebas aisladas en el clon YA aprobadas; tiempo tambien. Concretar recursos aislados y rollback antes de ejecutarlos. El bloqueo actual es metodologico, no falta de esos consentimientos. |
| P1, procedencia | Manifestar ambos commits/builds, archivos sucios permitidos, hashes completos del corpus y de cada documento, anclas/IDs de preguntas, n efectivo, contratos de chunking, imagen/version/esquema de Qdrant, embeddings codigo/resumen, prompts/modelos LLM, reranker/binario/tokenizer, pesos/pools/umbrales/gates y configuracion efectiva sin secretos. Identificar toda diferencia intencional; no exigir igualdad falsa entre MiniLM y arctic. |
| P2, composicion real | Arrancar API mediante su composicion real con Empresarial+remoto: rechazo antes de HTTP/catalogo/consulta, embeddings, reranker y LLM; contadores cero y ningun fallback. Ejercitar tambien CLI/puerto y cada Authorized, incluso admin/sin claims; tenant activo en Local se rechaza. Los tests aislados del traductor no sustituyen esto. |
| P3, filtros/consumo | Positivos L/N/M y ANY de simbolos; negativos de otros lenguajes, namespace ausente/ruta coincidente, fronteras alpha/alpha-private/Alpha, conjuncion imposible; falsos candidatos de mayor score. Mismos filtros en todas las peticiones/saltos; ningun candidato ajeno llega a semillas, reranker, LLM o fuentes. Version/esquema iguales al oraculo textual. |
| P4, contratos | Citacion tecnica/prosa/simple y resumen remoto con cache local vacia e incluso con entrada local contradictoria; no inventar metadatos ausentes. Ranking/desempate/escalas y limites alrededor de cada pool; fallo visible de 400/401/403/409/503/red/cancelacion, sin ampliacion ni resultado parcial exitoso. |
| P5, ingesta | Nueva coleccion aislada con resumen desde creacion; idempotencia, cambio/eliminacion de documento, omitidos, 429/Retry-After, pendiente/procesando/error/completada, reinicio/404, resumen pendiente/sentinel y reanudacion. Verificar conteos y perfil antes de aceptar; ninguna cache local sustituye la fase remota. |
| P6, parejas search/ask | Mismas preguntas, anclas y configuracion no intervenida en ambos brazos; llamar ambos endpoints reales, no solo CLI. Conservar respuestas/citas/rankings/latencias y adjudicacion sustentada por pregunta. Incluir negativos y riesgos, sin descontarlos del conteo de cobertura. Ninguna ausencia o excepcion cuenta como acierto. |
| P7, repetibilidad/rollback | Repetir ambos brazos con orden prerregistrado y control conservado. Cambio de instrumento justificado exige nueva procedencia y ambos brazos, no solo candidato. Probar seleccion explicita del backend local; conservar puntos experimentales separados, no borrarlos como supuesto rollback de codigo. |

**Bloqueo de oraculo, responsable usuario + ejecutor de 1.12:** el criterio literal
de la ficha pide paridad con eval-set/baseline 5.e, pero no fija un margen numerico
para el cambio de algoritmo ni una adjudicacion de ask; 5.e no tiene esas respuestas.
Antes de producto, acordar referencia reconstruible (conservar 5.e como historia
y las 55 preguntas recuperadas), procedimiento de control aislado
y criterio de paridad por pregunta/total/categoria. No importar automaticamente
el +/-1 de olas 5/6 ni el comparador de calibracion 12.10.

### Propuesta concreta consultada tras la autorizacion

**Sin respuesta; no aprobada ni ejecutada.** La consulta no volvio a pedir
implementacion ni tiempo: pidio resolver el cambio algoritmico y la referencia.

- Conservar los cinco JSON 5.e intactos. Cohorte principal de 116 preguntas:
  55 bsuite-repo, 16 auditorias, 26 docs y 19 tickets. Son 104 con anclas textuales
  y 12 negativos, aun no n efectivo validado; repro2 es una replica, no otra cohorte.
  Los 29 casos posteriores serian suplementarios, nunca sustitutos.
- Construir control local aislado desde el original y candidato remoto sobre
  el mismo snapshot. Registrar referencia pareada nueva de search/ask con
  procedencia completa. No presentar 5.e como comparable automaticamente
  ni reemplazarlo sin decision expresa.
- Dos corridas pareadas completas; cero perdidas por pregunta de hit_any@10
  e hit_full@10 frente al control. Ninguna fabricacion nueva ni transicion
  de respuesta correcta a incorrecta/abstencion en ask, con adjudicacion sustentada.
  Reportar tambien @1/3/5/10, categorias y perdidas individuales. Negativos,
  filtros, composicion, errores parciales y rollback obligatorios.
- Candidato inicial propuesto: codigo Denso y resumen separado donde corresponda;
  RRF de esos dos rankings en Rag, pesos 1/1 y k=60 como hipotesis no calibrada;
  pool remoto de 100 por espacio y pool primario de 100. TopK publico 1..100;
  salto por simbolos y reranking en Rag. No equivale a tres ramas y cambia pools.
  MinScore incompatible con el piso configurado del servidor se rechaza,
  no se ignora. Si no supera el criterio, no se activa ni se bajan umbrales.

La alternativa consultada fue preparar explicitamente la ampliacion contractual
si se exige equivalencia algoritmica. Antes de producto siguen pendientes la
resolucion de esta eleccion, escalas/desempates/presupuesto exacto de saltos,
orden de las corridas, rubricado verificable de ask, configuracion y congelacion
de corpus/anclas. Esta propuesta concreta **no es todavia un oraculo completo**.
El labeled-set de abstencion podria ser suplemento, nunca reemplazo silencioso.

Un futuro runner debe fallar con evidencia/corpus/infra ausentes, cero tests,
skips o casos requeridos omitidos, identidad de instrumento distinta y parejas
incompletas. Registrar `verificacion_comando` solo cuando exista y cubra TODO
el criterio. No se crea un comando ficticio ni se modifica el instrumento en
esta fase documental.

## Corpus, misses y horas de maquina

Inventario completado: artefactos de evaluacion anteriores. Tras la autorizacion
se comprobo solo existencia de las tres carpetas locales siguientes, sin leer
sus fuentes, crear snapshots ni consultar red/indices:

| Corpus local | Uso historico, no ingesta ejecutada |
|---|---|
| `D:\Git\BusinessSuite.Xaf` | bsuite-repo completo y tres subcarpetas para auditorias |
| `D:\Git\docs-bsute-innovapp-plan` | innovapp-docs |
| `D:\Git\Reyma.TI.Tickets.Microservice` | micro-repo, eval-set tickets-microservice |

`replicate-env\scripts\04-ingest-collections.sh` del original identifica para
auditorias las subcarpetas `src\Modules\REYMA.XAFR1PV.Compras\Auditorias`,
`src\Modules\REYMA.XAFR1PV.Compras\Compras\Utils` y
`src\Modules\REYMA.XAFR1PV.Base`. Se leyo el procedimiento historico,
**no se ejecuto** ni se comprobaron aun esas subcarpetas. Historial con resumen
en ambos corpus bsuite y sin resumen en docs/tickets: no autoriza omitirlo
del control/candidato donde corresponda.

**No hay inventario completo del corpus:** faltan revisiones/snapshots,
documentos elegibles, hashes, anclas comprobadas y conteos de ambos contratos.
La presencia de carpetas no prueba identidad con 5.e ni cobertura del indice.
No se accedio a otros repositorios corporativos por red ni a colecciones servidas.

Se encontro por nombre la cache heredada y archivos WAL/SHM en
`R\replicate-env\data\`; no se abrio, copio ni conto su contenido. Su presencia
no prueba que haya un escritor activo ni permite una copia consistente por
copiar solo el .sqlite3.

Antes de ingerir: inventariar documentos elegibles y chunks/hash por cada
contrato; fijar prompt_version/modelo; contar claves unicas y misses
`(content_hash,prompt_version)` contra cache aislada consistente. El sentinel
es acierto de cache, el error no. La cache del servidor es en memoria y su clave
incluye modelo: no asumir que la SQLite de Rag evita sus llamadas LLM.
No existe un conteo de misses realizado en esta conversacion.

| Partida | Estimacion/conteo actual |
|---|---|
| Preparacion documental y separacion Git | 0.25 h estimadas, no cronometraje de maquina |
| Continuacion documental tras aprobacion de implementacion | 0.1 h adicionales estimadas, no cronometraje ni consumo de tokens medido |
| Reserva tecnica historica | 4 h numericas provisionales: 1 pruebas + 1.42 embedding/upsert candidato + 1 eval + 0.58 margen; incompleta |
| Reconstruccion y embeddings del control | Pendiente de corpus/modelos/configuracion; NO cero |
| Embeddings codigo/resumen del candidato | Recontar por contrato y ambas bandas; 1.42 h es referencia historica, no medida vigente |
| Resumenes control/candidato | Misses, tokens y throughput separados pendientes; 19 h es referencia historica de regeneracion total, no presupuesto universal |
| Consumo ejecutado aqui | 0 ingestas/consultas a indices/embeddings/resumenes/generaciones LLM; 0 servicios iniciados |

Sumar tiempos de reconstruccion, embeddings, resumenes, evaluacion y rollback
para AMBOS brazos; registrar conteos/tokens/tiempos reales. No omitir resumenes o
control para encajar en 4 h ni pedir mas aprobacion solo por duracion.

## Aislamiento, rollback y reentrada

Colecciones exclusivamente de prueba, identificadas por corrida, con caches
SQLite/auditoria/estado separadas por brazo y escritor. No iniciar el compose
heredado contra volumenes/caches compartidos. Nunca dos escritores host/contenedor
en el mismo WAL; si un procedimiento necesita parar un consumidor compartido,
su impacto necesita autorizacion especifica, no se lo detiene automaticamente.

Rollback actual: revertir solo los commits documentales de 1.12 en sus
repositorios respectivos. Original Rag sin cambios; no borrar el clon.
Rollback futuro: seleccionar backend local conservado, revertir commits de
composicion y restaurar configuracion/perfiles del control como unidad.
Un error remoto nunca selecciona local automaticamente. Revertir codigo no
revierte puntos ya escritos; no modificar/eliminar colecciones servidas.

Reentrada: NO volver a pedir implementacion/activacion ni tiempo; resolver
la propuesta metodologica sin respuesta, cerrar ranking/contrato y oraculo
pareado; inventariar/congelar los corpus locales y contar misses;
congelar ambos brazos y recursos aislados antes de marcar en_curso. Publicacion
GitHub requiere propietario/destino confirmados y autorizacion separada; sin push.
`1.21.5-definir-tenencia-del-cliente` y
`1.21.6-definir-autorizacion-del-cliente` siguen bloqueadas, sin peticion expresa.
No hay otra ficha autorizada y ejecutable para sustituir esta preparacion.

Comprobaciones documentales y commits se registran en `resultado` del ledger.
`arnes validar` comprueba fichas, no ejecuta la aceptacion de arriba.
