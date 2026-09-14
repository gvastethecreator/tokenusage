# P4 · Explicaciones, distribuciones y exportaciones reproducibles

**Estado:** propuesta. **Dependencias:** P1/P2 para análisis por modelo; P3 sólo para funciones que usan sesiones/proyectos. **Fuentes:** TU-04/05/07/09/11/15, AS-02 y EXT-07/08 en [Fuentes](../references/10-SOURCES.md).

## 1. Resultado y criterio de calidad

Convertir resultados numéricos en explicaciones navegables, sin inventar causalidad ni recurrir a otro LLM. Cada insight abre la población que lo originó, muestra método, tamaño de muestra y exclusiones, y deja claro cuándo un cambio de medición puede explicar el patrón.

La primera entrega puede tener cuatro reglas excelentes, no cuarenta superficiales. Se implementa junto con un sistema de exportación que conserve el significado del informe. Un HTML o CSV elegante que pierde exclusiones no es una exportación correcta.

## 2. Requisitos

| ID | Requisito |
|---|---|
| REQ-P4-01 | Reglas deterministas, versionadas y basadas en una población reproducible. |
| REQ-P4-02 | Comparaciones controlan definición de métrica, fuente, tiempo y cobertura. |
| REQ-P4-03 | Estadísticas por solicitud excluyen agregados, provisionales y duración no medida. |
| REQ-P4-04 | Escenarios de precio no se presentan como ahorro de factura ni calidad de modelo. |
| REQ-P4-05 | Exportadores consumen un snapshot canónico, no recalculan cada formato. |
| REQ-P4-06 | Campos privados, fórmulas CSV y HTML activo no se filtran en la salida. |
| REQ-P4-07 | La retención o revocación no se ocultan al reabrir informes antiguos. |

## 3. Catálogo inicial de reglas

| Regla | Pregunta | Evidencia | Mensaje permitido |
|---|---|---|---|
| Model contribution | ¿Qué modelos explican aritméticamente el cambio? | Totales comparables y mismos tipos de valor. | `Model X contributed ... to the observed change.` |
| Input-size shift | ¿Cambió el tamaño de entrada de llamadas observadas? | Solicitudes finales y semántica de entrada común. | `Median observed input size increased ... in this cohort.` |
| Coverage change | ¿Cambió lo que podemos observar o valorizar? | Contadores de exclusión y procedencia por período. | `Price coverage changed; cost comparisons may not reflect usage alone.` |
| Session outlier | ¿Hay una sesión atípica dentro de una cohorte? | P3, muestra suficiente y métrica compatible. | `This session is above the selected distribution.` |

La regla de cobertura tiene prioridad: si cambia el parser, la atribución, la granularidad o la población de precios, mostrar esa explicación antes que sugerir una causa de uso. No confundir frescura de recolección con completitud de la PC.

No publicar en esta fase «productividad», «ROI del modelo», «tokens desperdiciados», «mejor agente», «mal prompt» ni reintentos inferidos desde edición de archivos. La clasificación heurística de CodeBurn utiliza señales de contenido que el alcance actual excluye. [CB-01]

### Parámetros iniciales para una implementación determinista

Estos defaults son propuestas de producto para calibrar con fixtures y uso consentido; no umbrales estadísticos universales. Guardarlos con `RuleVersion` para que cambiar sensibilidad no reinterprete un informe guardado.

| Regla | Disparo inicial propuesto | Control de ruido |
|---|---|---|
| Model contribution | Cambio no nulo en una métrica comparable; ordenar por valor absoluto de contribución. | Máximo dos modelos por comparación. No dividir por un delta total cercano a cero para inventar «porcentaje explicado». |
| Input-size shift | Cumplida la muestra mínima, diferencia absoluta de medianas >= 1.000 tokens y relativa >= 25%, con base positiva. | Con base cero mostrar sólo aparición de tamaño observado, sin porcentaje. Si cambió la semántica de entrada, suprimir el insight. |
| Coverage change | Diferencia absoluta de cobertura >= 10 puntos porcentuales, o cualquier cambio relevante de método/fuente. | Ante cambio de método, mostrar el aviso aunque no cruce 10 puntos. No traducirlo a un cambio de consumo. |
| Session outlier | Al menos 30 sesiones elegibles; para la misma métrica positiva, valor > mediana + 6 × MAD, con MAD > 0. | Sólo cola superior; máximo una tarjeta por cohorte. Es distancia exploratoria, no z-score ni significancia estadística. |

