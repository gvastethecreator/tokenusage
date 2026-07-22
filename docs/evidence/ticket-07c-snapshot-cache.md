# Evidencia del Ticket 07C: caché de snapshots

Fecha: 2026-07-22

Estado: aceptado. El enlace con la App y la prueba visual siguen en 07D.

## Resultado

- Core incluye `SnapshotStore` con un documento JSON v1 de snapshots
  normalizados. No guarda outcomes, warnings, credenciales ni respuestas crudas.
- La lectura distingue archivo ausente, documento válido, corrupción y versión
  futura. Una versión futura queda intacta y bloquea la escritura.
- Un documento corrupto se mueve a un archivo `corrupt-*` en la misma carpeta.
  El resultado no expone la ruta completa ni sirve datos parciales como cero.
- La escritura toma un mutex `Local` derivado del path, carga y mezcla bajo ese
  mutex, escribe un temporal único en la misma carpeta, hace flush a disco y usa
  `File.Move(..., overwrite: true)` para reemplazar dentro del mismo volumen.
- Los temporales huérfanos son inertes: nunca se leen ni se borran durante una
  lectura normal.
- `CacheFirstRefresh` publica el resultado de caché antes de llamar al runtime.
  Solo éxito y éxito parcial con el mismo provider ID actualizan el último valor.
- Un fallo de escritura conserva el outcome del provider y entrega un
  `CacheUpdateStatus` tipado. La cancelación sigue cancelando la secuencia.

## Límites del documento

- Esquema: `schemaVersion: 1`.
- Tamaño máximo: 4 MiB.
- Máximo: 256 providers y 512 métricas por provider.
- Métricas v1: progreso y valor escalar.
- Fechas: UTC; `sourceObservedAtUtc` no puede superar `fetchedAtUtc`.
- La lista de providers se escribe en orden estable; el orden de métricas se
  conserva.
- JSON UTF-8 con o sin BOM se acepta; los campos nulos no se emiten.

## Pruebas

| Gate | Resultado |
|---|---|
| Core | 32/32 |
| Providers | 7/7 |
| Arquitectura | 22/22 |
| Carrera multiproceso | correcta; dos procesos conservan ambos providers |
| `scripts/check.ps1 -Platform x64` | correcto |
| `scripts/check.ps1 -Platform ARM64` | correcto |
| Solución x64 | build correcto; 0 warnings/errores |
| Solución ARM64 | cross-build correcto; 0 warnings/errores |

Las pruebas cubren round-trip y orden, fixture independiente, BOM, corrupción
sintáctica y de dominio, entradas nulas, versión futura byte por byte, temporal
huérfano, primer write, reemplazo, fallo de reemplazo, merge multiproceso,
cancelación previa, orden cache-first, parcial, error, provider ID ajeno y fallo
de E/S al guardar.

## Revisión Grok Build

- Plan de solo lectura: sesión `e191e5c9-0e9c-4abe-af3b-ae36924692dc`.
- Revisión y confirmación: sesión `9a92af5c-036c-4f12-b4db-ea1af5c0226a`.
- Primera vuelta: un P1 válido; una entrada nula en arrays escapaba de la
  cuarentena.
- Reparación: rechazo explícito de snapshots y métricas nulas más dos casos de
  regresión.
- Segunda vuelta: `accept`, sin P0/P1/P2.
- Recibo final:
  `.scratch/agent-cli-delegation/grok-build/runs/2026-07-22T06-42-24-090Z-review-e0512321/result.json`.

## Decisiones y límites

1. 07C incluye un seam cache-first pequeño para probar el orden. El scheduler,
   timeout, backoff y fan-out siguen en M3.
2. El mutex usa el hash del path normalizado y nunca incluye el path sin hash.
3. La cuarentena conserva el archivo original. Si el move falla, la operación
   falla y no sobrescribe el archivo.
4. El store no migra versiones futuras ni las trata como corrupción.
5. 07C no resuelve `ApplicationData.LocalFolder`; la App elegirá el path en 07D.
6. 07C no cambia la UI ni afirma que el fake contiene datos reales.
7. 07D probó el store dentro del `LocalState` cifrado del paquete. `File.Replace`
   devolvió `0x80070057`; `File.Move` con overwrite conservó el atributo cifrado
   y permitió dos upserts. Los tests de lock ahora clasifican el fallo como
   `AccessDenied` cuando Windows niega el reemplazo.
