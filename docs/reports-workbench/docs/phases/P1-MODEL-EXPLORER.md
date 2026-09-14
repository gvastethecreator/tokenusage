# P1 · Model Explorer sobre la infraestructura existente

**Estado:** propuesta. **Dependencia:** semántica P0; no depende de sesiones/proyectos. **Fuentes:** TU-02/05/06/07/12/17; EXT-01/06 en [Fuentes](../references/10-SOURCES.md). **Resultado:** una primera mejora utilizable sin ampliar recolección de contenido ni introducir un nuevo motor de informes.

## 1. Experiencia que se entrega

El usuario abre Reports, selecciona un período y ve qué modelos participaron, cuánto consumo se observó y qué valor es reportado, estimado o no valorizado. Al abrir una fila, conserva filtros y posición de tabla; el panel lateral explica composición, evolución y evidencia. Cambiar entre tabla y gráfico no modifica la población.

El flujo completo debe funcionar con teclado, datos parciales, nombres largos y cambios rápidos de selección. No basta diseñar el estado ideal con tres modelos y cobertura perfecta.

## 2. Dos cortes, sin infraestructura anticipada

**P1A:** filtros de período, herramienta y modelo, tabla y detalle sobre dimensiones conservadas en rollups. Costos separados, evidencia, ordenar, buscar y navegación. Ninguna migración es necesaria para este corte salvo que una corrección mínima y demostrada la requiera.

**P1B:** filtros por configuración observada utilizando eventos retenidos. Esfuerzo y tier existen en el dominio, pero no todas las proyecciones históricas los conservan. Al activarlos, mostrar que se filtra la población con esa información y cuantificar lo no clasificable. Si el detalle expiró, la UI no «reconstruye» configuración a partir del modelo más reciente.

No publicar Sessions como pestaña funcional hasta P3. No mostrar llamadas, p95, tokens/segundo o tamaño de contexto por solicitud antes de P2. El recuento actual de eventos, si aparece, se llama `Records`, no `Requests`.

## 3. Requisitos

| ID | Requisito |
|---|---|
| REQ-P1-01 | Un filtro inmutable define una población compartida por tabla, gráfico, detalle y exportación. |
| REQ-P1-02 | Cambiar filtros sólo consulta la base de TokenUsage; no escanea proveedores. |
| REQ-P1-03 | Filtros de configuración declaran soporte y horizonte temporal. |
| REQ-P1-04 | Top N + Other conserva total y categorías sin asignar. |
| REQ-P1-05 | Respuestas tardías no reemplazan la selección vigente. |
| REQ-P1-06 | La UI permanece útil en vacío, parcial, stale, error y fuente no disponible. |
| REQ-P1-07 | Persisten navegación, teclado y accesibilidad en tamaños/temas afectados. |

## 4. Filtros y semántica

Período inicial: conservar la elección actual del producto, no introducir un nuevo default sin motivo. El filtro de herramienta usa `AgentId`; el proveedor del modelo es otra dimensión. `Cursor` y un fabricante de modelos no pertenecen al mismo selector conceptual. Un modelo canónico no borra el observado; aliases se documentan y desconocidos permanecen desconocidos.

Combinar selecciones de una dimensión con OR y dimensiones distintas con AND. Un selector vacío significa «todos», no conjunto vacío. Para expresar «desconocido», usar una opción explícita distinta de vacío. Ordenar de forma estable por valor y clave secundaria; no reordenar mientras una persona navega una tabla ya cargada salvo refresh explícito.

Filtro textual de tabla: búsqueda local por etiquetas admitidas; no búsqueda en conversaciones. Debe quedar claro si sólo reduce filas visibles o cambia el resultado analítico. Recomendación: búsqueda analítica aplica al mismo resultado y actualiza sus totales; ocultar filas visualmente sin actualizar el total sólo se permite con la etiqueta `Visible rows` y total de selección separado.

Zona horaria: P1A conserva las zonas civiles de los rollups y las muestra. No promete rebucketizar historia a una zona arbitraria. P1B puede resolver rangos UTC para eventos retenidos; las filas diarias incompatibles quedan identificadas. En un resultado mixto, no reemplazar las zonas reales con `Local` sólo para simplificar.

