# Calidad · criterios de aceptación y evidencia

**Estado:** protocolo propuesto, alineado con TU-12/TU-16. [Fuentes](../references/10-SOURCES.md). Una implementación no queda aprobada por el número de tests, por compilar o por una captura. La calidad se demuestra por riesgo y por superficie.

## 1. Gates no negociables

| Gate | Condición | Evidencia |
|---|---|---|
| GATE-Q1 · Exactitud | No duplicación; componentes y poblaciones correctos. | Oráculos de dominio, ingesta/revisión y casos adversos. |
| GATE-Q2 · Privacidad | Ningún campo prohibido sale del lector hacia persistencia/salida. | Canarios, inspección de almacenes y carreras de consentimiento. |
| GATE-Q3 · Contratos | UI/CLI/Core coherentes; versiones anteriores tratadas explícitamente. | DTO canónico, tests de proceso y snapshots legacy. |
| GATE-Q4 · Recuperación | Error no destruye historia fiable; migración recuperable. | Fault injection, replay y restore real. |
| GATE-Q5 · Experiencia | Flujo completo en estados reales y accesibilidad Windows. | Evidencia empaquetada, teclado, Narrator y temas. |
| GATE-Q6 · Rendimiento | Presupuestos aprobados medidos y trabajo acotado. | Protocolo repetible con entorno y datos declarados. |

Un fallo Q1/Q2 bloquea publicación de la función, incluso si el resto es excelente. Un gate no aplicable requiere razón, no una casilla omitida. Pruebas del lector HTML de esta documentación no cuentan como Q5 para la aplicación.

## 2. Estrategia de pruebas por capas

**Dominio puro:** fórmulas, estados, selección, deduplicación conceptual, atribución lineal, percentiles y redondeo. Tablas de ejemplos y propiedades con semillas estables. Reutilizar tests ya existentes antes de crear suites duplicadas.

**Persistencia:** SQLite real temporal para ingesta, revisión, rollups, transacciones, migración, límites y rollback. Un repositorio fake no prueba SQL, locks ni recuperación. Testear commits interrumpidos mediante seams de fallo, no sólo el camino feliz.

**Fuentes:** fixtures sanitizados de formas reales con campos permitidos, versiones y unidades; ausente, parcial, malformado y cambiado. Los fixtures sintéticos del paquete prueban el diseño, no soporte real de proveedor. Prueba de detección y comparación con referencia de igual ámbito en Windows para activar la fuente.

**Integración Core/CLI:** lanzar procesos reales para comandos cambiados, validar códigos de salida, JSON y errores sin rutas privadas. Comparar al mismo DTO/fixture y revisión. No consumir stdout humano como contrato de máquina.

**Presentation/App:** lógica de estado/proyección con tests aislados; comportamiento de foco/resize/ventana mediante evidencia del paquete Windows. El repo excluye añadir Playwright u otro browser runner al producto, solución, paquete o CI. No usar un prototipo HTML como sustituto de esas pruebas.

## 3. Matriz de escenarios transversales

| ID | Familia | Casos obligatorios |
|---|---|---|
| TEST-Q-01 | Ingesta | Repetición, colisión aparente, orden cambiado, final tardío, cursor repetido. |
| TEST-Q-02 | Autoridad | Dos raíces, lectura parcial, ventana vacía completa, snapshots coexistentes. |
| TEST-Q-03 | Tiempo | Medianoche, límites contiguos, UTC/local, DST, intervalo cruzado, día sin hora. |
| TEST-Q-04 | Costo | Reportado cero, no reportado, modelo sin precio, host distinto, tier ausente, umbral. |
| TEST-Q-05 | Cohorte | Igual volumen con IDs distintos, cambio de cobertura, cambio de normalización. |
| TEST-Q-06 | Retención | Raw expira, rollups quedan, evidencia/versiones parcialmente desconocidas. |
| TEST-Q-07 | Privacidad | Prompts, comandos, ruta, secretos, título, account ID y HTML en campos no admitidos. |
| TEST-Q-08 | Permisos | Deshabilitado, revocar durante lote/export, crash/purge, reactivar, restore. |
| TEST-Q-09 | Interacción | Empty/loading/ready/partial/stale/error, selección fuera de orden, cierre. |
| TEST-Q-10 | Persistencia | Disco lleno, DB ocupada, fuente desaparece, rollback de transacción y copia coherente. |
| TEST-Q-11 | Contratos | Campos desconocidos, límites, grandes enteros, legacy, round-trip y redacción. |
| TEST-Q-12 | Regresión | Tray, cuotas, resets, precios históricos, proveedores sin integración nueva. |