`MAD = mediana(|x - mediana(x)|)`, calculada sobre la misma población; no aplicar la constante de normalización normal si el método declarado es la distancia directa anterior. Una sesión parcialmente observada no se trata como completa: excluirla de una regla que exige totales comparables. Igual valor al umbral no dispara una condición estricta `>`; incluir casos justo debajo, igual y encima en los tests de TASK-P4-02.

## 4. Contrato de insight

```text
Insight
  RuleId + RuleVersion + MethodId
  Severity = Informational | Attention
  MessageKey + NumericArguments
  MetricId + Unit
  QueryFingerprint + DataRevision + CohortFingerprint
  BaselineRange? + CurrentRange
  EligibleCount + EligibleTokens + Exclusions
  Assumptions + Confounders
  DrilldownSelection
  GeneratedAtUtc + PrivacyEpoch
```

Guardar claves y argumentos estructurados, no prosa producida por un modelo remoto. La UI genera texto con recursos en inglés. La comparación puede tener un efecto grande y aun así baja evidencia; `Attention` significa atención al patrón o a la medición, no diagnóstico de fallo del modelo.

Ordenar por reglas estables y relevancia explicada. Limitar inicialmente a cinco tarjetas y permitir ver el catálogo completo; no repetir el mismo hecho como tres alertas. La persistencia automática es opcional y acotada. Una regla se puede desactivar sin borrar ni cambiar eventos.

## 5. Elegibilidad de comparaciones

Alinear definición de métrica, denominador, modo de precios, fuente/versión, configuración conocida, rango y retención. Igualar «últimos siete días completos» con otros siete días completos; no comparar una semana a medio terminar con otra entera sin una política explícita.

Para períodos locales, contar días civiles reales; para actividad exacta, comparar duración transcurrida. No asumir 24 horas por día en cambios de zona/DST. Una comparación descriptiva con confusores puede seguir mostrándose, pero no produce una explicación causal. Un cambio de método divide la serie o marca el corte.

Una mayor participación de un modelo no demuestra preferencia consciente del usuario, ni una caída de costo prueba eficiencia. Un menor costo puede coincidir con peor cobertura. Una distribución de solicitudes sólo describe las solicitudes observadas, no las que la fuente no expone.

## 6. Estadística de tamaños y duraciones

Input size se define en el diccionario como entrada normalizada completa por solicitud, incluidos componentes de caché cuando sean parte de la entrada y su semántica esté demostrada. No equivale necesariamente a toda la ventana de contexto utilizada ni a contenido único. Denominar la vista `Request input sizes`, no «mapa completo del contexto».

Método de percentil propuesto: nearest-rank `x[ceil(p*n)-1]` sobre valores ordenados; fijar versión y usar el mismo algoritmo en Core, fixture y export. Mediana para n par: promedio de los dos valores centrales; su valor puede ser decimal aunque cada token sea entero. Los promedios se obtienen de suma/cantidad, nunca de promediar promedios diarios no ponderados.

Umbrales de producto propuestos, no garantías estadísticas: mostrar mediana desde 5 solicitudes y p95 desde 100; debajo informar `Insufficient sample` y permitir consultar valores individuales. Para un insight de cambio de mediana, exigir al menos 30 solicitudes por lado y representación de al menos 5 sesiones cuando exista esa dimensión. Si no hay sesiones, declarar la limitación y evitar afirmar independencia de las observaciones.

