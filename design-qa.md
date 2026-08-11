# Design QA — Integración cache-first 07D

Fecha: 2026-07-22

## Evidencia conjunta

- Verdad visual: `docs/design/selected-flyout-option-1.png`.
- Implementación: `artifacts/ticket-07d/02-normal.png`.
- Comparación conjunta: `artifacts/ticket-07d/design-qa-comparison.png`.
- Estados: `03-near-limit.png`, `04-partial-stale.png`, `05-stale.png` y
  `06-error-cache.png` dentro de `artifacts/ticket-07d/`.
- Viewport lógico: 320 × 720 DIPs.
- Fuente: 836 × 1881 px.
- Implementación: 560 × 1260 px a 175 % de escala de Windows.
- Normalización: la implementación se llevó a 836 × 1881 px con bicúbico de
  alta calidad. La comparación final mide 1672 × 1881 px.

La fuente usa tema claro y cuatro series. La app usa el tema oscuro del sistema
y cinco providers aprobados. La comparación evalúa jerarquía, proporciones,
densidad, orden, activos, copy y continuidad durante el refresco. La matriz de
tema claro, alto contraste, escala 200 %, teclado y lector sigue en 11C.

## Findings

No quedan diferencias P0, P1 o P2 dentro del alcance de 07D.

El estado de caché ocupa el subtítulo del periodo y el progreso usa un ring de
14 DIPs junto al total. El panel conserva su alto y la tarjeta de gasto no
cambia de tamaño. Éxito, parcial, vencido y error tienen texto visible; color y
animación solo refuerzan ese texto.

La división por escenario evita combinar cifras de una muestra con el shell de
otra. Durante un cambio se conserva el dashboard anterior completo hasta que
el nuevo outcome llega. En un refresco del mismo escenario se publica su caché
antes del fake y el donut sigue visible.

## Superficies revisadas

- Tipografía: estilos WinUI y Segoe UI conservan la jerarquía aceptada en 11A.
- Layout: ancho de 320 DIPs, padding de 14 DIPs, donut de 104 DIPs y tarjetas
  compactas sin una fila nueva para el estado.
- Activos: las cinco marcas SVG, gradientes oscuro a color base y sombras leves
  conservan la decisión aceptada en 11A.1.
- Copy: `Muestra` y `Datos de muestra` mantienen la procedencia visible.
- Interacción: selector, refresco, caché visible, retry, scroll, opciones,
  cierre por pérdida de foco y salida pasaron por UI Automation.
- Accesibilidad: los controles mantienen AutomationId; los estados usan texto
  y live region polite. Importes y nombres acompañan a cada color.

## Desvíos aceptados

- Fuente clara frente al tema oscuro actual.
- Cuatro series en la fuente frente a cinco providers del alcance.
- Datos distintos: la captura solo usa cifras sintéticas y las etiqueta.
- La prueba visual amplia de temas, 200 %, teclado y lector permanece en 11C.

## Implementation Checklist

- [x] Caché visible antes del refresco para el mismo escenario.
- [x] Dashboard visible mientras el refresh está activo.
- [x] Estados fresh, partial, stale, error, not-saved y unavailable modelados.
- [x] Particiones normal, near-limit, partial y stale sin mezcla.
- [x] Error usa el último Normal; primer Error sin Normal queda unavailable.
- [x] UIA de muestra 13/13, incluido primer error sin caché.
- [x] Regresión de bandeja 12/12.
- [x] Comparación conjunta inspeccionada.

final result: passed

---

# Design QA — Rediseño Global/Proveedor definitivo

Fecha: 2026-08-09

## Evidencia runtime

- Lista: `.scratch/design-audit/2026-08-09/ruthless/proof/after.png`.
- Circular: `.scratch/design-audit/2026-08-09/ruthless/proof/flyout-circular.png`.
- Mapa: `.scratch/design-audit/2026-08-09/ruthless/proof/heatmap-final.png`.
- Hover exacto: `.scratch/design-audit/2026-08-09/ruthless/proof/detail.png`.
- Proveedor Codex: `.scratch/design-audit/2026-08-09/ruthless/proof/provider-codex.png`.
- Proveedores Grok/OpenCode/Antigravity: capturas separadas en el mismo directorio.
- Informe Global/Proveedor: `.scratch/design-audit/2026-08-09/ruthless/proof/report-global.png` y `report-provider.png`.
- Comparaciones conjuntas: `.scratch/design-audit/2026-08-09/ruthless/comparisons/`.
- Finish ledger: `.scratch/design-audit/2026-08-09/ruthless/finish-ledger.md`.

