# Gate de fuente Cline

Fecha de corte: 2026-07-22

Decisión: `block`

## Pregunta

¿Puede TokenUsage integrar Cline en Windows para mostrar cuota, tokens o gasto
sin reutilizar login, credenciales, contenido de tareas ni automatización de la
interfaz?

## Respuesta

Todavía no hay un adapter aprobado. Cline publica una API Enterprise que
enumera lectura de balance, historial de uso, métricas de organización y uso
agregado. Es una candidata remota manual: una persona puede crear su propia API
key y entregarla a TokenUsage de forma explícita. La app nunca debe tomar el
token de sesión de Cline ni buscar la key en variables o archivos existentes.

La documentación no publica el esquema, filtros, paginación, unidades,
semántica de periodo ni respuestas de error de los endpoints de balance y uso.
El enlace OpenAPI anunciado por la documentación devolvió HTTP 404 el
2026-07-22. Tampoco documenta una key de solo lectura: una misma API key sirve
para la API Enterprise y la API de inferencia, que también expone operaciones
de cuenta y de facturación. Sin contrato y sin prueba de permiso, TokenUsage no
puede construir un parser seguro ni prometer cuota o gasto correctos.

Las tareas locales quedan fuera. Cline documenta que cada una conserva la
conversación completa, cambios de código, comandos, decisiones, tokens y
costes. El archivo local de conversación contiene entradas y salidas de
herramientas. Leerlo rompería el límite de privacidad del producto.

## Identidad y soporte

- ID propuesto para un futuro descriptor: `cline`.
- Nombre visible: `Cline`.
- Cliente observado: CLI `cline`, paquete npm `cline`.
- Versión observada: `3.0.46`.
- Windows: CLI Node.js documentada; no se evaluó un provider ni una cuenta.
- Modalidades de facturación que se deben conservar separadas:
  - `Cline (usage-billing)`: créditos Cline de pago por uso;
  - `ClinePass`: suscripción y cuota propias;
  - BYOK: facturación del proveedor de modelo, no de Cline;
  - modelos locales: sin coste de API.

## Fuentes primarias

Consultadas el 2026-07-22:

