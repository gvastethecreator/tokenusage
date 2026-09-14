# P0 · Exactitud contable y comparación Rates

**Prioridad:** primera entrega. **Estado:** especificación. **Entrada:** baseline revalidado, casos sintéticos y contratos v1 inventariados. **Salida:** Rates no confunde precio con cobertura; las regresiones contables tienen evidencia. **Fuentes:** TU-02/04/05/07/09/12/15 en [Fuentes](../references/10-SOURCES.md).

## 1. Qué se corrige y qué no

El problema no es que TokenUsage carezca de comparación. Existe un eje Rates que revaloriza la misma cohorte a dos fechas de catálogo. La fórmula genérica `SplitKnownCost` no identifica una mezcla histórica, y los resultados de repricing pueden tener elegibilidad distinta entre fechas. P0 debe establecer el significado de ese experimento antes de mostrar atribuciones de cambio.

No se reescriben eventos, catálogos, cuotas, resets, ni todas las comparaciones. No se incorpora una tarifa actual al histórico por defecto. Tampoco se declara un bug en cada parser sin fixture que lo demuestre.

## 2. Recorrido de código a inspeccionar

`UsageReportViewModel.Comparison.cs` → `ApplyRatesComparisonAsync` → `RepriceAtAsync` → `UsageReferencePricing.Apply` → `UsageComparison.SplitKnownCost` → `CreateMeasurementRows`/recursos de texto. Revisar además `SavedUsageComparison`, la construcción de métricas en `UsageReportQuery`, los catálogos Codex/Cursor y las pruebas próximas a esos símbolos.

El agente debe registrar los llamadores encontrados en su SHA de trabajo. Si main cambió, la evidencia de este paquete es baseline, no una orden de sobrescribir nuevas correcciones.

## 3. Requisitos de fase

| ID | Requisito |
|---|---|
| REQ-P0-01 | Rates compara los mismos eventos y revisiones en ambas fechas. |
| REQ-P0-02 | Exclusiones por precio, tier, tiempo y retención son visibles y no se imputan a cero. |
| REQ-P0-03 | Volumen y mezcla no se presentan como efectos medidos de una cohorte fija. |
| REQ-P0-04 | El modo registrado, el escenario de catálogo y el snapshot guardado no se confunden. |
| REQ-P0-05 | Las unidades y categorías de tokens tienen casos de conservación por fuente cambiada. |
| REQ-P0-06 | No se rompe silenciosamente el contrato `tokenusage.report.v1`. |

## 4. Algoritmo recomendado para Rates

Congelar una revisión de datos R y el filtro F. Obtener las observaciones retenidas permitidas por F, con su identidad, revisión, componentes y contexto necesario para pricing. Aplicar la elegibilidad temporal vigente sin promover agregados diarios. Para cada observación, intentar el cálculo con catálogo A y B usando los mismos contadores y condiciones.

Definir `E = {e: precio_A(e) y precio_B(e) son aplicables al mismo registro y bajo la misma política}`. Calcular `A = suma(precio_A(e), e en E)`, `B = suma(precio_B(e), e en E)`, `delta = B - A`. La cantidad de eventos, tokens y distribución de modelos de E es exactamente la misma en ambos lados. La firma de cohorte incluye identidad y revisión, no sólo la suma de tokens.

Las observaciones que sólo tienen precio A, sólo B, ninguno, precisión insuficiente o detalle expirado quedan en grupos de exclusión. Se pueden mostrar subtotales disponibles por fecha como información auxiliar, pero su resta **no es** el cambio de tarifa sobre una cohorte común. No confundir el volumen excluido del histórico con eventos observados de valor cero.

**Caso límite decisivo:** A puede valorizar 100 tokens del modelo X y B otros 100 del modelo Y. Ambos porcentajes de cobertura son iguales; la intersección es vacía. El resultado de cambio de tarifa es `Unavailable`, no la resta de dos costos ni cero.

## 5. Contrato semántico propuesto

```text
RateScenarioComparison
  MethodId = "fixed-cohort-repricing/v1"
  DataRevision
  QueryFingerprint
  CohortFingerprint
  CatalogA / CatalogB
  PricingPolicyId
  ComparableEvents / ComparableTokens
  CostA / CostB / Delta
  VolumeEffect = NotApplicable
  MixEffect = NotApplicable
  ExclusionsByReason
  Assumptions
```

