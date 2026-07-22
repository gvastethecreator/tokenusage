# Blanco visual del flyout

Fecha: 2026-07-21

Estado: elegido por el usuario

## Captura elegida

El usuario eligió la primera de tres propuestas: [captura de paridad OpenUsage](selected-flyout-option-1.png).

La captura guía jerarquía y densidad. No funciona como especificación literal cuando contradice el código upstream o una convención nativa de Windows.

## Fuente auditada

La propuesta parte de `robinebers/openusage@9d2bf09f10e21f769494a525a9d65c84d7aeb1df`:

| Regla | Fuente upstream |
|---|---|
| ancho fijo de 320 puntos | `DashboardView.swift`, líneas 51–66 |
| alto medido por contenido y limitado por pantalla | `DashboardView.swift`, líneas 67–80, y `PanelHeightController.swift` |
| margen exterior de 14 puntos | `DashboardView.swift`, líneas 51–57 |
| radio de tarjeta de 12 puntos | `Theme.swift`, líneas 59–66 |
| cabecera de proveedor sobre la superficie base | `ProviderSectionHeader.swift`, líneas 55–113 |
| filas limitadas con título, barra y lectura | `WidgetRowView.swift`, líneas 82–105 |
| pie fijo con estado de actualización | `PopoverFooter.swift`, líneas 51–109 |
| gasto total condicionado por capacidad y ajuste | `DashboardContentView.swift`, líneas 48–55 |

La extracción delegada de Grok Build está en `.scratch/agent-cli-delegation/grok-build/runs/2026-07-22T00-56-40-857Z-plan-dda5663e/result.json`. El parent contrastó sus cifras y estructura con el checkout local antes de aceptar la dirección.

## Reglas bloqueantes para WinUI

- Ancho base: 320 DIPs. El escalado físico se calcula con el DPI del monitor.
- Alto: contenido medido, mínimo de 200 DIPs y máximo de 720 DIPs o 85 % del área de trabajo, el menor.
- Sin barra de título visible ni encabezado de aplicación dentro del dashboard.
- Orden: gasto total cuando exista; proveedores; pie fijo.
- Margen exterior de 14 DIPs y separación de sección de 14 DIPs en densidad normal.
- Cabecera de proveedor fuera de la tarjeta. Una sola tarjeta agrupada por proveedor; sin tarjetas anidadas.
- Tarjetas sin borde dominante, radio de 12 DIPs y relleno semántico de tema.
- Barras de 5 DIPs. El texto comunica el estado además del color.
- `Segoe UI` y recursos Fluent. Los iconos usan Segoe Fluent Icons o assets propios.
- El pie contiene identidad, antigüedad/actualización y acceso a opciones.
- Claro, oscuro, alto contraste, teclado, foco y texto al 200 % forman parte del gate visual.

## Correcciones a la imagen generada

La implementación no conserva tres defectos del mockup:

1. La cabecera `Claude Team 5x` sale del contenedor exterior y queda sobre la superficie base.
2. La ventana elimina el espacio vacío y ajusta el alto al contenido.
3. Compartir, actualizar y opciones usan controles e iconos Fluent nativos.

## Primer estado implementable

Ticket 05 no necesita datos reales. Debe probar bandeja, posición, apertura/cierre y un estado vacío con la misma superficie, ancho, pie y reglas de acceso. Las métricas y el anillo llegan en los verticales siguientes.
