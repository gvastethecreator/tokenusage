# TokenUsage · Reports Engineering Pack

**Versión documental 2.1 · 12 de septiembre de 2026 · Estado: propuesta técnica, no implementación**

Este paquete convierte el RFC de Reports Workbench en un programa de implementación verificable. Está escrito para el mantenedor y para un agente que trabaje sobre el repositorio: explica qué cambiar, qué preservar, cómo probarlo y qué evidencia impide dar una fase por terminada. La documentación está en español; los nombres de interfaz de la aplicación permanecen en inglés.

**Base revalidada:** `gvastethecreator/tokenusage@df50c367b083e5624bf09213eddc7013d8299aa4`. La rama `main` consultada sigue en ese commit. Esta copia local reúne la biblioteca completa de 58 archivos. No se ejecutó TokenUsage ni se accedió a los datos de una computadora personal.

**Publicación:** esta preparación es local. En remoto ya existen la [issue #61](https://github.com/gvastethecreator/tokenusage/issues/61) y el [PR #62](https://github.com/gvastethecreator/tokenusage/pull/62) en borrador, pero publican sólo tres archivos: un README de importación parcial, el [plan maestro](docs/00-MASTER-PLAN.md) y [P0](docs/phases/P0-CORRECTNESS.md). Esas dos especificaciones coinciden con este árbol y con el ZIP original por blob SHA (`861c7c6f1073127f718da46b95d843d826c07f16` y `3463cbe0e1f875a7f3111e92b351f3eadd71af6a`). El README remoto describe el subconjunto y no coincide con este índice. El enlace en `docs/README.md`, P1–P4 y el resto de anexos siguen inéditos en remoto. Actualizar #61/#62 queda pendiente de autorización del mantenedor; esta preparación local no es esa publicación.

**Integración:** [alcance y reproducción](docs/operations/11-REPOSITORY-INTEGRATION.md) · [revisión adicional](docs/operations/12-REVIEW-AND-ACCEPTANCE.md) · [gate SQLite nativo](docs/operations/13-NATIVE-SQLITE-GATE.md).

## Empezar aquí

| Documento | Para qué sirve |
|---|---|
| [Plan maestro](docs/00-MASTER-PLAN.md) | Orden de ejecución, dependencias, cortes verticales, riesgos y decisiones. |
| [Base técnica y correcciones](docs/01-BASELINE-AND-FINDINGS.md) | Evidencia del código existente y precisión del diagnóstico anterior. |
| [P0 · Exactitud](docs/phases/P0-CORRECTNESS.md) | Corregir la comparación Rates, fijar semántica y proteger contratos existentes. |
| [P1 · Model Explorer](docs/phases/P1-MODEL-EXPLORER.md) | Consultas, filtros, tabla, detalle, evidencia y experiencia nativa. |
| [P2 · Detalle verificable](docs/phases/P2-VERIFIABLE-DETAIL.md) | Identidad, granularidad, reconciliación, capacidades y actividad temporal. |
| [P3 · Sesiones y proyectos](docs/phases/P3-SESSIONS-PROJECTS.md) | Consentimiento, atribución, pseudónimos, jerarquías y borrado. |
| [P4 · Explicaciones y exportación](docs/phases/P4-INSIGHTS-EXPORTS.md) | Insights deterministas, distribuciones, escenarios y exportaciones reproducibles. |
| [Arquitectura y contratos](docs/architecture/02-CONTRACTS-AND-DATA-FLOW.md) | Fronteras de responsabilidad, revisión coherente, DTO y planificación. |
| [Diccionario de métricas](docs/architecture/03-METRIC-SEMANTICS.md) | Fórmulas, unidades, elegibilidad, denominadores y exclusiones. |
| [Calidad y pruebas](docs/quality/04-QUALITY-GATES.md) | Pruebas por riesgo, casos adversariales y evidencias exigidas. |
| [Especificación de experiencia](docs/quality/05-UX-AND-ACCESSIBILITY.md) | Estados, teclado, diseño, accesibilidad y revisión visual. |
| [Rendimiento](docs/quality/06-PERFORMANCE-PROTOCOL.md) | Fixtures de carga, medición, cancelación y objetivos provisionales. |
| [Privacidad y seguridad](docs/operations/07-PRIVACY-AND-THREATS.md) | Datos permitidos, amenazas, permisos, exports y límites del borrado. |
| [Migración y recuperación](docs/operations/08-MIGRATIONS-AND-RECOVERY.md) | Evolución aditiva, copias coherentes, fallos y rollback seguro. |
| [Entrega a agentes y backlog](docs/operations/09-AGENT-HANDOFF.md) | Tareas acotadas, plantilla de issue/PR y comandos comprobados. |
| [Fuentes](docs/references/10-SOURCES.md) | Permalinks, alcance de inspección y referencias oficiales. |

`index.html` contiene una edición navegable de los 20 documentos vigentes (incluido este índice) y el RFC archivado, sin CDN ni servidor. No es otra interfaz de TokenUsage; es el lector de esta documentación. `archive/` conserva el RFC inicial como antecedente, no como especificación vigente cuando exista una corrección expresa.

## Qué cambió respecto del RFC inicial

La fórmula de `SplitKnownCost` tiene la limitación algebraica descrita, pero el llamador de UI inspeccionado la usa en **Rates**, que revaloriza una misma cohorte a dos fechas de catálogo. Por eso P0 no parte de afirmar que toda comparación histórica está mal. La solución prioritaria es definir una población idéntica y valorizable en ambas fechas, distinguir el cambio de precio de los cambios de cobertura y tratar volumen/mezcla como dimensiones no aplicables a ese experimento. La descomposición histórica de tres factores se reserva para P4 y exige datos adicionales. Véanse [base](docs/01-BASELINE-AND-FINDINGS.md) y [P0](docs/phases/P0-CORRECTNESS.md).

## Qué hay de ejecutable

`schemas/` y `fixtures/` son contratos y casos **de diseño**, no un nuevo contrato publicado de la CLI. `tools/validate_pack.py` comprueba estructura del paquete, contratos de ejemplo y propiedades matemáticas seleccionadas. Esas comprobaciones no sustituyen tests C#, integración de fuentes ni pruebas WinUI. El informe generado en `validation/` enumera exactamente lo ejecutado y lo no ejecutado. El validador requiere Python 3.10 o posterior. La comprobación de esquemas utiliza `jsonschema` si está instalado. Las comprobaciones `date-time` requieren además `rfc3339-validator`, declarado en `tools/requirements-docs.txt`. Si falta `jsonschema`, el validador devuelve estado parcial y código de salida 2, sin declarar esa validación aprobada. Si `jsonschema` está presente y falta el comprobador RFC 3339, falla con un error explícito y no da por válidas fechas inválidas.

```powershell
# Sólo para validar este paquete documental; no toca el repositorio ni tus datos.
python .\tools\validate_pack.py
```

## Convenciones

**VERIFICADO** significa leído en el código o documentación citados, no ejecutado. **PROPUESTO** significa una decisión recomendada de este paquete. **GATE** es un requisito pendiente para habilitar o publicar. **OBJETIVO PROVISIONAL** identifica un presupuesto de calidad que debe calibrarse y aprobarse con una medición en Windows.

Los IDs `REQ-*`, `TEST-*`, `TASK-*` y `GATE-*` enlazan obligaciones, pruebas y tareas. No son números de issues existentes. Ninguna métrica pendiente, escenario sintético o comparación algebraica debe presentarse como uso real del usuario.

## Prototipo e históricos

[Model Explorer Lab](prototypes/model-explorer-lab.html) conserva el prototipo sintético anterior. Los dos ZIP originales completos están en [archivo v2](archive/engineering-v2-original.zip) y [plan inicial](archive/initial-plan-original.zip). Los resultados antiguos están bajo `validation/historical-v2/`; no acreditan el lector regenerado ni avances de P0–P4.

La plantilla y el registro de documentos están versionados. `python tools/build_reader.py --check` detecta divergencias entre Markdown y HTML. [Herramientas de documentación](tools/README.md).

## Reparaciones deliberadas de esta preparación

El ZIP original se conserva en `.scratch/tokenusage-reports-workbench/sources/TokenUsage-Reports-PR-Ready.zip`. Los archivos de `archive/` y `validation/historical-v2/` no se modifican. P0–P4 siguen byte a byte con el archivo de ingeniería original. El plan maestro y P0 también coinciden con los blobs ya publicados en el PR #62.

Cambios deliberados sobre fuentes activas; los originales importados no se presentan como inalterados:

- `tools/update_manifest.py` ordena por la ruta POSIX relativa en texto, independiente de la plataforma.
- `.gitattributes` fija `eol=lf` para el texto hasheado de este paquete y marca ZIP e imágenes como binarios. No forma parte de los 57 archivos originales del ZIP; es una reparación adicional para que un checkout Windows con `core.autocrlf=true` conserve los bytes del manifiesto.
- `tools/update_manifest.py` no incluye `.git` en el inventario hasheado.
- `tools/requirements-docs.txt` declara `rfc3339-validator==0.1.4`.
- `tools/validate_pack.py` exige ese comprobador `date-time` y rechaza fechas de calendario inválidas; falla con un mensaje claro si el comprobador no está disponible.
- Este README, [integración](docs/operations/11-REPOSITORY-INTEGRATION.md) y [entrega a agentes](docs/operations/09-AGENT-HANDOFF.md) distinguen la preparación local, el subconjunto de #62 y lo inédito.
- `tools/README.md` y el estado escrito por `validate_integration.py` siguen esas mismas dependencias y el estado de publicación.
- El lector y el manifiesto se regeneran desde estas fuentes.
- `validation/reader-validation.json` y `validation/reader-*.png` permanecen como evidencia del ZIP original. No son una nueva inspección de navegador de esta preparación.
