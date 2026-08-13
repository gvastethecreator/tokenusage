# Revisión completa de rendimiento — 2026-08-13

Estado: diez mejoras aprobadas implementadas y verificadas.

Arquitectura: `docs/architecture/architecture-review-2026-08-13.md`

## Resultado medido

| Superficie | Antes | Después | Cambio |
| --- | ---: | ---: | ---: |
| Ingesta SQLite de 10,000 eventos | 1218 ms mediana | 385 ms mediana | -68.4% |
| Cursor, corpus 10,000 antiguos + 100 recientes | 123 ms y 10,100 filas incorrectas | 22 ms y 100 filas correctas | -82.1%; contrato corregido |
| OpenCode, 5,000 mensajes / 15,000 partes | 5504 ms mediana | 38 ms mediana | -99.3% |

Las mediciones son harnesses locales sobre esta máquina. No representan tiempo de apertura del MSIX ni una promesa universal de latencia.

## Diez resultados de rendimiento

| ID | Resultado | Evidencia |
| --- | --- | --- |
| PERF-01 | Ingesta transaccional por lotes preserva deduplicación, tombstones y rollups. | 386/385/328 ms; mediana 385 ms; invariantes 3/3. |
| PERF-02 | Schema v4 añade índices y migra bases antiguas propiedad de TokenUsage antes de la lectura cacheada. | Migración real v3→v4, `quick_check=ok`, `EXPLAIN QUERY PLAN`. |
| PERF-03 | Replace, reconcile y upsert reconstruyen sólo el rango afectado, conservan historia y limpian la fecha anterior cuando una clave cambia de día. | Seis ramas más regresiones cross-day; UsageRepository 28/28. |
| PERF-04 | La retención de 400 días se ejecuta como máximo una vez cada seis horas. | Prueba con `TimeProvider`, dos ramas. |
| PERF-05 | Claude descarta JSONL anteriores a la ventana antes de abrirlos o parsearlos. | Regresión de skip-old-file. |
| PERF-08 | Cursor normaliza timestamps numéricos, texto numérico, real e ISO en bubbles y composer; oversized queda Partial sin parse JSON. | Cursor 16/16; corpus devuelve 100 recientes y 0 antiguos. |
| PERF-09 | OpenCode reemplaza la subconsulta correlacionada por `ROW_NUMBER()` y conserva las mismas filas. | 16/16 pruebas; checksum idéntico; 5504→38 ms. |
| PERF-10 | El arranque es cache-first y fuerza una sola actualización viva por proceso. | `StartAsync` repetido conserva exactamente un forced refresh. |
| PERF-11 | Panel, selección y tray reutilizan el mismo snapshot y límites por proveedor. | Pruebas de identidad y una lectura por proveedor. |
| PERF-12 | El informe hace una lectura durable por rango y filtra proveedor en memoria; Refresh sigue siendo vivo. | Equivalencia completa con la consulta SQLite por agente. |

## Correctitud preservada

- Costo reportado, costo estimado, costo no disponible, cobertura y tokens sin precio permanecen estados diferentes.
- La reconciliación mantiene 35 días; la retención conserva 400 días de eventos y rollups históricos anteriores.
- Cursor y OpenCode no reciben escrituras.
- No se guardan prompts, conversaciones, comandos, tool calls, correos, identificadores de cuenta ni rutas locales completas.
- Report Refresh sigue llamando la actualización viva cuando `refreshSource=true`.

## Puertas

- Pruebas focales de repositorio, fuentes, sesión, proyección e informe: correctas.
- Gate final Release x64: 1081/1081 pruebas, solución y MSIX correctos en 166.4 s.
- Producto empaquetado: dashboard e informe cargan datos locales reales.

## Experimentos inconclusos o neutrales

- Un `check.ps1` usó por error un timeout de un segundo y otro se interrumpió cuando el revisor encontró una rama no cubierta. Se descartaron; el gate final de 166.4 s pasó sobre las reparaciones aceptadas.
- El validador Playwright del mapa no pudo ejecutarse porque la dependencia opcional no está instalada. La UI del mapa se comprobó con el navegador integrado y no se añadió Playwright al producto.
- Claude, retención, refresh y proyección tienen prueba de mecanismo y contrato, pero no se les asigna una mejora inventada en milisegundos.

## Veredicto

Las tres rutas con baseline comparable mejoraron y mantuvieron equivalencia. Las otras siete reducen trabajo por contrato verificable sin afirmar números no medidos. La revisión independiente terminó en `ACCEPT`; no quedan bloqueos de rendimiento dentro de los diez tickets aprobados.
