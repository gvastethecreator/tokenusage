# Experiencia nativa · diseño, estados y accesibilidad

**Estado:** especificación de producto propuesta; no es un rediseño aprobado del branding. **Fuentes:** TU-05/06/12/16/17, EXT-06 en [Fuentes](../references/10-SOURCES.md).

## 1. Principios visibles

La jerarquía debe llevar del alcance al dato y del dato a su evidencia. El usuario no tiene que abrir un modal para descubrir que un costo es estimado. Las advertencias no compiten todas por atención: un resumen breve acompaña cada resultado y el detalle queda a una acción.

Conservar WinUI, recursos y temas actuales. No introducir una paleta paralela, tipografías externas o controles custom innecesarios. La mejora se obtiene con espaciado, alineación, estados y navegación consistentes. Los números usan cifras tabulares donde estén disponibles, alineación por unidad y precisión legible.

## 2. Anatomía del informe

**Zona de contexto:** título, intervalo, alcance y última lectura. **Zona de selección:** filtros primarios visibles; avanzados colapsados; chips activos. **Zona de resumen:** tokens y valores reportado/estimado separados, más exclusiones. **Zona exploratoria:** tabla/gráfico sincronizados. **Zona de detalle:** panel lateral con información contextual, no otro total independiente.

Para ventanas estrechas el detalle pasa debajo de la tabla o a una vista con navegación de retorno; nunca reduce ambas columnas hasta hacerlas ilegibles. El panel conserva selección y filtros. No hay horizontal scroll de toda la ventana; tablas/código pueden tener su propio desplazamiento cuando corresponda.

## 3. Especificación de estados

| Estado | Qué mostrar | Qué no hacer |
|---|---|---|
| Initial loading | Estructura estable y texto de carga. | Números ficticios animándose desde cero. |
| Ready | Datos, alcance y procedencia. | Ocultar costos sin precio para «limpiar» la tabla. |
| Refreshing | Últimos datos con hora y aviso. | Borrar toda la pantalla o mezclar revisiones. |
| Partial | Subtotal conocido y exclusión visible. | Llamarlo total exacto. |
| Stale | Evidencia anterior y causa/acción. | Mostrar cero si la lectura nueva falla. |
| Empty | Ningún registro en selección y opciones pertinentes. | «No usaste IA» sin cobertura que lo pruebe. |
| Unavailable | Fuente/capacidad no disponible y motivo. | Pestaña vacía con números cero. |
| Error | Fallo recuperable, datos anteriores cuando sean válidos. | Excepción cruda con rutas y payloads. |
| Legacy detail | Historia agregada, detalle expirado/desconocido. | Generar solicitudes sintéticas desde rollups. |

## 4. Texto y cifras

Interfaz en inglés, documentación en español. Usar `Observed tokens`, `Reported value`, `Estimated API value`, `Unpriced tokens`, `Not available`, `Not applicable`, `Unknown configuration`, `Observed span`. No llamar factura a un valor reportado por una herramienta sin evidencia de facturación.

No escribir `0%` para ausencia de población. Para cero observado, mostrar cero con procedencia. Los valores abreviados tienen acceso al número completo por detalle accesible, no sólo hover. Diferencias porcentuales y puntos porcentuales llevan unidades diferentes. Un signo negativo de costo significa cambio, no crédito salvo que se documente así.

Explicar top-N/Other. Si una parte no se puede ubicar por hora, mantener una banda `Not placed on exact timeline`. Un gráfico no debe aparentar cobertura continua a través de huecos.

## 5. Teclado y foco

Tab sigue lectura visual: intervalo, filtros, acciones, tabla, detalle. Las filas seleccionables funcionan con flechas y Enter; Escape cierra popover/panel pertinente sin perder todo el informe. Los chips se eliminan con acción accesible, no sólo una X sin nombre. Un botón refresh tiene nombre, estado busy y feedback al completar.

Panel no modal: no atrapa foco. Al cerrarlo, volver a la fila original; si ya no existe, al encabezado de tabla con un anuncio breve. Cambiar modelo no mueve foco automáticamente a una tarjeta remota. Evitar que actualizaciones live repitan todos los valores por lector de pantalla.

No sobrescribir atajos comunes de Windows. Un atajo nuevo se documenta y expone en tooltip/accesibilidad, pero el flujo nunca depende exclusivamente de conocerlo.

## 6. Accesibilidad de gráficos

Usar controles nativos cuando sean suficientes. Para custom rendering, exponer AutomationPeer/patrones adecuados y una alternativa tabular del mismo resultado; no tratar la descripción de un gráfico como equivalente a poder consultar sus puntos. Microsoft destaca nombre, rol, teclado y recursos de alto contraste como bases de accesibilidad. [EXT-06]

No distinguir proveedores o calidad sólo por color. Combinar etiqueta, símbolo/patrón y texto. Evitar gradientes muy suaves que desaparezcan en alto contraste. La selección y foco tienen indicadores diferentes. El contenido informativo de tooltip se puede alcanzar con teclado y lector.

## 7. Escala, movimiento y densidad

Revisar 100%, 150%, 200% y escala de texto aumentada; los valores son escenarios de QA, no una afirmación de compatibilidad ya probada. Probar mínimo práctico de ventana y monitor amplio, light/dark/alto contraste. Etiquetas largas y números grandes no deben solaparse ni cortar la unidad.

Espaciado preferentemente en múltiplos del sistema existente; densidad compacta no significa controles imposibles de enfocar. Mantener alturas de fila suficientes y separar acciones peligrosas de las frecuentes. No usar animaciones continuas de fondo en informes.

Movimiento propuesto: transiciones de apertura/estado breves, aproximadamente 120–180 ms como objetivo de diseño, y cero movimiento no esencial cuando reduced motion está activo. No animar reordenamientos automáticos de filas que el usuario está leyendo.

## 8. Escenarios de revisión visual

Fixture mínimo: 30 modelos, 4 herramientas, nombres largos, IDs desconocidos, un costo cero, estimaciones parciales, datos legacy, 2 zonas y una fuente stale. No evaluar sólo un fixture perfecto. Para P3 sumar alias autorizado, proyecto no asignado y sesión con varios proyectos.

Revisar alineación de encabezados/números, separación de valores monetarios, textos truncados con alternativa accesible, consistencia del estado entre tabla y panel, foco después de filtrar, y comportamiento de resize durante carga. La captura de un estado no prueba cancelación ni lectura de pantalla: registrar el recorrido.

## 9. Criterios de aceptación UX

El usuario puede completar selección → modelo → evidencia → comparación → retorno sólo con teclado. Un lector de pantalla puede identificar alcance, valor y si es estimado o no disponible. El resize no pierde filtros. El refresh no cambia arbitrariamente selección ni scroll. Un error de fuente no transforma números previos en ceros.

La revisión visual requiere antes/después de la superficie afectada y notas de cambios. No hace falta rehacer todo el producto para pasar estos criterios. El objetivo es una superficie consistente y confiable, no una demostración aislada con estilo distinto al resto de TokenUsage.
