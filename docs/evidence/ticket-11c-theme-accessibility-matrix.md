# Ticket 11C — Temas, escala y acceso del dashboard

Fecha: 2026-07-22
Estado: aceptado para el dashboard central; la validación manual con voz queda en 39

## Resultado

- `App.xaml` define recursos separados para Light, Dark y HighContrast.
- Los arcos conservan el gradiente de oscuro a color en Light y Dark. En alto
  contraste usan el color de énfasis del sistema y ocultan la sombra.
- Los logos cambian a iniciales monocromas en alto contraste.
- Los recursos de arcos, sombras y marcas cambian en vivo. No requieren reinicio.
- A 200 % el resumen de gasto cambia a una columna, el pie conserva Opciones y
  los valores completos siguen disponibles mediante scroll.
- Los importes, porcentajes restantes, resets y métricas dejaron de usar
  elipsis.
- Los iconos no crecen como texto. Los botones conservan tamaño mínimo.
- Los argumentos de tema y muestra que abren el flyout existen solo en Debug.

## Matriz visual empaquetada x64

| Tema | 100 % | 200 % |
|---|---|---|
| Light | `artifacts/ticket-11c/01-light-100.png` | `artifacts/ticket-11c/02-light-200.png` |
| Dark | `artifacts/ticket-11c/03-dark-100.png` | `artifacts/ticket-11c/05-dark-200.png` |
| HighContrast | `artifacts/ticket-11c/04-high-contrast-100.png` | `artifacts/ticket-11c/06-high-contrast-200.png` |

Pruebas extra:

- `02b-light-200-scrolled.png`: contenido inferior a 200 %.
- `07-live-high-contrast-toggle.png`: cambio a alto contraste con la app viva.
- `08-live-theme-recovery.png`: vuelta a Light en el mismo proceso.
- `text-200-uia.json`: nombres completos de importes, cuota y resets a 200 %.
- `ui-results.json`: ocho controles interactivos con nombre e ID.

Las capturas usan el mismo escenario Normal, ancho de 400 DIPs y datos de
muestra. La altura se ajusta al área útil del monitor y el cuerpo usa scroll.

## Teclado y lector

La prueba empaquetada recorrió con Tab, en este orden:

1. Actualizar.
2. Detalles de Codex.
3. Detalles de uso.
4. Detalles de Claude.
5. Detalles de Grok Build.
6. Detalles de OpenCode.
7. Detalles de Antigravity CLI.
8. Opciones.

Los ocho controles tienen `AutomationProperties.Name` y `AutomationId`. El
árbol UIA expone nombres completos para gasto, cuota, reset y ritmo. Esto cubre
el contrato técnico que consume Narrator. No se grabó ni evaluó salida de voz;
esa prueba manual, junto con más resoluciones y DPI, sigue en el ticket 39.

## Cambio de ajustes del sistema

Las pruebas de 200 % guardaron el valor previo de `TextScaleFactor`, emitieron
el cambio de accesibilidad y restauraron el estado en `finally`. El valor no
existía antes y quedó ausente después.

Alto contraste guardó los flags de `HIGHCONTRAST`, cambió 126 a 127 durante la
prueba y restauró 126. La app siguió viva al entrar y salir del modo.

## Grok Build

Las revisiones iniciales separaron recursos, donut y layout. Sesiones:

- `95a418a3-02a7-47e1-ad5d-61f9b7e2c932`.
- `e0167f8f-99cb-4dab-b654-9ad2fdc542ce`.
- `a2ba7c76-805f-42f5-8723-3b6b9fec5020`.

La revisión final usó snapshots sin secretos:

- tema/donut: `cb96cab7-de55-479b-b6bd-5b3a71fb30ad`, USD 0.1264792;
- contraste/arranque: `12f3ecf8-258b-4919-b035-deba0d29c6f3`, USD 0.106056;
- escala: `f390e952-0955-44df-acee-c474bdcf35d8`, falló por `read_file`
  y alcanzó cuatro turnos, USD 0.0622956.

Se aceptaron dos hallazgos: los argumentos de prueba necesitaban compilar solo
en Debug y el donut no cambiaba sus brushes al activar alto contraste en vivo.
El segundo fallo se reprodujo con captura y se corrigió con proxies XAML y
bindings a `ThemeResource`. La prueba en vivo pasó después del cambio.

## Prueba final

| Check | Resultado |
|---|---:|
| Arquitectura x64 | 25/25 |
| Core x64 | 44/44 |
| Providers x64 | 116/116 |
| Plataforma Windows x64 | 52/52 |
| Interactivos UIA | 8/8 |
| Temas a 100 % | 3/3 |
| Temas a 200 % | 3/3 |
| Cambio HighContrast en vivo | pasó |
| Build solución x64 Debug | 0 advertencias, 0 errores |
| Build app ARM64 Debug | 0 advertencias, 0 errores |
| `git diff --check` | pasó |

## Referencias

- Microsoft: text scaling supports 100–225 % and WinUI controls apply it by
  default: <https://learn.microsoft.com/windows/apps/develop/input/text-scaling>.
- Microsoft: theme resources and explicit HighContrast dictionaries:
  <https://learn.microsoft.com/windows/apps/develop/platform/xaml/xaml-theme-resources>.
- Microsoft: contrast-theme design guidance:
  <https://learn.microsoft.com/windows/apps/design/accessibility/high-contrast-themes>.
