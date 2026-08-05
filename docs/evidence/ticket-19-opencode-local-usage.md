# Ticket 19: uso local real de OpenCode

Fecha: 2026-07-22

## Resultado

La app resuelve `OPENCODE_DATA_DIR`, `XDG_DATA_HOME/opencode` o
`%USERPROFILE%\.local\share\opencode`, normaliza tokens y gasto, y los suma a la
tarjeta de uso local junto a Claude y Grok Build.

## Fuente y privacidad

- Abre `opencode*.db` en modo read-only, caché privada y sin pooling ni copia.
- Usa `busy_timeout`, `query_only` y una sola transacción de lectura para obtener
  un snapshot WAL coherente.
- El esquema actual lee los agregados cerrados de `session`; no recorre mensajes.
- Los esquemas anteriores extraen solo rol, fecha, modelo, proveedor, tokens y
  coste desde `message` y el último `step-finish` cuando hace falta.
- El lector JSON usa una lista cerrada de campos, límites de archivos y bytes,
  cancelación y archivos compartidos. No persiste prompts, respuestas, comandos
  ni salidas de herramientas.
- SQLite gana por sesión frente al JSON legado. Las claves SHA-256 son estables y
  el coste cero cuenta como informado.
- Esquemas desconocidos, límites o fallos de acceso producen `Partial`. El
  coordinador conserva el último snapshot completo.

## Integración real

La composición WinUI crea `OpenCodeUsageEventSource` junto a Claude y Grok. El
modo de prueba acepta una raíz sanitizada mediante `--test-opencode-data`. La UI
muestra `Claude + Grok Build + OpenCode · datos locales`, gasto informado,
estimado, tokens sin precio, total y cobertura.

El smoke opt-in `TOKENUSAGE_OPENCODE_SMOKE=1` compara la fuente con
`opencode stats --pure --days 400 --tools 0`. No guarda ni imprime la salida del
CLI. El coste admite un centavo; tokens admite 6 % porque el CLI abrevia K/M/B a
una cifra decimal. La prueba real pasó.

## Grok Build

Grok revisó el contrato final y señaló que varias consultas podían observar
estados WAL distintos. Se añadió una transacción única y se repitieron las
pruebas. Una segunda tarea priorizó frescura por origen, detección de cambios y
cancelación para el siguiente corte.

El bloqueo al ejecutar varias tareas Grok quedó aislado: resultados en una misma
carpeta comparten `worker.stderr.log` e `invocation.json`. Cada tarea usa ahora un
subdirectorio propio. La segunda ejecución terminó en
`.scratch/agent-cli-delegation/grok-build/t20-next/result.json`.

## Pruebas

- OpenCode focal: 12/12.
- Smoke diferencial real: 1/1.
- Architecture: 26/26.
- Core: 60/60.
- CLI: 1/1.
- Providers: 159/159.
- Platform Windows: 52/52.
- Solución x64 Debug: 0 advertencias, 0 errores.
- Solución ARM64 Debug: 0 advertencias, 0 errores.
- UI Automation empaquetada: 5/5.
- Auditoría NuGet transitiva: sin paquetes vulnerables informados.
- `git diff --check`: sin errores.

Captura: [opencode-local-usage.png](../../artifacts/ticket-19/opencode-local-usage.png)

## Límite

Este corte cubre OpenCode nativo en Windows. WSL, tarjetas separadas y vigilancia
de archivos quedan para tareas posteriores.
