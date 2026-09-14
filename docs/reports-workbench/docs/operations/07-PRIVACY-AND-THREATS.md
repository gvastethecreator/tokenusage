# Privacidad y seguridad · contrato de acceso y amenazas

**Estado:** especificación técnica, no revisión legal. **Base:** política y contribución actuales TU-11/TU-16; P3 exige aprobación explícita de su ampliación. [Fuentes](../references/10-SOURCES.md).

## 1. Frontera de confianza

Los archivos, bases y respuestas de un proveedor son entradas no confiables para TokenUsage aunque estén en la misma PC. Pueden estar incompletos, haber cambiado de formato o contener texto sensible junto a números. El lector debe proyectar sólo campos admitidos y validar límites antes de construir objetos persistibles.

El pipeline es allowlist: lectura mínima → validación de forma/unidades → objeto numérico/metadata permitida → persistencia propia → DTO de informe → exportador. No deserializar y guardar un objeto de proveedor entero para «decidir después qué mostrar». Un campo oculto de HTML o un JSON de debug también es almacenamiento/salida.

## 2. Matriz de campos

| Clase de dato | Base P0–P2 | P3 | Exportación |
|---|---|---|---|
| Contadores y costos normalizados | Permitidos por fuente admitida. | Igual. | Según selección y evidencia. |
| Modelo, esfuerzo, tier allowlist | Permitidos cuando la fuente los expone. | Igual. | Permitidos con semántica. |
| Timestamp/precisión/procedencia | Permitidos dentro del contrato. | Igual. | Sin rutas o IDs nativos. |
| Clave técnica opaca de registro | Sólo lo necesario para identidad actual. | Igual, con revisión de vínculo. | Omitir o pseudónimo específico de export según necesidad. |
| Sesión/proyecto vinculables | No habilitarlos como capacidad nueva. | Sólo opt-in por fuente. | Desactivado por defecto o claves efímeras del export. |
| Alias elegidos por el usuario | No necesarios. | Almacén local separado. | Consentimiento separado. |
| Ruta completa / ID nativo | No en DB analítica, logs ni exports. | Sólo lectura transitoria específicamente aprobada si imprescindible. | Nunca por defecto; la propuesta no exporta rutas. |
| Prompts, respuestas, conversación, comandos, herramientas, código | Prohibidos en el alcance. | El permiso P3 no los habilita. | Prohibidos. |
| Credenciales/session tokens de otro producto | Prohibidos. | Prohibidos. | Prohibidos. |

La configuración técnica para localizar una fuente debe usar el mecanismo actual admitido y no duplicar rutas en cada evento. Un dato que sólo se necesita para una operación no se convierte automáticamente en una nueva columna.

## 3. Amenazas y controles

| Amenaza | Entrada | Control | Prueba |
|---|---|---|---|
| Filtración por parser | JSON mixto con texto. | Proyección allowlist y descarte sin logging. | Canarios en campos ignorados. |
| Filtración por excepción | Ruta/consulta/payload en mensaje. | Errores tipados y mensajes sanitizados. | Fallo de parse/IO inducido. |
| Inyección SQL | Filtros/aliases. | Parámetros; dimensión allowlist. | Cadenas con sintaxis y Unicode. |
| HTML activo | Alias o metadato en export. | Escape de texto/atributos; sin HTML crudo. | Script, atributos, URLs y cierres de tags. |
| Fórmula CSV | Alias/texto. | Excluir por defecto; política de destino y pruebas. | Prefijos peligrosos, separadores, controles y variantes. |
| Denegación de servicio | Archivos grandes/profundos. | Budgets de bytes, filas, profundidad y tiempo. | Casos en límites y por encima. |
| Carrera de permiso | Revocar durante ingesta/export. | Época y barrera antes de commit/publicación. | Interleavings deterministas. |
| Reexposición tras restore | Backup con enlaces borrados. | Journal de privacidad vigente fuera del backup restaurado. | Restore posterior a purge. |
| Doble conteo/mala autoridad | Raíces equivalentes o snapshot parcial. | Identidad y partición autoritativa. | Multi-root y lecturas parciales. |

## 4. Logs y diagnóstico