## Cierre

La interfaz usa negro/gris neutro, orden estable Codex/OpenCode/Antigravity/Grok, una jerarquía de lectura única y controles contextuales. Lista, Circular y Mapa son alternativas persistentes. El Mapa comunica rango, intensidad, proveedores y datos diarios exactos sin texto permanente. El informe recompone el encabezado en DPI alto y reduce ticks en mini-gráficos para evitar solapes.

Los estados ausente, parcial, estimado y sin precio conservan su procedencia: nunca se convierten en cero o costo completo. La revisión independiente abrió 1 P1 y 4 P2; todos fueron reparados y el veredicto final confirmó cero P0, P1 o P2.

Verificación final: `scripts/check.ps1 -Platform x64 -Configuration Release`, 855 pruebas verdes y MSIX x64 correcto.

final result: passed

---

# Design QA — Dashboard de uso inspirado en T3 Code

Fecha: 2026-08-08

## Evidencia conjunta

- Fuente de costo: `C:/Users/cristian/AppData/Local/Temp/codex-clipboard-4c42cf96-2127-407e-a5db-a706066aa20a.png`.
- Implementación de costo: `.scratch/ui/usage-report-reference-viewport.png`.
- Comparación de costo: `.scratch/ui/usage-report-comparison.png`.
- Fuente de tokens: `C:/Users/cristian/AppData/Local/Temp/codex-clipboard-0958ac6d-b7a2-4e16-ad70-7fa7d0701edc.png`.
- Implementación de tokens: `.scratch/ui/usage-report-tokens-reference-viewport.png`.
- Comparación de tokens: `.scratch/ui/usage-report-tokens-comparison.png`.
- Hover diario: `.scratch/ui/usage-report-hover-tokens-day.png`.
- Tooltip de cobertura: `.scratch/ui/usage-report-coverage-tooltip-final.png`.
- Viewport de costo: 1182 × 839 px, tema oscuro, 30 días, costo y desglose por modelo.
- Viewport de tokens: 1148 × 723 px, tema oscuro, 30 días, tokens y desglose por modelo.
- Capturas realizadas sobre la app WinUI x64 empaquetada, con datos locales reales.

## Comparación completa

La implementación conserva la jerarquía de la referencia: resumen y proveedores
a la izquierda, serie diaria a la derecha, métricas compactas en una fila y
desglose con calidad del costo debajo. No hay recortes, solapamientos ni saltos
de alineación en los dos viewports de referencia.

Las diferencias son deliberadas: marco nativo de Windows, cuatro proveedores
locales en vez de dos y cobertura de precio en vez del ahorro hipotético de
caché. La app no presenta el costo estimado como una factura.

## Comparación enfocada

La comparación de tokens usa el segundo viewport de referencia y la misma
selección de 30 días. El gráfico mantiene la escala legible y el estado hover
muestra fecha, total y aporte por proveedor. Los selectores 7/30/90, costo/tokens
y modelo/día fueron comprobados con UI Automation.

## Superficies revisadas

- Tipografía: jerarquía compacta, cifras tabulares y etiquetas secundarias sin bloat.
- Layout: columnas estables, tarjeta de cinco métricas y tabla alineada en ambos viewports.
- Color: una serie por proveedor, contraste suficiente y soporte de alto contraste en el control.
- Copy: costo observado, estimación, cobertura y tokens sin precio están separados con claridad.
- Interacción: apertura en ventana independiente, refresco, filtros, scroll inicial, hover del gráfico y tooltip de cobertura.
- Accesibilidad: AutomationId estable en todos los controles principales, nombres y HelpText en el hint y navegación de teclado en el gráfico.

## Historial de comparación

1. La primera salida reveló un recurso `SharedShadow` inexistente; se retiró y se eliminó el crash de XAML.
2. La segunda salida reveló un offset inicial del ScrollViewer; la carga ahora vuelve a la parte superior.
3. La tercera salida reveló que el tooltip enlazado no se abría después de cargar datos; ahora se controla explícitamente en hover y foco.
4. Las comparaciones finales de costo y tokens no muestran diferencias P0, P1 o P2 dentro del alcance.

final result: passed
