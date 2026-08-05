# ADR-0002: transición del nombre TokenUsage

Estado: implementado el 2026-08-04

Fecha: 2026-07-22

## Decisión

El producto formal y técnico se llama `TokenUsage`. La UI, los proyectos, los
namespaces, los ensamblados, los ejecutables, las rutas y las pruebas usan este
nombre. La Identity y el AUMID del paquete permanecen estables para conservar
la ruta de actualización.

## Motivo

Un cambio de nombre a mitad de los verticales abiertos mezclaría dos identidades
en el paquete y en la evidencia. También haría más difícil comprobar upgrades,
datos locales, alias de CLI y desinstalación.

## Corte técnico

El corte del 2026-08-04 cambió estos elementos como una sola unidad:

- solución, proyectos, carpetas, namespaces y ensamblados;
- ejecutables de la app y la CLI;
- alias `tokenusage.exe` y contratos JSON `tokenusage.*.v1`;
- rutas del tracker local, scripts, pruebas y documentación.

El corte mantiene la Identity, el AUMID y el Publisher actuales. La instalación,
la actualización empaquetada, el dominio, el logo y el canal beta requieren sus
pruebas o decisiones propias. El Ticket 02 conserva esos puntos pendientes.
