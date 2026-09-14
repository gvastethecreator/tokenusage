# P3 · Sesiones y proyectos con atribución voluntaria

**Estado:** propuesta sometida a aprobación de privacidad. **Dependencias:** identidad/granularidad P2 y al menos una fuente admitida con metadata suficiente. **Fuentes:** TU-02/10/11/12; referencias CB/AS como contraste, no autorización, en [Fuentes](../references/10-SOURCES.md).

## 1. Resultado esperado

Poder responder «¿en qué proyecto observé este consumo?» y «¿cómo se distribuyó dentro de esta sesión?», sin guardar títulos de conversaciones, prompts, rutas literales ni comandos. No se promete identificar todo el trabajo de la PC. Una sesión observada tampoco equivale a una sesión humana de trabajo ni su duración a tiempo de inferencia.

La entrega inicial es una única fuente validada. Otras fuentes permanecen sin atribución y sus totales no desaparecen del panorama general. El usuario puede revocar el permiso sin perder los totales numéricos previos que no necesiten esas asociaciones.

## 2. Decisión de producto previa

Agregar identificadores de sesión/proyecto cambia las posibilidades de vinculación de los datos. Aunque sean opacos y no contengan texto, permiten reconstruir patrones. Por eso P3 requiere actualización explícita de política/documentación, revisión del seam del lector y consentimiento por capacidad/fuente. «Es un hash» no basta.

Separamos tres permisos: recolección numérica actual, asociación a sesión/proyecto y exportación de alias. Habilitar uno no habilita los otros. El permiso de atribución no autoriza leer prompts para inferir qué proyecto es ni recopilar el contenido de archivos de configuración.

## 3. Requisitos

| ID | Requisito |
|---|---|
| REQ-P3-01 | Atribución desactivada por defecto y habilitada por fuente/capacidad. |
| REQ-P3-02 | No hay atribución por contenido ni por el cwd de TokenUsage. |
| REQ-P3-03 | Claves opacas locales; alias voluntarios con exportación separada. |
| REQ-P3-04 | `Unassigned` y `Ambiguous` no se convierten en un proyecto inventado. |
| REQ-P3-05 | Agregaciones de jerarquía no duplican hijos incluidos en padres. |
| REQ-P3-06 | Revocación bloquea escrituras/salidas en curso y permite purga verificable. |
| REQ-P3-07 | Rehabilitar no importa automáticamente el pasado borrado. |
| REQ-P3-08 | Capturas/exports informan si contienen alias o asociaciones. |

## 4. Modelo mínimo de datos

```text
SessionAttribution
  EventKey + EventRevision
  SessionKey
  ParentSessionKey?       # sólo relación observada y permitida
  EvidenceKind
  ConsentEpoch

ProjectAttribution
  EventKey + EventRevision
  ProjectKey?
  State = Observed | UserMapped | Unassigned | Ambiguous
  EvidenceKind
  ConsentEpoch

LocalAlias
  OpaqueKey
  UserChosenLabel
  ExportAllowed = false
```

Una sesión puede abarcar más de un proyecto. La atribución primaria es por evento cuando la fuente lo permite; no imponer una única carpeta a toda la sesión. Si sólo se conoce una asociación de sesión completa y es ambigua, conservar la ambigüedad. No repartir dinero entre proyectos con porcentajes arbitrarios.

Los alias se guardan en un almacén local separado de los hechos analíticos y no se vuelven identificadores. Renombrar el alias no reescribe historia ni duplica proyectos. Un alias puede revelar un cliente; tratarlo como dato personal voluntario, con límite de longitud y salida escapada.

## 5. Pseudonimización e identidad

Propuesta: HMAC-SHA-256 con secreto generado por TokenUsage y separación de dominios para sesiones/proyectos. La entrada sólo incluye identificadores admitidos y su ámbito. No usar tokens de sesión ajenos, correo, usuario del sistema o credenciales como secreto. Proteger el secreto con mecanismos Windows ya aprobados del proyecto, tras revisar cómo se comportan MSIX y portable.