## 5. Consulta compartida

Extender el seam de `UsageReportQuery` en Core. Los nombres de diseño `ReportSelection`, `ReportPlan` y `ReportResult` representan responsabilidades, no obligatoriedad de crear tres nuevos subsistemas. Reutilizar los tipos actuales donde no impidan expresar elegibilidad.

```text
ReportSelection
  Range + RangeKind + DisplayZone
  AgentIds / ModelProviderIds / CanonicalModelIds
  ObservedModelFilter / EffortFilter / TierFilter
  PricingMode
  RequestedDimensions + RequestedResolution
  MethodVersion
```

El planificador elige rollups para dimensiones conservadas, raw para atributos sólo granulares, o una combinación **sin solapar eventos y sus rollups**. Para el corte P1, es preferible devolver una población granular acotada con exclusiones a mezclar totales incompatibles. Una consulta inválida devuelve un diagnóstico tipado, no excepciones con SQL/rutas.

El resultado incluye `QueryFingerprint`, revisión de datos, alcance, totales, filas, series, elegibilidad y exclusiones. La presentación no calcula otra fórmula de cobertura ni «rellena» huecos. Los filtros posteriores a un DTO conservan denominadores explícitos; no reutilizar por error el total global como total de selección.

## 6. Fronteras de implementación

**Core:** interpretación de selección, consulta, agrupación, costos y elegibilidad. **Presentation:** orden, etiquetas de estado, datos para gráficos y formato desacoplado de WinUI cuando sea posible. **App:** XAML, recursos, foco, comandos y composición. **Providers:** sin modificaciones de lectura en P1A. **CLI:** usa el mismo resultado semántico, preservando v1 y distinguiendo cualquier nueva versión.

No mover todo el ViewModel de una vez. Extraer primero la construcción de filas/totales a una función con tests y sustituir el llamador; después la consulta. Mantener los gráficos y geometrías actuales cuando sirvan. No añadir librería de gráficos sólo para una captura más atractiva.

La conexión de lectura pertenece a una operación; no se comparte entre hilos. La ejecución de trabajo de SQLite debe ser acotada y fuera del hilo de UI. Los métodos Async del proveedor no garantizan I/O asíncrono. Reutilizar/centralizar el patrón existente, evitando `Task.Run` ilimitados al escribir en un filtro.

## 7. Composición visual concreta

Barra superior: título Reports, intervalo, alcance, refresh y menú de exportación. Segunda fila: filtros primarios y botón `More filters`. Los filtros activos aparecen como chips eliminables, con `Clear filters` que no cambia el período sin avisar.

Encabezado analítico: `Observed tokens`, `Reported value`, `Estimated API value`, `Unpriced tokens` y una síntesis breve de evidencia. No ofrecer un gran número USD único que oculte su procedencia. El total combinado, si se conserva por compatibilidad, debe rotularse `Known usage value` y permitir ver sus componentes.

Tabla de modelos: identidad, herramienta, tokens, reportado, estimado, sin precio y participación. Entrada/salida/caché en columnas opcionales o detalle; no obligar a un scroll horizontal gigante en la primera vista. La ausencia se ve con estado y tooltip accesible, no como `$0.00`.

Panel de detalle: título del modelo y herramienta, chips de selección, resumen de composición, serie temporal y `Evidence`. Es un panel no modal: no atrapa foco ni bloquea la tabla. Un enlace `Open in comparison` reutiliza el filtro y explica si debe cambiar el modo de precios.

## 8. Estado y concurrencia

Usar estados explícitos: `InitialLoading`, `Ready`, `Refreshing`, `Partial`, `Stale`, `Empty`, `Unavailable`, `Error`. No modelarlos como muchos booleanos que puedan producir combinaciones imposibles; el ViewModel existente puede adaptarse gradualmente a un estado calculado.

Cada selección recibe una generación creciente. Cancelar la consulta anterior; al completar, comprobar generación, ventana viva y política de privacidad antes de publicar. Cancelar es una optimización, no la garantía: descartar por generación es obligatorio aunque la operación de SQLite tarde en observar la cancelación.

Durante un refresh de recolección se mantiene visible el último resultado con su hora y aviso. Al terminar la ingesta, invalidar cachés por revisión y pedir una consulta nueva. No actualizar la mitad de las tarjetas con datos nuevos y la otra mitad con los anteriores.

