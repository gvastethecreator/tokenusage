# Ticket 11B — Estados, detalle y métricas bajo demanda

Fecha: 2026-07-22
Estado: aceptado; 11C sigue abierto

## Resultado

- Cada tarjeta expone un botón con nombre, ayuda y `AutomationId` estable.
- El flyout muestra fuente y hora observada. Codex usa la API local oficial y
  `SourceObservedAtUtc`; las muestras se marcan como tales.
- Las cuatro métricas diarias de Codex quedan plegadas por defecto. Cuota,
  porcentaje restante, reset y ritmo siguen visibles.
- La barra accesible incluye reset. El estado de muestra usa un solo live region
  y conserva los IDs previos según el estado actual.
- El pie anuncia cambios de estado con live region cortés.
- No se muestran paths, payloads, correo, tokens de sesión ni credenciales.

## Integración real

La app empaquetada x64 leyó una sesión Codex instalada, mostró
`Provider.Codex.Details` y publicó en `HelpText` la fuente `API local oficial`
con la hora local observada. El refresh manual conservó la tarjeta. No se tomó
captura de la cuota real.

## Grok Build

La primera edición amplia agotó 8 turnos tras errores `read_file`. Se cambió el
contrato a un archivo por snapshot, sin exploración. La edición terminó en 3
turnos:

- modelo: sesión `238706a1-f82d-486c-8cbc-2440d3bee253`, USD 0.060854.

Tres revisiones finales corrieron a la vez, cada una con un archivo:

- XAML: `7f7739e2-a2d9-4a10-8984-8c5f7732c98f`, USD 0.0905536.
- proyector: `1f116e52-fd0a-416e-83a7-6f9144f866fb`, USD 0.0943872.
- UIA: `a58f72d8-9a56-45bf-9dc3-e6b61deaf41f`, USD 0.0677312.

Se aceptaron el live region único y pruebas UIA específicas. Se rechazó el
riesgo de división por cero porque `ProgressMetricSnapshot` exige límite mayor
que cero. La captura real del flyout descartó clipping.

## Prueba

| Check | Resultado |
|---|---:|
| Arquitectura x64 | 22/22 |
| Core x64 | 44/44 |
| Providers x64 | 116/116 |
| Plataforma Windows x64 | 52/52 |
| UIA empaquetada | 6/6 |
| Codex real empaquetado | pasó |
| Build WinUI x64 | 0 advertencias, 0 errores |
| Build WinUI ARM64 | 0 advertencias, 0 errores |
| `git diff --check` | pasó |

Recibos de muestra:

- `artifacts/ticket-11b/01-usage-surface.png`
- `artifacts/ticket-11b/02-near-limit.png`
- `artifacts/ticket-11b/03-partial.png`
- `artifacts/ticket-11b/04-provider-details.png`
- `artifacts/ticket-11b/ui-results.json`

## Límite

11B no cierra tema claro, alto contraste, escala de texto 200 %, Narrator ni
comparación visual final. Esos gates quedan en 11C. ARM64 tiene build cruzado,
sin runtime nativo.
