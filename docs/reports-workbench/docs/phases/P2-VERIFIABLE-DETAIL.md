# P2 · Detalle verificable, identidad y granularidad

**Estado:** propuesta. **Entrada:** P1 y contratos de exactitud. **Salida:** una fuente piloto con detalle respaldado, ingesta idempotente y actividad temporal honesta. **Fuentes:** TU-02/03/07/08/10/13/14; EXT-01/04/05 en [Fuentes](../references/10-SOURCES.md).

## 1. Objetivo y límite

Conservar suficiente evidencia numérica para explicar de dónde provienen los totales, sin retener conversaciones. El piloto comienza con el colector Codex existente; no se presupone que sus deltas sean solicitudes finales. Si la fuente permite intervalos y no llamadas, el resultado correcto es un explorador de intervalos con la limitación visible.

P2 prepara instancias, revisiones y capacidades. No habilita proyectos/sesiones personales todavía, no modifica credenciales ajenas ni abre automáticamente WSL. El campo técnico de correlación usado para deduplicar no se convierte por eso en una función de navegación por conversaciones.

## 2. Requisitos

| ID | Requisito |
|---|---|
| REQ-P2-01 | Identidad lógica separada de parser, precio, ubicación accidental y observación repetida. |
| REQ-P2-02 | Granularidad y precisión temporal se almacenan por separado. |
| REQ-P2-03 | Actualizaciones finales y revisiones no se suman a su versión previa. |
| REQ-P2-04 | Reconciliación sólo reemplaza la partición para la que tiene autoridad. |
| REQ-P2-05 | No hay llamadas, percentiles o velocidades sin evidencia adecuada. |
| REQ-P2-06 | Historial de captura informa fallos y límites sin registrar contenido. |
| REQ-P2-07 | Retención no elimina totales durables ni inventa detalle expirado. |
| REQ-P2-08 | La activación exige evidencia de una instalación real y su versión. |

## 3. Contrato de observación

Proponer una extensión lateral, vinculada al evento existente, con `SourceInstanceKey`, `RecordKind`, `RecordRevision`, disponibilidad de componentes, versión de normalización y procedencia. Los eventos legacy sin ese dato quedan `Unknown`; no deducir solicitudes a partir de `EventCount` ni llenar con la hora de importación.

| RecordKind propuesto | Significado | Cómo puede contribuir |
|---|---|---|
| RequestFinal | Solicitud final identificada, con contadores definitivos. | Tokens/costo y estadísticas por solicitud si sus campos son elegibles. |
| IntervalDelta | Consumo incremental durante un intervalo conocido. | Totales e intervalos; no necesariamente cantidad de solicitudes. |
| Snapshot | Contador acumulativo o estado observado. | Se reconcilia/normaliza antes de convertirse en consumo; no se suma cada lectura. |
| DailyAggregate | Total diario sin distribución dentro del día. | Totales civiles compatibles; nunca histograma horario exacto. |
| Unknown | Tipo no demostrado o legado. | Mantiene totales existentes; no habilita métricas nuevas de detalle. |

Separar `ObservedAtUtc` —cuándo lo leyó TokenUsage— de `OccurredAtUtc` —cuándo corresponde la actividad—. Para intervalos, documentar si la fuente usa extremo abierto/cerrado; no reinterpretar el final de un acumulador como inicio de solicitud. La UI no llama «latencia» a la diferencia entre dos observaciones de un archivo.

Los snapshots que no puedan convertirse en consumo no entran como nuevos eventos sumables. Pueden conservarse como evidencia numérica acotada en un canal de observaciones. Las correcciones firmadas de un total de sesión no son deltas negativos sueltos en una colección diseñada sólo para valores no negativos: actualizan/reconcilian la versión canónica correspondiente.

## 4. Identidad de fuentes y registros

`SourceInstanceKey` identifica una raíz/perfil admitido de una instalación, no una cuenta remota ni una dirección de carpeta exportable. La autoridad de lectura se registra separadamente: qué partición y ventana puede corregir esa instancia.

La identidad lógica usa IDs nativos cuando están disponibles y permitidos; se representan mediante claves opacas locales con ámbito. No basarla únicamente en tokens, timestamp ni nombre de modelo. Dos solicitudes distintas con contadores iguales deben permanecer distintas; el mismo registro repetido después de rotación debe seguir siendo uno.

Parser y catálogo no forman parte de la identidad lógica. Sí pueden formar parte de la revisión de representación. Una corrección numérica aumenta revisión y modifica la proyección; un cambio sólo de catálogo en un escenario no altera el evento histórico. Guardar una firma de payload **numérico permitido**, no un hash del prompt completo.

