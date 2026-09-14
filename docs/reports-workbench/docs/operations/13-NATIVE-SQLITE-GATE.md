# Gate adicional · SQLite nativo y fallos de persistencia

**Estado:** verificación requerida antes de ampliar persistencia y concurrencia. No es una declaración de vulnerabilidad confirmada de TokenUsage. No se ejecutó el runtime Windows ni se inspeccionaron los binarios MSIX/portable.

## 1. Evidencia consultada

El [proyecto Core de la base](https://github.com/gvastethecreator/tokenusage/blob/df50c367b083e5624bf09213eddc7013d8299aa4/src/TokenUsage.Core/TokenUsage.Core.csproj) declara `Microsoft.Data.Sqlite` 10.0.11 y `SQLitePCLRaw.bundle_e_sqlite3` 3.0.5. La versión del paquete administrado no se debe presentar como versión de la biblioteca SQLite realmente cargada.

La [documentación oficial de WAL, sección 11](https://www.sqlite.org/wal.html) informa el fallo WAL-reset: puede producir corrupción en condiciones raras; se corrigió en SQLite 3.51.3 y existen backports 3.44.6 y 3.50.7. La página sitúa el descubrimiento en el 3 de marzo de 2026 y la release 3.51.3 en el 13 de marzo de 2026. Referencia consultada durante esta integración documental del 12 de septiembre de 2026, hora de Argentina.

No se infiere de esos números que TokenUsage esté afectado. Falta comprobar el artefacto resuelto y la biblioteca cargada para cada distribución y arquitectura relevante. Registrar el resultado aunque la versión ya esté corregida; no introducir un cambio de dependencias no relacionado sin caracterización.

## 2. Procedimiento requerido en Windows

1. Construir el artefacto de la distribución bajo revisión con su grafo de dependencias resuelto. Registrar commit y versiones sin incluir rutas privadas.
2. Abrir una base de prueba propiedad de TokenUsage con el mismo proveedor nativo que utiliza la app. Consultar `SELECT sqlite_version();` y `SELECT sqlite_source_id();` desde esa conexión, no desde una instalación arbitraria de sqlite3 del PATH.
3. Registrar arquitectura, distribución, versión y source ID. Verificar por evidencia oficial que esa versión o backport incorpora la corrección. Una versión desconocida no pasa por una coincidencia textual aproximada.
4. Probar lectores, escrituras y checkpoints bajo la política real que se propone. Aceptar sólo resultados coherentes y errores transitorios controlados. Esta prueba funcional no demuestra por sí sola la ausencia del bug; también hace falta procedencia de la versión.
5. Repetir para portable/MSIX y x64/ARM64 que se publiquen, incluyendo una actualización real que pueda conservar binarios anteriores.

La salida es evidencia técnica sanitizada de una base sintética. No exportar la base privada del usuario ni abrir fuentes de terceros para cambiar su journal.

## 3. Condición de cierre

`GATE-SQLITE-NATIVE` exige versión cargada identificada, procedencia de la corrección, integridad después del escenario de concurrencia, comportamiento de fallos y evidencia por artefacto publicado. Si falta evidencia, la nueva política de concurrencia/migración permanece pendiente; no declarar una vulnerabilidad explotable ni sugerir borrar archivos de producción como remedio.

## 4. Copias y recuperación

El archivo WAL forma parte del estado persistente mientras contiene transacciones comprometidas. Copiar sólo `.db` no debe considerarse backup coherente de una base activa. Emplear la [Online Backup API](https://www.sqlite.org/backup.html) o una estrategia de cierre/copia demostrada en la frontera apropiada. No eliminar `-wal` o `-shm` para superar un error de apertura.

Un ensayo de recuperación debe incluir interrupción, disco lleno, permiso denegado, schema posterior al soportado y dos procesos abriendo el almacén. Un fallo no autoriza reemplazar la base por una vacía ni continuar con una migración a medias.

El proveedor SQLite de este paquete documental es irrelevante para certificar TokenUsage: los oráculos Python prueban propiedades de diseño y archivos, no la versión que carga la app .NET.

## 5. WSL y discos compartidos

La documentación WAL excluye su operación normal sobre sistemas de archivos de red. No abrir desde Windows una base mutable compartida de WSL o un recurso de red suponiendo que todas las garantías locales permanecen iguales. Validar el mecanismo concreto y preferir exportaciones numéricas acotadas o una interfaz local admitida cuando la topología no sea segura. Esto no declara que todo uso de WSL esté bloqueado: diferencia ubicación, puente y semántica del filesystem.

## 6. Casos de prueba propuestos

| Caso | Resultado requerido |
|---|---|
| TEST-SQLITE-01 | La versión se consulta desde el mismo proveedor nativo del artefacto, no desde otra CLI. |
| TEST-SQLITE-02 | Una versión afectada o de procedencia desconocida no se marca como verificada. |
| TEST-SQLITE-03 | Un backport oficial válido no se rechaza por ser numéricamente menor a la rama principal corregida. |
| TEST-SQLITE-04 | Un lector largo y un checkpoint concurrente conservan corrección y límites de recursos. |
| TEST-SQLITE-05 | Una actualización no mezcla bibliotecas nativas antiguas y nuevas sin detectarlo. |
| TEST-SQLITE-06 | Backup/restauración reproducen un snapshot coherente sin borrar archivos WAL manualmente. |

Todos estos casos permanecen propuestos, no ejecutados sobre el producto en esta entrega.
