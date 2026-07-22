# Design QA — Dashboard compacto 11A

Fecha: 2026-07-22

## Evidencia de comparación

- Verdad visual: `docs/design/selected-flyout-option-1.png`
- Implementación: `artifacts/ticket-11a/02-normal.png`
- Comparación conjunta final: `artifacts/ticket-11a/design-qa-comparison-final.png`
- Estado: muestra normal, periodo de 30 días, dashboard en el tope del scroll.
- Viewport lógico: 320 × 720 DIPs.
- Fuente: 836 × 1881 px; mock generado para el mismo encuadre lógico.
- Implementación: 400 × 900 px a 125 % de escala de Windows.
- Normalización: la implementación se amplió a 836 × 1881 px. Ambas imágenes
  tienen la misma relación de aspecto, por lo que no hubo recorte ni deformación.

La fuente solo existe en tema claro y usa datos distintos. La implementación se
capturó con el tema oscuro actual del sistema. La comparación evalúa jerarquía,
proporciones, densidad, ritmo, tipografía relativa, activos y copy. No afirma
paridad de color entre temas; la matriz claro/oscuro/alto contraste pertenece a
11C.

No se necesitó un recorte de detalle: en la comparación de 1672 × 1881 px se
leen el total, la leyenda, los cinco activos, los dos grupos de cuota y el footer.
También se abrieron por separado las imágenes originales para revisar bordes y
nitidez.

## Findings

No quedan diferencias P0, P1 o P2 que sean accionables dentro de 11A.

## Superficies de fidelidad

- Tipografía: Segoe UI y los estilos tipográficos de WinUI conservan la
  jerarquía de título, total, proveedor, cuota y metadatos. El cambio frente a la
  tipografía de la referencia es la traducción aprobada a Windows.
- Espaciado y layout: el ancho base se mantiene en 320 DIPs, las tarjetas usan
  radio semántico de 12 DIPs y el contenido muestra dos proveedores en el área
  visible. La mayor densidad responde al pedido de condensar la interfaz.
- Colores y tokens: el tema y los estados usan recursos de WinUI; donut e iconos
  usan la paleta documentada por proveedor. El estado cerca del límite combina
  color de cautela con texto y porcentaje.
- Calidad de activos: las cinco marcas son SVG vectoriales copiados del commit
  fijado de OpenUsage. No hay emoji, glifos de texto ni logos dibujados en código.
- Copy y contenido: `Muestra`, el periodo y `Datos de muestra` mantienen la
  procedencia visible. `WOpenUsage 0.1` se conserva durante la transición formal
  a TokenUsage.

## Desvíos aceptados

- El mock fuente es claro; la captura final respeta el tema oscuro del sistema.
- La fuente enseña un selector Hoy/Ayer/30 días, compartir, reordenar, refrescar
  por proveedor y expandir. 11A valida el resumen compacto con datos de muestra;
  esos controles siguen en 11B y no se simulan aquí.
- La implementación muestra cinco proveedores en el donut y dos tarjetas sobre
  el pliegue. La fuente usa cuatro proveedores y una tarjeta. Los fixtures de
  WOpenUsage cubren el alcance aprobado de Codex, Claude, Grok Build, OpenCode y
  Antigravity CLI.

## Historial

1. Preflight visual, antes de una comparación conjunta válida: las columnas de
   marca aparecían vacías. Se detectó texto ajeno al inicio de los SVG, se
   restauraron los cinco archivos y se verificó su carga vectorial a tamaño real.
2. Primera comparación conjunta: `artifacts/ticket-11a/design-qa-comparison-1.png`.
   No encontró una diferencia P0/P1/P2 dentro del corte 11A.
3. Revisión de Grok y reparación técnica: se corrigieron alto contraste, estado
   sin animación y costo por frame. La captura final conservó el resultado visual.
4. Comparación final: `artifacts/ticket-11a/design-qa-comparison-final.png`.
   Donut, activos, densidad, radios, copy y jerarquía permanecen sin fallos
   visuales P0/P1/P2.

## Implementation Checklist

- [x] Donut real con valores de los fixtures.
- [x] Cinco marcas vectoriales con procedencia y licencia.
- [x] Interfaz de 320 DIPs más compacta.
- [x] Barras y donut con entrada 0 → valor y easing de salida.
- [x] Estado final inmediato cuando Windows desactiva animaciones.
- [x] Resumen accesible que no depende de color ni movimiento.
- [x] Comparación conjunta normalizada e inspeccionada.

## Follow-up Polish

La paridad de controles y la matriz de tema, escala, teclado y lector de pantalla
siguen asignadas a 11B y 11C. Este resultado no las da por cerradas.

final result: passed
