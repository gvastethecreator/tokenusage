# Baseline técnico · hechos, hipótesis y correcciones

**Fecha de inspección:** 12/09/2026. **Commit revalidado:** `df50c367b083e5624bf09213eddc7013d8299aa4`. **Método:** lectura estática a través de GitHub, del RFC original y de documentación oficial. No hubo compilación, ejecución WinUI, benchmark Windows ni inspección de datos personales.

Los IDs TU-* y EXT-* se resuelven en [Fuentes](references/10-SOURCES.md). Un árbol de archivos prueba existencia, no comportamiento. Una función leída prueba su implementación en ese commit, no que todas sus rutas se ejecuten correctamente en producción.

## 1. Base que debe preservarse

| Área | Verificado | Consecuencia |
|---|---|---|
| Dominio | `UsageEvent` separa agente, proveedor del modelo, modelo, observado, tokens, costo, parser y cobertura. [TU-02] | Extender sin crear otra ontología incompatible. |
| Tiempo | `UsageTimePrecision` contiene Unknown/Timestamp/Interval/Daily. [TU-02] | No confundir granularidad con precisión. |
| Recolección | Snapshot y snapshot por ventana son contratos actuales. [TU-03] | No sustituirlos por append-only universal. |
| Persistencia | `CurrentSchemaVersion = 5`; apertura read-only y excepciones de versión. [TU-08] | Evolución por migración y validación de versión. |
| Eventos y rollups | La ingesta aplica los rollups a eventos insertados dentro de la transacción. [TU-08] | Preservar atomicidad en revisiones y metadata nueva. |
| Consultas | `UsageReportQuery.ReadAsync` usa rollups; `ReadExactAsync` filtra tiempo y evidencia. [TU-07] | Reutilizar servicios y añadir unidad de lectura coherente. |
| Informe | Ya tiene filtros, comparaciones, modelos, perfiles y detalles de evidencia. [TU-05/06] | Mejorar navegación y profundidad, no venderlo como creación desde cero. |
| Retención | Matriz documenta 400 días raw, rollups durables, checkpoint Codex 35 días y journal de cuota acotado. [TU-10] | Retención granular es distinta de historia de totales. |
| Privacidad | No recopilar intencionalmente prompts, conversaciones, comandos ni credenciales ajenas. [TU-11] | P3 necesita decisión explícita de metadata. |
| Verificación | El proyecto pide prueba en la frontera fiable más barata y evidencia Windows para UI/fuentes. [TU-12] | No inflar suites ni tratar mocks como prueba universal. |

## 2. Matiz del hallazgo de SplitKnownCost

La fórmula leída en `UsageComparison.cs` calcula volumen, un residual de mezcla y una diferencia de costo efectivo. Algebraicamente, cuando ambos volúmenes son positivos, el residual mezcla se anula. Eso sigue siendo cierto. [TU-04]

La inspección ampliada localizó el llamador en `CreateMeasurementRows` de `UsageReportViewModel.Comparison.cs`: se invoca cuando `IsCompareRatesAxis`. `ApplyRatesComparisonAsync` toma `_globalReport` como cohorte y la revaloriza a dos fechas. [TU-05]

**Corrección del encuadre:** no hay evidencia en esta revisión para afirmar que ese método de tres factores se use en todas las comparaciones de semanas, modelos o proveedores. El ejemplo previo de cambiar de modelo ilustra por qué la fórmula no sirve como descomposición histórica general, pero no reproduce por sí solo el flujo actual de Rates.

**Riesgo específico:** `UsageReferencePricing.Apply` calcula elegibilidad para cada precio, conserva tokens no valorizables y agrega por modelo/día. Dos fechas pueden valorizar poblaciones distintas. [TU-09] Presentar la diferencia resultante como tarifa requiere controlar identidades, revisiones, componentes y reglas de pricing comunes. Igual número de tokens no demuestra igual cohorte.

**Propuesta P0:** valorizar la intersección idéntica y explicitar exclusiones; no convertir cambios de cobertura en volumen o tarifa. Volumen y mezcla son `NotApplicable` en ese experimento fijo; una descomposición histórica real se implementa sólo en P4. Los informes antiguos no se reescriben ni reciben un método nuevo por inferencia.

