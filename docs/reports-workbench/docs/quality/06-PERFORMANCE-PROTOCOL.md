# Rendimiento · protocolo y presupuestos provisionales

**Estado:** objetivos propuestos, no benchmarks ejecutados. **Fuentes:** TU-08/12/14/16 y EXT-01/04/05. [Fuentes](../references/10-SOURCES.md).

## 1. Qué se mide

Separar apertura de Reports, planificación/consulta, proyección, renderizado, consulta de detalle, recolección inicial e incremental, exportación y migración. Una consulta rápida no prueba que el scroll sea fluido; un arranque rápido con caché no prueba backfill. El costo de una migración no debe ocultarse dentro de un primer render sin progreso.

Registrar duración wall-clock, CPU, asignaciones/memoria de proceso, tamaño DB/WAL, filas leídas, cantidad de consultas y tareas simultáneas. Nunca incluir rutas privadas o IDs de sesión en trazas de rendimiento compartidas.

## 2. Entorno de referencia

Usar Windows, build Release sin debugger, arquitectura y paquete declarados, unidad local y política de energía registrada. Anotar CPU, RAM, disco, resolución/DPI, versión .NET/Windows/SQLite efectivo y antivirus relevante. Elegir una máquina de referencia reproducible; no usar la GPU del usuario como sustituto de ese entorno.

Fixture A: 100.000 registros; B: 1.000.000; C: 5.000.000 como stress, sin promesa automática de latencia. Mezclar al menos 30 modelos, varias herramientas/instancias, datos no valorizados, días/intervalos/snapshots correctamente representados y filas legacy. Distribuciones skewed y períodos densos son necesarios; datos uniformes solos ocultan planes malos.

Los fixtures de carga se generan con seed publicada y sin datos reales. Registrar tamaño final y distribución; «un millón de filas» con todas las columnas vacías no representa el mismo costo que historia enriquecida.

## 3. Objetivos provisionales que deben calibrarse

| Operación | Escenario | Objetivo inicial, no medido |
|---|---|---|
| Respuesta visual a selección | Sin esperar DB. | Feedback perceptible antes de 100 ms. |
| Consulta Explorer caliente | B, filtro de 30 días. | p95 <= 500 ms en máquina aprobada. |
| Primera consulta fría | B, 30 días, sin escaneo de proveedor. | p95 <= 2 s. |
| Primera página de detalle | B, 200 filas. | p95 <= 300 ms con índice adecuado. |
| Descartar resultado obsoleto | Cambio rápido de selección. | Ninguna publicación tardía; cancelación lógica inmediata. |
| Trabajo UI atribuible a consulta | Durante interacción. | Ningún bloqueo recurrente > 50 ms en hilo de UI. |
| Memoria de consulta | B, Explorer sin export masivo. | Crecimiento transitorio objetivo <= 100 MiB, sujeto a baseline. |
| Recolección al filtrar | Cualquier tamaño. | Cero escaneos de fuentes. |

Estos números son criterios iniciales de ingeniería, no garantía universal ni evidencia de que el código actual falle. Medir baseline, acordar presupuesto por escenario y registrar cualquier ajuste con razón. No reducir la carga o quitar evidencia para «pasar» una cifra.

## 4. Cómo ejecutar un benchmark

Preparar DB/fuentes sintéticas, registrar versiones y verificar los totales antes de medir. Cerrar procesos no pertinentes; ejecutar calentamientos fuera de la muestra. Para consultas calientes, al menos 30 muestras por escenario; para frías, varias ejecuciones de proceso nuevo con política de caché documentada. Un proceso nuevo no implica necesariamente caché de disco del sistema fría: declarar lo medido.

Guardar distribución, mediana, p95, máximo y errores. No reportar sólo el mejor intento. Alternar versiones baseline/candidata para reducir sesgo por temperatura/caché. Comparar mismo fixture, mismos índices y misma configuración. La repetición adicional no convierte automáticamente correlación en causalidad, pero vuelve el resultado revisable.

## 5. Ejecución y cancelación

Microsoft.Data.Sqlite no vuelve asíncrono el I/O por usar Async. Planificar consultas fuera del hilo de UI mediante un executor acotado, no un Task.Run por cada evento de teclado. Una consulta de lectura posee su conexión; un escritor coordinado actualiza la base. Ajustar locks/timeouts con pruebas de contención, sin waits infinitos.

Propuesta inicial: una consulta principal activa y una pendiente reemplazable por ventana; detalle separado con cota pequeña. Cancelar trabajo anterior y descartar por generación aunque la cancelación física no sea inmediata. Paginación y consultas acotadas son preferibles a abortar comandos gigantes. No prometer un timeout de cancelación que no se haya verificado en el proveedor.

## 6. Optimización en orden

Primero asegurar plan SQL y proyección correcta. Después índices selectivos sobre patrones reales de consulta, selección de columnas permitidas y agregación en DB donde preserve semántica. Luego caché por revisión y virtualización de filas. Sólo después considerar estructuras agregadas adicionales con invalidación/reconstrucción comprobables.

No agregar todos los índices imaginables: encarecen escritura, espacio y migración. Usar EXPLAIN QUERY PLAN sobre fixtures relevantes y comparar. No almacenar cada combinación de filtros como una tabla permanente. No cambiar a otra base analítica sin evidencia de que el diseño simple es insuficiente.

## 7. Recolección y WSL

Medir initial scan separado del incremental. Las cotas existentes por proveedor no se eliminan para perseguir completitud; cuando se alcanzan, devolver estado parcial y continuar por un mecanismo documentado. Watchers son avisos, no fuente de verdad: debounce y reconciliación limitada.

La base propia debe permanecer en una ubicación local soportada. No abrir una DB de proveedor WSL vía una ruta que rompa semántica de locks y asumir seguridad por read-only. Esa integración requiere prueba específica o export numérico admitido; nunca copiar transcripciones completas. WAL no es una solución universal para filesystems de red. [EXT-04]

## 8. Export y recursos

Liberar el snapshot de lectura antes de renderizar varias vistas cuando sea posible. Exportar con límites declarados, progreso/cancelación y temporal atómico. No cargar millones de registros y HTML duplicado en memoria sin límite. Si se elige export parcial por tamaño, debe decirlo antes y en el manifiesto; preferir rechazar/solicitar un rango menor a truncar silenciosamente.

## 9. Gate de cierre

Entregar el script/protocolo compatible con los mecanismos del repo, fixtures reproducibles, baseline y candidata, valores completos, capturas de perfil y cambios de planes. No añadir browser runners a TokenUsage; su contribución los excluye. Este paquete no incluye benchmarks del producto: la carpeta validation sólo informa verificaciones documentales/sintéticas realizadas.
