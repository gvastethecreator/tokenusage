# Evidencia del Ticket 07D: muestra cache-first conectada

Fecha: 2026-07-22

Estado: aceptado. Ticket 07 cerrado; Ticket 08 queda como siguiente corte.

## Resultado

- La App resuelve una raíz aislada en
  `ApplicationData.Current.LocalFolder/cache/sample/`.
- Normal, cerca del límite, parcial y vencido usan particiones distintas. Error
  comparte la partición Normal para mostrar un último dato coherente.
- `CacheFirstRefresh` publica la caché antes del fake. Un delay cancelable de
  1200 ms permite observar el panel y el ring mientras el resultado llega.
- Opciones permanece abierta durante el trabajo en segundo plano. Cambiar de
  escenario cancela el trabajo anterior mediante versión y token.
- El projector aplica solo el gasto y la sesión de Codex sobre el shell de cinco
  providers. Recalcula total, leyenda y nombre accesible.
- Éxito, parcial, vencido, error, fallo de guardado y ausencia de último dato
  tienen estados tipados. El primer Error sin caché Normal usa la superficie de
  no disponible con retry.
- Apagar el modo de muestra restaura el estado vacío. El toggle sigue apagado al
  iniciar y nunca persiste datos reales ni credenciales.

## Corrección de LocalState cifrado

La primera escritura funcionaba y los reemplazos siguientes terminaban como
`NotSaved`. Un probe .NET 10 dentro del `LocalState` real obtuvo:

- `File.Replace` sin backup: `IOException`, HRESULT `0x80070057`;
- `File.Replace` con backup: el mismo error;
- `File.Move(source, destination, overwrite: true)`: reemplazo correcto;
- dos upserts de `SnapshotStore`: valor final correcto y atributo `Encrypted`.

El store conserva temporal en la misma carpeta, `WriteThrough`, flush a disco,
mutex por documento y limpieza del temporal. El destino previo queda intacto si
Windows niega acceso.

## Prueba visual y de interacción

- UIA de muestra: `artifacts/ticket-07d/ui-results.json`, 13/13.
- Primer error sin último dato: `artifacts/ticket-07d/00-error-no-cache.png`.
- Normal: `artifacts/ticket-07d/02-normal.png`.
- Cerca del límite: `artifacts/ticket-07d/03-near-limit.png`.
- Parcial: `artifacts/ticket-07d/04-partial-stale.png`.
- Vencido: `artifacts/ticket-07d/05-stale.png`.
- Error con último dato: `artifacts/ticket-07d/06-error-cache.png`.
- Comparación con opción 1:
  `artifacts/ticket-07d/design-qa-comparison.png`.
- Regresión de bandeja:
  `artifacts/ticket-07d/tray-regression/ui-results.json`, 12/12.
- `design-qa.md`: `passed`.

## Gates locales

| Gate | Resultado |
|---|---|
| Arquitectura | 22/22 |
| Core | 32/32 |
| Providers, coordinator y projector | 17/17 |
| UI de muestra | 13/13 |
| Regresión de bandeja | 12/12 |
| `scripts/check.ps1 -Platform x64` | correcto |
| `scripts/check.ps1 -Platform ARM64` | correcto |
| Solución x64 | 0 warnings/errores |
| Solución ARM64 | 0 warnings/errores |

## Revisión Grok Build

- Plan: sesión `4aec5d8d-a477-4bf4-a57c-e9314281bf22`.
- Revisión final: sesión `05a05745-5eb5-4501-99e7-8b65fcea1033`.
- Primera vuelta: un P1 válido. La caché única no guardaba afinidad de escenario
  y podía combinar el shell Normal con cifras NearLimit tras un reinicio.
- Reparación: particiones estables por escenario, Error sobre Normal y tres
  pruebas del coordinator para partición, último dato y primer error.
- Segunda vuelta: `accept`, sin P0/P1/P2.
- Recibo final:
  `.scratch/agent-cli-delegation/grok-build/runs/2026-07-22T07-47-06-171Z-review-09a0cf1c/result.json`.

## Límite de la afirmación

Este corte prueba datos sintéticos, persistencia local, estados WinUI y el
recorrido cache-first. No prueba cuota ni gasto reales, autenticación, providers
de cliente, tema claro, alto contraste visual, escala 200 % ni lector de
pantalla. Esos puntos siguen en tickets posteriores.
