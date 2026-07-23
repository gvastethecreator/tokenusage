# Ticket 12B2C — controles de métricas

Fecha: 2026-07-23

Estado: implementado y verificado en x64 y ARM64.

## Resultado

- Cada proveedor ofrece un detalle plegable para ordenar, mostrar, ocultar, destacar y cambiar de sección sus métricas.
- Los controles usan IDs estables, nombres accesibles localizados y áreas activas de 32 DIP.
- El detalle conserva su estado mientras se guardan cambios y solo crea filas al abrirse.
- El límite de dos métricas destacadas muestra un aviso localizado y restaura el toggle rechazado.
- Orden, visibilidad, sección y destacado persisten en el documento v1 y se cargan en un proceso nuevo.

## Delegación y revisión

Grok Build creó el modelo aislado de nombres de acciones en
`ticket-12b2c-metric-action-model-v2`. El snapshot quedó dentro de `.snapshots`,
se revisó antes de aplicarlo y se limpió con el flujo oficial. Coste informado:
USD 0.0651684.

La primera revisión independiente rechazó el corte por tres P2: falta de aviso
al alcanzar el límite, creación de controles cerrados y acciones de 28 DIP. El
corte añadió aviso, carga diferida y acciones de 32 DIP. La segunda revisión dio
`ACCEPT`, sin P0–P2.

## Prueba empaquetada

`tests/ui/ticket-12b2c-dashboard-metrics.ps1` usa dos procesos MSIX lanzados por
AUMID con `winapp run`. No ejecuta el binario empaquetado de forma directa.

- Configure v7: 4/4.
- Verify v7 tras reinicio: 2/2.
- Probó nombres UIA, orden mixto, sección, visibilidad, destacado, límite de dos,
  continuidad del expander y carga desde el mismo JSON.
- Evidencia visual y resultados: `.scratch/ui/ticket-12b2c/configure-v7` y
  `.scratch/ui/ticket-12b2c/verify-v7`.

## Gates finales

- Architecture: 62/62.
- Core: 134/134.
- CLI: 82/82.
- Providers: 268/268.
- Platform Windows: 98/98.
- MSIX Release x64: `BUILD SUCCEEDED`.
- MSIX Release ARM64: `BUILD SUCCEEDED`.
- `git diff --check`: sin errores.

## Alcance restante

Ticket 12C conserva la navegación completa por teclado/UI Automation, undo de
sesión y reset confirmado.
