# Ticket 16: recorrido local desde una fuente fake

Fecha: 2026-07-22

## Resultado

El corte recorre esta ruta dentro de la app empaquetada:

`SyntheticUsageEventSource` -> `UsageEvent` -> `usage.v1.db` -> `DailyUsageRollup` -> tarjeta WinUI y lector CLI compartido.

La fuente sigue siendo sintética. Los adaptadores locales de Claude, Grok Build, OpenCode y Antigravity pertenecen a los tickets siguientes.

## Contrato y privacidad

- `UsageEvent` separa agente, proveedor de modelo y modelo.
- Los tokens conservan entrada, salida, razonamiento, lectura de caché y escritura de caché.
- El coste usa una unión cerrada: informado por proveedor, estimado por catálogo o ausente.
- El evento no expone ni persiste prompt, respuesta, proyecto, tarea, herramienta, comando, sesión, ruta, cuenta, transcripción, contenido o texto.
- Una prueba de superficie pública y otra del esquema SQLite mantienen esa lista cerrada.

## Persistencia

- `Microsoft.Data.Sqlite` 10.0.10 abre `LocalState/scanner/usage.v1.db`.
- `SQLitePCLRaw.bundle_e_sqlite3` queda fijado en 2.1.12 porque la resolución transitiva inicial 2.1.11 falló el control de vulnerabilidades.
- La migración v1 crea eventos, rollups, cursores y catálogo.
- La migración v2 agrega tombstones de hashes para impedir que un evento podado vuelva a sumar su rollup.
- Cada evento y su delta de rollup comparten una transacción corta.
- WAL y `busy_timeout=5000` permiten lectura UI/CLI y serializan escritores.
- La retención elimina eventos anteriores a 400 días UTC por lotes, conserva rollups y registra el hash retirado.
- El borrado de datos limpia eventos, tombstones, rollups, cursores y catálogo en una transacción.

Referencias de dependencias: [Microsoft.Data.Sqlite 10.0.10](https://www.nuget.org/packages/Microsoft.Data.Sqlite/10.0.10) y [SQLitePCLRaw.lib.e_sqlite3](https://www.nuget.org/packages/SQLitePCLRaw.lib.e_sqlite3/2.1.12).

## UI y CLI

- La tarjeta compacta muestra coste informado, coste estimado, tokens sin precio, tokens totales y cobertura de coste.
- Un valor de coste ausente aparece como `Sin datos`; nunca se convierte en cero.
- La fuente queda rotulada como fixture sintético y SQLite local.
- Si la cuota de Codex falla, el estado lo indica y el uso local permanece visible.
- `LocalUsageCliAccess` abre la misma ruta y devuelve los campos separados. Los comandos públicos siguen reservados para Ticket 25.

Captura: [local-usage-card.png](../../artifacts/ticket-16/local-usage-card.png)

## Pruebas

Gate x64, `scripts/check.ps1`:

- Architecture: 25/25.
- Core: 57/57.
- CLI: 1/1.
- Providers: 119/119.
- Platform Windows: 52/52.
- Solution x64 Debug: 0 warnings, 0 errors.

Gate adicional:

- Solution ARM64 Debug: 0 warnings, 0 errors.
- UI Automation empaquetada: 6/6.
- Auditoría NuGet, incluida la resolución transitiva: 0 paquetes vulnerables informados.
- `git diff --check`: sin errores.

Las pruebas de persistencia cubren duplicados, migración fresca, migración v1->v2, esquema futuro, WAL, dos repositorios concurrentes, rollback por overflow, retención, reingesta tras retención, borrado y acceso compartido UI/CLI.

## Revisión de Grok Build

Grok Build 0.2.106 ejecutó tres revisiones de diseño y una revisión adversaria final. El primer lote excedió cuatro turnos por lecturas amplias. Los prompts autocontenidos y un turno por tarea resolvieron el fallo; las cuatro revisiones posteriores terminaron.

Acciones tomadas:

- se aceptó el fallo de doble conteo tras retención y se agregó la migración v2 con tombstones;
- se definió la retención por UTC;
- se mantuvo el coste informado, estimado y ausente como canales separados;
- se rechazó guardar hashes de payload y permitir dos clases de coste en un evento.

La alerta de pérdida de updates concurrentes no se reproduce: cada transacción escribe primero `usage_event`, por lo que SQLite obtiene el bloqueo de escritor antes de leer el rollup. La prueba con dos repositorios agrega 40 eventos distintos al mismo rollup y conserva los 40.

## Límite de la evidencia

Este corte prueba el motor propio y la integración con datos sintéticos. Aún faltan scanners reales, catálogo de precios completo, recomputación histórica, comandos CLI públicos y pruebas diferenciales contra corpus de proveedores.
