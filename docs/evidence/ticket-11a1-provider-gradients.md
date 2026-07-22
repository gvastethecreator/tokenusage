# Evidencia del Ticket 11A.1: gradientes de proveedor

Fecha: 2026-07-22

Estado: aceptado como corte visual de muestra. La paridad final sigue en 11B y
11C.

## Resultado

- Los cinco arcos usan un gradiente diagonal de variante oscura a color base.
- Codex usa verde agua, Grok Build violeta y OpenCode rosa.
- Cada marca usa el color base sólido de su serie.
- Cada arco tiene un trazo de sombra negro con alfa `0x30` y un desplazamiento
  vertical de 0,75 DIP.
- Alto contraste usa el realce sólido del sistema y oculta la sombra.
- La animación conserva el reveal de 480 ms y su salida inmediata con movimiento
  reducido.

## Prueba visual

- Fuente: `docs/design/selected-flyout-option-1.png`.
- Base: `artifacts/ticket-11a1-baseline/02-normal.png`.
- Final normal: `artifacts/ticket-11a1/02-normal.png`.
- Cerca del límite: `artifacts/ticket-11a1/03-near-limit.png`.
- Parcial y vencido: `artifacts/ticket-11a1/04-partial-stale.png`.
- Comparación conjunta: `artifacts/ticket-11a1/design-qa-comparison.png`.
- Recorte del donut: `artifacts/ticket-11a1/design-qa-comparison-donut.png`.

La revisión de `design-qa.md` terminó en `passed`. La matriz de tema claro,
alto contraste, escala 200 %, teclado y lector de pantalla pertenece a 11C.

## Pruebas locales

| Gate | Resultado |
|---|---|
| UI de muestra | 10/10 |
| Regresión de bandeja | 12/12 |
| `ProviderPaletteTests` | 5/5 |
| Arquitectura | 22/22 |
| Plataforma Windows | 21/21 |
| App empaquetada x64 | build correcto |
| App ARM64 | cross-build correcto |
| Solución x64 | build correcto |
| Recursos en-US / es-ES | 60/60; sin claves faltantes |
| Paquetes vulnerables | ninguno en los orígenes actuales |
| `git diff --check` | sin errores |
| Búsqueda de secretos | sin credenciales; un falso positivo en `_lastRevealToken` |

## Incidentes y correcciones

1. Compartir una `PathGeometry` entre sombra y color cerró el proceso dentro de
   WinUI. El control ahora guarda dos geometrías por segmento y muta ambas sin
   asignarlas de nuevo durante cada frame.
2. La búsqueda de Antigravity corrió antes de que UI Automation asentara el
   scroll. La aserción espera hasta 1 s y sigue fallando si el contenido falta.
3. `winapp ui focus` devolvió éxito mientras Windows enfocaba la barra del área
   de notificación. La regresión automatizada usa `InvokePattern`. La navegación
   real con Win+B queda como prueba manual de 11C.

## Revisión externa

Grok Build trabajó en modo de solo lectura y devolvió `accept` en la sesión
`5e0424f2-7eca-48d5-b361-7c44d81b531d`. El recibo final está en
`.scratch/agent-cli-delegation/grok-build/runs/2026-07-22T05-27-12-103Z-review-0daece8c/result.json`.
Los P2 de documentación y nombre de test se corrigieron antes de cerrar.

## Límite de la afirmación

Este corte prueba datos sintéticos y la ruta WinUI actual. No prueba cuotas
reales, gasto real, autenticación, persistencia, tema claro, alto contraste
visual, escala 200 %, teclado de bandeja ni lector de pantalla.
