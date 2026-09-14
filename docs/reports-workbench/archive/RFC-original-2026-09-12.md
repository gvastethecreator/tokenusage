# TokenUsage — Reports Workbench

**RFC de producto y arquitectura · 12 de septiembre de 2026**

Estado: propuesta para revisión; no implementada ni publicada en el repositorio.
Base de TokenUsage inspeccionada: `df50c367b083e5624bf09213eddc7013d8299aa4` (`main` al consultar).
Método: lectura estática de contratos, consultas auxiliares, interfaz de informes, documentación y referencias. No se compiló ni ejecutó la aplicación de Windows. No se inspeccionaron datos personales de una PC. El HTML adjunto emplea únicamente datos sintéticos.

## 1. Decisión recomendada

Evolucionar el informe existente hacia un **explorador local de uso por modelo**, conservando C#, WinUI 3, SQLite, los recolectores admitidos y la CLI. Extraer lógica de consulta y políticas de medición a componentes comprobables del Core; reutilizar Presentation para modelos de presentación. No introducir un segundo motor de precios, una aplicación Electron, un servidor remoto ni una dependencia de ejecución en CodeBurn.

El objetivo no es registrar indiscriminadamente la actividad de la computadora. Es explicar el uso observado, su procedencia y sus límites. Debe ser posible navegar de un total a su desglose y a la evidencia que lo sustenta, sin convertir estimaciones en facturas ni heurísticas en pruebas de productividad.

### No objetivos de la primera entrega

- Clasificar tareas leyendo prompts, comandos o transcripciones.
- Puntuar la calidad de un modelo a partir de tokens, commits o cantidad de ediciones.
- Prometer observación completa de navegadores, cuentas remotas u otras máquinas.
- Activar proveedores bloqueados para alcanzar paridad de catálogo.
- Reescribir el sistema de cuotas, precios, tray o almacenamiento.
- Implementar perfiles públicos, sincronización social o un analista basado en otro LLM.

## 2. Línea de base verificada

| Área | Evidencia existente | Consecuencia para el diseño |
| --- | --- | --- |
| Eventos normalizados | `UsageEvent.cs`: agente, proveedor del modelo, modelo, modelo observado, tokens, costo, parser, cobertura | Extender el contrato; no reemplazarlo |
| Precisión temporal | `Unknown`, `Timestamp`, `Interval`, `Daily`, comienzo de intervalo | Planificar consultas según precisión; no inventar horas |
| Configuración | `ReasoningEffort`, `ServiceTier` con etiquetas admitidas | Convertirlos en filtros y agrupaciones donde estén disponibles |
| Datos y comparación | Esquema 5, cuenta/día separado, estado de recolección, comparaciones guardadas | Migración aditiva; no volver a implementar las funciones existentes |
| Interfaz | Global/proveedor/comparación; modelos/días/fuentes; períodos y ciclos | Agregar profundidad y navegación antes que más gráficos aislados |
| Evidencia | `UsageReportViewModel.MeasurementDetails.cs` | Reutilizar la evidencia y vincularla al gráfico/selección |
| Presentación | Proyecto `TokenUsage.Presentation` ya presente | Aprovechar esa separación; no presentarla como una novedad |
| Recolección | Fuentes de snapshot y snapshot por ventana | Reutilizar la reconciliación; no asumir que todo es un evento append-only |
| Retención | Documentados 400 días de eventos, rollups diarios durables, horizonte Codex de 35 días | El detalle histórico sólo es posible donde todavía exista evidencia granular |

La documentación distingue 56 identidades de catálogo de 11 lectores activos. La cobertura adicional requiere evidencia de fuente, no sólo un nombre o una pantalla.

### Archivos relevantes inspeccionados

```text
src/TokenUsage.Core/Usage/
  UsageEvent.cs
  IUsageEventSource.cs
  UsageRepository.Measurement.cs
  UsageComparison.cs
src/TokenUsage.App/ViewModels/Reports/
  UsageReportViewModel.cs                     # primera sección inspeccionada
  UsageReportViewModel.MeasurementDetails.cs
src/TokenUsage.Presentation/ViewModels/Reports/ # estructura verificada
  ReportDataProjection.cs
  UsageReportModels.cs
  UsageReportRequest.cs
  UsageReportResetMarkers.cs
PRIVACY.md
README.md
docs/PROVIDER-MATRIX.md
```

