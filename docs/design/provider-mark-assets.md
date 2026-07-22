# Assets de marca de proveedores

Fecha: 2026-07-22

Estado: assets de prototipo para el corte visual 11A

## Procedencia

Los cinco SVG fuente bajo `src/WOpenUsage.App/Assets/ProviderMarks/` proceden de
`robinebers/openusage@9d2bf09f10e21f769494a525a9d65c84d7aeb1df`, ruta
`Sources/OpenUsage/Resources/ProviderIcons/`. El repositorio fuente publica el
código con licencia MIT. El aviso requerido está en `THIRD-PARTY-NOTICES.md`.

| Asset | Proveedor visible | Ajuste de presentación |
|---|---|---|
| `codex.svg` | Codex | relleno `#10A37F` |
| `claude.svg` | Claude | relleno `#DE7356` |
| `grok.svg` | Grok Build | relleno `#98989D` |
| `opencode.svg` | OpenCode | relleno `#AEAEB2` |
| `antigravity.svg` | Antigravity CLI | conserva `#4285F4` |

No se modificaron los trazados. Los colores siguen la paleta por proveedor de
`TotalSpendCard.swift` en el mismo commit. El cambio evita que marcas negras se
pierdan en el tema oscuro del prototipo.

La app carga estos SVG por medio de `SvgImageSource`. Este camino mantiene la
marca nítida en las escalas de pantalla de Windows y evita un segundo asset
derivado.

## Uso y límites

- Las marcas identifican proveedores dentro de tarjetas y leyendas.
- No forman el logo ni la identidad de WOpenUsage o TokenUsage.
- No expresan afiliación o aprobación.
- La revisión de identidad final, dominio, logo, Publisher y canal sigue en el
  Ticket 02.
- Un cambio de asset debe conservar fuente, commit, licencia y revisión visual
  a 16 DIPs antes de aceptarse.

El donut se genera con geometría de datos en WinUI. No es un asset de marca y
no reemplaza ninguno de estos archivos.