### Compatibilidad de claves actuales

No recalcular todas las claves existentes al añadir instancia: eso puede reinsertar la misma historia. Incorporar un puente explícito entre la identidad nueva y la clave legacy cuando se pueda demostrar correspondencia. Cuando no se pueda, conservar la historia legacy como partición no atribuida y limitar el backfill para evitar solapamiento; no adivinar el enlace por igualdad de totales.

Una segunda ruta al mismo archivo requiere reconocimiento de equivalencia o una selección explícita de autoridad. Junctions, aliases de carpeta y vistas Windows/WSL no deben contarse automáticamente dos veces. La ausencia de acceso a una raíz no indica cero consumo ni ausencia de instalación.

## 5. Reconciliación y transacción

Por lote, validar límites y fuente; resolver identidades; determinar inserciones, revisiones y registros retirados dentro de la ventana **completa y autoritativa**; actualizar eventos, metadata, rollups y revisión global en una transacción. No confirmar el checkpoint como procesado antes del commit numérico.

Si el checkpoint vive en otro archivo, usar un journal de lote/versionado que haga seguro repetir la operación tras crash. No afirmar atomicidad entre un JSON de checkpoint y SQLite por guardarlos uno después del otro. La repetición debe ser idempotente incluso si el proceso murió entre esos pasos.

La API actual de reemplazo/reconciliación se orienta por agente. Antes de habilitar varias instancias, extender ese ámbito y todos sus deletes/rollups/tombstones para que una raíz no borre la historia de otra. No activar multi-root hasta probar esta propiedad. Los filtros de UI nunca cambian la autoridad del colector.

Una lectura `Partial`, truncada, cancelada o con esquema desconocido no puede borrar registros ausentes como si su lista fuera completa. Mantener la última evidencia fiable, registrar el fallo y no avanzar el cursor más allá del último límite confirmado.

## 6. Contadores acumulativos: política explícita

Ejemplo sintético: el contador de una generación pasa 100 → 160 → 160 → 200. El total observado al final sigue siendo 200, no 620. Si sólo se observa la primera lectura 100 sin hora de inicio conocida, no asignar esos 100 a los últimos segundos: conservar su alcance acumulativo o desconocido según contrato.

Cuando baja 200 → 30, distinguir reinicio de generación, corrección del registro, cambio de cuenta/perfil, archivo rotado o corrupción. `max(0, nuevo - anterior)` oculta esas diferencias. Si existe identificador de generación, abrir una nueva; si no hay evidencia suficiente, poner la transición en estado incierto y reconciliar por la fuente autoritativa.

Streaming: acumular o reemplazar dentro de la misma identidad de solicitud; sólo una versión final participa en métricas de solicitud final. En un fallo sin final, el consumo conocido puede permanecer en una categoría parcial, sin contarlo como respuesta final exitosa. Reintentos reales sólo se cuentan cuando existe un identificador/estado que los distingue; no deducirlos de dos ediciones o llamadas cercanas.

## 7. Capacidades y disponibilidad

Una capacidad se define por tipo y versión de fuente/lector, más evidencia de instalación. No basta `Codex supports requests = true` para siempre. Describir precisión, granularidad, identidad, caché, costos, configuración, duraciones y alcance.

La capacidad potencial no sustituye disponibilidad por registro. Una fuente puede admitir tier, pero una versión antigua no tenerlo en ese evento. El planificador cruza soporte, permisos, metadatos reales y retención. Cada métrica devuelve estado, población, denominador y motivos de exclusión.

No exponer rutas/IDs nativos en diagnósticos. `UnsupportedSchema` debe mencionar versión de lector, forma reconocida y acción posible sin interpolar JSON original. Mantener tamaño de entrada, profundidad JSON, filas, archivos y tiempo de lectura acotados por fuente.

## 8. Activity y límites temporales

El planificador construye la vista exacta con timestamps de actividad y sólo usa intervalos donde su tratamiento esté definido. Las consultas de tiempo son semiabiertas `[inicio, fin)`; un evento puntual en el límite final pertenece al rango siguiente. No cambiar retroactivamente la convención interna de un intervalo legacy: mapearlo con pruebas de frontera.

Si un intervalo cruza el límite o abarca varios buckets, no prorratear por defecto. Mostrarlo como intervalo aparte o excluirlo de la serie exacta con su cantidad. Si en el futuro existe distribución estimada, va en modo distinto, con método, y no se agrega a la serie exacta duplicando consumo.