El archivo principal de `UsageReportViewModel` ocupa aproximadamente 104 KB, además de sus archivos parciales. Dividir por clases parciales ya existe; la mejora propuesta es separar responsabilidades y probar políticas, no producir más archivos parciales por sí mismos.

## 3. Qué tomar de las referencias

### CodeBurn

Tomar la navegación por modelo, proyecto y sesión, las distribuciones de consumo, las comparaciones y las explicaciones que llevan del resumen al detalle. No adoptar todo su recolector o su runtime como una caja negra.

Su `src/classifier.ts` clasifica a partir de nombres de herramientas y palabras del mensaje del usuario. Su detección de reintentos emplea secuencias de edición del mismo archivo, verificación mediante shell y nueva edición. Son heurísticas que necesitan contenido y no equivalen a fallos de una API ni a calidad de trabajo. Trasladarlas tal cual cambiaría el contrato de privacidad de TokenUsage.

### AIStack

Tomar la construcción de agregados combinables desde una misma secuencia de respuestas, identificadores de proyecto opacos, tratamiento explícito de tokens sin precio y asociación entre costo y referencia de precio.

En `packages/cli/src/usage/days.ts` se construyen sumas por herramienta/día/modelo, con desglose de cache-write por TTL cuando existe. Las medias y porcentajes no se envían como átomos agregables. Esta distinción es útil: sumar cantidades y denominadores, no promediar promedios.

Su CLI también documenta un mapa de tamaños de llamadas de Grok. Reconoce que el desglose de instrucciones/harness no está disponible y que la rotación del log puede dejar huecos. Esa honestidad de cobertura es más importante que copiar su estética.

No adoptar su publicación de perfiles, subida de configuraciones, login remoto o sincronización social para resolver informes locales.

## 4. Experiencia de producto propuesta

### 4.1 Model Explorer — primera entrega visible

Barra común: intervalo, zona de presentación, herramienta, proveedor del modelo, modelo canónico/observado, configuración y calidad de evidencia. Los filtros no deben desaparecer al abrir un detalle o cambiar de pestaña.

Tabla inicial: modelo, herramienta, tokens observados, desglose disponible, valor reportado, valor estimado, tokens sin precio, variación y evidencia. El usuario abre una fila para ver su composición, evolución y límites. Una selección en un gráfico aplica el mismo filtro que la tabla.

No todas las métricas se habilitan para todas las filas:

| Métrica | Evidencia mínima | Cuando falta |
| --- | --- | --- |
| Tokens observados | Contador numérico válido | Desconocido, no cero |
| USD estimados | Precio aplicable al host/modelo/tier/fecha y componentes medidos | Tokens sin precio; subtotal conocido separado |
| Costo por llamada | Solicitudes finales deduplicadas con costo comparable | No disponible; un evento diario no cuenta como llamada |
| Tokens por llamada, mediana, p95 | Muestra de solicitudes finales con granularidad suficiente | No calcular a partir de totales de sesión/día |
| Distribución horaria | Timestamp o intervalos con tratamiento explícito | Excluir del histograma exacto y mostrar volumen no ubicable |
| Cache read share | Entrada y lectura/escritura de caché con semántica compatible | Ratio no disponible o cobertura parcial explícita |
| Modelo observado | Identidad suministrada por la fuente | `Unknown`/`Auto unresolved`, sin adivinar |
| Esfuerzo y tier | Metadatos admitidos de la solicitud/intervalo | Sin configuración conocida |

Nombres de interfaz en inglés para respetar la convención actual del proyecto: `Models`, `Activity`, `Sessions`, `Comparisons`, `Evidence`. `Sessions` no se publica como una función vacía antes de tener un lector admitido.

### 4.2 Activity

