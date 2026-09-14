# Fuentes y trazabilidad

**Fecha documental:** 12 de septiembre de 2026. **Método:** lectura estática; no ejecución del producto ni inspección de datos personales. Los IDs citados en el paquete identifican estas fuentes.

## 1. Baseline revalidado

La lectura de la rama main mediante el conector GitHub devolvió `df50c367b083e5624bf09213eddc7013d8299aa4`, el mismo commit del RFC inicial. Los enlaces de TokenUsage se fijan a ese commit. Esta comprobación no demuestra estado de PRs abiertos, CI ni instalaciones del usuario.

## 2. Fuentes de TokenUsage

### TU-01 · README y estructura pública

[README y estructura pública](https://github.com/gvastethecreator/tokenusage/blob/df50c367b083e5624bf09213eddc7013d8299aa4/README.md)

Alcance: Lectura previa; commit revalidado.

### TU-02 · Dominio de eventos y tokens

[Dominio de eventos y tokens](https://github.com/gvastethecreator/tokenusage/blob/df50c367b083e5624bf09213eddc7013d8299aa4/src/TokenUsage.Core/Usage/UsageEvent.cs)

Alcance: Lectura completa previa en el mismo commit.

### TU-03 · Contratos de fuente/snapshot

[Contratos de fuente/snapshot](https://github.com/gvastethecreator/tokenusage/blob/df50c367b083e5624bf09213eddc7013d8299aa4/src/TokenUsage.Core/Usage/IUsageEventSource.cs)

Alcance: Lectura completa previa en el mismo commit.

### TU-04 · Comparación y fórmula SplitKnownCost

[Comparación y fórmula SplitKnownCost](https://github.com/gvastethecreator/tokenusage/blob/df50c367b083e5624bf09213eddc7013d8299aa4/src/TokenUsage.Core/Usage/UsageComparison.cs)

Alcance: Lectura completa previa; álgebra revisada, C# no ejecutado.

### TU-05 · ViewModel de comparación, Rates y snapshots

[ViewModel de comparación, Rates y snapshots](https://github.com/gvastethecreator/tokenusage/blob/df50c367b083e5624bf09213eddc7013d8299aa4/src/TokenUsage.App/ViewModels/Reports/UsageReportViewModel.Comparison.cs)

Alcance: Relectura ampliada de líneas 1–440, no de todas las clases parciales.

### TU-06 · Presentación de evidencia

[Presentación de evidencia](https://github.com/gvastethecreator/tokenusage/blob/df50c367b083e5624bf09213eddc7013d8299aa4/src/TokenUsage.App/ViewModels/Reports/UsageReportViewModel.MeasurementDetails.cs)

Alcance: Lectura completa previa en el mismo commit.

### TU-07 · Consulta de informes y población temporal

[Consulta de informes y población temporal](https://github.com/gvastethecreator/tokenusage/blob/df50c367b083e5624bf09213eddc7013d8299aa4/src/TokenUsage.Core/Automation/UsageReportQuery.cs)

Alcance: Relectura de líneas 1–250; no auditoría exhaustiva de todos los métodos.

### TU-08 · Repositorio y schema 5

[Repositorio y schema 5](https://github.com/gvastethecreator/tokenusage/blob/df50c367b083e5624bf09213eddc7013d8299aa4/src/TokenUsage.Core/Usage/UsageRepository.cs)

Alcance: Relectura de líneas 1–215; cuerpo completo de reconciliación queda como gate de implementación.

### TU-09 · Repricing y elegibilidad

[Repricing y elegibilidad](https://github.com/gvastethecreator/tokenusage/blob/df50c367b083e5624bf09213eddc7013d8299aa4/src/TokenUsage.Core/Usage/UsageReferencePricing.cs)

Alcance: Relectura completa.

### TU-10 · Fuentes activas, límites y retención

[Fuentes activas, límites y retención](https://github.com/gvastethecreator/tokenusage/blob/df50c367b083e5624bf09213eddc7013d8299aa4/docs/PROVIDER-MATRIX.md)

Alcance: Lectura de secciones pertinentes en revisión inicial; commit revalidado.

### TU-11 · Política de privacidad

[Política de privacidad](https://github.com/gvastethecreator/tokenusage/blob/df50c367b083e5624bf09213eddc7013d8299aa4/PRIVACY.md)

Alcance: Lectura de política y alcance en revisión inicial; commit revalidado.

### TU-12 · Guía de pruebas y evidencias

[Guía de pruebas y evidencias](https://github.com/gvastethecreator/tokenusage/blob/df50c367b083e5624bf09213eddc7013d8299aa4/docs/CONTRIBUTOR-TESTING.md)

Alcance: Relectura completa.

### TU-13 · Metadata de medición y colección

[Metadata de medición y colección](https://github.com/gvastethecreator/tokenusage/blob/df50c367b083e5624bf09213eddc7013d8299aa4/src/TokenUsage.Core/Usage/UsageRepository.Measurement.cs)

Alcance: Lectura completa previa en mismo commit.

### TU-14 · Entrada del colector Codex

[Entrada del colector Codex](https://github.com/gvastethecreator/tokenusage/blob/df50c367b083e5624bf09213eddc7013d8299aa4/src/TokenUsage.Providers/Codex/CodexUsageEventSource.cs)

Alcance: Relectura del archivo; no de todas las clases parciales ni instalación real.

### TU-15 · Pricing y semántica del eje Rates

[Pricing y semántica del eje Rates](https://github.com/gvastethecreator/tokenusage/blob/df50c367b083e5624bf09213eddc7013d8299aa4/docs/PRICING.md)

Alcance: Relectura completa; no se revalidaron tarifas comerciales en este paquete.

### TU-16 · Contribución, issue previa y exclusión de browser runners

[Contribución, issue previa y exclusión de browser runners](https://github.com/gvastethecreator/tokenusage/blob/df50c367b083e5624bf09213eddc7013d8299aa4/CONTRIBUTING.md)

Alcance: Relectura de líneas 1–130, que contienen las normas citadas.

### TU-17 · Proyecto Presentation existente

[Proyecto Presentation existente](https://github.com/gvastethecreator/tokenusage/tree/df50c367b083e5624bf09213eddc7013d8299aa4/src/TokenUsage.Presentation)

Alcance: Existencia/estructura del árbol verificada en revisión inicial; roles nuevos son propuesta.

## 3. Referencias comparativas y documentación oficial

No se incorporó código de CodeBurn/AIStack ni un nuevo runtime. Las URLs main de esas referencias pueden cambiar; se conserva el hash de blob observado para trazabilidad, que no debe confundirse con un SHA de commit. Las páginas oficiales son consultadas, no copias congeladas.

### CB-01 · CodeBurn · clasificador

[CodeBurn · clasificador](https://github.com/getagentseal/codeburn/blob/main/src/classifier.ts)

Alcance: Lectura en revisión inicial. Blob observado 44582db8d99a5736637f049cea6d09e862e2df3b. URL main mutable; fijar commit antes de adoptar código.

### AS-01 · AIStack · CLI

[AIStack · CLI](https://github.com/alp82/aistack/blob/main/packages/cli/README.md)

Alcance: Lectura inicial. Blob observado 936015446b7bf317229a7efc97622f5439a05f2a. Referencia comparativa, sin ejecución ni nueva integración.

### AS-02 · AIStack · agregados combinables

[AIStack · agregados combinables](https://github.com/alp82/aistack/blob/main/packages/cli/src/usage/days.ts)

Alcance: Lectura inicial. Blob observado 74e4ffda6ea70d2f0be9aebcaa2bd4db0a18bef0. No se copió su implementación.

### EXT-01 · Microsoft.Data.Sqlite · limitaciones Async

[Microsoft.Data.Sqlite · limitaciones Async](https://learn.microsoft.com/en-us/dotnet/standard/data/sqlite/async)

Alcance: Consultada el 12/09/2026; uso acotado para diseño del executor.

### EXT-02 · SQLite · Online Backup API

[SQLite · Online Backup API](https://www.sqlite.org/backup.html)

Alcance: Consultada el 12/09/2026; no se ejecutó backup del producto.

### EXT-03 · Microsoft.Data.Sqlite · backup

[Microsoft.Data.Sqlite · backup](https://learn.microsoft.com/en-us/dotnet/standard/data/sqlite/backup)

Alcance: Consultada el 12/09/2026.

### EXT-04 · SQLite · WAL

[SQLite · WAL](https://www.sqlite.org/wal.html)

Alcance: Consultada el 12/09/2026; consistencia, lectores/escritor y restricciones de filesystem.

### EXT-05 · Microsoft.Data.Sqlite · database errors

[Microsoft.Data.Sqlite · database errors](https://learn.microsoft.com/en-us/dotnet/standard/data/sqlite/database-errors)

Alcance: Consultada el 12/09/2026; contención y timeouts, no benchmark.

### EXT-06 · Windows · accesibilidad

[Windows · accesibilidad](https://learn.microsoft.com/en-us/windows/apps/design/accessibility/accessibility-overview)

Alcance: Consultada el 12/09/2026. Requisitos de teclado, UI Automation y temas; sin validación WinUI en este entorno.

### EXT-07 · OWASP · CSV Injection

[OWASP · CSV Injection](https://community.owasp.org/attacks/CSV_Injection)

Alcance: Consultada el 12/09/2026. No existe sanitización universal; prueba por consumidor/flujos de reapertura.

### EXT-08 · JSON Schema · draft 2020-12

[JSON Schema · draft 2020-12](https://json-schema.org/draft/2020-12/json-schema-core)

Alcance: Consultada el 12/09/2026. Los schemas incluidos son borradores locales de diseño.

## 4. Fuente documental previa

El [RFC inicial](../../archive/RFC-original-2026-09-12.md) se conserva sin modificar como antecedente. El paquete actual precisa el uso real de `SplitKnownCost` en Rates y no afirma que su fórmula se use en todas las comparaciones históricas. Cuando una decisión del RFC difiere de esta ampliación expresa, usar los documentos actuales.

## 5. Límites de evidencia

No se verificaron aquí llamadas reales de proveedores, totales de una cuenta, hardware ARM64, seguridad forense del borrado, tiempos de respuesta del producto ni accesibilidad WinUI. Los comandos de Windows están documentados desde fuentes del repositorio, no ejecutados. El resultado de validación local se limita a documentos, esquemas de diseño, oráculos sintéticos y lector HTML.

Los estándares de aceptación y presupuestos numéricos nuevos son propuestas de este paquete, no requisitos oficiales atribuidos a Microsoft ni resultados medidos. Las fórmulas matemáticas derivadas se acompañan de ejemplos propios, no de citas comerciales de precios.

## 6. Evidencia adicional de integración

### TU-18 · Proveedor SQLite declarado

[Core.csproj en la base](https://github.com/gvastethecreator/tokenusage/blob/df50c367b083e5624bf09213eddc7013d8299aa4/src/TokenUsage.Core/TokenUsage.Core.csproj). Lectura estática: Microsoft.Data.Sqlite 10.0.11 y SQLitePCLRaw.bundle_e_sqlite3 3.0.5. No determina por sí sola la versión nativa cargada.

### TU-19 · Frontera de publicación Pages

[Pages workflow en la base](https://github.com/gvastethecreator/tokenusage/blob/df50c367b083e5624bf09213eddc7013d8299aa4/.github/workflows/pages.yml). Publica el directorio docs completo. No se modificó el workflow.

La fuente EXT-04 (SQLite WAL) se reconsultó durante esta integración, incluyendo su sección 11 sobre WAL-reset. Las pruebas y límites necesarios están en el [gate nativo](../operations/13-NATIVE-SQLITE-GATE.md). No se consultaron binarios de TokenUsage.