Ejemplo conceptual: `HMAC(K, "project/v1" || fuente || identificador_normalizado)`. Definir framing de campos, no concatenar cadenas ambiguas. No exportar K ni un mapa reversible de rutas. Si se necesita resolver una raíz por funcionamiento del lector, almacenarla en configuración de acceso ya permitida, no copiarla a eventos ni a informes.

Las claves son locales, no anónimas universalmente. MSIX/portable o instalaciones distintas no se correlacionan automáticamente. Una importación voluntaria de alias requiere un contrato posterior; no sincronizar secretos para facilitar deduplicación global. Rotar/eliminar K no borra por sí solo todos los vínculos existentes: la purga debe eliminar las asociaciones persistidas.

## 6. Máquina de consentimiento

Estados propuestos: `Disabled`, `Enabled(epoch=N)`, `Revoking`, `PurgePending`, `DisabledAfterPurge`. La preferencia persiste antes de iniciar recolección; un diálogo visual sin una barrera en la capa de escritura no protege nada.

Al habilitar, explicar fuente, campos, uso y retención. Por defecto la captura comienza desde ese momento. El backfill es una acción separada con rango, fuente y confirmación; no se ejecuta «para completar el gráfico». El usuario puede aceptar sólo una fuente.

Cada lote toma una época de consentimiento. Antes del commit, verificar que siga vigente bajo el mismo mecanismo de coordinación que la revocación. Si cambió, descartar metadata opcional del lote aunque los tokens base admitidos puedan persistirse. El exportador también revisa la época antes de finalizar un archivo.

Revocar primero bloquea nuevas asociaciones, cancela trabajos y eleva la época; después purga. El inicio tras crash detecta `PurgePending` y mantiene la función bloqueada hasta completar o informar el fallo. No reactivar por encontrar una configuración vieja o un checkpoint anterior.

## 7. Purga: alcance exacto

Eliminar asociaciones, aliases seleccionados, índices/proyecciones de sesión/proyecto, cachés, snapshots que contengan vínculos y manifiestos persistidos con esas claves. Preservar los totales numéricos base, dejando asociaciones como desconocidas/sin asignar. Los snapshots enriquecidos afectados se retiran o redactan conforme a una política explícita: la promesa de inmutabilidad del informe no prevalece sobre la revocación de privacidad.

No se pueden retirar archivos ya exportados, copias externas, capturas del usuario ni backups que no controla la aplicación. Mostrar esa limitación antes de confirmar. Las copias de respaldo propias con metadata revocada deben inventariarse y eliminarse o quedar bloqueadas para restauración mediante el journal/época de privacidad vigente.

No prometer borrado forense perfecto en SSD, paginación del sistema o medios externos. Una purga lógica comprobable y una política de backups clara son obligaciones reales. No ejecutar VACUUM ni manipular WAL de bases de terceros como supuesto mecanismo de privacidad.

Al reactivar, comenzar una nueva época. No reimportar asociaciones históricas borradas sin backfill explícito. El journal de supresión puede usar mínimos intervalos/ámbitos no reversibles; no conservar precisamente la lista sensible que se afirma haber borrado.

## 8. Atribución por proyecto

Fuentes posibles: ID estructurado de workspace admitido, metadato documentado del lector o vinculación manual del usuario. La ruta completa, si se debe leer transitoriamente bajo permiso para calcular una clave, no se materializa en DTO/log/DB analítica y se documenta esa lectura. Una ruta sincronizada no prueba que el uso ocurrió físicamente en esa PC.

No deducir proyecto de palabras del prompt, nombres de archivos editados, historial de shell ni utilización de GPU. Tampoco tomar la carpeta donde se inició TokenUsage. Para monorepos, permitir alias/mapeo explícito sin fingir divisiones por paquete si la fuente sólo conoce el workspace raíz.

`UserMapped` conserva que fue una decisión del usuario, no dato observado por el proveedor. Un cambio de mapeo debe indicar si se aplica a futuro o reinterpreta asociaciones anteriores, con nueva revisión y sin modificar tokens/costos.

## 9. Sesiones, subagentes y agregación

Definir ID de sesión del proveedor frente a intervalo de actividad. No unir sesiones distintas porque ocurrieron con menos de cierta pausa; eso sería una heurística independiente y no forma parte del MVP.