Seleccionar resolución según evidencia, no sólo según zoom. Un registro diario nunca se convierte en una llamada a medianoche. Un intervalo puede mostrarse como intervalo; repartir sus tokens uniformemente entre minutos requiere una política de estimación explícita y no debe ser el modo exacto predeterminado.

Conservar tres estados distintos por segmento temporal: observación de cero actividad, falta de observación y dato no suficientemente preciso. Mostrar marcas de cambio de parser, fuente y catálogo para explicar discontinuidades.

Comparar períodos con igual tiempo transcurrido cuando corresponda. Las fechas elegidas se convierten a límites UTC semiabiertos `[inicio, fin)`; los rollups con una zona de agrupación incompatible no se rebucketizan fingiendo conservar precisión. Las cuentas con fecha propia sin zona conocida continúan en un canal aparte.

### 4.3 Sesiones y proyectos — extensión opt-in

Primero aprobar qué metadatos se pueden leer y almacenar. La política actual no debe ampliarse por implicación. Un hash no elimina por sí solo la necesidad de permiso, minimización o límites de uso.

Propuesta: identificador local opaco y estable, alias elegido por el usuario y relación con la fuente. Evitar nombres originales de conversación, rutas completas, argumentos de herramientas, código y mensajes en la base analítica. Un HMAC con secreto local puede impedir correlación directa entre instalaciones; no transforma datos en anónimos de manera absoluta. El borrado incluye índices, snapshots y exportaciones futuras, sin alterar los datos del proveedor.

Atribuir proyecto por metadatos documentados de la fuente o una vinculación explícita. No deducirlo del texto del prompt ni de la carpeta en la que TokenUsage está ejecutándose. Para sesiones que atraviesan varios proyectos, preservar el dato por evento o marcarlo ambiguo; no forzar toda la sesión a un único proyecto.

Mostrar `Unassigned` cuando no se pueda atribuir. Distinguir duración de ventana, duración de inferencia y tiempo humano de trabajo: no son intercambiables. Las relaciones padre/subagente sólo se habilitan si son observables; un subtotal inclusivo no se vuelve a sumar a los eventos de los hijos.

### 4.4 Insights explicables

Comenzar por reglas puras y comprobables, sin LLM. Ejemplos: variación de volumen, aportes de modelos al cambio, pérdida de cobertura, una sesión atípica en una muestra comparable o aumento del tamaño de entrada por llamada. Cada insight incluye filtro reproducible, denominador, cantidad de muestras, método y limitaciones.

Redacción: “El tamaño de entrada mediano aumentó” no “Tu prompt está mal”; “Mayor costo estimado en este conjunto” no “Este modelo es peor”. Las recomendaciones contrafactuales deben declarar que una alternativa puede necesitar otros tokens, tiempos y cantidad de intentos. No prometer ahorro de factura a usuarios con suscripción sólo porque bajaría el equivalente API.

### 4.5 Evidencia y exportación

Reutilizar las secciones de evidencia actuales. Añadir trazabilidad por selección, historia acotada de la recolección, cobertura de campos y motivos de exclusión. `Complete` significa que la lectura admitida terminó, no que se midió el 100% de la computadora.

Exportación JSON/CSV: filtros, zona, intervalo, momento de captura, precisión, versiones de parser/precios, métricas y exclusiones. CSV debe neutralizar fórmulas y escapar correctamente etiquetas. Sin rutas, identificadores secretos ni contenido. El JSON v1 mantiene semántica; un contrato nuevo incompatible debe publicarse como v2.

## 5. Arquitectura incremental

```text
Fuentes locales admitidas / integraciones opt-in
                    |
     Lectores actuales + capacidades por fuente
                    |
   Normalización, identidad, deduplicación, reconciliación
                    |
          SQLite de TokenUsage
  eventos numéricos + rollups + metadatos acotados
                    |
       Report query / políticas en Core
                    |
   Resultados semánticos + evidencia + exclusiones
                    |
        Presentation: proyecciones
             /                 \
       WinUI 3                 CLI

Cuotas observadas ----------------> canal paralelo de evidencia
Totales de cuenta ----------------> canal separado de referencia
(no sumar estos canales otra vez al uso local)
```

