# Ticket 25A: comando `usage`

Date: 2026-07-22

## Outcome

El ejecutable de consola ya acepta:

```text
wusage usage
wusage usage --days 7
wusage usage --days 30 --format json
```

Lee `scanner/usage.v1.db` mediante `LocalUsageCliAccess`, el mismo acceso de
rollups que usa la composición de la app. La variable `TOKENUSAGE_DATA_DIR`
permite aislar pruebas y desarrollo; con identidad de paquete usa
`ApplicationData.Current.LocalFolder`.

## Contract

- JSON: `wusage.usage.v1`, golden en
  `tests/WOpenUsage.Cli.Tests/Golden/wusage.usage.v1.json`.
- Códigos del corte: `0` con datos, `2` para argumentos no válidos, `4` sin
  datos o ante fallo de lectura sanitizado.
- Periodo inclusivo de 1 a 3650 días, calculado desde fecha UTC.
- Salida JSON y humana estable frente a la cultura del proceso.
- Errores no incluyen rutas, argumentos desconocidos ni texto de excepciones.

## Evidence

- `dotnet test tests/WOpenUsage.Cli.Tests/WOpenUsage.Cli.Tests.csproj -c Debug -p:Platform=x64 --no-restore`: 19/19.
- La suite inicia `WOpenUsage.Cli.exe` como segundo proceso contra una SQLite
  temporal poblada por `SyntheticUsageEventSource` y valida schema, eventos,
  tokens y coste informado.
- `dotnet build src/WOpenUsage.Cli/WOpenUsage.Cli.csproj -c Release -p:Platform=x64 --no-restore`: 0 avisos, 0 errores.
- Smoke de proceso con DB vacía: JSON válido y código `4`.

## Review

Grok Build recibió dos cortes Windows aislados. Ambos terminaron `Cancelled`
antes del primer `Edit`, incluso al reducir el trabajo a un archivo. No se
aceptó código suyo. El padre implementó el contrato, corrigió dos fallos de
compilación de tests y eliminó el eco de argumentos desconocidos por riesgo de
filtrar secretos.

## Boundary

Este corte cubre `usage`. Ticket 25 sigue abierto para `limits`, `providers`,
`doctor`, lectura concurrente entre procesos y alias MSIX `wusage.exe`.
