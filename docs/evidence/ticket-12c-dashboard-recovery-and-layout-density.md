# Ticket 12C — recuperación y densidad del dashboard

Fecha: 2026-07-23

Estado: implementado y verificado en x64 y ARM64.

## Resultado

- El dashboard usa 400 DIP de ancho y ajusta ese valor al área útil real del monitor.
- La app vuelve a medir el alto con el ancho final para evitar recortes con DPI alto o pantallas estrechas.
- Títulos y cifras usan una escala más compacta, con 12 DIP como mínimo, y más separación entre grupos.
- Orden, visibilidad y reset tienen acceso por teclado, nombres de UI Automation y áreas activas de 32 DIP.
- `Ctrl+Z` deshace el último cambio de layout de la sesión sin interceptar la edición de texto.
- El reset global pide confirmación, guarda el estado vacío y se puede deshacer durante la sesión.
- Un fallo al guardar conserva el historial y no publica un estado que no quedó persistido.

## Delegación y revisión

Dos intentos Grok amplios fallaron al leer archivos en Windows y se descartaron
sin aplicar cambios. El corte reducido `ticket-12c-layout-session-history-v2`
creó el historial de sesión en un solo archivo. Informó 3 turnos, 65 232 tokens
y USD 0.0615072. El padre revisó el resultado, corrigió CA1512, añadió pruebas e
integró la operación transaccional.

La primera revisión independiente encontró un P2: 400 DIP podían superar el
ancho físico disponible tras el escalado. `FlyoutSizePolicy` ahora limita el
ancho en DIP con el área útil y el DPI, y `MainWindow` usa ese mismo ancho para
medir y colocar la ventana. La revisión final dio `ACCEPT`, sin P0–P2.

## Prueba empaquetada

`tests/ui/ticket-12c-dashboard-recovery.ps1` lanza la app empaquetada con
`winapp run`. No ejecuta el binario del paquete de forma directa.

- Configure v4: 3/3.
- Verify v4 tras reinicio: 1/1.
- Probó mover Codex con Enter, ocultar Antigravity con Espacio, deshacer con
  `Ctrl+Z`, cancelar y confirmar el reset, deshacer el reset y cargar el layout
  restaurado en un proceso nuevo.
- Confirmó que el historial de sesión queda vacío tras reiniciar.
- Evidencia: `.scratch/ui/ticket-12c/configure-v4` y
  `.scratch/ui/ticket-12c/verify-v4`.

## Prueba visual

- Dashboard ancho y compacto: `.scratch/ui/ticket-12c/visual-v2/dashboard-wide.png`.
- Controles de layout: `.scratch/ui/ticket-12c/visual-v2/options-top.png`.
- Diálogo de reset: `.scratch/ui/ticket-12c/configure-v3/01-reset-confirmation.png`.

Las capturas muestran menos saltos de línea, grupos separados y una lectura más
clara de total, gráfico, leyenda y proveedores.

## Gates finales

- Architecture: 62/62.
- Core: 134/134.
- CLI: 82/82.
- Providers: 274/274.
- Platform Windows: 101/101.
- MSIX Release x64: `BUILD SUCCEEDED`.
- MSIX Release ARM64: `BUILD SUCCEEDED`.
- `git diff --check`: sin errores.
