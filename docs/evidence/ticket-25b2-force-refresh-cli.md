# Ticket 25B2: refresco Codex real desde la CLI

Date: 2026-07-23

## Outcome

`tokenusage limits --force --format json` inicia Codex mediante el mismo runtime de
proceso y protocolo que usa WinUI. El refresco escribe
`cache/providers/codex/snapshots.v1.json` con `SnapshotStore` y devuelve el
snapshot nuevo.

La fábrica y el coordinador salieron de `TokenUsage.App`. El proyecto nuevo
`TokenUsage.Runtime.Windows` es la única copia de esa composición y la comparten
app y CLI. Los tests dejaron de compilar esos archivos mediante links.

## Failure contract

- `Success` y `PartialSuccess` devuelven el snapshot nuevo.
- Throttle o fallo con `LastGood` conservan ese snapshot.
- Un fallo sin snapshot conserva la caché publicada al inicio.
- Sin refresco ni caché, la CLI devuelve JSON vacío y código `4`.
- Ninguna razón, error, ruta, cuenta o texto del proceso llega a stdout o stderr.
- Cancelación del caller se propaga.
- Un provider ID distinto de `codex` no inicia Codex ni crea su estado local.

La CLI permite código `0` con datos vencidos marcados. Por eso un `--force` que
falla conserva last-good. Esta regla permite consultar datos recientes aunque
Codex no esté disponible en ese momento.

## Evidence

- `dotnet test tests/TokenUsage.Cli.Tests/TokenUsage.Cli.Tests.csproj -c Debug -p:Platform=x64 --no-restore`: 51/51.
- Un child process ejecuta `TokenUsage.Cli.exe limits codex --force --format json`
  contra Fake Codex, completa handshake y RPC, devuelve cuota y crea la caché.
- Otro child process usa un override inválido con caché previa: devuelve el
  last-good vencido, código `0` y ninguna ruta privada.
- El selector de outcomes cubre success, fallo con last-good, fallo con solo
  caché, ausencia total y cancelación.
- El provider ID llega al lector; `grok --force` devuelve sin crear el
  directorio Codex ni iniciar su proceso.
- `TokenUsage.Platform.Windows.Tests`: 58/58.
- `TokenUsage.Providers.Tests`: 167/167.
- `TokenUsage.Architecture.Tests`: 59/59.
- `dotnet build TokenUsage.slnx -c Release -p:Platform=x64 --no-restore`:
  0 warnings, 0 errors.

## Grok review

Grok Build 0.2.106 revisó la propuesta en modo read-only. Marcó `repair` por
reloj, ruta de caché, grafo, consumidores y matriz de outcomes. Se aceptaron
reloj, ruta, grafo y tests. Se rechazó descartar last-good ante fallo porque el
contrato de producto permite respuestas vencidas con código `0`. Coste informado
por el runner: USD 0.1623192.

La revisión local independiente marcó un P2: el reader recibía solo `force`,
por lo que cualquier provider ID podía iniciar Codex antes del filtro. El
contrato ahora pasa también el provider ID y corta proveedores no soportados
antes de tocar disco o crear el runtime. La suite CLI subió a 51 casos.

## Boundary

La exclusión mutua de la fábrica limita procesos dentro de cada proceso host.
App y CLI pueden iniciar Codex a la vez; el mutex compartido protege la caché.
Ticket 25D debe probar lectura/escritura simultánea entre ambos hosts y el alias
MSIX.