Para outliers usar inicialmente una regla robusta explícita basada en mediana/MAD con umbral documentado; MAD cero produce `No robust spread estimate`, no división por cero ni alarma universal. Evitar valores p o intervalos de confianza sin método y supuestos aprobados. Una regla exploratoria no es una prueba causal ni un detector de fraude.

Throughput sólo se habilita con tokens de salida y tiempo de generación medido de la misma solicitud. Latencia total, tiempo hasta primer token y tokens/segundo son métricas distintas. No calcular velocidad a partir de `last - first` de una sesión. Una duración cero o negativa es inválida, no velocidad infinita.

## 7. Cambios de volumen, mezcla y tarifa

P0 entrega repricing de cohorte fija. Para comparar dos períodos históricos, una descomposición lineal de tres factores exige celdas comparables por agente/host/proveedor/modelo/tier/componente/régimen tarifario. Para volumen total Q, participación s y tarifa r:

```text
C0 = Q0 * sum(s0[j] * r0[j])
C1 = Q1 * sum(s1[j] * r1[j])
V  = (Q1 - Q0) * sum(s0[j] * r0[j])
M  = Q1 * sum((s1[j] - s0[j]) * r0[j])
P  = Q1 * sum(s1[j] * (r1[j] - r0[j]))
C1 - C0 = V + M + P
```

El orden volumen → mezcla → tarifa es una decisión de método, no causalidad. Cambiar el orden cambia la atribución. Guardar `MethodId=sequential-vmp-linear/v1`. No llamar «mix de modelos» al efecto si también incluye la mezcla de componentes de token.

Requiere Q0 y Q1 positivos y tarifas de referencia para la unión de celdas usada. Si falta la tarifa base de un modelo nuevo, excluir esa celda con su volumen; no suponer precio cero. Un cambio en cobros reportados con descuentos desconocidos no puede explicarse mediante precios de lista sin declararlo escenario distinto.

Para tarifas no lineales, el MVP P4 no fuerza esta fórmula usando tasas medias. Mantiene el análisis de cohorte fija por solicitud de P0 y deja la atribución histórica de tres factores no disponible hasta aprobar un método de escenarios que preserve umbrales y composición. Este límite evita una implementación sofisticada pero matemáticamente ambigua.

Desconocido no es un residual monetario de cero. Si no se conoce costo de una parte, se informa `Unpriced usage outside decomposition`; sólo se reconcilian importes del conjunto comparable. Un residual conocido por redondeo se muestra por separado y tiene tolerancia versionada.

## 8. Snapshots reproducibles

Congelar resultado, selección, versión del método, revisión de datos, versiones de precios/parser, zona/offset, exclusiones y política de privacidad. `GeneratedAtUtc` indica cuándo se produjo el informe; no reemplaza fecha del uso. Capturar mediante una lectura coherente, liberar la transacción y renderizar formatos después; no mantener una transacción abierta mientras el usuario elige destino.

Un resultado guardado sigue siendo visible aunque expire el raw, con su fecha y método. El drill-down que requiere datos expirados queda deshabilitado con explicación. No inventar eventos para reconstruirlo. Si se revocan asociaciones P3, purgar/redactar los snapshots que las contienen según esa política; reproducibilidad no significa conservar datos revocados.

Un hash de contenido sirve para identidad/integridad accidental, no es una firma de autenticidad. Un archivo modificado por un tercero no se considera confiable sólo porque contenga su propio hash recalculable.

## 9. Exportadores

**JSON:** fuente semántica canónica, con versión explícita. Para v2 propuesto, contadores de 64 bits y micro-USD pueden representarse como cadenas decimales para evitar pérdida en clientes JavaScript. La CLI v1 no se cambia silenciosamente. Usar esquemas estrictos y límites de tamaño/profundidad al importar; rechazar claves desconocidas cuando puedan ocultar contenido no admitido.

