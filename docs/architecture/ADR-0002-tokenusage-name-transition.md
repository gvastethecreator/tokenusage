# ADR-0002: transición del nombre TokenUsage

Estado: aceptado para planificación

Fecha: 2026-07-22

## Decisión

El producto formal se llamará `TokenUsage`. La implementación conserva
`WOpenUsage` mientras se completa el trabajo que ya usa ese nombre en tickets,
ensamblados, namespaces, recursos, manifiesto, ejecutables, pruebas y evidencia.

## Motivo

Un cambio de nombre a mitad de los verticales abiertos mezclaría dos identidades
en el paquete y en la evidencia. También haría más difícil comprobar upgrades,
datos locales, alias de CLI y desinstalación.

## Regla de transición

Hasta la tarea de migración:

- la UI del prototipo sigue mostrando `WOpenUsage`;
- no cambian `Package.appxmanifest`, AUMID, Publisher, nombres de proyecto,
  namespaces, assembly names, rutas de datos ni alias de CLI;
- la documentación nueva puede nombrar `TokenUsage` como producto formal y debe
  indicar que `WOpenUsage` es la identidad transitoria;
- no se crea un logo final dentro del corte visual 11A.

La futura tarea de migración deberá cambiar todos los puntos en un solo corte,
probar instalación y actualización, decidir si se migran datos locales y dejar
un plan de rollback. Publisher, dominio, logo y canal beta siguen pendientes en
el Ticket 02.
