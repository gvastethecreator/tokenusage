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
| `grok.svg` | Grok Build | relleno `#7C5CFC` |
| `opencode.svg` | OpenCode | relleno `#E5488C` |
| `antigravity.svg` | Antigravity CLI | conserva `#4285F4` |

No se modificaron los trazados. Los rellenos sólidos enlazan cada marca con el
color base al que llega su gradiente en el donut. Son colores de visualización propios de
WOpenUsage; no se presentan como colores oficiales de los proveedores. La
paleta separa Grok Build y OpenCode, que en el primer corte usaban dos grises
cercanos.

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

El donut se genera con geometría de datos en WinUI. Cada arco parte de una
variante oscura y llega al color base mediante un `LinearGradientBrush`. Un
trazo negro con alfa `0x30`, desplazado 0,75 DIP, añade separación leve entre
capas. Alto contraste reemplaza los cinco gradientes por el color de realce que
el usuario eligió en Windows y oculta la sombra.

## Proveedores en gate

Kilo Code y Zed no añaden una marca al paquete mientras sus gates no permitan
una tarjeta pública. Cuando una fuente se apruebe, el ticket de integración debe
fijar el asset oficial, licencia, SHA de procedencia, color de visualización y
captura a 16 DIPs antes de incluirlo.