No se requiere event sourcing genérico ni microservicios. Proponer inicialmente componentes dentro de las carpetas/proyectos existentes, sin crear un ensamblado por cada responsabilidad.

### Contratos propuestos, no existentes

Extender las consultas existentes y `UsageReportQuery`, no crear un circuito paralelo de reportes. Los siguientes nombres son ilustrativos: un `ReportQuery` inmutable especifica período, dimensiones, filtros, resolución solicitada y política de precios. El planificador valida qué puede producir a partir de las capacidades y de los datos efectivamente retenidos. Un `ReportResult` devuelve datos, evidencia y exclusiones juntos.

Una capa lateral de metadatos puede extender `UsageEvent` de forma aditiva para no romper los constructores y serializaciones actuales:

```text
UsageRecordMetadata
  EventKey                       # referencia al evento existente
  SourceInstanceKey              # instalación/raíz/perfil local; no ruta literal
  RecordKind                     # RequestFinal / IntervalDelta / Snapshot / DailyAggregate / Unknown
  ObservationScope              # local observado; los totales de cuenta permanecen aparte
  SessionKey? / ProjectKey?       # sólo con contrato opt-in admitido
  ComponentAvailability          # observado / no expuesto / no aplicable / parcial
  NormalizationVersion           # separado de identidad lógica
```

Separar `RecordKind` de `UsageTimePrecision`: una instantánea acumulada puede tener timestamp exacto de observación y no ser una solicitud. Guardar un timestamp no habilita automáticamente costo por llamada ni velocidad de inferencia.

Para campos ausentes, añadir un estado de disponibilidad sin reinterpretar retroactivamente los ceros históricos. Un cero sin evidencia histórica suficiente pasa a ser “disponibilidad no establecida”, no un cero medido por decreto.

`SourceCapabilities` debe describir evidencia de una versión de lector y de su fuente: llamadas, sesiones, atribución de proyecto, timestamps, caché, costos, esfuerzo, tier, duración de inferencia. No basta un booleano global por marca.

### Almacenamiento

Conservar `usage_event`, rollups y tablas de esquema 5. Considerar tablas laterales para metadatos de registro, instancias de fuente y un journal acotado de recolección. La función `RecordCollectionAsync` inspeccionada guarda el último estado por agente; el journal propuesto permitiría saber qué ocurrió en el período histórico de un informe.

No copiar transcripciones para “poder reprocesar después”. Reprocesar únicamente fuentes que sigan admitidas y disponibles; cuando ya no existan, conservar las limitaciones de los resultados históricos.

Las proyecciones agregadas deben ser reconstruibles desde los eventos retenidos. No acumular repetidamente snapshots completos. Mantener las políticas existentes de retención salvo una decisión y migración específicas. No reconstruir p95 ni sesiones a partir de los rollups diarios después de expirar los datos granulares.

### Identidad y captura

Clave lógica basada en identidad de fuente y del registro, independiente de la versión del parser o de precios. Resolver explícitamente solicitudes repetidas en logs, respuestas actualizadas, sesiones reanudadas y eventos duplicados entre fuentes. Cuando no hay identidad suficiente, documentar la política; no deduplicar registros distintos sólo porque tienen tokens idénticos.

Para contadores acumulativos: observar generación/reset y producir deltas o reemplazos según el contrato. `max(0, nuevo - anterior)` puede ocultar un reinicio y no es una política universal. Conservar cursores/checkpoints e incorporar truncamiento, rotación y última línea incompleta. Reutilizar `IWindowedSnapshotUsageEventSource` y su horizonte actual.

Los watchers son avisos de cambio, no un registro confiable de actividad. Lectura incremental con debounce y reconciliación limitada; sin abrir de nuevo todas las bases de todos los editores en cada actualización de la interfaz. No mutar la base ajena ni copiar credenciales.

### Fuentes Windows/WSL

Registrar raíces explícitas por instalación y permitir WSL como una fuente diferenciada una vez validada. Detectar que dos rutas apuntan a los mismos datos antes de sumar. No asumir que se puede atribuir toda la actividad de una cuenta a esta máquina: una sesión sincronizada o importada debe conservar su alcance conocido o desconocido.

