# Design QA — Dashboard compacto 11A.1

Fecha: 2026-07-22

## Evidencia de comparación

- Verdad visual: `docs/design/selected-flyout-option-1.png`.
- Base del corte: `artifacts/ticket-11a1-baseline/02-normal.png`.
- Implementación: `artifacts/ticket-11a1/02-normal.png`.
- Comparación completa: `artifacts/ticket-11a1/design-qa-comparison.png`.
- Recorte de donut y leyenda:
  `artifacts/ticket-11a1/design-qa-comparison-donut.png`.
- Estado: muestra normal, periodo de 30 días, dashboard en el tope del scroll.
- Viewport lógico: 320 × 720 DIPs.
- Fuente: 836 × 1881 px.
- Implementación: 400 × 900 px a 125 % de escala de Windows.
- Normalización: la implementación se amplió a 836 × 1881 px con Lanczos.
  Ambas imágenes tienen la misma relación de aspecto; no hubo recorte ni
  deformación. La comparación completa mide 1672 × 1881 px y el recorte mide
  1672 × 650 px.

La fuente solo existe en tema claro y usa datos distintos. La implementación se
capturó con el tema oscuro del sistema. La comparación evalúa jerarquía,
proporciones, densidad, ritmo, tipografía relativa, activos, color de series y
copy. No afirma paridad de color entre temas; la matriz claro, oscuro, alto
contraste y 200 % pertenece a 11C.

## Findings

No quedan diferencias P0, P1 o P2 que sean accionables dentro de 11A.1.

El recorte enfocado confirma que Grok Build y OpenCode ya no comparten dos
grises cercanos. Violeta y rosa se distinguen en arco, marca y fila. Cada arco
parte de una variante oscura y llega al color base que usa su marca. Una sombra
negra de alfa `0x30`, desplazada 0,75 DIP, separa las capas sin tapar huecos ni
centro.

## Superficies de fidelidad

- Tipografía: Segoe UI y los estilos de WinUI conservan jerarquía, peso y
  lectura. El cambio de paleta no altera medida, salto ni truncado.
- Espaciado y layout: permanecen el ancho de 320 DIPs, la tarjeta de 12 DIPs, el
  donut de 104 DIPs y la densidad aceptada en 11A.
- Colores y tokens: los cinco arcos usan recursos semánticos oscuro → base.
  Grok Build usa violeta; OpenCode usa rosa; Codex usa verde agua. La sombra
  conserva 0,75 DIP y alfa `0x30`. Alto contraste mantiene un brush sólido con
  el realce del sistema y oculta la sombra. Nombres e importes evitan que el
  dato dependa del color.
- Calidad de activos: las cinco marcas siguen como SVG vectoriales copiados del
  commit fijado de OpenUsage. Solo cambian los rellenos sólidos de Grok y
  OpenCode para enlazar marca y color base; no cambian los trazados.
- Copy y contenido: `Muestra`, periodo y `Datos de muestra` conservan la
  procedencia visible. `WOpenUsage 0.1` se mantiene durante la transición a
  TokenUsage.
- Estados e interacción: normal, cerca del límite y parcial/vencido se
  recorrieron con el mismo test. Refresco, selector de muestra, opciones y salida
  funcionaron 10/10. La animación y la geometría no cambiaron.
- Accesibilidad: el resumen accesible, el texto y el orden siguen dando el
  significado. Reduced motion y alto contraste conservan sus rutas de 11A; su
  prueba visual amplia sigue en 11C.

## Desvíos aceptados

- La fuente es clara y la captura usa el tema oscuro actual del equipo.
- La fuente muestra cuatro series y la implementación cinco, según el alcance
  aprobado de Codex, Claude, Grok Build, OpenCode y Antigravity CLI.
- Los gradientes y sus sombras son una paleta de datos de WOpenUsage. No se
  presentan como colores oficiales ni como afiliación de los proveedores.
- El segmento de Antigravity CLI es muy pequeño y su gradiente puede verse casi
  sólido; mantiene un azul distinto y la fila muestra nombre e importe.

## Historial de comparación

1. 11A corrigió activos vacíos, alto contraste, reduced motion y costo por frame.
2. La captura base de 11A.1 mostró dos series grises cercanas para Grok Build y
   OpenCode. Se clasificó como P2 de relación rápida entre arco y leyenda.
3. Se cambiaron Grok Build a violeta y OpenCode a rosa; los cinco arcos pasaron
   a gradientes diagonales de dos tonos.
4. La captura final mantuvo geometría, densidad y layout. El recorte conjunto
   prueba la separación posterior al cambio.
5. El usuario pidió gradientes oscuro → base y una sombra muy leve por capa. La
   primera sombra compartió geometría y WinUI cerró el proceso. La corrección
   mantiene dos geometrías cacheadas por segmento y pasó UI 10/10.
6. Grok revisó la dirección final y devolvió `accept`. Sus P2 de documentación
   y nombre de test se corrigieron sin cambiar la captura.

## Implementation Checklist

- [x] Cinco gradientes oscuro → base en recursos XAML.
- [x] Sombra cacheada de 0,75 DIP, oculta en alto contraste.
- [x] Grok Build y OpenCode con colores distintos en arco e icono.
- [x] Codex con variante verde oscura y color base verde agua.
- [x] Fallback y alto contraste siguen como brushes sólidos del sistema.
- [x] Test estructural enlaza colores, stops, SVG y brushes del sistema.
- [x] Estado normal, cerca del límite y parcial/vencido capturados.
- [x] Comparación completa y recorte enfocado inspeccionados.

## Follow-up Polish

Claro, alto contraste, 200 %, teclado y lector de pantalla siguen en 11C. Este
corte no cambia ni cierra esos gates.

final result: passed
