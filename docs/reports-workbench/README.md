# TokenUsage · Reports Engineering Pack

**Importación parcial. PR en borrador; no fusionar todavía.**

[PR #62](https://github.com/gvastethecreator/tokenusage/pull/62) · [Issue #61](https://github.com/gvastethecreator/tokenusage/issues/61)

Base revalidada: `df50c367b083e5624bf09213eddc7013d8299aa4`.

Esta biblioteca especifica la evolución de los informes nativos de TokenUsage. Es documentación de diseño: este cambio no implementa las fases P0–P4 ni modifica el comportamiento de la aplicación.

## Documentos publicados

- [Plan maestro](docs/00-MASTER-PLAN.md): alcance, dependencias, requisitos transversales y cortes de entrega.
- [P0 · Exactitud contable y comparación Rates](docs/phases/P0-CORRECTNESS.md): especificación completa de cohorte fija, exclusiones, compatibilidad y pruebas propuestas.

## Documentos y anexos pendientes de importar

P1 Model Explorer, P2 detalle verificable, P3 sesiones/proyectos y P4 explicaciones/exportación; base y hallazgos; arquitectura y métricas; calidad, accesibilidad y rendimiento; privacidad, migración y handoff; fuentes; revisión de integración y SQLite nativo; esquemas, fixtures, validadores, trazabilidad, lector HTML, prototipo y archivos históricos.

Los enlaces desde los documentos originales a esos materiales todavía no pueden resolverse en esta rama. No confundir este subconjunto con la biblioteca completa preparada anteriormente.

## Estado del intento de publicación

La creación de la issue, rama, PR y los documentos listados funcionó. La llamada para subir P1 fue rechazada con `This tool call was blocked by OpenAI's safety checks.` P1 no quedó publicado y la causa específica del rechazo no fue expuesta por la herramienta. El PR conserva el estado de borrador y no está listo para revisión final ni para merge.

El paquete completo permanece en la entrega original de la conversación. Sus validaciones documentales no acreditan la integridad de esta importación parcial ni pruebas de C#, WinUI, proveedores, migraciones o SQLite nativo.

## Convenciones

**VERIFICADO** significa inspeccionado en una fuente citada, no ejecutado. **PROPUESTO** es diseño pendiente. **GATE** impide habilitar una capacidad sin evidencia. **OBJETIVO PROVISIONAL** requiere calibración real en Windows.

Los IDs REQ, TEST y TASK son referencias del plan, no issues reales ni pruebas ejecutadas del producto. Antes de implementar, revalidar el SHA actual y leer CONTRIBUTING, PRIVACY, PRICING y la matriz de proveedores del repositorio.
