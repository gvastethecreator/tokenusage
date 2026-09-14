# Arquitectura · contratos, consistencia y fronteras

**Estado:** diseño propuesto sobre arquitectura existente. **Fuentes:** TU-02/03/07/08/09/13/17, EXT-01/04/05/08. Resolver IDs en [Fuentes](../references/10-SOURCES.md).

## 1. Un solo motor de informes

```text
Fuentes admitidas ── lectores existentes ── normalización/reconciliación
                                                   │
                                       SQLite de TokenUsage
                                                   │
                                   ReportQuery / ReportReadSession
                                                   │
                                Resultado + evidencia + exclusiones
                                      │                     │
                                Presentation               CLI
                                      │                     │
                                    WinUI             JSON / exports

Cuota observada y cuenta/día ── canales de referencia, no otro sumando
```

No se crea un ensamblado por cada caja. Las responsabilidades nuevas viven inicialmente en proyectos/carpetas existentes. Core no depende de WinUI ni conoce colores, traducciones o clases de Providers. Si necesita valorar un escenario, recibe una política/interfaz pura que implementa la composición usando catálogos existentes.

Presentation construye filas/gráficos a partir del resultado, sin reinterpretar la métrica. App administra interacción y ejecución. Runtime.Windows conecta dependencias. CLI comparte interpretación; no reconstruye totales con su propia lógica.

## 2. Modelo de consulta

Un `ReportSelection` inmutable contiene rango y clase de rango, zona solicitada, filtros tipados, agrupación, resolución, modo de precios y versión de método. No aceptar SQL libre, nombres arbitrarios de columnas ni diccionarios de metadata genéricos. La selección se canonicaliza: listas ordenadas/sin duplicados, vacíos con significado fijo, IDs y límites validados.

Un `ReportPlan` declara qué materialización puede responder: rollups, raw retenido o combinación de particiones sin solapamiento. Explica dimensiones no disponibles y exige una decisión del usuario cuando un filtro cambia la población. `Effort=high` no filtra registros legacy desconocidos como si fueran low; los excluye explícitamente.

Un fingerprint de consulta identifica la selección semántica; el orden visual, tema y tamaño de ventana no pertenecen a ese hash. El fingerprint de cohorte identifica eventos/revisiones o manifiesto equivalente de una proyección retenida, según el método. Igual cantidad de tokens nunca reemplaza esa identidad.

## 3. Consistencia de lectura

Propuesta: `ReportReadSession` con conexión de lectura y transacción de snapshot limitada. Lectura de totales, versiones, exclusiones y detalle de una página ocurre dentro de esa revisión, y produce DTO inmutables. No compartir una conexión entre operaciones concurrentes ni dejar una transacción viva mientras el usuario navega.

El repositorio inspeccionado abre conexiones en varios métodos. Refactorizar mediante overloads internos que acepten conexión/transacción o una sesión explícita; conservar APIs públicas como envoltorios si hay consumidores. No arreglarlo envolviendo llamadas que abren sus propias conexiones en una transacción que no usan.

La ingesta/reconciliación aumenta una revisión monotónica en la misma transacción de hechos/rollups/metadata. Cambios de aliases, método o permisos invalidan las proyecciones mediante revisiones separadas o una firma compuesta. `AsOfUtc` por sí solo no garantiza coherencia.

Para páginas sucesivas, incluir revisión y cursor estable. Si cambió la revisión y el motor no conserva snapshots históricos abiertos, devolver `SelectionChanged` y permitir recargar; no fingir continuidad exacta mezclando páginas de revisiones diferentes. Para export grande, materializar un snapshot acotado o generar bajo una lectura consistente con límites y medición; no abrir una transacción por cada formato.

## 4. Contrato de métrica

```text
MetricValue
  MetricId + DefinitionVersion
  State: Available | Partial | Unavailable | NotApplicable | Invalid
  Value? + Unit
  EvidenceKind: Observed | Derived | CatalogEstimated | Mixed | LegacyUnknown
  EligibleRecords + EligibleTokens
  Denominator? + DenominatorUnit?
  Exclusions[]
  MethodId
```

