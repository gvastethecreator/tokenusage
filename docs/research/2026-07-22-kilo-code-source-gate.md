# Gate de fuente Kilo Code

Fecha de corte: 2026-07-22

Decisión: `gate`

## Pregunta

¿Puede TokenUsage integrar Kilo Code en Windows para mostrar cuota, tokens o
gasto sin reutilizar login, credenciales, contenido de sesión o automatización
de la interfaz?

## Respuesta

Kilo Code ofrece una superficie candidata: el CLI oficial `kilo stats` muestra
tokens y coste por sesiones, modelos, herramientas y proyecto. La referencia
oficial no publica JSON para ese comando, un esquema versionado ni una garantía
de solo lectura. Por eso no hay adapter aprobado todavía.

La extensión conserva estado local en `kilo.db`, incluidas sesiones e historial.
Ese almacén queda fuera de alcance aunque contenga contadores. TokenUsage no
puede abrirlo, copiarlo, inferir sus tablas ni recorrer las sesiones para
recalcular gasto.

## Identidad y soporte

- ID propuesto para un futuro descriptor: `kilo-code`.
- Nombre visible: `Kilo Code`.
- Clientes cubiertos por este gate: CLI `kilo` y extensiones Kilo Code.
- Cliente observado: `kilo 7.4.15`, ejecutado desde el paquete oficial
  `@kilocode/cli` en un perfil aislado.
- Windows: la documentación publica binario Windows x64 y el CLI por npm.

## Fuentes primarias

Consultadas el 2026-07-22:

| Fuente | Hecho que respalda |
|---|---|
| [Kilo Code CLI](https://kilo.ai/docs/code-with-ai/platforms/cli) | Define el CLI, Windows, sesiones y `kilo stats`. |
| [Referencia CLI](https://kilo.ai/docs/code-with-ai/platforms/cli-reference) | Define los filtros de `kilo stats`; no documenta salida JSON para ese comando. |
| [Troubleshooting de extensiones](https://kilo.ai/docs/getting-started/troubleshooting/troubleshooting-extension) | Identifica `kilo.db` como estado local con sesiones e historial. |
| [Repositorio oficial](https://github.com/Kilo-Org/kilocode) | Confirma el proyecto, el paquete CLI y la distribución Windows. |

## Prueba Windows aislada

Se creó `.snapshots/kilo-t56-smoke`, con `HOME`, `USERPROFILE`, `APPDATA`,
`LOCALAPPDATA`, `XDG_CONFIG_HOME`, `XDG_DATA_HOME`, `KILO_DIR` y caché npm
aislados. `npx --yes --package @kilocode/cli kilo --version` devolvió `7.4.15`.

`kilo stats --help` mostró filtros `--days`, `--tools`, `--models` y
`--project`, sin formato JSON. `kilo stats` devolvió una tabla de cero sesiones,
cero mensajes, cero tokens y coste `$0.00`, sin login, API key ni datos del
perfil real. La prueba no certifica que el comando no escriba estado, que el
formato sea estable o que cubra una cuenta con uso real.

## Clasificación de fuentes

| Dato | Fuente observada | Estado para TokenUsage | Decisión |
|---|---|---|---|
| Cuota restante | Perfil, gateway y equipos de Kilo | No hay endpoint o contrato de cuota de solo lectura para terceros | Bloqueada |
| Tokens y coste agregado | `kilo stats` | Salida humana sin esquema ni garantía de solo lectura | Gate |
| Sesiones y detalle local | `kilo.db` | Incluye sesiones e historial | Bloqueado |
| Exportación de sesiones | `kilo export` | Puede incluir datos de sesión; no es exportación mínima de métricas | Bloqueada |
| API keys y auth | `kilo auth`, gateway y configuración | Credenciales de uso, sin scope de monitor documentado | No se admite |

## Límite de privacidad y seguridad

TokenUsage puede detectar `kilo --version` en un diagnóstico futuro. No debe:

- abrir `kilo.db`, WAL, SHM, configuración, auth, cachés, sesiones o historial;
- usar `kilo export`, `/copy-session`, `/export`, la TUI, Console, web, daemon,
  gateway, perfil, equipos o automatización de interfaz;
- tomar API keys, tokens, cookies, variables de entorno o Credential Manager de
  Kilo Code;
- deducir rutas, tablas o endpoints privados desde archivos locales o tráfico.

## Decisión de producto

- No crear scanner local, parser de tabla ni tarjeta Kilo Code pública ahora.
- No pedir ni guardar credenciales Kilo Code.
- Mantener Ticket 57 en `needs-info`.
- Reabrir el adapter solo si Kilo publica una salida estructurada y agregada, o
  confirma por escrito una invocación de `kilo stats` de solo lectura con un
  contrato estable y apto para terceros.
- La prueba posterior necesita una cuenta de ensayo autorizada, límites de
  proceso, captura de salida sin datos sensibles, fixtures sanitizados y pruebas
  de ausencia, error, formato nuevo y coste desconocido.

## Revisión independiente

Grok Build revisó matriz, plan, tickets y gates con `Read` y `Grep` solamente.
Detectó que la matriz podía presentar `kilo stats` como fuente elegida. La fila
ahora declara que no existe fuente apta y que el comando es solo candidato. La
revisión no certifica fuentes externas ni comportamiento del CLI en vivo.

## Incertidumbre restante

Kilo puede añadir JSON o cambiar la superficie de estadísticas. Antes de
anunciar soporte hay que repetir el gate con la versión Windows vigente y un
contrato de lectura verificable.
