# Ticket 09A — cliente de uso diario Codex

Fecha: 2026-07-22
Estado: implementado y verificado con respuestas sintéticas

## Resultado

- `CodexAppServerClient` envía `account/usage/read` con `params: null` tras el
  handshake y bajo el mismo timeout y exclusión mutua que el resto del cliente.
- El parser exige `summary`, acepta buckets ausentes, nulos o vacíos y conserva
  orden, duplicados y ceros informados por el proveedor.
- Fechas usan `yyyy-MM-dd` estricto. Tokens y resumen aceptan enteros `Int64` no
  negativos. La respuesta admite como máximo 400 buckets.
- Los modelos públicos solo exponen fechas, tokens y cinco cifras de resumen.
  Campos nuevos, correo, IDs y JSON crudo no entran al snapshot ni a errores.
- El runtime todavía no pide uso diario. Esa composición pertenece a 09B.

## Pruebas y fallos hallados

El primer test falló al compilar porque no existían `CodexTokenUsageSnapshot` ni
`ReadTokenUsageAsync`. Después del primer pase, los casos hostiles hallaron una
excepción de tipo al recibir JSON con clase errónea. Se añadió el control de
`JsonValueKind` antes de `TryGetInt64`. También se corrigió un fixture de
`long.MaxValue` que no representaba el contrato buscado.

La suite cubre handshake, petición, respuesta válida, campos extra, privacidad,
buckets ausentes/nulos/vacíos, cero, `Int64.MaxValue`, duplicados, orden, límite
400/401, lista de solo lectura, fechas y valores inválidos, notificaciones e ID
de respuesta en texto.

## Evidencia local

- Tests Codex enfocados: 77/77.
- Suite `WOpenUsage.Providers.Tests`: 94/94.
- Tests `WOpenUsage.Platform.Windows`: 48/48.
- `dotnet format ... --verify-no-changes --no-restore --include ...`: pasó.
- `git diff --check`: pasó; Git solo avisó sobre normalización LF/CRLF.
- `BuildAndRun.ps1 ... -SkipRun /p:Platform=x64 /p:Configuration=Debug`: build
  empaquetado x64 pasó.

## Revisión Grok

Tres sesiones read-only revisaron protocolo, cálculo/ritmo y ajuste de UI. Dos
intentos acotados de edición terminaron cancelados sin cambios; el padre escribió
el corte con TDD. Una sesión aparte revisó el diff final y aceptó 09A sin P0, P1
  ni P2. Sus tres huecos P3 —límite exacto, lista inmutable y fecha numérica— tienen
pruebas. Coste Grok total del corte: US$1.2641232.

## Límite de la afirmación

No se usó una cuenta real. 09A no afirma agregados locales, cálculo de ritmo,
integración de caché/runtime, UI ni ejecución ARM64. Esos gates siguen en 09B,
09C y el lote final del Ticket 09.
