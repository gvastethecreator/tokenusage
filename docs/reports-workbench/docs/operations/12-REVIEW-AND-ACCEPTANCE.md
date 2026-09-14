# Revisión adicional · omisiones y aceptación adversarial

**Estado:** decisiones y casos propuestos; no fallos de producción reproducidos. Complementa P0–P4 sin afirmar que los colectores actuales incumplan estos contratos. Los IDs `REQ-INT-*` y `TEST-INT-*` son referencias documentales, no tickets reales.

## 1. Matriz adicional de revisión

| Requisito | Fase | Caso de aceptación pendiente | Riesgo controlado |
|---|---|---|---|
| REQ-INT-01 | P1 | TEST-INT-01: cambiar Markdown y ejecutar `build_reader.py --check` debe fallar. | El HTML deja de describir la especificación fuente. |
| REQ-INT-02 | P0 | TEST-INT-02: una etiqueta de modelo cambia sin cambiar ID; el filtro guardado sigue resolviendo el ID. | Alias visuales convierten una comparación en otra población. |
| REQ-INT-03 | P2 | TEST-INT-03: reconciliar una raíz no elimina observaciones de otra instancia del mismo agente. | Borrado entre fuentes durante snapshot replacement. |
| REQ-INT-04 | P2 | TEST-INT-04: GUI y CLI intentan migrar la base a la vez; hay un escritor autorizado y el resto reintenta con límite. | Una coordinación sólo en memoria no protege procesos separados. |
| REQ-INT-05 | P2 | TEST-INT-05: expira detalle mientras un informe conserva un snapshot; no se mezclan rollups nuevos con eventos viejos. | Totales internamente inconsistentes. |
| REQ-INT-06 | P3 | TEST-INT-06: revocar consentimiento durante export cancela material no publicado y no recrea metadatos al reintentar. | Fuga de atribución por una tarea pendiente. |
| REQ-INT-07 | P3 | TEST-INT-07: una sesión atraviesa medianoche; unión de dos días devuelve una sesión distinta, no dos. | Contadores de sesiones no aditivos. |
| REQ-INT-08 | P4 | TEST-INT-08: cuota de alcance ambiguo no se atribuye a un modelo o cuenta por coincidencia temporal. | Normalización inventada y mezcla de pools. |
| REQ-INT-09 | P4 | TEST-INT-09: HTML/CSV reciben valores sintéticos maliciosos; se conservan como datos y no ejecutan acciones. | Inyección de documentos y fórmulas. |
| REQ-INT-10 | P2 | TEST-INT-10: runtime nativo desconocido o afectado por WAL-reset no pasa el gate de nueva persistencia. | Basar compatibilidad sólo en la versión del paquete NuGet. |
| REQ-INT-11 | P4 | TEST-INT-11: un subtotal truncado no se exporta como total completo; conserva límites y exclusiones. | UI resumida y archivo completo con significados distintos. |
| REQ-INT-12 | P1 | TEST-INT-12: escanear una fuente falla; los datos retenidos no reciben una falsa fecha de lectura satisfactoria. | Confundir «intentado» con «observado» o «vigente». |

Los casos de producto permanecen `planned_not_executed_in_product`. Una comprobación del renderer documental con el caso 01 no acredita un test del Model Explorer de WinUI.

## 2. Sumas que no se pueden trasladar entre niveles

Tokens normalizados y costos bajo una base explícita pueden ser aditivos. Sesiones distintas, proyectos distintos, días activos y percentiles no se suman en general entre grupos o fechas. Definir `sessionsStarted` por separado de `sessionsActive` y `distinctSessionsInRange`; documentar cuál muestra cada tarjeta.

Cuando expiran las claves necesarias, un rollup diario no permite reconstruir exactamente la unión de sesiones de dos días. Conservar una estructura admitida por privacidad, limitar la métrica al horizonte de detalle o declararla no disponible. No inventar exactitud ni introducir una estructura probabilística sin documentar su error y revisar su impacto de privacidad.

La agrupación Top N debe incluir Other y Unknown cuando corresponda, con población explícita. Una lista paginada y un total no deben depender del número de filas cargadas en la UI. El denominador de un porcentaje debe quedar fijado: selección completa, modelo o página no son intercambiables.

## 3. Concurrencia entre procesos y archivos

El bloqueo de un ViewModel no coordina la GUI con `tokenusage refresh`. El gate requiere caracterizar todos los escritores admitidos, incluidas instancias portables. Limitar reintentos y tiempos de espera; conservar una respuesta de error visible sin reiniciar silenciosamente el almacén.

SQLite WAL no ofrece una transacción atómica de conjunto para varias bases adjuntas. Si cuotas, configuración, permisos y uso viven en archivos diferentes, «un snapshot» necesita una definición realizable: una transacción para cada base y un vector explícito de revisiones, o un mecanismo de snapshot coordinado. No declarar atomicidad global sólo por usar WAL. [SQLite WAL](https://www.sqlite.org/wal.html).

Una purga o migración debe controlar procesos concurrentes y sus lectores, sin terminar aplicaciones de terceros. No borrar checkpoints ajenos ni cambiar el modo de journal de bases de proveedores.

## 4. Cancelación real y límites de carga

Microsoft.Data.Sqlite ejecuta de manera síncrona sus métodos ADO.NET Async; un nombre Async no prueba ausencia de bloqueos. El plan debe medir cancelación a la profundidad real de consulta/lectura y no sólo la cancelación de publicación del resultado. [Microsoft Learn](https://learn.microsoft.com/en-us/dotnet/standard/data/sqlite/async).

Separar cancelación del trabajo, invalidación del resultado y liberación de recursos. Verificar que una respuesta vieja no gane la carrera, que al cerrar una ventana se liberen lectores y que un escaneo truncado retenga el último estado fiable sin promoverlo a Complete.

## 5. Revisión de UI y exports

La presentación puede abreviar cifras; los exports preservan valores exactos, unidades y método. Un selector desconocido no se convierte en el valor Standard. El cambio de etiqueta visible no cambia la identidad de filtros ni snapshots. El historial de resultados mantiene la versión de definición que lo produjo.

En CSV separar los campos textuales sujetos a neutralización de los numéricos serializados bajo un contrato. En HTML escapar contenido, no renderizar instrucciones de proveedor y no incluir scripts remotos. Probar estos comportamientos con canarios sintéticos; no pegar conversaciones o bases reales en fixtures.

## 6. Revisión de publicación y mantenimiento

El número de documentos y fuentes se deriva del inventario, no de un literal fijo que pueda quedar viejo. El HTML generado tiene un modo de comprobación y el manifiesto verifica hashes de los archivos incluidos. La evidencia lleva alcance y fecha, y distingue resultados del lector, del paquete y del producto.

Mantener la edición inicial como archivo histórico evita perder investigación; también exige señalar sus correcciones. Nunca presentar el diagnóstico algebraico inicial como un bug demostrado de todos los informes: P0 describe la cohorte fija de Rates y P4 la futura descomposición histórica.

## 7. Decisiones que no deben resolverse por accidente

Antes de implementar, asignar responsable a la política de sesiones distintas después de retención, separación de instancias, definición de snapshot multifichero, plazo máximo de cancelación, cambios de idioma visible, duración de evidencias y acceso público a los archivos históricos. Documentar la decisión en la tarea correspondiente. No ampliar todos esos temas en un único PR de código.