## 6. Corrección identificada en `SplitKnownCost`

La implementación inspeccionada calcula, con costos `a`, `b` y volúmenes con precio `qa`, `qb`:

```text
volume = a * (qb / qa - 1)
rate   = (b / qb - a / qa) * qb
mix    = b - a - volume - rate
```

Al sustituir:

```text
volume = a * qb / qa - a
rate   = b - a * qb / qa
mix    = 0
```

Puede quedar un residuo de redondeo, pero no una contribución identificable de mezcla. La variable `rate` absorbe cambios de composición, no sólo de tarifas. La conclusión se obtiene algebraicamente del código leído; no implica que se haya ejecutado la aplicación.

**Hotfix seguro:** denominar ese término “Mix + effective-rate change” y declarar que la separación no está disponible. No presentar un cero como una medición del efecto mix.

### Descomposición posterior correcta, bajo supuestos explícitos

Para celdas comparables `j = proveedor/host/modelo/tier/componente`, cantidad total Q, participación s y precio por token r:

```text
C0 = Q0 * Σ(s0[j] * r0[j])
C1 = Q1 * Σ(s1[j] * r1[j])
V  = (Q1 - Q0) * Σ(s0[j] * r0[j])
M  = Q1 * Σ((s1[j] - s0[j]) * r0[j])
P  = Q1 * Σ(s1[j] * (r1[j] - r0[j]))
C1 - C0 = V + M + P
```

Es una descomposición secuencial con orden volumen → mezcla → tarifa; no es una atribución causal. El orden debe figurar en el método. Para tarifas no lineales o umbrales, usar una función de repricing definida y las diferencias de escenarios correspondientes, no forzar una tasa lineal promedio.

Se necesitan precios base para las celdas actuales. Modelos nuevos sin precio de referencia, datos sin precio, créditos, cuotas, descuentos no modelados y cobros no proporcionales a tokens permanecen fuera con residual explicado. No dividir por cero ni imputar costo cero. La comparación del gasto observado y la comparación contrafactual a precio fijo son modos diferentes.

## 7. Tokens, caché y costos: invariantes

`TokenBreakdown.Total` suma input + output + reasoning + cacheRead + cacheWrite. Es correcto únicamente si la normalización hace disjuntas esas categorías. Este RFC **no afirma un bug de duplicación en los adaptadores**: propone verificarlo con fixtures de cada fuente.

- Si un contador de entrada incluye caché, no volver a sumar la caché sin normalizar.
- Si el total de salida incluye razonamiento, no volver a sumarlo.
- No agregar subtotales al total que ya los contiene.
- Diferenciar tokens sin precio, dato no expuesto y cero medido.
- Separar valor reportado, equivalente estimado, factura/suscripción y créditos.
- No promediar ratios: sumar numeradores y denominadores elegibles.
- Los tokens procesados no representan necesariamente contenido único escrito por la persona.
- Mantener trazabilidad del host que cobra; una tarifa directa del fabricante no necesariamente aplica al intermediario.

Una métrica útil es `priceCoverage = pricedObservedTokens / observedTokens`, cuando ambas magnitudes son conocidas y compatibles. No denominarla “cobertura de la PC”. Para `cacheReadShare`, definir explícitamente el denominador (por ejemplo, todos los tokens de entrada elegibles, incluidos read/write ya normalizados) y mostrar cuántos registros fueron excluidos. No interpretarlo como porcentaje de solicitudes que tuvieron un hit.

## 8. Plan por entregas