Es un contrato propuesto interno; no una clase existente ni un esquema CLI aprobado. Para una cohorte vacía, `CostA`, `CostB` y `Delta` no se publican como resultados de una comparación de uso. El exportador conserva la causa. Un conjunto no vacío de solicitudes con precio legítimamente cero sí puede producir costo cero.

Si no se puede obtener la intersección con el cambio acotado de P0, el hotfix mínimo es retirar la descomposición engañosa y marcar el efecto como no disponible. No sustituirla por otra fórmula agregada que siga sin identificar la población. El refinamiento de intersección debe ser una tarea explícita para cerrar P0 completo.

## 6. Precios no lineales y tier desconocido

El cálculo usa el motor existente por observación: umbrales de contexto, host y composición se aplican antes de agregar. No valorar un millón de tokens agregado como si fuera una sola solicitud; eso puede cruzar umbrales que ninguna solicitud cruzó. No deducir el nivel de servicio desde esfuerzo de razonamiento o sufijos de modelo.

El estimator actual admite Standard y tier ausente con una limitación documentada. Preservar el histórico registrado. Para Rates, la política recomendada es excluir configuración desconocida cuando sea necesaria para determinar aplicabilidad; una variante compatible que conserve el supuesto del estimador debe mostrarlo y tener `PricingPolicyId` distinto. No fusionar ambos resultados bajo una misma etiqueta de exactitud.

P0 no requiere añadir nuevos precios ni consultar proveedores al abrir el informe. Las fechas de catálogo son escenarios sobre datos locales, no períodos de gasto ni fechas de facturación.

## 7. Contabilidad y redondeo

Los costos persistidos actuales soportan seis decimales USD. Mantener enteros de micro-USD o `decimal` con política explícita; no introducir `double` en sumas monetarias. Reutilizar el redondeo del catálogo para eventos históricos. Para escenarios nuevos, fijar versión y etapa del redondeo: valorar cada evento con el motor, cuantizar una vez según política, sumar unidades enteras, restar los dos totales.

Para comparar muestras sintéticas lineales puede usarse Decimal como oráculo; no sustituye el pricing C#. La validación debe comprobar que cambiar el orden de eventos no altera el resultado. Cuando un método futuro de atribución genere fracciones, mostrar residual de redondeo explícito si existe en lugar de cargarlo al término «mix».

Separar tres situaciones: costo no reportado, costo reportado de cero y precio no aplicable. El mismo criterio aplica a tokens: ausencia de un componente no se convierte automáticamente en cero conocido. El esquema v1 puede conservar su representación, pero P1/P2 deben explicar su disponibilidad mediante un contrato adicional.

## 8. Presentación y compatibilidad

En Rates, usar `Catalog value at A`, `Catalog value at B`, `Change on comparable usage` y `Excluded usage`. El detalle explica que se mantienen constantes las observaciones y cambia únicamente la referencia de precios. No mostrar insignias «winner» que impliquen calidad o productividad; el importe menor puede describirse literalmente como menor valor en ese escenario.

Si la pantalla conserva filas de volumen/mezcla por compatibilidad visual, renderizarlas como `Not applicable — fixed cohort`, no `$0`. En comparaciones históricas no habilitar las tres columnas hasta P4. Los costos reportados siguen separados de los estimados; no reemplazar sus totales con escenarios de lista.

No cambiar las claves públicas v1 para convertir un valor en otro concepto. Inventariar consumidores y snapshots. Una corrección incompatible exige versión nueva explícita; las salidas v1 mantienen forma y semántica documentada. El nuevo resultado puede vivir inicialmente como DTO interno de Rates.

Un snapshot antiguo sin versión de método se abre como `Legacy method`. No recalcularlo silenciosamente con datos actuales. Ofrecer una nueva comparación separada cuando exista evidencia; conservar el resultado anterior y su advertencia. Si hay datos incorrectos conocidos en un snapshot, marcarlos como legado no validado; nunca ocultar la corrección bajo la palabra «reproducible».

## 9. Secuencia de trabajo

