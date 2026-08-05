# Ticket 25D1: acceso concurrente de app y CLI

Fecha: 2026-07-23

## Resultado

La CLI comparte la caché de límites y la base de uso con un escritor activo sin
publicar JSON parcial, perder eventos ni dejar archivos temporales. Las pruebas
ejecutan procesos `TokenUsage.Cli.exe` reales contra las mismas clases públicas
que usa la app: `SnapshotStore` y `UsageRepository`.

Este corte no cambia código de producción. Fija el contrato de concurrencia que
debe conservar el alias empaquetado del Ticket 25D2.

## Caché de límites

- Un escritor alterna dos snapshots Codex completos mediante
  `SnapshotStore.UpsertLastGoodAsync`.
- Dos procesos CLI ejecutan `limits codex --format json` durante las escrituras.
- Cada salida cumple `tokenusage.limits.v1` y contiene uno de los dos estados
  completos. Nunca mezcla valores.
- La caché final conserva esquema 1, se puede cargar y no deja `*.tmp` ni
  `*.corrupt-*`.
- Una prueba separada retiene el mutex nombrado real desde otro hilo, confirma
  que `LoadAsync` espera, cancela la lectura y verifica que los bytes quedan
  intactos.

El timeout externo del proceso es 45 segundos. Supera el timeout interno de 30
segundos del mutex y permite observar primero el fallo propio del almacén.

## SQLite de uso

- Un escritor inserta eventos únicos mientras cuatro procesos CLI ejecutan
  `usage --days 30 --format json`.
- Una conexión de prueba queda abierta, solicita una escritura posterior y
  comprueba `PRAGMA journal_mode = wal` y el sidecar `usage.v1.db-wal` durante
  la carga.
- Cada reporte cumple `tokenusage.usage.v1`. Su total de tokens y coste coincide de
  forma exacta con su número de eventos.
- El rollup final contiene todos los eventos confirmados. Solo quedan la DB y
  sus sidecars WAL/SHM.

## Prueba ejecutada

Release x64:

- prueba de cancelación del mutex: 1/1;
- pruebas de escritor más CLI: 2/2;
- repetición sin rebuild de las dos pruebas de proceso: 3 ejecuciones, 2/2 en
  cada una.

## Revisión

Grok Build produjo el plan del corte con acceso de solo lectura. Coste informado
por el runner: USD 0.2458224. Su intento de implementación agotó turnos y no
produjo cambios aceptables; coste informado: USD 0.1509348. El parent implementó
y validó el corte.

Una revisión independiente pidió prueba directa de WAL, cancelación durante una
espera real, margen entre timeouts y una escritura confirmada con el probe WAL
abierto. Las cuatro correcciones forman parte de la prueba final.

## Límite pendiente

25D1 prueba el almacenamiento compartido con procesos CLI reales. 25D2 debe
registrar `tokenusage.exe` mediante MSIX, conservar identidad y recursos, y probar el
alias instalado. No se debe ejecutar el binario empaquetado por su ruta física.