Una relación padre/hijo observada permite vista de árbol, pero no indica automáticamente si costos del padre incluyen al hijo. Cada fuente debe declarar contabilidad `exclusive`, `inclusive` o `unknown`. Si el padre es inclusivo, no sumar su subtotal y los hijos. Para el total global elegir una representación autoritativa de los eventos, no sumar filas visuales de un árbol.

Si la relación existe pero el alcance contable es desconocido, mostrar los grupos por separado sin un total consolidado engañoso. Un subagente con dos referencias no se cuenta dos veces. Los ciclos de relaciones se detectan y rechazan del árbol; la anomalía no autoriza borrar uso base válido.

Duración de sesión: `last_observed - first_observed`, rotulada `Observed span`. No significa tiempo activo, tiempo humano, trabajo facturable ni tiempo de GPU. Eventos de duración real, cuando existan, alimentan métricas distintas; no sumar duraciones superpuestas para afirmar horas de trabajo únicas.

## 10. Experiencia de usuario

La primera apertura muestra el estado y el botón de configuración, no una tabla de ceros que sugiera ausencia de uso. Con permiso, `Sessions` y `Projects` muestran sólo capacidades activadas. Las filas sin asociación permanecen en `Unassigned`, con porcentaje **de los tokens observados elegibles** si ese denominador es conocido.

La pantalla de consentimiento permite revisar ejemplos de campos, sin enseñar valores privados reales antes de aceptar. En la tabla, aliases son opcionales; por defecto una etiqueta neutral. `Copy report` usa modo sin aliases; incluirlos exige elección explícita. El modo captura redacta identificadores/aliases y evita filtrar tooltips ocultos.

El panel de proyecto conserva filtros de modelo/herramienta. Volver al explorador restaura el contexto. Las diferencias entre `Observed`, `User mapped` y `Ambiguous` tienen texto accesible y no sólo colores. Las rutas de carpetas no aparecen como breadcrumbs de la UI analítica.

## 11. Tareas y pruebas

**TASK-P3-01:** aprobar campo por campo y documentar amenazas. **TASK-P3-02:** implementar estados, época y barreras de escritura/export. **TASK-P3-03:** almacenes de vínculos/alias y purga. **TASK-P3-04:** lector piloto sin contenido, atribución y jerarquía cuando existan. **TASK-P3-05:** vistas, redacción y prueba real sanitizada.

| ID | Escenario | Resultado esperado |
|---|---|---|
| TEST-P3-01 | Instalación nueva y permiso apagado. | Cero lecturas/persistencia de metadata nueva. |
| TEST-P3-02 | Revocar entre parse y commit. | Lote no guarda vínculos con época vieja. |
| TEST-P3-03 | Crash durante purge. | Reinicio bloqueado y purga reanudable. |
| TEST-P3-04 | Rehabilitar tras borrar. | No recupera historia sin backfill explícito. |
| TEST-P3-05 | Una sesión atraviesa proyectos. | No se fuerza atribución única ni se duplica dinero. |
| TEST-P3-06 | Padre inclusivo y dos hijos. | Total global conservado, suma visual no duplicada. |
| TEST-P3-07 | Alias con fórmula/HTML/ruta privada. | No se exporta por defecto; cuando autorizado, salida segura. |
| TEST-P3-08 | Export iniciado antes de revocar. | Se cancela o se regenera sin asociaciones; no finaliza con datos revocados. |
| TEST-P3-09 | Snapshot enriquecido y backup previo. | Purga/restauración respeta época y no reexpone vínculos. |
| TEST-P3-10 | Fuente sin atribución y tokens válidos. | Tokens permanecen; proyecto/sesión no se inventan. |

## 12. Gates y entrega

**GATE-P3-A:** política y permiso aprobados; P3 no se habilita «experimentalmente» a espaldas del usuario. **GATE-P3-B:** pruebas de carreras y purge pasan. **GATE-P3-C:** evidencia real de fuente y ausencia de contenido en todas las salidas. **GATE-P3-D:** navegación y redacción verificadas en Windows.

Entregar a P4 cohortes y etiquetas de atribución, no un permiso implícito para hacer análisis de comportamiento. Los insights deben seguir diciendo qué parte del uso está asignada y qué parte es desconocida.
