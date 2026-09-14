# Diccionario de métricas · definiciones y exclusiones

**Estado:** especificación normativa propuesta para funciones nuevas. Los nombres/semántica v1 existentes no se alteran sin proceso de versión. **Base:** TU-02/04/07/09/15 y [P0](../phases/P0-CORRECTNESS.md).

## 1. Vocabulario de población

**Universo seleccionado:** uso que satisface filtros admitidos, con el alcance que realmente se conoce. **Población elegible:** subconjunto con campos suficientes para una métrica. **Población comparable:** registros/celdas con definición compatible entre ambos lados. **Visible:** filas o buckets dibujados; no necesariamente todo el universo.

**Record** es una fila normalizada. **Request** es una solicitud final deduplicada demostrada por la fuente. **Session** es una identidad permitida del proveedor, no una agrupación por pausas. **Observed span** es distancia entre primera y última actividad observada, no tiempo de trabajo. **Source instance** es una raíz/perfil de captura, no una cuenta ni garantía de ejecución física en esa máquina.

## 2. Tokens y componentes disjuntos

Para el modelo interno actual, `T = I + O + R + CR + CW`: entrada nueva, salida visible, razonamiento, lectura de caché y escritura de caché. La normalización debe asegurar disjunción. Si una fuente incluye razonamiento dentro de salida, restarlo al separar; si incluye cache-read dentro de input, separarlo sin volverlo a sumar.

La disponibilidad de cada campo es independiente: observado, no expuesto, no aplicable, parcial o desconocido legacy. Un número positivo puede ser un subconjunto observado, no la totalidad del componente. Un cero sin semántica demostrada no habilita ratios avanzados. Ante un subcontador mayor que su total, devolver dato inválido/diagnóstico, no usar `max(0,...)` para ocultarlo.

Los tokens observados no equivalen a texto único: una entrada repetida o en caché sigue representando trabajo contabilizado según la fuente. No presentar cache-read como tokens «gratis» ni como porcentaje de solicitudes con cache hit.

## 3. Tabla de métricas

| ID | Métrica / fórmula | Unidad | Población y exclusión |
|---|---|---|---|
| M-01 | Observed tokens = suma T. | tokens | Eventos canónicos sumables; excluir snapshots no convertidos y subtotales duplicados. |
| M-02 | Reported value = suma costo reportado. | USD | Sólo importes reportados compatibles; no implica invoice. |
| M-03 | Estimated API value = suma estimaciones aplicables. | USD | Precio válido por fecha/host/tier; sin mezclar con cuota o suscripción. |
| M-04 | Unpriced observed tokens. | tokens | Cantidad observada sin valor aplicable; no incluye consumo que nunca se observó. |
| M-05 | Value coverage = T_con_valor / T_observado. | fracción / % | Denominador > 0 y compatible. No es cobertura de la PC. |
| M-06 | Cache-read token share = CR / (I+CR+CW). | fracción / % | Sólo registros con componentes de entrada compatibles conocidos; no ratio de requests. |
| M-07 | Model share = T_modelo / T_selección. | fracción / % | Partición por modelo/host y desconocidos visible; no usar total global ajeno al filtro. |
| M-08 | Request count. | solicitudes | Sólo RequestFinal únicos; cero sólo si la fuente cubre ese ámbito. |
| M-09 | Mean tokens/request = suma T / n. | tokens/solicitud | Mismo conjunto con numerador y denominador completos. |
| M-10 | Median input/request. | tokens/solicitud | Entrada total compatible; mediana ordenada; mínimo de producto 5. |
| M-11 | p95 input/request. | tokens/solicitud | Nearest-rank, n >= 100 en el producto propuesto. |
| M-12 | Mean known value/request. | USD/solicitud | Subconjunto valorizado; n corresponde exactamente a los importes sumados. |
| M-13 | Cost per million priced tokens = C_conocido * 1e6 / T_con_valor. | USD/M tokens | Composición mixta; describe costo efectivo, no precio de catálogo de salida. |
| M-14 | Generation throughput = output_generated / generation_seconds. | tokens/s | Duración medida y positiva de misma solicitud; semántica del contador explícita. |
| M-15 | Observed active days. | días | Días civiles con uso observado > 0; no prueba productividad ni horas trabajadas. |
| M-16 | Attributed token share. | fracción / % | Tokens con proyecto/sesión admitido / tokens observados del mismo ámbito. |
| M-17 | Absolute change = B-A. | unidad de A/B | Misma definición/unidad y valor disponible en ambos. |
| M-18 | Relative change = (B-A)/A. | fracción / % | A > 0; de cero a positivo se etiqueta New activity, no infinito. |
| M-19 | Fixed-cohort catalog change. | USD | P0: misma identidad/revisión elegible a ambas fechas. |
| M-20 | Linear V/M/P decomposition. | USD | P4: celdas lineales comparables; método secuencial versionado. |

