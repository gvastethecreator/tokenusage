# Plan maestro · evolución de informes

**Estado:** propuesto. **Baseline:** `df50c36`. **Responsable de aceptación:** mantenedor del proyecto. **Fuentes de base:** TU-01 a TU-13 en [registro](references/10-SOURCES.md).

## 1. Resultado de producto

TokenUsage debe permitir pasar de un total observado a los modelos, configuraciones y fuentes que lo componen, y desde ahí a detalle verificable cuando exista. Cada pantalla debe contestar dos preguntas: «¿qué mide este número?» y «¿qué quedó fuera?». Más datos no justifican mezclar importes no comparables, invadir contenido o sugerir precisión inexistente.

El primer incremento utilizable es P0 + P1: corregir la semántica de Rates y convertir los datos existentes en un explorador. P2 añade profundidad de medición. P3 introduce una frontera nueva de privacidad y se habilita por consentimiento. P4 explica patrones y exporta resultados sobre esas bases. No hace falta tener P3 para publicar insights por modelo que sólo dependan de P2.

## 2. Límites del programa

Se conservan C#, WinUI 3, SQLite, las separaciones Core/Presentation/App/Providers, la composición Windows y la CLI. No se añade una segunda aplicación web, base analítica, servidor, cuenta de TokenUsage, motor de precios paralelo ni dependencia de ejecución en CodeBurn/AIStack. Las referencias sirven para diseño y metodología, no autorizan copiar parsers o ampliar permisos.

Quedan fuera de estas cinco fases: análisis de prompts, clasificación de tareas por comandos, perfiles públicos, sincronización social, ranking de calidad de modelos, auditoría de todo el navegador, estimación de tokens a partir de GPU y activación masiva de proveedores preparados/bloqueados. Inferencia realmente local y raíces WSL son extensiones posteriores con contratos propios; P2 prepara identidades, no promete esas integraciones.

## 3. Obligaciones transversales

| ID | Obligación | Evidencia mínima |
|---|---|---|
| REQ-001 | Conservación contable: componentes, eventos y rollups no se duplican. | Oráculos sintéticos y reconciliación sobre fuentes admitidas. |
| REQ-002 | Ausencia, cero, no aplicable y parcial son estados distintos. | Tabla de estados + tests de proyección y exportación. |
| REQ-003 | UI, Core y CLI comparten semántica y revisión de datos. | Comparación de DTO canónicos, no capturas de números abreviados. |
| REQ-004 | Timestamp no implica solicitud ni duración. | Casos con snapshot fechado, intervalo y día. |
| REQ-005 | Reportado, estimado, créditos, cuotas y cuenta no se suman indiscriminadamente. | Casos mixtos y etiquetas verificadas. |
| REQ-006 | Ningún contenido prohibido entra en persistencia o salida. | Canarios y pruebas negativas por frontera. |
| REQ-007 | Los cambios de filtro no disparan recolección. | Espía de recolección + prueba integrada. |
| REQ-008 | Una respuesta vieja no reemplaza una selección nueva. | Finalización fuera de orden + cierre de ventana. |
| REQ-009 | Toda nueva atribución es opt-in, reversible y explícita. | Máquina de consentimiento y carrera revocación/ingesta. |
| REQ-010 | Los resultados guardados conservan método y exclusiones. | Round-trip y versiones antiguas. |
| REQ-011 | Migraciones recuperables sin operar bases de proveedores. | Matriz de fallos y copia restaurada. |
| REQ-012 | Funciones críticas utilizables con teclado, Narrator y alto contraste. | Evidencia Windows empaquetada. |

Ningún promedio de calidad compensa una violación contable o de privacidad. La salida de una fase es booleana: sus gates críticos pasan, o la función permanece desactivada. No basta «compila» ni un porcentaje global de cobertura de código.

## 4. Dependencias reales

| Trabajo | Depende de | Puede avanzar en paralelo |
|---|---|---|
| P0 · Rates y semántica | Baseline y fixtures. | Inventario visual P1 sin modificar datos. |
| P1A · Explorer básico | Contratos de P0; datos actuales. | Especificación y fixtures de P2. |
| P1B · Configuración | Consulta raw elegible y avisos de retención. | No depende de nuevas sesiones. |
| P2 · Identidad y granularidad | REQ-001/002 y consulta compartida P1. | Política P3, sin activar colectores. |
| P3 · Atribución | Identidades P2, fuente admitida, política aprobada. | P4 por modelo, sin proyectos. |
| P4A · Insights por modelo | P1/P2 y diccionario de métricas. | Validación de exports. |
| P4B · Insights por sesión | P3 completo para esa fuente. | Ninguna extrapolación a proveedores sin soporte. |

Preparar un documento no equivale a habilitar una capacidad. Una fase posterior puede especificarse antes, pero sus cambios de esquema/lectura no se fusionan anticipadamente por comodidad.