## 3. Observaciones adicionales para el plan

### 3.1 Disponibilidad de componentes

Los cinco campos de `TokenBreakdown` son enteros no negativos y su total los suma. [TU-02] Esto no prueba duplicación en colectores; exige verificar que cada fuente produzca categorías disjuntas. Tampoco demuestra que cada cero sea un cero observado: el nuevo contrato necesita disponibilidad por componente sin alterar retroactivamente el v1.

### 3.2 Granularidad distinta de timestamp

`UsageReportQuery` y el repricing usan precisión para elegir registros. [TU-07/09] P2 debe incorporar evidencia de si se trata de solicitud final, delta o snapshot. No se publican latencia ni percentiles por llamada a partir de un campo temporal aislado.

### 3.3 Varias consultas, misma revisión

`ReadAsync` solicita rollups, versiones, cuenta y estado de recolección a través del repositorio. Los métodos inspeccionados abren conexiones propias. [TU-07/08] Esta revisión no reproduce una carrera; identifica una frontera a fortalecer: el resultado compuesto debería pertenecer a una revisión coherente, especialmente al exportar o comparar durante un refresh.

### 3.4 Versiones con retención

`ReadReportVersionsAsync` obtiene versiones desde `usage_event`. [TU-13] Si sobreviven rollups pero expiraron eventos, no se puede deducir de esas filas ausentes toda la procedencia histórica. Propuesta: manifestar la limitación o preservar referencias agregadas antes de expirar detalle. Nunca completar versiones históricas desconocidas con la versión actual.

### 3.5 Concurrencia y UI

En la comparación existen operaciones envueltas en `Task.Run`; por eso no se afirma que toda la UI esté haciendo I/O síncrono. [TU-05] Microsoft.Data.Sqlite documenta que sus operaciones Async no realizan I/O asíncrono. [EXT-01] La evolución necesita una política central de ejecución y medir la capacidad de cancelación real, no añadir Async a los nombres.

### 3.6 Fuente Codex

`CodexUsageEventSource` implementa reconciliación por ventana y mantiene `codex-jsonl/9`, límites de lectura y checkpoint opcional. [TU-14] No se auditaron todas las clases parciales ni se observó una instalación. El piloto P2 debe caracterizar esas rutas antes de afirmar que puede identificar cada llamada final.

## 4. Lista de inspección para quien implemente

Leer versiones actuales de los archivos TU-*; buscar referencias a `SplitKnownCost`, `UsageReferencePricing.Apply`, `QueryUsageEventsAsync`, `ReconcileAgentEventRangeAsync`, `SavedUsageComparison` y todos los serializadores de `tokenusage.report.v1`. Localizar los tests existentes por símbolo y reutilizarlos. Los nombres de nuevas pruebas del paquete son casos propuestos, no archivos ya presentes.

Antes de modificar múltiples raíces, inspeccionar el cuerpo completo de reconciliación y tombstones: el API actual está orientado por agente/parser/ventana, y una ampliación por instancia debe impedir que una raíz borre o oculte otra. No basta añadir una columna sin ajustar la autoridad del reemplazo.

Antes de un cambio de pricing, revisar catálogos actuales y `docs/PRICING.md`. Esa documentación admite tier ausente como estimación bajo una limitación explícita; no equivale a prueba de Standard. [TU-15] P0 no altera silenciosamente el costo histórico por esa observación. Los escenarios nuevos muestran el supuesto o lo excluyen, con una política versionada.

## 5. Fuera del alcance verificado

No están probadas aquí la detección real de proveedores, exactitud de sus formatos actuales, presencia de suscripciones del usuario, rendimiento, accesibilidad empaquetada, compatibilidad ARM64 ni corrección de todos los tests existentes. No se revisaron PRs abiertos como parte de esta ampliación. Los contratos externos se vuelven a verificar al integrar cada fuente.

Las verificaciones del paquete sólo validan documentos y ejemplos. Mantener esta separación en la descripción de cada PR evita que una buena especificación se transforme en una falsa declaración de implementación.