| Entrega | Alcance | Condición de aceptación |
| --- | --- | --- |
| P0 — Correctness | Corregir etiqueta o descomposición de mix/tarifa; fixtures de granularidad y tokens | Caso de cambio de mezcla no se presenta como subida de tarifa; ceros/ausencias diferenciados |
| P1 — Model Explorer | Consultas y filtros sobre datos actuales; navegación y evidencia en el informe | Mismos filtros producen mismos totales en Core, interfaz y CLI; sin nueva lectura de contenido |
| P2 — Detalle verificable | Capacidades e identidad de fuente; solicitud/intervalo/snapshot; detalle Codex donde exista evidencia | Importar dos veces no duplica; tablas horarias no inventan precisión; llamadas sólo con evidencia |
| P3 — Proyectos y sesiones | Contrato opt-in, pseudónimos, metadatos por fuente, lectores admitidos uno a uno | Modo base no recolecta metadatos nuevos; `Unassigned` funciona; borrar desactiva y purga metadatos |
| P4 — Explicaciones y salidas | Insights deterministas, tamaños de llamadas, exportaciones y comparaciones refinadas | Todo insight tiene población y método; exclusiones conservadas; exports sin datos sensibles |

Cada entrega debe ser pequeña, enlazada a una issue y revisable de forma independiente. P1 no debe esperar una infraestructura hipotética para 50 proveedores. Los módulos actuales de cuotas, tray, pricing y comparación guardada tienen pruebas de regresión antes de activar el nuevo informe.

### Primera entrega vertical recomendada

**Un explorador de modelos en la ventana de informes existente, con selección de herramienta y período, separación reportado/estimado/sin precio y panel de evidencia; acompañado del hotfix de `SplitKnownCost`.** Después, agregar la primera navegación a detalle granular sólo donde el colector realmente lo produzca.

## 9. Matriz de pruebas

### Contabilidad

Importación repetida idempotente; dos solicitudes distintas con contadores iguales no colisionan; streaming provisional y respuesta final no se suman dos veces; deltas de acumuladores se reconcilian; suma de componentes disjuntos coincide con total; un subtotal padre no duplica hijos; créditos y cuotas no entran en USD/tokens.

### Tiempo y cobertura

Eventos en medianoche local, cambio de zona y DST; rangos `[start,end)` sin doble inclusión; agregados diarios y precisión desconocida fuera de gráficos exactos; intervalos que cruzan el rango; comparación de período incompleto; fallo de lector conserva último snapshot sin declararlo fresco; variación de cobertura no produce insight causal.

### Precio y comparaciones

Cambio sólo de volumen; sólo de mezcla; sólo de tarifa; cambio de proporción entrada/salida; mezcla con datos sin precio; precio de host distinto al directo; catálogo corregido; modelo nuevo sin precio base; volumen cero; comparación guardada conserva método/versiones.

### Fuentes

Archivo rotado/truncado; línea final incompleta; base ocupada; esquema no soportado; límites de escaneo y cancelación; dos raíces equivalentes; fuente WSL no disponible; snapshot con corrección de registros anteriores; retención de eventos sin fingir retención granular de más largo plazo.

### Privacidad y exportación

Canarios de prompt, ruta, comando, secreto y título de conversación en fixtures: ninguno debe aparecer en SQLite, logs, JSON, CSV o captura. Metadatos opt-in apagados por defecto, permisos revocables, ninguna configuración ajena sobrescrita. Aliases de usuario escapados y exportados sólo bajo la política elegida. Comprobar inyección de fórmulas CSV.

### Interfaz y rendimiento

Pruebas de teclado, alto contraste, zoom/DPI, estados vacío/parcial/error y cancelación al cambiar filtros. Benchmark con fixture sintético grande y máquina de referencia declarada. Fijar un presupuesto después de medir, no prometer latencia constante. Confirmar que la interfaz no dispara reescaneo total al cambiar el gráfico.

## 10. Inferencia realmente local: evolución independiente

Ollama documenta métricas por respuesta (`prompt_eval_count`, `eval_count`, duraciones de evaluación y generación). Una integración admitida podría enviar sólo esos campos numéricos y permitir calcular velocidad de generación con unidades claras. Esto exige que el cliente o runtime proporcione los metadatos de las solicitudes; consultar qué modelo está cargado no recupera toda la historia de uso.

No registrar automáticamente `response`, `thinking`, mensajes ni prompts aunque compartan el mismo objeto de respuesta. No mezclar valor equivalente API con costo eléctrico medido. La utilización de GPU no determina por sí sola tokens, modelo, proyecto ni dinero gastado. Evaluar otros runtimes por separado, sin prometer soporte antes de verificar un contrato.

