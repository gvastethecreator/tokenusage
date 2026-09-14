# Migraciones y recuperación · evolución sin pérdida silenciosa

**Estado:** runbook propuesto. **Baseline verificado:** esquema 5, `UsageRepository`, apertura read-only y controles de versión. **Fuentes:** TU-08/10/13 y EXT-02/03/04/05. [Fuentes](../references/10-SOURCES.md).

## 1. Principios

P0/P1 no deben exigir una migración grande para corregir textos o mejorar consultas. P2 añade metadata y revisión; P3 añade asociaciones sólo tras consentimiento. No agrupar cambios de datos sensibles con un simple PR visual. Asignar la próxima versión de schema sobre main al implementar, no asumir que siempre será 6.

Nunca migrar, modificar PRAGMA de escritura o limpiar bases de proveedores. Sólo la base propia y settings/checkpoints admitidos de TokenUsage entran en este runbook. La copia de seguridad también es un dato sensible local.

## 2. Inventario previo

Registrar versión de esquema, build, distribución MSIX/portable, ubicación lógica del almacén, tamaño de DB/sidecars, retención, versiones de checkpoint y época de privacidad. No exportar las rutas personales en el manifiesto compartido. Confirmar espacio libre para copia y operaciones antes de modificar; un porcentaje fijo de disco no sustituye calcular el tamaño requerido.

Contabilizar todos los escritores: aplicación, CLI refresh, tareas existentes y cualquier proceso auxiliar autorizado. Deben compartir una barrera de migración por almacén con alcance y permisos Windows apropiados. Reutilizar el mecanismo existente si lo hay; verificarlo antes de introducir uno nuevo.

## 3. Preflight y copia coherente

Entrar en modo mantenimiento para ese almacén, pausar ingesta y cerrar consultas activas mediante una política acotada. Mostrar estado, no congelar la ventana. La CLI debe rechazar temporalmente escritura con un mensaje claro, no continuar con un schema parcial.

Crear copia coherente de la DB propia mediante el mecanismo de backup SQLite/Microsoft.Data.Sqlite o cierre coordinado completo. No copiar sólo `.db` de una base viva ignorando WAL: puede omitir commits. No borrar sidecars manualmente para «arreglar» la copia. Registrar que la copia terminó y verificar apertura/integridad antes de comenzar una migración destructiva o no trivial.

Si otros archivos forman parte del estado, coordinar su snapshot bajo la misma pausa y registrar versiones. Una transacción SQLite no hace atómicos por sí sola los archivos JSON de checkpoint; el plan de replay debe contemplarlo.

## 4. Migración aditiva P2

Crear tablas/columnas laterales para granularidad, instancia y disponibilidad, con constraints. Filas existentes quedan sin metadata o `Unknown`, sin timestamps/solicitudes fabricados. Añadir la revisión de datos y manifestar limitaciones de procedencia histórica. No reemplazar event keys sin un puente de identidad demostrado.

Aplicar DDL y registro de schema en transacción cuando la operación SQLite lo permita y dentro de la política actual. Evitar cambios pesados de todos los eventos en una única transacción que haga la app inutilizable. Un backfill por lotes separado es reanudable e idempotente, con progreso; la función avanzada permanece inactiva hasta que su población esté validada.

Los rollups históricos se preservan. Si se necesita reconstruir, hacerlo sólo sobre eventos retenidos y comparar contra el total anterior dentro del ámbito reconstruible; no eliminar historia durable que ya perdió raw. Los datos cuya fuente desapareció quedan con limitación, no se «corrigen» usando supuestos.

## 5. Índices, versiones y clientes viejos

Medir índices antes/después; construir sólo los necesarios y con progreso cuando el tamaño lo requiera. Conservar apertura read-only que detecta versión incompatible. Un binario antiguo no se considera compatible con un schema nuevo simplemente porque su SELECT aún funcione.

