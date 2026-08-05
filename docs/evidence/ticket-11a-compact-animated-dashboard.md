# Ticket 11A — Dashboard compacto y animado

Fecha: 2026-07-22
Baseline de implementación: `cb8fce8`
Estado: aceptado dentro del modo de muestra

## Resultado

- El gasto total usa un donut real de 104 DIPs. Sus segmentos salen de los
  importes del fixture, conservan el orden de proveedor y cierran el anillo.
- Los porcentajes muy pequeños mantienen un piso visual de 2,5 % antes de la
  normalización. El nombre accesible conserva los importes exactos.
- Donut y barras parten de cero y llegan a su valor con `CubicEase/EaseOut`.
  Las duraciones son 480 ms y 360 ms. Un token de snapshot evita reinicios por
  scroll o por volver de Opciones.
- `UISettings.AnimationsEnabled` selecciona el estado final inmediato cuando el
  usuario desactiva animaciones en Windows.
- Codex, Claude, Grok Build, OpenCode y Antigravity CLI usan SVG vectoriales del
  commit fijado de OpenUsage. La procedencia está en
  `docs/design/provider-mark-assets.md` y la licencia en
  `THIRD-PARTY-NOTICES.md`.
- La superficie conserva 320 DIPs de ancho, tarjetas de 12 DIPs de radio y una
  jerarquía más densa. La captura normal muestra el total y dos proveedores en
  720 DIPs de alto.
- `TokenUsage` queda registrado como nombre formal. Código, paquete y UI siguen
  con `TokenUsage`. El corte técnico de ADR-0002 se completó el 2026-08-04.

## Revisión con Grok Build

### Plan de implementación

- Sesión: `448013f0-3961-41b8-80a1-272b7e58685c`
- Stop reason: `EndTurn`
- Costo: USD 0.258216
- Recibo:
  `.scratch/agent-cli-delegation/grok-build/runs/2026-07-22T03-38-33-776Z-plan-26aaedcc/result.json`

Se aceptaron el donut de 104 DIPs, la razón interior 0,618, el espacio de 1,6
DIPs, el piso visual, el token de reveal y el uso de activos fijados. El padre
eligió `CubicEase/EaseOut`, compatible con WinUI, en lugar del easing propuesto
por el worker.

### Intento de edición acotada

- Sesión: `d3040ba7-5603-45ee-b095-1a38d3c243b4`
- Estado: cancelado tras un turno; cero archivos modificados.
- Costo: USD 0.0434984
- Recibo:
  `.scratch/agent-cli-delegation/grok-build/runs/2026-07-22T03-43-54-215Z-implement-3359cc2e/result.json`

La edición volvió a encontrar el límite de permisos de Grok en Windows. El
padre implementó y verificó el corte.

### Revisión independiente final

- Sesión: `8bf5eafb-a9a6-4451-94ae-ecf340025263`
- Stop reason: `EndTurn`
- Turnos: 12
- Costo: USD 0.502092
- Recibo:
  `.scratch/agent-cli-delegation/grok-build/runs/2026-07-22T04-16-04-591Z-review-69d45f2f/result.json`

El padre aceptó tres hallazgos: evitar una clave insegura en alto contraste,
eliminar el cuadro vacío con animaciones desactivadas y dejar de recrear todo
el árbol vectorial en cada frame. El donut ahora conserva sus geometrías y solo
actualiza puntos y barridos. Se rechazó aumentar el espaciado exterior a 14 DIPs
porque contradice el pedido de condensar la interfaz.

La primera corrección intentó escuchar `AccessibilitySettings.HighContrastChanged`.
El arranque instrumentado detectó una `COMException` en la suscripción. Se
reemplazó por `FrameworkElement.ActualThemeChanged`; el siguiente arranque fue
estable y toda la UI volvió a pasar.

## Prueba automatizada

| Prueba | Resultado |
|---|---:|
| Geometría y arquitectura | 17/17 |
| Plataforma Windows x64 | 21/21 |
| UI de muestra | 10/10 |
| Regresión de bandeja Ticket 05 | 12/12 |
| Recursos `en-US` / `es-ES` | 60/60 |
| Build WinUI con analyzer x64 | 0 advertencias, 0 errores |
| Build App ARM64 | 0 advertencias, 0 errores |
| Build solución x64 | 0 advertencias, 0 errores |
| Paquetes vulnerables | ninguno en los orígenes actuales |
| `git diff --check` | pasó |
| Scan de secretos sobre el diff | pasó |

Recibos de UI:

- `artifacts/ticket-11a/ui-results.json`
- `artifacts/ticket-11a/regression-05-ui-results.json`

## Prueba visual

- `artifacts/ticket-11a/02-normal.png`
- `artifacts/ticket-11a/03-near-limit.png`
- `artifacts/ticket-11a/04-partial-stale.png`
- `artifacts/ticket-11a/design-qa-comparison-final.png`
- `design-qa.md`: `final result: passed`

La referencia de 836 × 1881 px y la captura de 400 × 900 px representan el
mismo viewport de 320 × 720 DIPs. La implementación se normalizó a la medida de
la fuente y se evaluó en una sola imagen conjunta.

## Contratos de plataforma

- SVG en WinUI: <https://learn.microsoft.com/en-us/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.media.imaging.svgimagesource?view=windows-app-sdk-1.8>
- Preferencia de animación: <https://learn.microsoft.com/en-us/uwp/api/windows.ui.viewmanagement.uisettings.animationsenabled?view=winrt-26100>
- Recursos de tema y contraste:
  <https://learn.microsoft.com/en-us/windows/apps/develop/platform/xaml/xaml-theme-resources>

## Límite de afirmación

- Los datos siguen siendo fixtures. Este corte no prueba detección, cuota,
  gasto, cache ni persistencia de un proveedor real.
- La prueba visual cubre este equipo en tema oscuro y escala 125 %.
- ARM64 tiene prueba cruzada de build, no runtime.
- Las rutas de animación reducida y cambio de tema compilan y tienen revisión
  estática; no se cambió la configuración global de Windows para observarlas.
- Claro, alto contraste, 200 %, Narrator y la paridad de controles siguen en
  11C. El corte no declara cerrado el Ticket 11 completo.
