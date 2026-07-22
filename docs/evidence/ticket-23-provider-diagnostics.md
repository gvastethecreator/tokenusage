# Ticket 23: diagnóstico y cobertura por proveedor

Fecha: 2026-07-22

## Resultado

TokenUsage muestra en Opciones una fila compacta para Codex, Claude, Grok Build
y OpenCode. Cada fila separa cuota, uso local, gasto y cobertura. La raíz solo
se describe como detectada, ausente o pendiente; nunca muestra una ruta.

Los lectores locales conservan un motivo seguro: raíz ausente, fuente vacía,
lectura parcial, acceso bloqueado o esquema no compatible. OpenCode distingue
un esquema nuevo de un fallo parcial. Los snapshots retenidos se marcan como
`último dato fiable`, por lo que gasto y cobertura no parecen actuales cuando
la fuente desaparece o queda parcial.

El botón de actualización reutiliza el refresco existente. Un fallo local
transitorio conserva las filas conocidas. Codex separa falta de instalación,
cuenta no compatible, política bloqueada, cambio de contrato y fallo transitorio.

## Privacidad

- No se proyectan rutas, nombres de archivo, IDs de sesión ni contenido.
- UIA revisó todo el texto visible y falló ante patrones de ruta Windows, UNC o
  home Unix.
- Las acciones solo actualizan estado o explican una recuperación fuera de la app.

## Delegación y revisión

Grok Build `0.2.106`, modelo `grok-4.5`, produjo tres análisis acotados sobre
contrato, parsers y UI. Las primeras ejecuciones agotaron turnos durante lectura;
se corrigió el flujo con resultados aislados y reanudación de síntesis. La
revisión final devolvió `repair` y señaló estados retenidos poco claros y una
detección Codex basada en publicación. El parent aceptó esos hallazgos, rechazó
el borrado de snapshots fiables y corrigió el texto y la proyección.

Resultados durables:

- `.scratch/agent-cli-delegation/grok-build/t23-contract-v3/result.json`
- `.scratch/agent-cli-delegation/grok-build/t23-parsers-v3/result.json`
- `.scratch/agent-cli-delegation/grok-build/t23-options-ui-v3/result.json`
- `.scratch/agent-cli-delegation/grok-build/t23-final-review-v2/result.json`

## Prueba local

- `scripts/check.ps1 -Platform x64 -Configuration Debug`:
  Architecture 26, Core 60, CLI 1, Providers 166 y Windows 52; build de solución
  con 0 avisos y 0 errores.
- Prueba enfocada posterior a revisión: Providers 167/167.
- Build x64 mediante `BuildAndRun.ps1 -SkipRun`: 0 avisos y 0 errores.
- Restore y build ARM64 de `WOpenUsage.slnx`: 0 avisos y 0 errores.
- `tests/ui/ticket-23-provider-status.ps1`: 23/23 en la app empaquetada.
- Captura: `artifacts/ticket-23/provider-status-options.png`.

## Nombre visible

En este corte también se cambió el copy público a TokenUsage: recursos en inglés
y español, tooltip de bandeja, título, pie y nombre visible del paquete. La
Identity/AUMID, assemblies, namespaces, ejecutables y rutas técnicas siguen como
`WOpenUsage` según ADR-0002.
