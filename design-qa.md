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
