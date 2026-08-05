# Ticket 25B1: comando `limits` sobre caché

Date: 2026-07-23

## Outcome

El ejecutable acepta:

```text
tokenusage limits
tokenusage limits codex
tokenusage limits --format json
```

Lee `cache/providers/codex/snapshots.v1.json` mediante `SnapshotStore`, la misma
caché y el mismo mutex que usa el refresco Codex de la app. No abre sesiones,
credenciales ni datos del proveedor.

## Contract

- JSON `tokenusage.limits.v1` con golden estable.
- Orden ordinal de providers y métricas.
- Estado `stale` por provider y agregado en la raíz.
- Métricas de progreso y escalares conservan fuente y tipo de medición.
- Filtro exacto por provider ID.
- Códigos `0`, `2` y `4`; argumentos y fallos se redactan.
- Salida humana estable frente a la cultura del proceso.

En el commit de este corte, `--force` ya formaba parte del parser pero fallaba
cerrado porque la fábrica Codex vivía en la composición WinUI. Ticket 25B2
cerró ese límite; su evidencia está en
`docs/evidence/ticket-25b2-force-refresh-cli.md`.

## Evidence

- `dotnet test tests/TokenUsage.Cli.Tests/TokenUsage.Cli.Tests.csproj -c Debug -p:Platform=x64 --no-restore`: 45/45.
- El golden cubre dos providers, métricas desordenadas, campo opcional, stale y
  procedencia.
- Dos procesos `TokenUsage.Cli.exe` leen a la vez la misma caché temporal. Ambos
  devuelven JSON válido; una lectura posterior confirma el snapshot y ausencia
  de cuarentena.
- La suite conserva el smoke de `usage` contra una SQLite poblada.
- `dotnet test tests/TokenUsage.Architecture.Tests/TokenUsage.Architecture.Tests.csproj -c Debug -p:Platform=x64 --no-restore`: 59/59.
- `dotnet build src/TokenUsage.Cli/TokenUsage.Cli.csproj -c Release -p:Platform=x64 --no-restore`: 0 avisos, 0 errores.

## Review

Grok Build 0.2.106 recibió un snapshot Windows aislado con tres contratos de
Core y un único archivo de salida. Terminó `Cancelled` antes del primer `Edit`.
El snapshot se descartó y no se aceptó código.

Una revisión independiente detectó dos fallos: código `0` para un provider sin
métricas e inyección de controles de terminal desde textos del caché. El corte
ahora devuelve `4` sin métricas útiles y neutraliza controles C0, C1 y Unicode
de formato en la salida humana. La revisión también halló una diferencia entre
el segundo publicado y el usado para `stale`; ambos usan ahora un único reloj
congelado. Las regresiones cubren los tres casos.

## Boundary

Este corte implementó la lectura real de caché. Ticket 25B2 añadió el refresco
Codex real. Siguen pendientes `providers`, `doctor`, concurrencia app/CLI y el
alias MSIX.