| Fuente | Hecho que respalda |
|---|---|
| [API Enterprise](https://docs.cline.bot/enterprise-solutions/api-reference) | Base `api.cline.bot`, Bearer auth y endpoints GET para perfil, balance, uso, métricas y uso de organización. También enumera operaciones mutables y de facturación. |
| [Autenticación API](https://docs.cline.bot/api/authentication) | API key manual y token de cuenta gestionado por IDE/CLI; la key también puede gestionarse por API. |
| [Cline usage-billing](https://docs.cline.bot/getting-started/cline-provider) | Separa créditos Cline, ClinePass, el dashboard y View Usage. |
| [Autorización de proveedores](https://docs.cline.bot/getting-started/authorizing-with-cline) | Separa usage-billing, ClinePass, BYOK y modelos locales. |
| [Tareas](https://docs.cline.bot/core-workflows/task-management) | Cada tarea conserva conversación, cambios, comandos, decisiones, tokens, costes y tiempo. El coste mostrado puede diferir de la factura final de BYOK. |
| [Prompt Storage](https://docs.cline.bot/enterprise-solutions/monitoring/prompt-storage) | Fija `~/.cline/data/tasks/<taskId>/api_conversation_history.json` y confirma que las conversaciones contienen entradas y salidas de herramientas. |
| [Referencia CLI](https://docs.cline.bot/cli/cli-reference) | Fija `--data-dir`, `history`, `export`, rutas de configuración y `providers.json`; no publica un subcomando de cuota, saldo, gasto o exportación de métricas. |
| [OpenTelemetry](https://docs.cline.bot/enterprise-solutions/monitoring/opentelemetry) | Es una exportación opcional que la organización configura en su dashboard y collector; TokenUsage no la instala, configura ni lee. |
| [OpenAPI anunciado](https://docs.cline.bot/api-reference/openapi.json) | El enlace devolvió HTTP 404 en la comprobación directa de esta investigación. |

## Prueba Windows aislada

La máquina no tenía `cline` global. Se creó
`.snapshots/cline-t54-smoke`, se aislaron `HOME`, `USERPROFILE`, `APPDATA`,
`LOCALAPPDATA` y `CLINE_DATA_DIR`, y se instaló `cline@latest` sin scripts de
ciclo de vida. No se usó login, API key, `cline auth`, datos del usuario ni una
tarea.

La instalación excedió el límite de 120 segundos del comando, pero el paquete
local y su ejecutable quedaron disponibles. `cline --version --data-dir ...`
devolvió `3.0.46`. `cline --help --data-dir ...` confirmó `--data-dir`,
`--config`, `--key`, `auth` e `history`; no mostró un comando de cuota, saldo,
gasto o exportación de métricas. `cline history --help --data-dir ...` confirmó
que el CLI puede listar, borrar, actualizar y exportar sesiones. Esa superficie
no es una fuente apta porque las sesiones contienen contenido de tarea.

La prueba valida solo la superficie de ayuda y el aislamiento. No valida una
instalación completa, una cuenta, permisos, datos remotos ni esquemas de API.

## Clasificación de fuentes

| Dato | Fuente observada | Estado para TokenUsage | Decisión |
|---|---|---|---|
| Créditos Cline restantes | `GET /api/v1/users/{id}/balance` y balance de organización | Endpoint documentado sin esquema, unidades, scope ni smoke autorizado | Pendiente |
| Uso remoto Cline | `GET /api/v1/users/{id}/usages` y uso de organización | Endpoint documentado sin esquema, filtros, paginación ni smoke autorizado | Pendiente |
| Cuota ClinePass | Settings y View Usage | Sin endpoint público documentado | Bloqueada |
| Tokens y coste de tareas | Tareas locales y UI | Incluyen conversación, archivos, comandos y coste estimado | Bloqueados |
| BYOK | Coste mostrado por tarea | Pertenece al proveedor subyacente y puede diferir de su factura | Fuera de este provider |
| Datos locales | `~/.cline/data`, tareas, sesiones, logs y `providers.json` | Mezclan historial, contenido de tareas, configuraciones y claves | Bloqueados |
| OpenTelemetry | Configuración Enterprise y collector externo | Requiere infraestructura y activación de organización | Fuera de alcance |

Una futura tarjeta Cline solo podría representar la respuesta remota aprobada y
su cuenta activa. No debe sumar costes de BYOK como gasto Cline, inferir cuota
ClinePass ni combinar cuentas personales y de organización.

## Límite de privacidad y seguridad

TokenUsage puede detectar `cline --version` en un diagnóstico futuro. No debe
abrir, copiar ni usar:

- `%USERPROFILE%\.cline`, `CLINE_DATA_DIR`, tareas, sesiones SQLite, logs,
  exports, historial, checkpoints, prompt storage ni directorios de equipo;
- `api_conversation_history.json`, prompts, respuestas, archivos, código,
  comandos, resultados de herramientas, planes, reglas, skills, MCP o estado
  de una tarea;
- `providers.json`, variables `CLINE_API_KEY`, tokens de cuenta, cookies,
  OAuth, Credential Manager ni datos de `cline auth`;
- `history`, `export`, la TUI, dashboard, Settings, View Usage, tráfico
  observado, endpoints deducidos o automatización de interfaz;
- configuración, collector, logs o métricas OpenTelemetry ajenos.

Si se aprueba el cliente remoto, la persona deberá crear una API key propia y
escribirla en la app. TokenUsage la guardará solo en Windows Credential Locker,
la enviará solo a `https://api.cline.bot`, limitará sus llamadas a los `GET`
aprobados y permitirá eliminarla junto con su caché. El riesgo sigue abierto:
la documentación actual no ofrece una key de monitor con permiso de solo
lectura.

## Decisión de producto

- No crear scanner local, parser de tareas, cliente remoto ni tarjeta Cline en
  esta fase.
- No leer ni reutilizar el login de Cline, su token de cuenta o una API key
  encontrada fuera de TokenUsage.
- No pedir una key Cline mientras no exista el contrato de respuesta y una
  prueba explícita de la persona propietaria de la cuenta.
- Mantener Ticket 55 en `needs-info`.
- Reabrir el adapter solo cuando se cumplan todos estos puntos:
  1. esquema público versionado, o fixture sanitizado obtenido con una cuenta
     de prueba autorizada, para perfil mínimo, balance y uso;
  2. semántica documentada de Cline credits, periodos, moneda, unidades,
     paginación, errores y cuentas de organización;
  3. confirmación de permiso: una key de solo lectura publicada o aprobación
     explícita del riesgo de una key amplia creada para TokenUsage;
  4. smoke Windows con key desechable, solo GET, revocación y borrado de
     credencial y caché;
  5. fixtures, estados de cuenta ausente, vencida, sin permiso, throttle y
     cambio de esquema antes de activar la build pública.

## Revisión independiente

Grok Build revisó los documentos del gate en un snapshot aislado con `Read` y
`Grep` solamente y emitió `accept`. Confirmó que créditos Cline, ClinePass,
BYOK y métricas locales conservan límites separados, y que el texto no
certifica el comportamiento de la API en vivo. La revisión local incorporó
fecha y estado de matriz coherentes. Esta revisión no sustituye las fuentes
primarias ni un smoke autorizado.

## Incertidumbre restante

Cline puede restaurar el OpenAPI, publicar schemas y permisos de monitor, o
documentar mejor los endpoints de balance y uso. Antes de anunciar soporte,
hay que repetir el gate con una versión actual del CLI y una cuenta de prueba
autorizada.