Value coverage del diseño corresponde al alcance conceptual del pricing coverage actual, pero el nombre público final debe pasar por compatibilidad. Una cobertura de valor reportado no prueba que exista un precio unitario conocido para repricing. Mantener dos capacidades distintas: valor disponible y recalculabilidad bajo un catálogo.

## 4. Denominadores y ejemplo ponderado

Grupo A: 900 cache-read sobre 1000 tokens de entrada; B: 0 sobre 10. La media de ratios da 45%; el ratio agregado correcto es `900/1010`, aproximadamente 89,1089%. Sumar numeradores/denominadores elegibles y dividir al final. No promediar porcentajes mostrados con redondeo.

Un ratio con denominador cero es no disponible/no aplicable según contexto, no 0%. El campo legacy de `PriceCoveragePercent` devuelve 0 para cero tokens; no cambiar su semántica pública silenciosamente. El resultado nuevo debe expresar ausencia de población en su wrapper y la UI no rotular ese 0 legacy como observación fiable de cobertura.

Los porcentajes usan 0..1 internamente en contratos nuevos, y el campo de unidad declara si una salida usa fracción o puntos porcentuales. Una caída de 80% a 60% son -20 puntos, no -20% relativo; la variación relativa es -25%. La UI no intercambia ambos nombres.

## 5. Excluir sin duplicar

Cada métrica declara elegibles, excluidos y motivos. Una razón primaria permite sumar particiones; razones secundarias sirven para diagnóstico y no son sumandos adicionales. `Unknown coverage` no se convierte en un porcentaje inventado: si se desconoce el denominador de actividad total, se informa desconocido.

Para histogramas por hora, la suma visible más consumo conocido no ubicable puede reconciliarse con el total compatible. Si parte del detalle ya expiró, la cantidad excluida puede conocerse sólo a nivel de rollup; no inventar número de solicitudes. Un evento puede ser elegible para total de tokens e inelegible para p95.

## 6. Precios y modo de comparación

**As recorded:** valores de la revisión canónica actualmente almacenada, con origen reportado/estimado. **Saved snapshot:** resultado congelado al guardar. **Reference pricing:** estimación contrafactual de eventos elegibles bajo catálogo/política seleccionados. No son tres formas equivalentes de ver una factura.

No convertir créditos a USD salvo contrato explícito independiente; no dividir USD por puntos de cuota sin atribución de pool y consumo demostrada. Tampoco calcular «ahorro de suscripción» restando equivalentes de API. Un importe reportado por una herramienta puede ser un valor calculado por ella, no dinero debitado.

El catálogo tiene vigencia, scope de host y versión. Repricing no altera el evento original. Un valor sin tarifa por componente puede entrar en M-02 pero no en M-19/20. Los umbrales no lineales se evalúan en la unidad que exige el precio, normalmente la observación/solicitud elegible; no sobre una suma diaria arbitraria.

## 7. Tiempo

Timestamps UTC para actividad precisa; tiempo local sólo para presentación/agrupación con zona registrada. Rangos nuevos semiabiertos. Intervalos heredados mantienen su convención y requieren mapeo comprobado. Un total cuenta/día sin zona o ámbito compatible permanece separado.

No incluir dos veces un evento en dos períodos contiguos. No usar fecha de escaneo como fecha del uso. Los días con ausencia de evidencia no son días inactivos demostrados. `Stale` significa antigüedad de la última evidencia/lectura conforme al SLA de la fuente, no invalida automáticamente todo el histórico.

## 8. Precisión y redondeo

Sumar valores exactos antes de formatear. Para dinero usar micro-USD o decimal con la política histórica pertinente. Los tokens observados son enteros checked; medianas/promedios pueden ser decimales. JSON v2 propuesto usa cadenas para valores potencialmente superiores al rango entero exacto de JavaScript; v1 permanece intacto.

No mostrar 100% de cobertura si hay tokens excluidos que el redondeo escondería; reutilizar la intención actual de limitar ese redondeo y exponer los números originales en evidencia. La precisión visual puede variar por tamaño, pero export, tabla y tooltip se derivan del mismo valor.