Disponibilidad y procedencia son ejes distintos. Un costo estimado puede estar disponible y ser parcial en cobertura; no crear un enum donde `Estimated` y `Stale` se excluyan. Freshness pertenece a la recolección/población, no cambia la unidad de la métrica.

Una métrica sin denominador no recibe uno ficticio de cero. El campo `value` falta o es null según contrato cuando no hay valor calculable. No permitir `NaN`/Infinity ni serializarlos como texto que parece número. Los valores signed se reservan para diferencias; tokens/costos observados no admiten negativos arbitrarios.

## 5. Contrato de registro extendido

Metadata lateral por `EventKey` + revisión: instancia, granularidad, disponibilidad de componentes, método de normalización y época de privacidad cuando exista atribución. `Unknown` es default histórico. La ausencia de metadata no invalida automáticamente el consumo agregado existente.

Guardar `ObservedModelId`, canónico y política de canonicalización por separado. Un alias nuevo no modifica retroactivamente resultados guardados. No introducir un segundo catálogo de modelos sólo para la UI; reutilizar el existente y añadir procedencia donde falte.

`SourceCapabilities` se vincula a tipo/versión de fuente y lector. Un `CollectionAttempt` contiene tiempos, estado, cantidades, límites y códigos allowlist, no excepciones/JSON crudos. `Complete` significa ejecución de una lectura admitida dentro de su alcance, no observación de toda la cuenta o PC.

## 6. Elegibilidad, límites y exclusiones

Cada registro puede tener múltiples razones de exclusión. Para reconciliar cantidades totales sin duplicarlas, elegir una razón primaria con precedencia versionada y permitir razones secundarias no sumables. Ejemplo: raw expirado → fuente sin soporte → permiso → tiempo → componentes → precio → tamaño de muestra. No sumar repetidamente los tokens de un registro que carece de tiempo y de precio.

El resultado debe distinguir universo seleccionado, población elegible para la métrica y subconjunto visible Top N. La serie horaria puede no sumar el total civil porque hay consumo no ubicable; mostrar exactamente ese volumen o declarar que no puede cuantificarse, no ocultar la diferencia.

Top N + Other sólo suma grupos que sean una partición disjunta. Padre/hijos o datos de cuenta no cumplen esa propiedad automáticamente. El planificador impide consultas cuyo total implicaría sumar conjuntos solapados sin autoridad resuelta.

## 7. Compatibilidad y serialización

El v1 permanece estable. Los esquemas del paquete llevan `draft.v2` y sirven para discutir contratos, no para afirmar soporte de flags nuevos. Propuesta v2: cadenas decimales para 64-bit y micro-USD, discriminantes estrictos y método versionado. Publicar una versión sólo después de inventariar consumidores y completar tests de proceso CLI.

JSON Schema valida forma, no toda la semántica: rango UTC creciente, suma de componentes, elegibilidad y permisos requieren validaciones adicionales. No habilitar resolución de `$ref` remotos para archivos importados de usuarios. Limitar bytes, profundidad, filas y longitudes antes de materializar.

## 8. Pruebas de arquitectura

Agregar tests que impidan Core → App/WinUI/Providers si esa regla coincide con la arquitectura vigente, que UI/CLI no reimplementen fórmulas y que cambiar filtros no invoque lectores. No usar tests frágiles que sólo busquen una cadena de texto cuando se puede probar comportamiento en el seam.

Caracterizar `ReadAsync`, `ReadExactAsync`, `FilterByModel` y los modos de comparación antes de sustituir llamadas. Tests de interleaving: una ingesta entre consultas no genera un resultado híbrido; una revocación entre lectura y publicación no expone metadata antigua. Todas las salidas comparten revisión y selección, aunque sus formatos difieran.

## 9. Decisiones pendientes deliberadas

No se fija el nombre final del nuevo namespace ni el número de esquema. Sí se fija el principio de una sola semántica, defaults históricos desconocidos y migración aditiva. Antes de codificar, confirmar las fronteras que ya prueba `TokenUsage.Architecture.Tests` si ese proyecto existe en el SHA de trabajo; localizarlo, no inventar un comando de test hacia un archivo no comprobado.