Sólo códigos y valores mínimos: fase, tipo de fuente, versión de lector, conteos, duración y motivo allowlist. No volcar argumentos CLI arbitrarios, SQL con valores, stack que incluya ruta privada ni objeto de excepción completo. Un modo verbose no anula la política.

El diagnóstico exportable tiene su DTO propio. Una vista local detallada puede informar que una carpeta configurada no es accesible, pero la copia para soporte debe eliminar esa ruta. Documentar cualquier diferencia entre diagnóstico local y exportado, sin fingir que el usuario revisará cada secreto manualmente como única defensa.

Fixtures y capturas usan valores ficticios o sanitizados. Ningún archivo de usuario se incluye en este paquete. Los ejemplos matemáticos no prueban permisos de una fuente real.

## 5. Secretos propios y pseudónimos

Los secretos HMAC P3 pertenecen a TokenUsage, no a un proveedor. Generarlos con RNG del sistema y almacenarlos con el mecanismo Windows aprobado tras revisar la distribución. No escribirlos en settings sin protección, logs o exports. Un alias es dato sensible potencial incluso si el ID subyacente es opaco.

No prometer anonimato global: el patrón de actividad puede identificar proyectos o personas. Para compartir informes, usar por defecto agregados sin aliases ni claves persistentes; si hace falta correlación dentro de un export, usar IDs efímeros válidos sólo dentro de él, no correlacionables entre archivos por defecto.

## 6. Permiso, borrado y backups

Seguir la máquina de P3. Revocar impide nuevas asociaciones antes de iniciar purge. El borrado incluye cache, snapshots y backups propios afectados, o los bloquea hasta su tratamiento seguro. Una restauración no debe retroceder el estado de consentimiento; el journal de revocación vigente tiene precedencia.

No se garantiza retirar archivos que el usuario compartió, copias externas, capturas o rastros físicos del sistema. Esa limitación debe estar en la UI de borrado y export, no sólo en un documento para desarrolladores. No borrar jamás archivos del proveedor al eliminar datos de TokenUsage.

## 7. Red y fuentes externas

Estas fases no requieren enviar uso a un servicio ni instalar un servidor local de red. Los catálogos existentes y sus reglas de actualización continúan separados. Abrir Reports no debe contactar páginas de precios ni endpoints de cuenta por accidente.

Un nuevo proveedor/endpoint o runtime local se evalúa como integración distinta. «Localhost» no vuelve seguro cualquier permiso ni autoriza loggear prompts. Un collector de inferencia local debe aceptar sólo los números admitidos; no capturar todo el cuerpo de una respuesta para extraerlos después.

## 8. Límites de datos no confiables

Limitar tamaño de nombre/alias, profundidad de datos, cantidad de filas y cadenas. Normalizar controles de texto donde corresponda sin convertir el ID del modelo en otro distinto. IDs desconocidos no son URLs ni rutas a abrir automáticamente. Los enlaces de documentación de proveedor proceden de catálogo allowlist, no del contenido de logs.

Exportar desde DTO tipados; no pasar diccionarios arbitrarios al template. Schema de import debe tener tamaño y `$ref` locales conocidos, sin resolución remota. Firma/fingerprint no autentica por sí solo un archivo externo.

## 9. Aprobación de una fuente

Checklist de gate: producto/versión, forma de datos, contrato admitido, campos leídos/excluidos, root discovery, alcance, contadores, costos, límites, permisos, schema changes, ausencia de cuenta, identidad, duplicados y evidencia Windows. La documentación de un proyecto tercero no sustituye ese gate.

En P3 añadir consentimiento, pseudonimización, purge y export. Si sólo es posible atribuir leyendo contenido prohibido, no habilitar esa capacidad. Se puede entregar la fuente con tokens/costos y atribución no disponible.

## 10. Incidentes

Una filtración se trata como bloqueante: desactivar la capacidad afectada, impedir nuevos exports, preservar únicamente diagnóstico mínimo seguro y seguir el canal privado de seguridad del repositorio. No publicar un issue con el payload filtrado. Documentar alcance y medidas sin propagar los datos. Una corrección de logs no basta si el dato ya quedó en snapshots/backups propios.
