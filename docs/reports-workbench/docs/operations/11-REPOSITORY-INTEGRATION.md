# Integración al repositorio · alcance y reproducción

**Estado:** biblioteca completa preparada en el checkout local para revisión. La [issue #61](https://github.com/gvastethecreator/tokenusage/issues/61) y el [PR #62](https://github.com/gvastethecreator/tokenusage/pull/62) en borrador ya existen en remoto, pero publican sólo tres archivos: un README de importación parcial, el plan maestro y P0. El plan maestro y P0 coinciden con este árbol. El índice completo de esta biblioteca, P1–P4, anexos, herramientas, prototipo, archivos históricos y el enlace de `docs/README.md` no están publicados en ese PR. Esta nota describe la preparación local; no actualiza #61/#62.

**Base consultada:** `df50c367b083e5624bf09213eddc7013d8299aa4`. El único archivo existente que propone modificar el parche es `docs/README.md`, cuyo blob base es `9f9d7fc1e2dd9f065285d88d1de2caf5fb398649` y sigue coincidiendo en `main`. No se aplicó el parche completo del ZIP sobre la rama parcial `docs/reports-workbench-plan`: el checkout local partía de `main` sin `docs/reports-workbench/`. El README de importación parcial del PR no se reutilizó como índice de la biblioteca completa.

## 1. Alcance exacto

El nuevo subárbol `docs/reports-workbench/` contiene los 17 documentos originales, el RFC previo, los cinco documentos de fase, los contratos de diseño, fixtures, trazabilidad y herramientas documentales. Añade tres documentos de integración y validación: éste, [revisión adversarial](12-REVIEW-AND-ACCEPTANCE.md) y [SQLite nativo](13-NATIVE-SQLITE-GATE.md). `.gitattributes` es una reparación posterior al ZIP original: fija `eol=lf` para el texto hasheado y deja los ZIP e imágenes como binarios, de modo que `core.autocrlf=true` no cambie los bytes del manifiesto.

El lector `index.html` se genera desde Markdown. El prototipo `prototypes/model-explorer-lab.html` conserva los datos sintéticos del laboratorio anterior. Ninguno se integra en WinUI, MSIX, CLI, colectores ni solución .NET. El archivo de índice documental existente recibe una única entrada de navegación, sin reemplazar su contenido anterior.

No se modifica el comportamiento del producto, el esquema SQLite vigente, la política de privacidad, el catálogo, el pricing, los contratos `v1`, el estado de proveedores ni los workflows. No se incorpora un browser runner al repositorio activo, sus dependencias o su CI. El script experimental del paquete inicial se conserva únicamente dentro de su ZIP histórico, como antecedente inerte; no es una prueba del producto.

## 2. Preservación de toda la entrega anterior

Los ZIP `archive/engineering-v2-original.zip` y `archive/initial-plan-original.zip` conservan literalmente ambos paquetes originales, incluidos sus HTML, capturas, scripts y resultados históricos. Esto permite auditar los cambios editoriales sin confundir los resultados viejos con las comprobaciones de esta integración. No ejecutar scripts de esos archivos como si fueran parte del build oficial.

`validation/source-archives.json` registra SHA-256, tamaño y la lista de entradas de cada ZIP. Un hash comprueba integridad respecto del archivo registrado, no autoría, autenticidad ni ausencia de contenido sensible. Los Markdown activos son la especificación a revisar; los ZIP no reemplazan la documentación legible en el diff.

`validation/historical-v2/` identifica explícitamente la evidencia anterior. No trasladar el resultado «27 passed» al lector regenerado: éste recibe una prueba nueva y un alcance independiente. El avance de P0–P4 continúa en cero implementaciones acreditadas por este paquete.

## 3. Reproducción documental

Desde la raíz del paquete:

```powershell
# Estas dependencias son sólo para herramientas documentales.
# jsonschema es opcional: sin él, validate_pack.py queda parcial (exit 2).
# rfc3339-validator es obligatorio para rechazar date-time inválidos cuando jsonschema está instalado.
# Usar un entorno Python separado de cualquier entorno de TokenUsage.
python -m venv .venv-docs
.\.venv-docs\Scripts\python -m pip install -r tools\requirements-docs.txt
.\.venv-docs\Scripts\python tools\update_manifest.py --check
.\.venv-docs\Scripts\python tools\build_reader.py
.\.venv-docs\Scripts\python tools\build_reader.py --check
.\.venv-docs\Scripts\python tools\validate_pack.py
.\.venv-docs\Scripts\python tools\validate_integration.py
```

`build_reader.py --check` no escribe: falla si el HTML está desactualizado respecto del registro de documentos y los Markdown. La compilación normal es determinista para las versiones documentadas. El renderer no acepta HTML crudo de Markdown. `validate_pack.py` mantiene las pruebas y oráculos del paquete original, con el inventario ampliado. `validate_integration.py` comprueba archivos históricos, sincronización del lector y trazabilidad adicional. No conecta a fuentes de uso ni GitHub.

Después de una modificación documental deliberada, revisar el diff, regenerar el lector y ejecutar `python tools/update_manifest.py` para registrar el nuevo conjunto de fuentes. No regenerar el manifiesto ante una alteración no explicada.

El HTML generado puede abrirse sin CDN ni servidor; el navegador puede restringir algunos enlaces externos o descargas. Esas restricciones deben describirse por separado de las pruebas de navegación. No sustituir una prueba nativa de WinUI por el funcionamiento de este lector.

## 4. Publicación y privacidad

El [workflow Pages de la base](https://github.com/gvastethecreator/tokenusage/blob/df50c367b083e5624bf09213eddc7013d8299aa4/.github/workflows/pages.yml) sube todo el directorio `docs`. Por tanto, al integrar y publicar, también pueden quedar accesibles los prototipos, esquemas y ZIP de este subárbol. Esta entrega los trata como material público y sintético. No publicar aquí información de cuentas, bases personales, secretos, rutas reales ni capturas del equipo del mantenedor.

La documentación propuesta debe conservar «propuesta» y el estado de cada gate. No añadir las nuevas métricas al catálogo de funciones disponibles, notas de release o landing comercial antes de implementar y verificar las fases. Los ZIP históricos son deliberados para cumplir la preservación completa; si se decide retirarlos de Pages, deberá hacerse en una revisión de publicación explícita sin perder su archivo en un destino autorizado.

## 5. Revisión del diff

Verificar que `main` no contenía ya `docs/reports-workbench/` y que el índice base coincide. El PR #62 sí contiene un subárbol parcial: no aplicar el parche original del ZIP sobre esa rama sin revisar el README de importación parcial frente a este índice. Si main cambió, revisar el conflicto; no aplicar con `--reject`, no forzar reemplazos ni borrar archivos para hacer encajar el parche. Revisar también trabajo no confirmado de otras personas.

La documentación del repositorio exige una issue previa. La issue real es #61 y el PR real es #62. No insertar referencias ficticias, no cerrar una issue de implementación con este cambio documental y no tratar las tareas TASK-* como issues existentes. No actualizar esos recursos remotos en esta preparación.

## 6. Reversión

Antes de cualquier implementación del producto, revertir esta integración documental sólo implica retirar el subárbol nuevo y su enlace de índice. No ejecuta downgrade, purga ni migración de datos del usuario. Los cambios de fases posteriores tendrán procedimientos de recuperación propios y no heredan esta reversibilidad.

## 7. Condición de cierre de esta entrega

El contenido puede considerarse preparado localmente al verificar importación íntegra, enlaces, schemas, oráculos y HTML, con el orden del manifiesto independiente de plataforma y las fechas inválidas rechazadas en el entorno documental declarado. La publicación remota del árbol completo queda pendiente hasta que el mantenedor autorice actualizar #61/#62. La existencia de esos recursos con tres archivos no convierte esta preparación local en una publicación completa.