**CSV:** tablas derivadas del mismo snapshot; incluir un manifiesto JSON adyacente o paquete ZIP que conserve alcance y exclusiones. Un CSV aislado sin contexto se rotula como extracto. Las columnas numéricas provienen de valores tipados, no de etiquetas. Las cadenas controladas por el usuario se excluyen por defecto. Si se exportan, aplicar política de texto segura y validar en aplicaciones destino; OWASP advierte que no existe sanitización universal para todos los flujos de guardar/reabrir. [EXT-07] No afirmar que poner comillas alcanza.

**HTML autónomo:** vista estática escapada, sin scripts de terceros, imágenes remotas, analytics o enlaces que ejecuten acciones. No embeber el contenido completo de los logs en un bloque oculto. El generador acepta DTO allowlist, no HTML crudo de alias. La captura visual usa la misma población y permite ocultar alias/identificadores.

Escribir en temporal bajo control, verificar éxito y reemplazar/finalizar destino; fallo o cancelación no deja un archivo truncado con nombre de informe terminado. No sobrescribir archivos del usuario sin confirmación. La revocación de privacidad entre captura y finalización obliga a cancelar o generar un snapshot redactado nuevo.

## 10. Plan de implementación

**TASK-P4-01:** contrato de insight + elegibilidad + fixtures, sin UI. **TASK-P4-02:** primeras reglas puras y oráculos de estadísticas. **TASK-P4-03:** drill-down y tarjetas sobre selección existente. **TASK-P4-04:** snapshot común y JSON versionado. **TASK-P4-05:** CSV/HTML/captura con redacción, límites y pruebas de seguridad. **TASK-P4-06:** comparación histórica lineal sólo si cumple sus gates; nunca bloquear el resto por una función que no sea identificable con las fuentes.

No añadir un scheduler que lance inferencia o red para «redactar mejor» los insights. Las reglas corren sobre resultados locales y se invalidan por revisión. No releer fuentes de proveedores al renderizar cada tarjeta.

## 11. Pruebas

| ID | Caso | Oráculo |
|---|---|---|
| TEST-P4-01 | Misma consulta/datos/regla. | Mismos argumentos y orden de insights. |
| TEST-P4-02 | Cambia cobertura o parser. | Advertencia de medición; no causalidad de uso. |
| TEST-P4-03 | n=4/5/99/100 para estadísticas. | Umbrales definidos; p95 no disponible antes de 100. |
| TEST-P4-04 | Medianas y nearest-rank con n par/impar. | Valores del método documentado. |
| TEST-P4-05 | Agregado diario con enorme costo. | No entra como solicitud atípica. |
| TEST-P4-06 | Cambio sólo volumen/mezcla/tarifa en régimen lineal. | Identidad V+M+P = delta comparable. |
| TEST-P4-07 | Modelo nuevo sin precio base o umbral no lineal. | Atribución excluida/no disponible; no tarifa inventada. |
| TEST-P4-08 | JSON, CSV, HTML del mismo snapshot. | Totales y exclusiones concordantes. |
| TEST-P4-09 | Alias con HTML/fórmula/Unicode de control. | Redacción por defecto y salida segura cuando autorizada. |
| TEST-P4-10 | Cancelación/revocación al escribir. | No publica archivo incompleto o con permiso vencido. |
| TEST-P4-11 | Snapshot antiguo, raw expirado. | Resultado antiguo visible; drill-down no reconstruido. |
| TEST-P4-12 | Enteros mayores que 2^53 y importes diminutos. | Round-trip sin pérdida en contrato nuevo. |

## 12. Cierre

**GATE-P4-A:** reglas explicables y reproducibles, sin afirmaciones fuera de evidencia. **GATE-P4-B:** métricas y distribuciones con elegibilidad/umbrales. **GATE-P4-C:** un snapshot para todas las salidas, redacción y tests adversariales. **GATE-P4-D:** round-trip, versiones antiguas, Windows y revisión visual del informe compartido.

La fase puede cerrarse sin atribución histórica no lineal ni runtime local si esos ítems siguen explícitamente fuera de alcance. No puede cerrarse con una cifra presentada como exacta y un método insuficiente escondido en documentación.
