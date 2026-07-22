# Evidencia del Ticket 07B: contratos y proveedor fake

Fecha: 2026-07-22

Estado: aceptado. La caché y el enlace con la UI siguen en 07C y 07D.

## Resultado

- Core define `ProviderId`, `MetricId`, métricas de progreso y valor, procedencia,
  snapshot y frescura.
- `ProviderOutcome` tiene casos cerrados para éxito, parcial, sin configurar,
  cuenta no apta, throttle, fallo transitorio, fallo de contrato y bloqueo de
  política.
- `RefreshContext` recibe `TimeProvider`, último valor válido, flag de refresco
  forzado y umbral de vencimiento.
- Providers contiene `FakeProviderRuntime` con escenarios sintéticos de éxito,
  parcial, vencido y error.
- El escenario vencido sigue siendo un éxito con una observación antigua. La UI
  podrá derivar `Vencido` sin agregar un outcome que contradiga el ADR.
- El fake queda marcado como experimental y cada métrica declara
  `SourceKind.Synthetic` y `fake/1`.

## Decisiones

1. Se usó `TimeProvider` de .NET. No se agregó una interfaz de reloj propia.
2. El fake vive en Providers porque implementa un adaptador. Core conserva solo
   contratos y no referencia Providers.
3. La frescura usa `SourceObservedAtUtc`; `FetchedAtUtc` indica cuándo terminó el
   intento actual.
4. El límite por defecto es diez minutos y el dato vence solo después del límite,
   según la regla de producto “más de diez minutos”.
5. El caso parcial se llama `PartialSuccess`: el analizador CA1716 rechaza
   `Partial` como nombre público bajo warnings-as-errors.

## Pruebas

| Gate | Resultado |
|---|---|
| Core | 13/13 |
| Providers | 7/7 |
| Arquitectura | 22/22 |
| `scripts/check.ps1 -Platform x64` | correcto |
| Solución x64 | build correcto; 0 warnings/errores |
| Solución ARM64 | cross-build correcto; 0 warnings/errores |

Las pruebas fijan IDs, UTC, copias defensivas, métricas duplicadas, límite exacto
de diez minutos, edades no positivas, outcome parcial, error tipado, procedencia
sintética, cancelación y los cuatro escenarios del fake.

## Revisión Grok Build

- Plan de solo lectura: sesión `1f5682ec-fbdf-4737-899e-5b2a59e575be`.
- Revisión del código: sesión `2987dae2-5790-43e1-9ae2-90f92f284349`.
- Veredicto: `accept`, sin P0/P1.
- P2 corregidos: valor literal del umbral, edades no positivas, marcadores del
  fake y cancelación de detección.
- Confirmación posterior a las reparaciones: misma sesión, recibo
  `.scratch/agent-cli-delegation/grok-build/runs/2026-07-22T06-03-22-557Z-review-b0348808/result.json`;
  `accept`, sin P0/P1/P2 nuevos.

El padre rechazó dos sugerencias del plan: una interfaz `IClock` duplicaba el
BCL y ubicar el fake en Core mezclaba el contrato con un adaptador.

## Límites

07B no lee disco, red, procesos ni credenciales. Tampoco publica caché, escribe
JSON, reemplaza archivos o conecta estos outcomes con el ViewModel. Esos caminos
siguen en 07C y 07D.