Los IDs específicos TEST-P0-* a TEST-P4-* concretan esos riesgos en cada documento. `validation/traceability.json` relaciona requisitos de fase con pruebas y tareas. Esa matriz es un plan de cobertura, no un informe de ejecución del producto.

## 4. Canarios y datos de prueba

Los canarios son cadenas claramente ficticias, únicas por categoría. Inyectarlos en campos que un lector debe omitir y rastrear que no aparezcan en SQLite, settings, checkpoints, logs, JSON, CSV, HTML y capturas relevantes. Probar campos desconocidos, anidación y excepciones; no depender sólo de un grep final.

No compartir dumps de bases reales, aunque se crea que «sólo tienen uso». Extraer una forma mínima y sustituir identificadores/valores; registrar evidencia de cómo se obtuvo fuera del repositorio. Si el lector requiere abrir contenido prohibido para producir el fixture, el contrato de fuente no está aprobado.

Para P3, separar canarios de datos legítimamente opt-in: alias autorizado puede existir en su almacén local; no debe aparecer en export por defecto. No escribir una prueba que falle simplemente porque un fixture contiene la cadena que se diseñó para comprobar que se descarta.

## 5. Propiedades útiles

Permutar eventos no cambia totales. Ingerir el mismo lote dos veces no altera el resultado canónico. Agregar una fila no elegible no cambia una métrica cuyo denominador la excluye, aunque sí cambia su evidencia de exclusión. Top N + Other reconcilia el total de particiones disjuntas. Repricing de una cohorte idéntica a una misma tarifa produce delta cero; intersección vacía produce no disponible.

Una revisión de consumo reemplaza su contribución anterior, no suma ambas. Revocar nunca puede aumentar la cantidad de asociaciones. Dos instancias no se afectan al reconciliar ventanas independientes. Cambiar sólo tema o ancho no modifica selección, revisión ni métricas. La pérdida de raw no altera totales durables ya confirmados.

Propiedades de orden e identidad no bastan para validar pricing real: añadir fixtures de umbrales y contratos concretos de cada catálogo modificado.

## 6. Evidencia Windows requerida

Documentar edición/versión Windows, arquitectura, SDK/.NET, tipo MSIX o portable, commit y fixture. Para UI afectada: estado antes/después, tamaño, escala, tema, modo alto contraste, reduced motion y recorrido de teclado. Verificar al menos el camino empaquetado de destino; un binario ejecutado fuera de su contexto no prueba todo el comportamiento de paquete.

La guía del repo indica que el camino ARM64 de `check.ps1` compila/empaqueta, pero los tests corren en host x64. No presentar eso como prueba de ejecución real en hardware ARM64. Si no se dispone de ese hardware, declarar la limitación y mantener separadas compilación y validación runtime.

## 7. Comandos existentes y alcance

```powershell
# Confirmado en la guía de testing; ejecución futura en Windows.
dotnet test tests/TokenUsage.Core.Tests/TokenUsage.Core.Tests.csproj --configuration Release -p:Platform=x64 --verbosity minimal
.\scripts\check.ps1 -Platform x64 -Configuration Release
```

Para otros proyectos de tests, localizar su `.csproj` en el SHA de trabajo y ejecutar el real; no inventar nombres. Ejecutar pruebas enfocadas durante iteración y el gate completo en un límite de verificación, evitando repetir checks intactos sin motivo. No instalar dependencias de este lector documental en TokenUsage.

## 8. Matriz de evidencia por entrega

P0: dominio + rutas de comparación + UI Rates + snapshots. P1: consulta/proyección/CLI + navegación/teclado/temas + rendimiento. P2: SQL/migración/replay + fuente real + temporalidad. P3: consentimiento/purge/export + fuente real + UI. P4: estadística/cohortes + formatos/adversariales + snapshot/retención.

Guardar el manifiesto sin secretos: SHA, comando, resultado exacto, plataforma, fixture, fecha y riesgos. «No ejecutado» es un estado válido de evidencia, pero no aprueba un gate. No usar el resultado del validador de este paquete para marcar tareas de implementación como completadas.

## 9. Criterio de severidad

**Bloqueante:** números duplicados, privacidad, corrupción/pérdida, contrato de datos silenciosamente incompatible, falsa exactitud o permisos ignorados. **Alta:** flujo principal inaccesible, congelamiento importante, export no interpretable, estado stale oculto. **Media:** fricción visual/local con alternativa funcional, formato inconsistente sin cambiar significado. **Baja:** detalles de pulido no funcionales.

No rebajar un problema contable porque afecte sólo a un proveedor. No bloquear una corrección crítica esperando todo el pulido de una fase posterior; aplicar el hotfix seguro y mantener la función avanzada deshabilitada.
