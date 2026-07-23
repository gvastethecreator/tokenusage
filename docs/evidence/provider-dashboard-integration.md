# Integración visual de proveedores y gasto

Fecha: 2026-07-23

## Resultado

El dashboard live usa una sola composición para cuotas, gasto local y gasto de
cuentas remotas. Un gasto positivo crea el resumen superior, el anillo y una
leyenda con nombre e importe por proveedor. La tarjeta de uso local queda
plegada en una fila y mantiene el desglose, heatmap y modelos detrás de un
toggle de icono.

El gasto de Vercel AI Gateway se obtiene de la métrica numérica
`spend.gateway.total.30d`. TokenUsage lo suma al resumen live sin leer el texto
formateado de la tarjeta. Si el mismo provider ya existe en el gasto local, la
composición evita duplicarlo.

## Layout y estados

- Los providers que solo aportan gasto entran al catálogo de personalización.
- Orden, visibilidad y color afectan el anillo superior y el desglose local.
- El ViewModel conserva el detalle local sin filtrar, por lo que volver a
  mostrar un provider restaura sus datos.
- Ocultar un provider recalcula el total y el nombre accesible.
- Los avisos de lectura parcial o no disponible siguen visibles con el detalle
  plegado.
- El anillo inicia su reveal aunque los datos lleguen después de `Loaded`, no
  reinicia para valores iguales y no queda a medio dibujar tras `Unloaded`.
- Los dos anillos usan AutomationIds distintos.

## Revisión

Grok Build revisó el primer corte en modo de solo lectura. Detectó estados de
visibilidad, refresh y UIA que el parent corrigió. Una revisión independiente
posterior encontró seis fallos más: gasto Vercel ausente, layout local parcial,
avisos plegados, texto UIA viejo y una salida incompleta de la animación. Este
corte incluye esas correcciones y sus regresiones focales. La segunda revisión
independiente aceptó el resultado sin hallazgos P0-P2.

Artefactos del pase Grok:

- `.scratch/agent-cli-delegation/grok-build/runs/2026-07-23T17-50-18-485Z-review-0a97dcfa/`;
- `.scratch/ui/provider-integration/proof/dashboard-live-review-fixed.png`.

## Pruebas

- Proyección, composición, uso local y Vercel: 37/37.
- Suite Providers: 291/291.
- UI empaquetada Claude: 7/7.
- UI empaquetada de gasto agregado: 8/8.
- MSIX Release x64 con fixtures: correcto, sin avisos ni errores.
- MSIX Release ARM64: correcto, sin avisos ni errores.
- UIA encontró `SampleSpendDonut` visible, de 129 x 130 px, con tres providers
  y un nombre accesible localizado. Al plegar uso local, el anillo interno dejó
  de existir en el árbol UIA.

## Límite

Las pruebas de Vercel usan snapshots y clientes fake. El smoke con una cuenta
autorizada y una clave real sigue pendiente en Ticket 74F. No se guardaron
claves, rutas de usuario ni datos de cliente en el repo.
