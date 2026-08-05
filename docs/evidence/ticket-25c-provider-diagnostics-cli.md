# Ticket 25C: diagnóstico de providers desde la CLI

Fecha: 2026-07-23

## Resultado

`tokenusage providers` publica el catálogo activo de Claude, Codex, Grok Build y
OpenCode. Cada fila separa capacidad, detección de la fuente y presencia de
datos históricos propios. No anuncia providers bloqueados o futuros.

`tokenusage doctor` publica seis checks cerrados para la caché Codex, el CLI Codex,
las tres fuentes locales y `usage.v1.db`. Ningún resultado incluye rutas,
cuentas, credenciales, texto de errores o contenido del cliente.

Los contratos JSON son `tokenusage.providers.v1` y `tokenusage.doctor.v1`. Una salida
válida usa código 0, argumentos inválidos usan 2 y un fallo total de lectura o
contrato usa 4. La cancelación se propaga al host.

## Límites de solo lectura

- Codex usa `DetectAsync`; este corte nunca llama `CreateAsync` ni inicia el CLI.
- Claude, Grok Build y OpenCode detectan solo la raíz permitida. No abren auth,
  sesiones o contenido para producir el diagnóstico.
- La SQLite propia abre en modo `ReadOnly`, caché privada y `query_only`. No crea
  la base, no migra el esquema y bloquea cada mutador. SQLite puede coordinar
  lecturas mediante sus sidecars WAL/SHM; el lector no cambia filas, esquema,
  caché o configuración.
- `SnapshotStore.ProbeProviderAsync` valida que exista un snapshot Codex sin
  poner archivos dañados en cuarentena y sin reemplazarlos. Una caché válida
  vacía o con otro provider se informa como ausente.

La detección de fuente y los datos históricos propios son estados distintos.
Una fuente ausente puede conservar datos ya importados; una fuente detectada no
implica que exista uso importado.

## Prueba

- Los goldens fijan orden, nombres, estados y versiones de ambos contratos.
- Tests de cultura `es-ES` prueban que la salida humana conserva formato ordinal.
- Tests hostiles inyectan rutas, correo y tokens en fallos; stdout y stderr no
  los publican.
- Procesos CLI reales prueban ambos comandos con un ejecutable Codex trampa. El
  marcador no se crea y el directorio de datos temporal sigue ausente.
- Tests Core conservan bytes y fecha de la DB y la caché, no migran esquemas
  viejos, no ponen una caché dañada en cuarentena y prueban que un lector abierto
  antes de otro commit observa los datos nuevos.
- Tests de providers usan archivos trampa bloqueados para demostrar que la
  detección de raíz no abre su contenido.

Gates finales x64:

- Core: 70/70.
- Providers: 170/170.
- CLI: 80/80.
- Arquitectura: 59/59.
- Plataforma Windows: 58/58.
- Solución Release x64: 0 warnings, 0 errores.

## Revisión

Grok Build revisó el contrato antes del corte. Fijó el catálogo activo, los
estados cerrados, el uso exclusivo de `DetectAsync` para Codex y el límite de
solo lectura. Coste informado por el runner: USD 0.3299292.

La primera revisión final amplia de Grok llegó al límite de 12 turnos y su
resultado contradictorio se descartó; coste informado: USD 0.4112372. Un
segundo corte limitado a las tres reparaciones terminó con `ACCEPT`; coste:
USD 0.0692752.

Una revisión independiente encontró que `immutable=1` podía dejar al lector con
una vista vieja cuando la app empezaba a escribir. Se eliminó ese modo y se
añadió una prueba donde el lector observa un commit posterior. La revisión
también pidió guards explícitos en mutadores y pruebas aisladas de detección;
ambas correcciones forman parte del corte.

La revisión independiente final encontró dos P2 adicionales: enums fuera de
rango podían serializarse y una caché válida sin snapshot Codex se marcaba como
presente. El validador ahora rechaza estados no definidos y el probe exige el
provider solicitado. Grok y el revisor independiente aceptaron ambas
correcciones con tests focales 19/19 y 26/26.

## Límite pendiente

El catálogo de diagnóstico duplica por ahora la composición activa de la app y
un validador rechaza IDs extra. Un registro compartido queda para un corte
posterior. Ticket 25D cubre concurrencia app/CLI y el alias del paquete.
