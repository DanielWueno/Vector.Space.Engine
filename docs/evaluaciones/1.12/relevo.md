# Prompt de relevo - solo VectorSpace/1.12

Copia el texto siguiente en una conversacion nueva:

```text
Retoma unicamente 1.12-decidir-si-rag-context-engine-pasa-a-cliente de
Vector.Space.Engine. Sin subagentes, una conversacion/un item.
No ejecutes ninguna otra ficha, tampoco las heredadas en la bifurcacion.

REPOSITORIOS
- D:\Git\Vector.Space.Engine
  Ledger rector: docs\plan\ejecucion-plan.estado.json.
- D:\git\Rag.Context.Engine
  Original conservado como motor local; solo lectura.
  Ledger: docs\analisis-futuro\ejecucion-plan.estado.json.
- D:\Git\Rag.VectorSpace.Client
  Repositorio Git independiente creado por peticion posterior del usuario,
  desde 3c3972b90b74ee3d9cd7b86945e07d4775a9c965.
  Rama feature/vectorspace-client; no worktree, no alternates, sin remote/push.
  Preparacion documental commiteada en
  4e328bc7765033ea2f04a96d163dd1b534a5e017 (sin cambios de producto).
  Autorizacion expresa y bloqueo metodologico registrados despues en
  e7b1b7375a51322d99b7cc8087634cfb7a968603, tambien solo documentacion.
  Consultar status/log actual y el resultado del ledger rector.
  Ledger heredado: docs\analisis-futuro\ejecucion-plan.estado.json.
  La nota _bifurcacion_1_12 conserva procedencia; las 162 fichas heredadas no
  son una agenda nueva. La unica fuente de avance de 1.12 sigue en VectorSpace.

Lee las instrucciones de los tres repositorios, ambos ledgers de origen
completos y la nota del clon. Lee completa 1.12 y sus fuentes, incluidas
_decision_alcance_2026_11_03, _decision_compatibilidad_2026_11_03,
_solicitud_aprobacion_2026_11_03 y las actualizaciones posteriores a 122755f.
En particular, leer _autorizacion_implementacion_posterior_b50eb6b,
_propuesta_oraculo_posterior_b50eb6b y
_instrumento_recuperado_posterior_b50eb6b. Las solicitudes anteriores sin
respuesta son historia; NO describen la autorizacion actual.
Lee docs\evaluaciones\1.12\preparacion.md de VectorSpace; sus propuestas no
son un protocolo experimental completamente congelado ni aceptacion funcional.
Comprueba status/diff y staged de los tres repositorios, preservando cambios ajenos.

PROHIBICIONES
En VectorSpace NO leas, busques, hashees, modifiques ni incluyas en diff/commit:
src\Reyma.VectorSpace.Engine.Microservice.Api\appsettings.json.
Tiene un cambio ajeno. Excluyelo de las operaciones; no uses git add -A.
Preserva src\RagEngine.Api\Properties\ sin seguimiento en el original Rag:
no pertenece a 1.12 y no se copio al clon.
Sin colecciones servidas, despliegues, cambios de credenciales/permisos,
acceso corporativo adicional ni push. No inicies servicios/configuracion
heredados del clon: podrian apuntar a recursos compartidos.

DECISION Y AUTORIZACIONES
122755f decidio alcance TOTAL, no solo brain. VectorSpace sera dueno de
fragmentacion, embeddings, indice y resumen indexado. El cliente conserva
API/CLI, fusion/orquestacion, salto por simbolos, reranking, contexto,
generacion y fuentes. La bifurcacion conserva el proyecto original; no cambia
esa decision ni demuestra que el cliente este implementado.

Tiempo: el usuario aprobo el 2026-11-03 "te apruebo uso de tiempo sin limite",
solo para 1.12. Ya registrado como actualizacion posterior a 122755f.
No volver a pedir aprobacion solo por duracion. Mantener estimacion numerica,
inventario, misses, horas/tokens reales de ambas ramas. Las 4 h del ledger
son una estimacion historica incompleta, no limite ni bloqueo.

Alcance YA APROBADO, 2026-11-03 a las 12:21, posterior a b50eb6b:
"Autorizo implementar 1.12 en D:\Git\Rag.VectorSpace.Client y ejecutar ingestas
y pruebas aisladas, conservando el original y sin tocar colecciones servidas,
credenciales, despliegues ni hacer push."
No volver a pedir esta autorizacion. No se implemento todavia: se consulto
una propuesta concreta para resolver el ranking diferente y la referencia
pareada nueva, y el usuario no estuvo disponible. El bloqueo restante es
metodologico, NO presupuestario ni ausencia de aprobacion de implementacion.
Resolver ese oraculo y la preparacion antes de marcar en_curso/tocar producto.

GitHub: no se creo repositorio remoto. El usuario pidio titularidad personal
unica y escribio DanielWeno; gh api user mostro DanielWueno. La aclaracion
no obtuvo respuesta. Confirmar propietario/destino antes de crear remoto;
no interpretar Git local como garantia de permisos exclusivos. Sin push.

DEPENDENCIAS: verificar IDs completos y estado por lectura actual.
Las ocho adicionales estaban hechas:
1.18-exponer-metadatos-de-chunk-en-busquedas
1.20-busqueda-explicita-sobre-banda-resumen
1.21-decidir-filtro-de-modulo-en-busquedas
1.21.1-implementar-filtro-exacto-de-modulo
1.21.2-preparar-compatibilidad-de-filtros-del-cliente
1.21.4-implementar-filtros-textuales-de-lenguaje-y-namespace
1.21.3-implementar-guardias-y-traduccion-de-filtros-del-cliente
1.22-reparar-dos-pruebas-rojas-preexistentes
Tambien 1.4-portar-codigo-del-motor y
1.19-listar-documentos-indexados-de-una-coleccion estaban hechos.
1.19 NO pertenece a las ocho adicionales. bloqueado_por sigue apuntando a
1.21.3 hecha: no es el bloqueo actual.

Rag solo tenia en_curso
19.6-ci-que-distingue-fallos-reales-de-falta-de-infraestructura.
No ejecutarlo/cerrarlo ni arreglar su CI/categorias/TracingGenerationTests/
GenerationHttpHarnessTests. En el clon conservar su historia intacta y usar
pruebas propias de 1.12; si es imposible separar archivos, parar/documentar.
El componente de filtros de 3c3972b9 sigue sin DI y no implementa retrieval
completo: comprobar que no haya cambiado, sin confundir sus tests con
composicion real o aceptacion de 1.12.

LECTURAS TECNICAS
VectorSpace: docs\evaluaciones\1.21\compatibilidad-cliente.md,
docs\servicio\filtros.md, docs\servicio\contrato-api.md,
Business\Models\Requests\ColeccionesRequests.cs y
Business\Models\Responses\ColeccionesResponses.cs bajo src\
Reyma.VectorSpace.Engine.Microservice.Business;
fichas/evidencia de 1.18, 1.20, 1.21.1, 1.21.4 y 1.21.3.
Rag/clon: Api\Contracts.cs (SourceDto), Api\Program.cs, Core\
Abstractions\ISemanticRetriever.cs e IVectorStoreAdmin.cs,
Core\Infrastructure\VectorStore\QdrantSemanticRetriever.cs y
RetrievalProfileResolver.cs, Core\Infrastructure\VectorSpace\,
Core\Extensions\ServiceCollectionExtensions.cs, tests\
RagEngine.Core.Tests\VectorSpace\, docs\eval\README.md y
docs\eval\quality\5.e\. Las rutas de producto comienzan en src\RagEngine.*.

PREPARACION PENDIENTE, ANTES DE PRODUCTO
1. Fijar rankings/escalas/umbrales/pools. VectorSpace ofrece codigo Denso O
   FusionNativa segun instancia y resumen separado, no los tres rankings
   independientes de RRF ponderada local. El salto de simbolos tambien
   sigue el modo de codigo. topK remoto <=100 sin cursor; K=10 con rerank
   requiere prefetch local 120. MinScore no viaja hoy en el cliente.
   No recortar pools, cambiar escalas, quitar sparse/resumen ni prometer
   equivalencia sin decision explicita y criterio previo.
2. Resolver perfiles/manifiestos sin Qdrant propio, metadatos remotos
   ausentes, ingesta/estado (incluido estado desconocido tras reinicio),
   resumenes pendientes y traslado del texto remoto a SourceDto sin cache
   local. No inventar salud, hashes o metadatos ni esconder parciales.
3. Mantener lenguaje/namespace textuales y modulo opcional con restricciones
   en codigo/resumen/cada salto antes de semillas/reranker/LLM/fuentes.
   Rechazar tenant explicito activo y TODO Authorized. No convertir
   Empresarial a Local ni quitar filtros/claims; mantener JWT de VectorSpace.
4. Probar composicion REAL API y CLI: Empresarial+remoto incompatible
   falla antes de catalogo/consulta/embeddings/reranker/LLM, contadores cero,
   sin fallback. Los tests existentes del traductor no acreditan esto.
5. Resolver oraculo y baseline: 5.e contiene recall, NO respuestas ask.
   bsuite-repo 5.e tiene 55 preguntas (47+8); actual 84 (76+8).
   Las 55 se reconstruyeron EN MEMORIA por Question, en el orden de
   results[].question del baseline, con objetos/anclas completos:
   son los primeros 55 del eval-set actual. json.dumps(cohort,
   ensure_ascii=False, indent=2) + "\n", UTF-8, da SHA256
   5c0b6532ac7826f4afd7cbdeefd590a94e44b0cf284614b298b017ce9bc3c08a.
   Coincide con el prefijo historico 5c0b6532ac78; no se dispone del
   SHA256 historico completo. No afirmar que la cohorte es irrecuperable.
   No se editaron datasets/baselines ni se recupero corpus/indice/modelos.
   9a6e005 no existe como objeto local y el repo no es shallow.
   En los otros tres datasets el hash LF coincide con el prefijo historico;
   los bytes Windows CRLF difieren, registrado sin editar los archivos.
   No sustituir por 6.c/nightly ni importar +/-1 de otras olas.
   Acordar criterio de paridad, referencia reconstruible y adjudicacion ask;
   si no es inequivoco, seguir preparando sin implementar.
   PROPUESTA CONSULTADA SIN RESPUESTA, no decision aprobada:
   - 116 preguntas principales de 5.e: 55/16/26/19, 104 con anclas
     textuales y 12 negativos; repro2 no duplica casos. Las 29 posteriores
     son suplemento, no sustitucion.
   - Control local aislado del original y candidato sobre mismo snapshot;
     nueva referencia pareada search/ask, conservando 5.e historico sin
     afirmar comparabilidad automatica ni sustituirlo silenciosamente.
   - Dos corridas completas; cero perdidas POR PREGUNTA de hit_any@10 y
     hit_full@10; ninguna fabricacion nueva ni correcta -> incorrecta/
     abstencion en ask. Adjudicacion sustentada, no solo scores; negativos,
     riesgos, filtros, composicion, parciales y rollback obligatorios.
   - Denso codigo y resumen separado, RRF de dos rankings pesos 1/1,
     k=60, pool remoto 100 por espacio y primario 100; TopK publico
     1..100, saltos y reranking Rag. Hipotesis no calibrada y NO equivalente
     a tres ramas. Rechazo visible de MinScore incompatible con piso
     servidor. Si falla, no bajar umbrales ni activar.
   Alternativa consultada: preparar ampliacion contractual si se exige
   equivalencia. Faltan resolver eleccion, escalas/desempates/presupuesto
   de saltos, orden de corridas, rubricado ask y congelacion de entradas.
   No presentar esta propuesta como oraculo completo o ya aceptado.
6. Congelar corpus/hash por documento, anclas/n efectivo, modelos/binarios,
   prompts, versiones/esquemas y configuracion de ambos brazos. Datos
   ausentes no son evidencia. El inventario documental tiene 145 preguntas,
   133 con anclas/12 sin ellas; NO es n efectivo verificado contra indices.
7. Inventariar corpus autorizado y contar misses (content_hash,prompt_version)
   antes de ingerir, con caches consistentes separadas. No se conto la cache
   heredada ni se abrio su WAL. Cache remota es en memoria e incluye modelo;
   no asumir reutilizacion de SQLite. Presupuestar embeddings, resumenes,
   reconstruccion del control y evaluacion de ambos brazos por separado.
   Existen D:\Git\BusinessSuite.Xaf, D:\Git\docs-bsute-innovapp-plan y
   D:\Git\Reyma.TI.Tickets.Microservice (solo Test-Path, no lectura de fuentes).
   El script historico replicate-env\scripts\04-ingest-collections.sh
   identifica las tres subcarpetas de auditorias; NO ejecutarlo contra
   nombres servidos. Faltan snapshot/hash/anclas/chunks/misses: existencia
   no prueba disponibilidad de las anclas ni identidad con 5.e.

ACEPTACION Y ROLLBACK
Parear /api/search y /api/ask reales con mismo eval-set/baseline acordados,
negativos y riesgos. Conservar perdidas individuales, respuestas y procedencia.
No cambiar corpus/anclas/umbrales tras medir. Si cambia justificadamente el
instrumento, registrar y medir AMBOS brazos. No omitir resumenes o control.
Solo colecciones/caches de prueba por brazo; nunca dos escritores sobre WAL.
Conservar backend e indices locales: rollback por seleccion explicita y
reversion de commits de composicion, nunca fallback automatico. Revertir
codigo/bandera no revierte datos indexados.

No existe aun verificacion_comando de aceptacion completa: no inventarlo.
quality-baseline.py usa otro bundle y continua ante errores; rag eval
--baseline informa regresiones sin fallar. No usarlos solos como cierre.
El futuro runner debe fallar por falta de infraestructura/evidencia,
cero tests, omisiones, cobertura incompleta y parejas no comparables.
Ejecutar aceptacion en llamada separada de edicion y registrar salida/limites.
arnes validar solo comprueba ficha, no aceptacion funcional.

COMMIT Y CIERRE
Anunciar ID/alcance/modelo y esfuerzo efectivos (no inventar esfuerzo) y
horas_maquina. Registrar procedencia en _ejecucion sin cambiar opus/high
del plugin. Un item; preservar notas/resultados historicos.
Commits coordinados solo de 1.12 en VectorSpace y el clon, con sus ledgers
y resultados; original Rag sin cambios. Autor y committer DWeno,
sin Co-authored-by ni firmas de herramientas, sin amend ni push.
Tras cada commit comprobar git log -1 --format=%B y archivos incluidos.
No marcar hecho resultado parcial. Entregar evidencia, limites, commits
y prompt completo para la siguiente conversacion.

No hay siguiente item autorizado y ejecutable de esta tarea:
1.21.5-definir-tenencia-del-cliente y
1.21.6-definir-autorizacion-del-cliente siguen condicionadas a peticion
expresa y sus entradas, no autorizadas por tiempo ilimitado de 1.12.
El siguiente trabajo es desbloqueo/preparacion de ESTA ficha.
```
