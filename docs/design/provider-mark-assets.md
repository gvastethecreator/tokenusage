# Assets de marca de proveedores

Fecha: 2026-08-11

Estado: assets empaquetados para proveedores activos y módulos preparados

## Procedencia

Nueve SVG fuente bajo `src/TokenUsage.App/Assets/ProviderMarks/` proceden de
`robinebers/openusage@487cc8f19a9a28676f6924aafa48dee79ad7a7f6`, ruta
`Sources/OpenUsage/Resources/ProviderIcons/`. El repositorio fuente publica el
código con licencia MIT. El aviso requerido está en `THIRD-PARTY-NOTICES.md`.

| Asset | Proveedor visible | Ajuste de presentación |
|---|---|---|
| `codex.svg` | Codex | relleno `#10A37F` |
| `claude.svg` | Claude | relleno `#DE7356` |
| `grok.svg` | Grok Build | relleno `#7C5CFC` |
| `opencode.svg` | OpenCode | relleno `#E5488C` |
| `antigravity.svg` | Antigravity CLI | conserva `#4285F4` |
| `copilot.svg` | GitHub Copilot | relleno visible fijo para temas claro y oscuro |
| `devin.svg` | Devin | relleno visible fijo para temas claro y oscuro |
| `openrouter.svg` | OpenRouter | relleno visible fijo para temas claro y oscuro |
| `zai.svg` | Z.ai | relleno visible fijo para temas claro y oscuro |

`cursor.svg` conserva su procedencia oficial de Cursor registrada en
`THIRD-PARTY-NOTICES.md`; no forma parte de esos nueve archivos derivados del
upstream de OpenUsage.

`zcode.svg` es un glifo original de TokenUsage (letra "Z" simple); no procede
de un upstream de marcas ni representa el logo oficial de ZCode. Usa el
relleno `#4E6BFF` de la paleta de visualización.

No se modificaron los trazados. Los rellenos sólidos enlazan cada marca con el
color base al que llega su gradiente en el donut. Son colores de visualización propios de
TokenUsage; no se presentan como colores oficiales de los proveedores. La
paleta separa Grok Build y OpenCode, que en el primer corte usaban dos grises
cercanos.

La app carga estos SVG por medio de `SvgImageSource`. Este camino mantiene la
marca nítida en las escalas de pantalla de Windows y evita un segundo asset
derivado.

## Uso y límites

- Las marcas identifican proveedores dentro de tarjetas y leyendas.
- No forman el logo ni la identidad de TokenUsage o TokenUsage.
- No expresan afiliación o aprobación.
- La revisión de identidad final, dominio, logo, Publisher y canal sigue en el
  Ticket 02.
- Un cambio de asset debe conservar fuente, commit, licencia y revisión visual
  a 14 y 16 DIPs antes de aceptarse.

El donut se genera con geometría de datos en WinUI. Cada arco parte de una
variante oscura y llega al color base mediante un `LinearGradientBrush`. Un
trazo negro con alfa `0x30`, desplazado 0,75 DIP, añade separación leve entre
capas. Alto contraste reemplaza los cinco gradientes por el color de realce que
el usuario eligió en Windows y oculta la sombra.

## Proveedores en gate

Copilot, Devin y OpenRouter pueden empaquetar su marca porque la UI los identifica
como módulos preparados, no como fuentes activas. Z.ai aparece como bloqueado.
Kilo Code y Zed no añaden una marca al paquete mientras sus gates no permitan
una superficie pública. Cuando una fuente se apruebe, el ticket de integración
debe fijar el asset oficial, licencia, SHA de procedencia, color de visualización
y captura a 14 y 16 DIPs antes de incluirlo.
