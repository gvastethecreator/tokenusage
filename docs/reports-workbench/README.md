# TokenUsage · Reports Engineering Pack

**Estado: propuesta técnica; P0–P4 no están implementadas por este cambio.**

Issue de integración: [#61](https://github.com/gvastethecreator/tokenusage/issues/61).
Base revalidada: `df50c367b083e5624bf09213eddc7013d8299aa4`.

Esta biblioteca especifica cómo ampliar los informes nativos de TokenUsage sin perder exactitud contable, privacidad, evidencia ni compatibilidad. La documentación está en español; la interfaz del producto permanece en inglés.

## Ruta de lectura

| Documento | Propósito |
|---|---|
| [Plan maestro](docs/00-MASTER-PLAN.md) | Dependencias, límites, cortes y criterios de cierre. |
| [Base y hallazgos](docs/01-BASELINE-AND-FINDINGS.md) | Evidencia del repositorio y corrección del diagnóstico de Rates. |
| [P0 · Exactitud](docs/phases/P0-CORRECTNESS.md) | Cohorte fija de Rates y compatibilidad. |
| [P1 · Model Explorer](docs/phases/P1-MODEL-EXPLORER.md) | Consultas compartidas, filtros y experiencia nativa. |
| [P2 · Detalle verificable](docs/phases/P2-VERIFIABLE-DETAIL.md) | Identidad, granularidad, reconciliación y retención. |
| [P3 · Sesiones y proyectos](docs/phases/P3-SESSIONS-PROJECTS.md) | Consentimiento, atribución, revocación y borrado. |
| [P4 · Explicaciones y exportación](docs/phases/P4-INSIGHTS-EXPORTS.md) | Reglas deterministas, snapshots y formatos reproducibles. |
| [Arquitectura](docs/architecture/02-CONTRACTS-AND-DATA-FLOW.md) | Fronteras y contratos. |
| [Métricas](docs/architecture/03-METRIC-SEMANTICS.md) | Unidades, fórmulas, elegibilidad y exclusiones. |
| [Calidad](docs/quality/04-QUALITY-GATES.md) | Pruebas por riesgo y evidencia requerida. |
| [Experiencia y accesibilidad](docs/quality/05-UX-AND-ACCESSIBILITY.md) | Estados, teclado, Narrator y revisión visual. |
| [Rendimiento](docs/quality/06-PERFORMANCE-PROTOCOL.md) | Protocolo de medición y objetivos provisionales. |
| [Privacidad](docs/operations/07-PRIVACY-AND-THREATS.md) | Amenazas, permisos, datos permitidos y exports. |
| [Migración y recuperación](docs/operations/08-MIGRATIONS-AND-RECOVERY.md) | Copias coherentes, fallos y rollback. |
| [Handoff y backlog](docs/operations/09-AGENT-HANDOFF.md) | Tareas acotadas y trabajo verificable para agentes. |
| [Fuentes](docs/references/10-SOURCES.md) | Permalinks y alcance de la inspección. |
| [Integración](docs/operations/11-REPOSITORY-INTEGRATION.md) | Diseño original de publicación y reproducción. |
| [Revisión adicional](docs/operations/12-REVIEW-AND-ACCEPTANCE.md) | Casos adversariales adicionales. |
| [SQLite nativo](docs/operations/13-NATIVE-SQLITE-GATE.md) | Verificación del binario real; no implica vulnerabilidad confirmada. |

## Reglas de interpretación

Las fases y documentos originales conservan su contexto de preparación. Una frase histórica como «no se creó una issue» no describe la publicación actual, que está vinculada a #61. Los IDs `REQ-*`, `TEST-*`, `TASK-*` y `GATE-*` son referencias del plan, no issues reales ni pruebas ejecutadas del producto.

**VERIFICADO** significa inspeccionado en la fuente citada, no ejecutado. **PROPUESTO** es diseño pendiente. **GATE** impide habilitar una capacidad sin evidencia. **OBJETIVO PROVISIONAL** requiere calibración real en Windows.

Los fixtures, schemas y prototipos son sintéticos y de diseño. No amplían los contratos públicos de la CLI, no habilitan proveedores y no autorizan la lectura de contenido privado.

## Estado de publicación

La importación se realiza por commits pequeños en una rama separada. El PR registra qué archivos y anexos fueron efectivamente publicados y las validaciones realizadas. No confundir los resultados del paquete preparado con pruebas de C#, WinUI, proveedores, migraciones o SQLite nativo.

Las especificaciones de P0–P4 se preservan sin resumirlas. Antes de implementar, revalidar el SHA actual y leer CONTRIBUTING, PRIVACY, PRICING y la matriz de proveedores del repositorio.
