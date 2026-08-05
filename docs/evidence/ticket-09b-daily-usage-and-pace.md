# Ticket 09B — uso diario, ritmo y composición

Fecha: 2026-07-22
Estado: implementado y verificado con datos sintéticos

## Resultado

- `CodexDailyUsageAggregator` calcula hoy, ayer, 7 días y 30 días desde fechas
  civiles de Codex y una zona horaria inyectada.
- Un día ausente queda ausente. Un cero informado queda en cero. Las ventanas de
  7 y 30 días suman solo buckets informados dentro de sus límites inclusivos.
- Duplicados y overflow fallan con un error fijo. El runtime
  conserva la cuota fresca como `PartialSuccess` y omite todo dato de uso dudoso.
- `QuotaPace` replica el contrato de OpenUsage: espera `max(60 s, 1 % de la
  ventana)`, proyecta uso al reset y clasifica `Ahead`, `OnTrack` o `Behind`.
- Ritmo usa cuota, duración y reset. No usa tokens diarios. Falta de ventana,
  reset vencido, muestra corta o aritmética fuera de rango oculta el resultado.
- El cache v1 conserva métricas escalares de uso sin agregar tipos de dato. Un
  fallo posterior de uso reemplaza el snapshot con cuota fresca y quita uso viejo.

## Regresiones halladas y reparadas

1. El primer lote completo mostró que el fake Codex no implementaba
   `account/usage/read`; el resultado pasaba a parcial. El fake ahora responde
   con buckets sintéticos y la prueba cruza proceso, JSONL, runtime, caché y métrica.
2. La revisión Grok encontró que una ETA positiva menor a un tick podía crear
   `TimeSpan.Zero`, lanzar y ocultar todo el ritmo. La ETA ahora requiere al menos
   un tick; el estado `Behind` se conserva sin ETA.
3. Se añadieron regresiones de uso vacío, duplicados, cancelación, fallo parcial,
   eliminación de uso viejo, límites civiles, zona horaria y overflow.

## Evidencia local

- `TokenUsage.Core.Tests`: 41/41.
- `TokenUsage.Providers.Tests`: 109/109.
- `TokenUsage.Architecture.Tests`: 22/22.
- `TokenUsage.Platform.Windows.Tests`: 48/48.
- `dotnet format TokenUsage.slnx --verify-no-changes --no-restore`: pasó.
- Build empaquetado Debug x64 con `BuildAndRun.ps1 -SkipRun`: pasó.
- `git diff --check`: pasó; solo avisos de normalización LF/CRLF.

## Grok paralelo y revisión

Seis sesiones Grok read-only cubrieron contrato, zona horaria, agregados, ritmo,
runtime/caché y ataque adversario. El padre rechazó la suma silenciosa de días
duplicados y aplicó fallo parcial. El adversario revisó el diff, encontró el caso
sub-tick y aceptó su reparación sin P0, P1 ni P2 restantes.

Sesiones: `c055ca47-6523-48a4-8565-a507f3eca182`,
`336e1ca4-2b5b-4e8a-a0b6-ee8092944f27`,
`bfbf62bc-3559-4685-b105-09f762616807`,
`fef29764-a37a-483f-acab-e05c0c87b967`,
`ffbf6de5-f36c-4b7a-ae19-4a2d7b27f8d6` y
`db713889-e2f5-42f1-b6ef-56dfce5aeb23`. Coste Grok de 09B: US$1.971852.

## Límite de la afirmación

No se usó una cuenta real. La fecha civil asume que Codex etiqueta buckets con
la zona elegida por la app; el protocolo no declara otra zona. 09B no muestra
todavía uso ni ritmo en WinUI. UI, texto, accesibilidad, captura y ARM64 quedan
en 09C y el lote final del Ticket 09.