Todos los entrypoints de escritura verifican versión/estado de migración. No basta hacerlo una vez al iniciar si un proceso puede permanecer abierto mientras otro migra. Coordinar procesos o impedir migrar con escritores antiguos. La transición es una propiedad del conjunto App/CLI, no de un método aislado.

## 6. Verificaciones postmigración

Comprobar integridad, foreign keys donde se usen, conteos por partición, sumas exactas de tokens/costos, distribución de Unknown y versiones. Ejecutar escenarios de consulta e ingesta en copia de prueba primero. Reabrir como el producto lo hace, no sólo inspeccionar tablas con un editor externo.

Verificar que no se introdujeron rutas, prompts o IDs nativos en las tablas nuevas. Correr escenarios de repeat-run y schema-too-new. Registrar revisión inicial y finalizar el estado de mantenimiento únicamente después de comprobar consistencia. Un backfill parcial no se marca completo para desbloquear UI.

## 7. Fallos y respuesta

| Momento | Fallo | Respuesta |
|---|---|---|
| Antes del backup | Espacio/permisos insuficientes. | No modificar nada; informar requisito. |
| Durante backup | Cancelación o error. | Marcar copia incompleta; no usarla para restore. |
| Dentro de transacción | DDL/dato inválido/crash. | Rollback SQLite + estado de migración verificable al reabrir. |
| Después de commit | Checkpoint externo no actualizado. | Replay idempotente; no perder eventos ni duplicarlos. |
| Backfill interrumpido | Corte de proceso. | Reanudar desde límite confirmado; metadata avanzada parcial. |
| Verificación falla | Totales/constraints difieren. | Mantener feature off; diagnóstico seguro; no seguir importando a ciegas. |
| Purge P3 en curso | Crash o disco lleno. | Permiso sigue deshabilitado; reanudar purge, no reactivar. |

## 8. Rollback no es downgrade mágico

Primera opción: desactivar la función nueva preservando schema/datos válidos y usar rutas compatibles del build actual. No ejecutar automáticamente un binario viejo sobre schema nuevo.

Si se necesita restaurar backup previo, detener todos los escritores y coordinar archivos; verificar backup, versión y privacidad vigente. Una copia antigua no contiene actividad posterior. Antes de restaurar, determinar si esa actividad puede reimportarse desde fuentes todavía disponibles; cuando no, advertir la pérdida y obtener una decisión explícita. No prometer recuperación perfecta de logs rotados.

Una restauración nunca deshace una revocación P3. Comparar la época del backup con el journal de privacidad vigente fuera del estado restaurado y purgar/bloquear metadata vieja antes de abrir informes. Si no se puede garantizar esa barrera, impedir la restauración enriquecida y ofrecer recuperación sólo numérica mediante un procedimiento validado.

## 9. Retención y limpieza

El raw de 400 días y rollups durables documentados no se cambian por comodidad. Metadata lateral de evento se elimina junto al raw cuando corresponda; resúmenes durables no guardan accidentalmente asociaciones prohibidas. Las versiones/procedencia agregadas necesarias para interpretar historia requieren un contrato propio, con Unknown si no existen.

Definir cantidad/edad de backups automáticos propios y política de purge antes de introducirlos. Propuesta inicial de operación: una copia previa a cada migración relevante, con límite de almacenamiento y eliminación posterior a verificación/ventana aprobada; no crear backups infinitos. El usuario conserva control sobre copias externas, cuya revocación no puede garantizar la aplicación.

## 10. Pruebas de recuperación

Probar instalación vacía, schema 5 poblado, ejecución repetida, versión mayor, DB ocupada, espacio insuficiente, interrupción en puntos definidos y restore con/ sin actividad posterior. Incluir MSIX y portable según superficie cambiada. Guardar evidencia de abrir realmente una copia restaurada y consultar totales, no sólo que existe un archivo .bak.

El agente debe entregar el plan de rollback junto al PR que agrega migración, no después de un fallo. No se aplicó ninguna migración a TokenUsage durante la creación de este paquete.