Un día agregado no se coloca a medianoche, mediodía ni la hora de escaneo. La vista puede incluir una banda `Not placed on exact timeline`. Un hueco sin observación no se representa como actividad cero. En DST, dos horas locales iguales se distinguen por offset/instante; no comprimir días de 23/25 horas a 24 muestras ficticias.

## 9. Almacenamiento y retención

La migración aditiva propone tablas laterales de metadata, instancias y journal de captura. Los números de versión se asignan sobre main al implementar; «schema 6» no se reserva desde un documento. Índices candidatos: instancia+tiempo, modelo+tiempo y claves de identidad/revisión. Añadir sólo los que consultas medidas justifiquen.

El journal nuevo de recolección es distinto del journal existente de cuotas. Registrar inicio/fin, estado, contadores, límite alcanzado y código de error allowlist. Propuesta de cota inicial: 90 días y 50.000 intentos, configurable sólo mediante decisión revisada; no heredar por accidente los 250.000 de cuotas. Al purgarlo, no cambia la contabilidad.

Al vencer el raw, conservar rollups existentes y referencias agregadas necesarias para explicar precios/parser cuando estén disponibles. No conservar IDs de sesión sólo «por si acaso» antes de P3. Percentiles, sesiones y detalles por solicitud dejan de estar disponibles cuando expira su evidencia; no se reconstruyen a partir de una media diaria.

## 10. Secuencia de implementación

**TASK-P2-01:** caracterizar una fuente piloto real, autoridad, IDs, actualización de contadores y límites. **TASK-P2-02:** fijar metadata y plan de compatibilidad de claves. **TASK-P2-03:** migración, revisión coherente y transacción/replay. **TASK-P2-04:** extender únicamente el lector piloto con fixtures y budgets. **TASK-P2-05:** habilitar Activity y detalle según capacidades demostradas. **TASK-P2-06:** pruebas de fallos, retención, multi-root desactivado o probado y smoke Windows.

No habilitar `RequestFinal` como fallback cómodo para llegar a la pantalla de distribuciones. Un piloto que sólo demuestra `IntervalDelta` entrega ese valor correctamente y deja la capacidad solicitud como gate independiente.

## 11. Pruebas esenciales

| ID | Caso | Oráculo |
|---|---|---|
| TEST-P2-01 | Ingesta repetida y rotación. | Una identidad lógica, ningún aumento por repetición. |
| TEST-P2-02 | IDs diferentes, tokens iguales. | Dos registros distintos. |
| TEST-P2-03 | Provisional → final corregido. | Final reemplaza, no suma. |
| TEST-P2-04 | Contador resetea o cambia generación. | Política de generación explícita; no clipping silencioso. |
| TEST-P2-05 | Lote incompleto omite registros viejos. | No los elimina ni declara lectura completa. |
| TEST-P2-06 | Dos instancias del mismo agente. | Reconciliar una no afecta la otra. |
| TEST-P2-07 | Crash entre commit y checkpoint. | Replay idempotente y sin pérdida. |
| TEST-P2-08 | Snapshot con timestamp exacto. | No cuenta como solicitud ni latencia. |
| TEST-P2-09 | Día agregado + intervalo que cruza buckets. | No crea picos ni distribución uniforme implícita. |
| TEST-P2-10 | Detalle expira y rollups permanecen. | Total durable igual; estadísticas granulares no disponibles. |
| TEST-P2-11 | DB ocupada, última línea incompleta, archivo gigante. | Límites y estado parcial; UI no bloqueada indefinidamente. |
| TEST-P2-12 | Cambio parser con identidad legacy. | No duplica historia ni cambia precio registrado sin procedimiento. |
| TEST-P2-13 | Fuente piloto en Windows real, versión registrada y referencia del mismo intervalo. | Totales y capacidades coinciden con evidencia admitida; instalaciones ausentes o incompatibles no se anuncian como soporte activo. |

## 12. Gates y evidencia

**GATE-P2-A:** conservación e idempotencia bajo fallos. **GATE-P2-B:** capacidad realmente demostrada, incluyendo ejemplo de fuente sanitizado de forma admitida. **GATE-P2-C:** autoridad por partición y política de tiempo correctas. **GATE-P2-D:** migración/retención/rollback ensayados y UI/CLI concordantes. La fuente no se marca activa por existir un fixture inventado: es requisito del repositorio aportar evidencia real de Windows.

Entregar a P3 claves opacas, revisión de datos y frontera de consentimiento preparada, pero sin haber recolectado metadata de proyectos/sesiones fuera del permiso actual.
