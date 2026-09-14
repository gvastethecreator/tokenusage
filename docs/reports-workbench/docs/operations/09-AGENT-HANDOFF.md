# Entrega a agentes · backlog y ejecución por evidencia

**Estado:** instrucciones de implementación propuestas; no issues/PR creados. **Fuente normativa del repositorio:** TU-12/TU-16. [Fuentes](../references/10-SOURCES.md).

## 1. Contexto que el agente debe conservar

TokenUsage es nativo y local-first. Se mejora la ventana existente de Reports, no se introduce otra aplicación ni otro runtime. P0 corrige específicamente Rates y su cohorte. P1 entrega navegación sobre datos actuales. P2 añade detalle medible. P3 requiere consentimiento de metadata. P4 explica y exporta sin LLM.

Antes de modificar, leer las instrucciones del repositorio en el SHA actual, CONTRIBUTING, testing, PRIVACY, PRICING y provider matrix. El paquete es una propuesta: no prevalece sobre nuevas correcciones del repo ni sobre permisos más restrictivos. Una discrepancia se documenta y se resuelve en el cambio mínimo, no se sobrescribe main con el baseline viejo.

El repo pide una issue previa para cada PR y excluye añadir Playwright u otro browser runner al producto, solución, paquete o CI. El HTML de esta documentación y su inspección externa no se integran como motor ni sistema de testing de TokenUsage.

## 2. Primera tarea recomendada

Tomar TASK-P0-01 y TASK-P0-02 como primer corte, acotado al método Rates. Localizar todos los llamadores, caracterizar la intersección de cohortes y escribir el caso de igual volumen con identidades distintas. Implementar una solución que no altere costos registrados. Revisar si el DTO necesario puede reutilizar el seam existente.

No empezar por rediseñar todas las pantallas ni por añadir 50 proveedores. No cambiar schema para una corrección que se puede expresar sin persistencia nueva. Al completar, adjuntar evidencia suficiente para decidir el siguiente corte P1A.

## 3. Backlog ordenado

| Bloque | Tareas | Resultado demostrable |
|---|---|---|
| P0 | TASK-P0-01 a 04 | Caracterización, fixed cohort, textos/evidencia, compatibilidad. |
| P1A | TASK-P1-01 a 04 | Consulta compartida y Explorer nativo con estados. |
| P1B | TASK-P1-05 a 06 | Filtros avanzados con retención y QA de superficies. |
| P2 | TASK-P2-01 a 06 | Fuente piloto, identidad/revisión, migración, temporalidad y smoke. |
| P3 | TASK-P3-01 a 05 | Permisos/purge, vínculos, un lector admitido y vistas. |
| P4 | TASK-P4-01 a 06 | Reglas, snapshot, formatos y atribución lineal cuando sea elegible. |

No ejecutar fases en paralelo sobre el mismo seam sin coordinación. Un agente puede preparar fixtures/documentación P3 mientras otro implementa P1, pero no habilitar la captura de metadata antes de cerrar los gates.

## 4. Contrato de tarea

```text
Task ID:
Issue real vinculada:
SHA de partida:
Problema observable:
Resultado esperado:
Requisitos que cubre:
Fuentes/seams a leer:
Archivos esperados (existentes o propuestos, diferenciados):
Cambios expresamente fuera de alcance:
Prueba/oráculo que demuestra el resultado:
Impacto de schema y privacidad:
Validación Windows necesaria:
Rollback:
Estado final y riesgos no verificados:
```

Los IDs de este paquete no sustituyen el número de issue. No escribir `Closes #123` con un ejemplo ficticio. La importación documental reutiliza la issue #61 y el PR #62; no crear un issue de reemplazo. La plantilla real de las fases de producto se completa con el recurso existente o creado y autorizado en el repositorio.

## 5. Procedimiento por tarea

1. Leer y caracterizar el seam actual; registrar diferencias con el plan.
2. Ejecutar la prueba enfocada existente o preparar un caso que demuestre el fallo.
3. Implementar el cambio mínimo y sus constraints, sin refactor ajeno.
4. Ejecutar pruebas específicas; revisar numeradores, denominadores, fechas y privacidad.
5. Actualizar documentación de comportamiento y contratos afectados.
6. Verificar UI/proveedor en Windows cuando corresponda; adjuntar evidencia sanitizada.
7. Cerrar con comandos/resultados y riesgos; no declarar completed lo que sólo compila.