1. **TASK-P0-01 · Caracterizar.** Añadir/reutilizar casos de fórmula, eje Rates, precios desconocidos y snapshots actuales. Capturar salidas antes del cambio. Documentar llamadores y las rutas excluidas del alcance.
2. **TASK-P0-02 · Corregir.** Extraer la comparación de cohorte fija a Core, usando el pricing existente por inyección de una política/función. Eliminar la dependencia de la fórmula agregada para ese uso. No introducir dependencia Core → Providers.
3. **TASK-P0-03 · Presentar.** Actualizar textos y estados; alinear documento de pricing y conservar notas de limitación. Añadir evidencia del conjunto comparable y no comparable.
4. **TASK-P0-04 · Compatibilidad.** Verificar snapshots antiguos, contrato CLI y regresiones de comparación de períodos/modelos/ciclos. No marcar una verificación como pasada sin ejecutarla.

Cada tarea devuelve cambios, test que falla antes/pasa después cuando corresponda y riesgo residual. Si se descubre un defecto de tokens en una fuente, abrir corrección acotada por fuente; no modificar todos los parsers de forma especulativa.

## 10. Casos de prueba con oráculo

| ID | Caso | Resultado esperado |
|---|---|---|
| TEST-P0-01 | Fórmula antigua con volúmenes positivos. | Mix algebraicamente nulo; caso documenta limitación, no perpetúa diseño nuevo. |
| TEST-P0-02 | Mismas solicitudes, mismos precios. | Delta 0 sobre cohorte no vacía; volumen/mezcla no aplicables. |
| TEST-P0-03 | Mismas solicitudes, sube sólo precio de una. | Delta igual a suma de diferencias por solicitud. |
| TEST-P0-04 | Precio sólo en A o sólo en B. | Evento excluido del delta; contador y tokens en su motivo. |
| TEST-P0-05 | Igual número de tokens pero identidades disjuntas. | Intersección vacía; cambio no disponible. |
| TEST-P0-06 | Modelo/tier/precio desconocidos y raw expirado. | Exclusiones diferenciadas; no aparecen USD ficticios. |
| TEST-P0-07 | Dos solicitudes bajo umbral, agregado sobre umbral. | Se valoriza por solicitud; no se aplica umbral al agregado. |
| TEST-P0-08 | Snapshot previo sin MethodId. | Se abre como legado, sin mutación ni método inventado. |
| TEST-P0-09 | Entrada incluye caché, salida incluye razonamiento. | Fixture de normalización conserva el total disjunto esperado. |
| TEST-P0-10 | Orden de eventos permutado y miles de fracciones pequeñas. | Total estable con política de redondeo documentada. |
| TEST-P0-11 | Proceso CLI v1 y comparación histórica antes/después. | Claves, unidades y semántica v1 preservadas; cambio incompatible sólo con versión explícita. |

Para TEST-P0-09, el fixture canónico de diseño `input_total=1000, cached=400, output_total=250, reasoning=50` debe normalizar a 600 entrada nueva, 400 caché, 200 salida visible y 50 razonamiento: total 1250, no 1700. Es una forma sintética, no una afirmación del formato de todos los proveedores.

## 11. Criterios de salida

**GATE-P0-A:** cohorte común demostrada por identidad/revisión o comparación retirada explícitamente hasta tenerla. Para cerrar P0 completo, la intersección funcional debe estar implementada y probada. **GATE-P0-B:** ninguna regresión en costo histórico, cuotas ni contratos v1. **GATE-P0-C:** casos de ausencia y no aplicable visibles en UI/CLI cuando esas superficies cambien. **GATE-P0-D:** revisión de código + tests C# + evidencia empaquetada de los estados afectados.

La prueba sintética Python del paquete sólo acredita el oráculo, nunca GATE-P0-D. No utilizar el conteo de tests como sustituto de verificar los riesgos específicos.

## 12. Rollback y entrega a P1

La corrección de presentación puede desactivarse sin revertir datos porque P0 no revaloriza la base. Si hubiera un cambio de esquema imprescindible, separarlo y seguir el runbook; no mezclarlo con un hotfix de etiquetas. Dejar a P1 el DTO de resultados, los estados semánticos y fixtures de los tres costos. Documentar el método anterior como obsoleto para impedir su reutilización accidental en nuevos insights.
