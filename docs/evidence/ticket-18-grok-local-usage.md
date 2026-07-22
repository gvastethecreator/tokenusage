# Ticket 18: uso local real de Grok Build

Fecha: 2026-07-22

## Resultado

La app recorre esta ruta:

`GROK_HOME` o `%USERPROFILE%\.grok` -> snapshots locales -> `UsageEvent` ->
`usage.v1.db` -> rollup diario -> tarjeta WinUI.

Claude y Grok se leen en paralelo. La tarjeta suma ambos orígenes y muestra su
procedencia. La cuota remota de Grok queda visible como bloqueada por política.

## Fuente y privacidad

- El scanner descubre `sessions/<cwd>/<session>/summary.json` y lee solo el
  `updates.jsonl` vecino.
- `logs/unified.jsonl` se usa solo cuando ninguna sesión produjo uso y el scan
  de sesiones terminó sin errores.
- Nunca abre `auth.json`, `chat_history.jsonl` u otros logs.
- Extrae una lista cerrada de fecha, modelo, tokens y coste. No persiste prompt,
  respuesta, título, comando, herramienta, ruta o sesión.
- Las claves son hashes SHA-256 de identidad local. El hash de sesión incluye el
  directorio codificado para evitar colisiones entre proyectos.
- La lectura usa límites de archivos, bytes y línea, no sigue reparse points,
  comparte archivos activos y observa cancelación entre bloques.

## Snapshots, coste y fallback

- `params.update.usage` es una foto acumulada. Solo se conserva la última foto
  válida por sesión.
- Si `modelUsage` existe, se emite una fila por modelo y se ignoran los totales
  superiores.
- `inputTokens` incluye caché; la entrada fresca se calcula como
  `max(inputTokens - cachedReadTokens, 0)`.
- `costUsdTicks` informado gana, incluido cero. La conversión usa
  10.000.000.000 ticks por USD y redondea a seis decimales al persistir.
- El fallback unificado queda sin precio hasta contar con un catálogo oficial y
  versionado.
- El reemplazo por agente ocurre en una transacción. Una lectura parcial o una
  raíz que desaparece conserva el último total fiable.
- Los hashes retirados por retención pueden volver a entrar si una sesión larga
  recibe actividad nueva. Los rollups históricos anteriores al rango reemplazado
  se conservan.

## Revisión

Grok Build ejecutó dos revisiones de contrato y una revisión del código real. El
primer intento de la revisión final agotó un turno; el reintento aislado con dos
turnos terminó bien. Se aceptaron y corrigieron los riesgos de snapshots
acumulados, claves entre proyectos, tombstones, estado parcial, cancelación y
raíces Windows inválidas. Se rechazó borrar rollups durante retención porque el
contrato del producto exige conservarlos.

## Pruebas

- Corpus sanitizado bajo `tests/fixtures/grok-home`.
- Sesiones con totales y varios modelos, coste cero, claves estables y cwd
  repetido.
- Prioridad de sesión, fallback unificado, JSON roto, límites, cancelación,
  archivos sensibles ignorados y raíz inválida.
- Reemplazo transaccional, último total en lecturas parciales o sin datos y
  reactivación de una clave tombstone.
- Composición de producción Claude + Grok y recursos en español e inglés.

Gates finales:

- Architecture: 26/26.
- Core: 60/60.
- CLI: 1/1.
- Providers: 147/147.
- Platform Windows: 52/52.
- Solution x64 Debug: 0 advertencias, 0 errores.
- Solution ARM64 Debug: 0 advertencias, 0 errores.
- UI Automation empaquetada: 5/5.
- Auditoría NuGet transitiva: sin paquetes vulnerables informados.
- `git diff --check`: sin errores.

Captura: [grok-local-usage.png](../../artifacts/ticket-18/grok-local-usage.png)

## Límite de este corte

La tarjeta aún agrega Claude y Grok. Las tarjetas separadas, tendencias por
proveedor, cursor por byte y OpenCode forman los siguientes cortes de M6A.