## 5. Cortes de entrega

**Corte A — Corrección visible y navegabilidad.** P0 corrige sólo la semántica pertinente y añade regresiones. P1A añade una tabla de modelos útil en la ventana actual, detalle contextual y filtros agente/modelo/período. No agrega metadata nueva. El usuario puede abrir un número, entender su composición y ver lo excluido.

**Corte B — Primer detalle medido.** P2 habilita una fuente piloto, preferentemente el lector Codex ya admitido, después de demostrar qué clase de observaciones produce realmente. Si sólo existen deltas de intervalo, se entrega detalle de intervalos y no estadísticas de solicitudes. La fase no fracasa por negarse a fabricar llamadas: fracasa si las inventa.

**Corte C — Atribución voluntaria.** P3 incorpora proyecto/sesión para una única fuente validada. El modo sin permiso conserva el comportamiento anterior. Las otras fuentes siguen como no compatibles o sin asignar; no se llenan sus pantallas con ejemplos ficticios.

**Corte D — Explicaciones sustentadas.** P4 presenta un pequeño conjunto de reglas con buena evidencia. Exporta resultados con método, versiones y exclusiones. Los escenarios de precio son separados del costo registrado y nunca se convierten en una factura implícita.

## 6. Orden de PR propuesto

| PR lógico | Contenido acotado | No debe incluir |
|---|---|---|
| A | Caracterización P0 y corrección Rates. | Rediseño completo ni nueva base. |
| B | Consulta compartida y resultado con evidencia. | Nuevos permisos de lectura. |
| C | Model Explorer y proyecciones. | Cambios de precio o identidad. |
| D | Metadata/granularidad y migración aditiva P2. | Historial de conversaciones. |
| E | Fuente piloto P2 y actividad. | Activación simultánea de todo el catálogo. |
| F | Consentimiento, purge y contratos P3. | Recolección habilitada por defecto. |
| G | Una integración de atribución. | Heurísticas de prompts. |
| H | Insights, snapshot y exportadores. | Rankings causales o publicación remota. |

Estas letras no son ramas creadas. Cada PR real debe enlazar su issue y tener alcance aprobado, siguiendo la contribución del repositorio. Adaptar las divisiones a la revisión: no convertir el plan en ocho megacambios inevitables.

## 7. Decisiones que se fijan antes de implementar

P0: método de Rates basado en intersección de eventos/revisiones y políticas de pricing explícitas. P1: navegación nativa y consultas sin ingesta; configuración avanzada sólo cuando haya soporte retenido. P2: identidad lógica independiente del parser y metadata lateral con `Unknown` histórico. P3: consentimiento por fuente/capacidad, claves locales y alias voluntarios. P4: fórmulas versionadas, JSON canónico y salidas derivadas.

Los nombres propuestos de DTO/clases se pueden adaptar. No se pueden cambiar silenciosamente los significados. Las decisiones sobre versión de esquema definitiva, presupuesto de rendimiento aprobado y formas reales de cada proveedor son gates de implementación, no supuestos ocultos.

## 8. Riesgos con tratamiento

| Riesgo | Señal temprana | Respuesta |
|---|---|---|
| Se pierde detalle en rollups. | Filtros avanzados dejan de coincidir tras retención. | Declarar horizonte granular y población elegible. |
| Cohortes distintas parecen cambio de tarifa. | Mismo total de tokens, distintas identidades valorizadas. | Intersección por identidad/revisión, no por volumen. |
| Lectores de snapshot pisan otras raíces. | Reconciliación borra a nivel agente. | Acotar a instancia antes de activar múltiples raíces. |
| Nueva UI reimplementa cálculos. | CLI y pantalla difieren. | Un DTO semántico y tests de paridad. |
| Metadatos se filtran por errores. | Ruta o alias aparece en stack/log exportado. | Errores tipados y proyección allowlist. |
| «Async» bloquea la ventana. | Pausas al ejecutar consultas locales. | Scheduler de trabajo acotado y medición; no confiar sólo en sufijos Async. |
| Downgrade pierde datos. | App vieja abre esquema nuevo o se restaura backup obsoleto. | Barrera de escritores, plan de recuperación y consentimiento al descarte. |

## 9. Cierre y continuidad

Una fase termina con manifiesto de evidencia: SHA, cambios, comandos/resultados exactos, capturas, datos sintéticos, riesgos y verificaciones no ejecutadas. El resultado se incorpora al backlog siguiente, sin reabrir tareas ya probadas salvo cambios de código o entorno relevantes. El siguiente agente debe poder continuar desde ese manifiesto, no reconstruir la intención leyendo conversaciones.

La definición de éxito es concreta: información más profunda sin regresiones en datos, privacidad ni interacción. Las cantidades de documentos, tests o gráficos no son indicadores de éxito por sí solas.
