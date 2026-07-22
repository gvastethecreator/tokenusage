# ADR-0002: transición del nombre TokenUsage

Estado: revisado; copy público aprobado

Fecha: 2026-07-22

## Decisión

El producto formal se llama `TokenUsage`. Desde 2026-07-22, la UI, textos,
tooltip de bandeja y nombre visible del paquete usan `TokenUsage`. La
implementación conserva `WOpenUsage` en ensamblados, namespaces, ejecutables,
rutas, pruebas y evidencia previa hasta el corte técnico de migración.

## Motivo

Un cambio de nombre a mitad de los verticales abiertos mezclaría dos identidades
en el paquete y en la evidencia. También haría más difícil comprobar upgrades,
datos locales, alias de CLI y desinstalación.

## Regla de transición

Hasta la tarea de migración técnica:

- la UI y el copy muestran `TokenUsage`;
- el manifiesto puede mostrar `TokenUsage`, pero no cambian Identity, AUMID,
  Publisher, nombres de proyecto, namespaces, assembly names, rutas de datos ni
  alias de CLI;
- la documentación nueva puede nombrar `TokenUsage` como producto formal y debe
  indicar que `WOpenUsage` es la identidad transitoria;
- no se crea un logo final dentro del corte visual 11A.

La futura tarea de migración deberá cambiar todos los puntos en un solo corte,
probar instalación y actualización, decidir si se migran datos locales y dejar
un plan de rollback. Publisher, dominio, logo y canal beta siguen pendientes en
el Ticket 02.
