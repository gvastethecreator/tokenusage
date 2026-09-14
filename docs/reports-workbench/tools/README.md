# Herramientas documentales

Estos scripts no pertenecen al producto ni a su CI. No leen bases de proveedores, no crean recursos GitHub y no ejecutan la aplicación Windows.

- `build_reader.py`: genera el lector desde Markdown; `--check` comprueba sincronización sin escribir. Requiere `markdown-it-py`.
- `validate_pack.py`: pruebas originales del paquete, inventario vigente, JSON Schema y oráculos sintéticos. Si falta `jsonschema`, devuelve estado parcial y exit code 2. Si `jsonschema` está instalado, `rfc3339-validator` es obligatorio; el validador rechaza fechas de calendario inválidas y falla con un mensaje explícito si el comprobador `date-time` no está disponible.
- `update_manifest.py --check`: comprueba integridad de fuentes sin escribir. Ordena por la ruta POSIX relativa en texto. Regenerar sin `--check` sólo después de revisar cambios documentales deliberados; no usarlo para ocultar una discrepancia inesperada. Los informes de validación corrientes se excluyen de hashes porque se regeneran; los históricos se conservan con hashes. El manifiesto hashea bytes crudos; `.gitattributes` del paquete fija `eol=lf` para ese texto y deja ZIP e imágenes como binarios.
- `validate_integration.py`: hashes de originales, completitud de fuentes, trazabilidad adicional y sincronización del lector. Escribe su resultado sólo en `validation/`.

Las versiones comprobadas están en `requirements-docs.txt`. Instalar dentro de `.venv-docs`, no en la aplicación. Un informe «passed» de estos scripts no certifica C#, WinUI, proveedores, migraciones, seguridad de borrado ni binarios nativos.

Los browser checks de la revisión del lector se ejecutan externamente. No se añade Playwright como dependencia ni runner a TokenUsage. Los scripts de paquetes archivados permanecen dentro de los ZIP históricos y no forman parte de la ejecución activa.