## 11. Alternativas descartadas para este alcance

| Alternativa | Motivo de descarte |
| --- | --- |
| Ejecutar CodeBurn como motor de TokenUsage | Segundo runtime, política de lectura distinta, acoplamiento a formatos/costos y compatibilidad |
| Copiar todos sus parsers | Hereda decisiones de privacidad y mantenimiento sin validar las fuentes |
| Rehacer informes en web embebida | No resuelve la pérdida de granularidad y añade otra superficie técnica |
| Almacenar las conversaciones completas | Contradice el objetivo de minimización del producto |
| Agregar un LLM para explicar gráficos | No corrige errores de medida; añade costo y riesgos antes de tener evidencia suficiente |
| Nueva base analítica/distribuida | No justificada por la evidencia de volumen o rendimiento de esta revisión |

## 12. Fuentes y trazabilidad

Los enlaces de TokenUsage están fijados al commit revisado. Los enlaces de referencias apuntan a `main` consultado el 12/09/2026 y pueden cambiar; volver a fijar sus commits si se extraen implementaciones. Los enlaces de documentación no autorizan a copiar contenido o ampliar permisos de lectura.

1. TokenUsage README: https://github.com/gvastethecreator/tokenusage/blob/df50c367b083e5624bf09213eddc7013d8299aa4/README.md
2. Contrato de eventos: https://github.com/gvastethecreator/tokenusage/blob/df50c367b083e5624bf09213eddc7013d8299aa4/src/TokenUsage.Core/Usage/UsageEvent.cs
3. Fuentes de eventos: https://github.com/gvastethecreator/tokenusage/blob/df50c367b083e5624bf09213eddc7013d8299aa4/src/TokenUsage.Core/Usage/IUsageEventSource.cs
4. Esquema de medición: https://github.com/gvastethecreator/tokenusage/blob/df50c367b083e5624bf09213eddc7013d8299aa4/src/TokenUsage.Core/Usage/UsageRepository.Measurement.cs
5. Comparación: https://github.com/gvastethecreator/tokenusage/blob/df50c367b083e5624bf09213eddc7013d8299aa4/src/TokenUsage.Core/Usage/UsageComparison.cs
6. Informes: https://github.com/gvastethecreator/tokenusage/blob/df50c367b083e5624bf09213eddc7013d8299aa4/src/TokenUsage.App/ViewModels/Reports/UsageReportViewModel.cs
7. Evidencia: https://github.com/gvastethecreator/tokenusage/blob/df50c367b083e5624bf09213eddc7013d8299aa4/src/TokenUsage.App/ViewModels/Reports/UsageReportViewModel.MeasurementDetails.cs
8. Matriz de proveedores: https://github.com/gvastethecreator/tokenusage/blob/df50c367b083e5624bf09213eddc7013d8299aa4/docs/PROVIDER-MATRIX.md
9. Privacidad: https://github.com/gvastethecreator/tokenusage/blob/df50c367b083e5624bf09213eddc7013d8299aa4/PRIVACY.md
10. CodeBurn: https://github.com/getagentseal/codeburn
11. Clasificador heurístico: https://github.com/getagentseal/codeburn/blob/main/src/classifier.ts
12. AIStack CLI: https://github.com/alp82/aistack/blob/main/packages/cli/README.md
13. Agregación AIStack: https://github.com/alp82/aistack/blob/main/packages/cli/src/usage/days.ts
14. Ollama usage: https://docs.ollama.com/api/usage

## 13. Archivos acompañantes

- `tokenusage-reports-lab.html`: concepto interactivo autónomo. Filtros y cálculos sobre un fixture sintético, evidencia por selección, arquitectura y roadmap. No es un cliente de TokenUsage ni una integración de WinUI.
- `verify_cost_split.py`: reproducción algebraica con datos sintéticos y comprobaciones de la descomposición propuesta. No compila ni prueba el método C# original.
- `validation.json`: resultados de la comprobación de sintaxis, navegador y fixture, si se generaron. No acredita pruebas de la aplicación Windows.