Si falta acceso al proveedor, construir pruebas de dominio/forma permitida y dejar la integración no activada; documentar exactamente la evidencia faltante. No rellenar con mocks para afirmar soporte real.

## 6. Comandos confirmados, no ejecutados aquí

```powershell
# Desde la raíz real de TokenUsage, en el entorno Windows requerido.
dotnet test tests/TokenUsage.Core.Tests/TokenUsage.Core.Tests.csproj --configuration Release -p:Platform=x64 --verbosity minimal
.\scripts\check.ps1 -Platform x64 -Configuration Release

# Auditoría existente del catálogo, cuando pricing sea parte del cambio.
tokenusage pricing audit --format human
```

Para otros proyectos, buscar su `.csproj` y usar ese camino. No anunciar flags nuevos de la CLI derivados de los JSON de diseño: los schemas del paquete no significan que un comando los admita. No ejecutar pruebas Windows desde Linux y presentar el resultado como WinUI.

La validación Python del paquete es opcional para revisar documentos/oráculos. No se incorpora Python como runtime ni gate obligatorio del producto. Los cambios de aplicación deben tener su prueba equivalente en la suite real .NET.

## 7. Plantilla de evidencia de PR

```text
Issue:
SHA probado:
Resultado visible:
Alcance de datos y permisos:
Contratos preservados/cambiados:
Comandos ejecutados y resultado exacto:
Pruebas no ejecutadas y bloqueo:
Evidencia WinUI empaquetada:
Evidencia real de fuente y versión:
Migración/restore ensayados:
Riesgo residual:
Cómo desactivar o revertir:
```

Una fila «N/A» tiene justificación. Un test ausente es «Not run», no «Passed by inspection». La evidencia puede ser local y sanitizada: nunca adjuntar una base privada, un token, rutas personales o conversación completa.

## 8. Checklist de revisión de diseño

¿La métrica usa sólo registros elegibles? ¿El denominador corresponde al numerador? ¿El mismo evento puede llegar desde dos fuentes? ¿Cambiar precio/parser cambia identidad? ¿Se tratan distinta granularidad y precisión? ¿El filtro preserva Unknown? ¿La consulta mezcla revisiones? ¿Se recalcula algo en UI distinto de Core? ¿El export incluye contenido/aliases inesperados? ¿Se puede revocar a mitad de lote? ¿La recuperación conserva privacidad y declara pérdida potencial?

Responder con evidencia, no «debería». Si una pregunta no tiene respuesta verificable, la capacidad se mantiene deshabilitada o su resultado no disponible.

## 9. Qué no debe hacer el agente

No crear un motor paralelo de precios/informes; no copiar parsers de CodeBurn/AIStack sin revisar permisos y semántica; no clasificar conversaciones; no suponer que TODO timestamp es request; no sumar subtotales padre/hijo; no convertir datos ausentes a cero; no activar proveedores por catálogo; no reescribir snapshots legacy; no publicar «ahorro» de suscripción desde API equivalent; no prometer latencia sin medición.

Tampoco debe seguir mecánicamente nombres de clases propuestos si ya existe un tipo que cumple el contrato. La profundidad de la solución está en las propiedades verificables, no en añadir más abstracciones.

## 10. Handoff al siguiente agente

Dejar un resumen breve con SHA, tarea cerrada, tests reales, invariantes nuevas, decisiones y siguiente tarea. Los cambios pendientes deben estar en una issue o documento versionado, no sólo en la conversación. Indicar si hay datos/migraciones que no se pueden revertir con un simple checkout.

Este paquete comienza sin tareas de producto completadas. La validación documental no incrementa el avance de P0–P4.


## 11. Incorporación documental al repositorio

Ubicación sugerida, sujeta a las convenciones del repo: `docs/reports-workbench/`. Copiar allí la biblioteca como subárbol, no sobreescribir el README raíz ni la documentación vigente de privacidad/precios. Conservar enlaces relativos y añadir una entrada desde el índice documental existente. El HTML es documentación opcional, nunca payload necesario del ejecutable o parte del MSIX por defecto.

Los esquemas son propuestas `draft.v2`; no registrarlos como contrato público estable por el simple hecho de copiarlos. Los archivos de validation son evidencia de este paquete, no resultados de CI del repositorio. Al integrar, registrar el SHA actual y las diferencias respecto de `df50c36`, y mantener los resultados de producto en su propia evidencia.