Al cerrar el detalle, devolver foco a la fila que lo abrió. Al cambiar filtros, preservar selección sólo si la identidad sigue en la población; si desaparece, cerrar el panel con un anuncio breve. No seleccionar silenciosamente otro modelo.

## 9. Tabla, series y datos grandes

Virtualizar filas nativamente. Para detalle granular futuro, paginar por clave estable; P1 no materializa todos los eventos sólo para ordenar los modelos. Top N usa N como preferencia de presentación; la suma de `Other` incluye todo lo restante sin pérdida y cada desconocido tiene su categoría correspondiente.

No animar contadores desde cero en cada refresh. Animar apertura/cambio de estado de forma breve y respetar reduced motion. Tooltips no son el único acceso a precisión o exclusiones. El gráfico tiene una representación tabular equivalente y etiquetas legibles en alto contraste.

Una caché de resultados sólo es válida por selección canónica + revisión de datos + política de precios + versión de método + época de privacidad. No guardar indefinidamente modelos de UI con rutas/aliases. Los límites se definen en [rendimiento](../quality/06-PERFORMANCE-PROTOCOL.md).

## 10. Secuencia de tareas

**TASK-P1-01:** inventariar consulta/proyección actual, generar fixtures canónicos y comprobar paridad antes del cambio. **TASK-P1-02:** implementar selección, planner mínimo y seam de lectura compartido. **TASK-P1-03:** extraer proyecciones, conectar tabla y detalle. **TASK-P1-04:** filtros y navegación, incluyendo consultas fuera de orden. **TASK-P1-05:** P1B sólo con política de retención/ausencias aprobada. **TASK-P1-06:** evidencia visual y accesibilidad, contrato CLI afectado y regresiones de tray/cuotas.

Evitar un PR donde refactor, nueva UI, cambio de schema y cinco parsers deban aceptarse juntos. P1A debe seguir siendo publicable si P1B encuentra una limitación real.

## 11. Matriz de aceptación

| ID | Escenario | Comprobación |
|---|---|---|
| TEST-P1-01 | Mismo filtro Core/UI/CLI. | Igual DTO canónico; abreviaturas de texto no cuentan como igualdad numérica. |
| TEST-P1-02 | Cambiar 20 veces filtros. | Cero lecturas nuevas de proveedores; sólo resultado final publicado. |
| TEST-P1-03 | Raw expirado y filtro effort. | Total elegible + exclusiones; no se filtra el histórico por el último effort visto. |
| TEST-P1-04 | Unknown, no precio, costo cero y fuente stale. | Cada estado tiene etiqueta y salida distintas. |
| TEST-P1-05 | Top 5 de 30 modelos. | Top 5 + Other = total de selección con precisión completa. |
| TEST-P1-06 | Cerrar panel o desaparecer fila. | Foco y selección coherentes; ninguna sustitución silenciosa. |
| TEST-P1-07 | Lectura termina después de cerrar ventana. | No escribe UI ni deja excepción no observada. |
| TEST-P1-08 | Light/dark, alto contraste, escala y teclado. | Flujo completo y valores accesibles en WinUI empaquetado. |
| TEST-P1-09 | Datos cambian durante export/consulta. | Resultado de una revisión, no combinación de revisiones. |
| TEST-P1-10 | Dos agentes usan mismo modelo. | Filtros correctos y separación de host; no colisión por nombre de modelo. |

## 12. Gates y revisión final

**GATE-P1-A:** primera entrega vertical completa con datos actuales, sin nueva lectura. **GATE-P1-B:** contratos y denominadores comunes. **GATE-P1-C:** flujo teclado/escala/alto contraste y estados adversos verificados en Windows. **GATE-P1-D:** benchmark de consulta y navegación con resultados documentados, no cifras inventadas.

La revisión visual evalúa jerarquía, densidad, alineación numérica, contraste, comportamiento al resize y continuidad de filtros; una captura bonita del estado ideal no cierra la fase. Al terminar, entregar a P2 el punto de extensión de detalle, los límites de consulta y el catálogo de estados, no otra capa de cálculo paralela.
