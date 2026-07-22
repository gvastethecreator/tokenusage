# Ticket 17: uso local real de Claude

Fecha: 2026-07-22

## Resultado

La composición de producción recorre esta ruta:

`%USERPROFILE%\.claude\projects` o `CLAUDE_CONFIG_DIR` -> JSONL Claude -> `UsageEvent` -> `usage.v1.db` -> rollup diario Claude -> tarjeta WinUI.

La fuente sintética sigue disponible solo para pruebas. Una regla de arquitectura impide que vuelva a la composición de producción.

## Lectura y privacidad

- Solo se enumeran archivos `*.jsonl` dentro de `projects`; `.credentials.json` queda fuera del recorrido.
- Se extraen fecha UTC, modelo, IDs de deduplicación, tokens, `costUSD`, sidechain y modo de velocidad.
- No se guardan prompt, respuesta, contenido, proyecto, tarea, comando, herramienta, sesión o ruta.
- Las claves persistidas son hashes SHA-256. Los IDs y rutas no se guardan en claro.
- El lector usa `FileShare.ReadWrite | FileShare.Delete`, omite junctions y tolera archivos que Claude rota mientras se leen.
- Cada línea se limita antes de crecer en memoria. Un archivo, línea o carpeta omitidos marcan la lectura como parcial.

## Deduplicación y coste

- La primera pasada deduplica `(message.id, requestId)`.
- La segunda quita replays sidechain por `message.id`; gana la copia principal, luego la de más tokens y luego la que informa coste.
- `costUSD` se conserva como coste informado.
- Si falta, un catálogo versionado estima solo IDs de modelo exactos. Modelos y fast mode sin tarifa exacta quedan sin precio.
- El catálogo distingue entrada, salida, lectura de caché y escrituras de 5 minutos y 1 hora.
- Claude Sonnet 5 usa el precio introductorio hasta el 31 de agosto de 2026 y el precio estándar documentado desde el 1 de septiembre.
- La tarjeta mantiene separados coste informado, coste estimado y tokens sin precio.

Fuentes primarias: [precios de Claude](https://platform.claude.com/docs/en/about-claude/pricing) e [IDs y versiones de modelos](https://platform.claude.com/docs/en/about-claude/models/model-ids-and-versions).

## Cobertura visible

La tarjeta dice que cubre sesiones guardadas en este equipo. No incluye sesiones sin persistencia, otros equipos ni datos que nunca llegaron al disco local. Si el scanner alcanza un límite o no puede leer un archivo, muestra un aviso de lectura parcial y evita presentar el resultado como completo.

La cuota de suscripción Claude sigue fuera de este corte porque no existe una interfaz pública y segura aprobada para leerla sin usar OAuth privado.

## Persistencia

La migración SQLite v3 elimina las filas `fixture/1` de Ticket 16 y reconstruye todos los rollups restantes dentro de la misma transacción. La tarjeta consulta solo `agent_id = claude`, por lo que otros agentes no contaminan sus totales.

## Pruebas

- Corpus JSONL sanitizado y compartido bajo `tests/fixtures/claude-config`.
- Localizador por defecto, override, root directo `projects` y override inválido.
- Parser de tokens, caché, coste informado, catálogo exacto, modelo desconocido, casing, JSON inválido, velocidad inválida y línea acotada.
- Deduplicación exacta y sidechain.
- Estados completo, parcial y sin datos.
- Migración v3 y filtro por agente.
- Integración Claude -> SQLite -> tarjeta.
- UI Automation empaquetada y captura visual con coste informado, estimado y sin precio.

Gate x64 final, `scripts/check.ps1`:

- Architecture: 26/26.
- Core: 58/58.
- CLI: 1/1.
- Providers: 134/134.
- Platform Windows: 52/52.
- Solution x64 Debug: 0 warnings, 0 errors.

Gate adicional:

- Solution ARM64 Debug: 0 warnings, 0 errors.
- UI Automation empaquetada: 7/7.
- Auditoría NuGet, incluida la resolución transitiva: sin paquetes vulnerables informados.
- `git diff --check`: sin errores.

Captura: [claude-local-usage.png](../../artifacts/ticket-17/claude-local-usage.png)

## Límite de este corte

El scanner relee el corpus permitido y confía en claves estables para que SQLite no duplique eventos. El cursor incremental, la reconciliación de sesiones borradas y los rangos visibles distintos de 30 días siguen pendientes. El coste estimado representa precios API, no el cobro de un plan Claude.
