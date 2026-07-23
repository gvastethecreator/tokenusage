# Matriz de proveedores

Fecha de corte: 2026-07-21

Upstream: `robinebers/openusage@9d2bf09f10e21f769494a525a9d65c84d7aeb1df`

Referencias de gasto: `getagentseal/codeburn@6e3c57a9ff95a624f1d9affa7384d32a67f359b7` y `kenn-io/agentsview@1ee2de88e2dae54326d8b47aeb2de2f58b5944f9`

## Estados

- `MVP`: ruta técnica y de producto elegida.
- `Local`: se puede publicar una vista basada solo en datos locales.
- `Gate`: falta prueba, contrato público o revisión de uso permitido.
- `Manual`: requiere una clave que el usuario entrega a la app.
- `Experimental`: fuente frágil; sin promesa de soporte.
- `Bloqueado`: la fuente conocida no se puede usar en una build pública.

## Resumen

| Proveedor | Cuota en vivo | Tokens y gasto local | Fuente elegida | Estado | Entrega |
|---|---|---|---|---|---|
| Codex | Sí, interfaz oficial local | Sí, API oficial y logs | `codex app-server` | MVP | M4; detalle M6 |
| Claude | Bloqueada sin interfaz pública | Sí, logs | sesiones Claude Code | Local + Gate | M6; cuota pendiente |
| OpenCode | No hay cuota común | Sí, coste informado y tokens | `opencode.db` y `storage` | Local | M6A |
| Grok Build | Bloqueada sin interfaz pública | Sí, coste informado o estimado | `sessions` y `unified.jsonl` | Local + Gate | M6A; cuota pendiente |
| OpenRouter | Sí, API con clave | Depende de API | clave manual propia | Manual | M9 |
| Z.ai | Bloqueada fuera del plugin oficial | Solo por logs admitidos | plugin oficial limitado a Claude Code | Bloqueado | M9; reabrir con contrato o permiso |
| Cursor | No con el contrato actual | Sí, para equipos | Admin API con clave manual | Manual parcial | M9; Individual bloqueado |
| GitHub Copilot | No con el contrato actual | Sí, personal pagado y organización | Billing API con token manual | Manual parcial | M9; smoke pendiente |
| ZCode | Bloqueada sin API pública | Bloqueados sin esquema local seguro | Sin fuente apta | Bloqueado | M9; Ticket 48 cerrado, Ticket 49 `needs-info` |
| Kimi Code | Bloqueada sin contrato de máquina | Bloqueados por contenido de sesión | Solo detección de versión | Bloqueado | M9; Ticket 50 cerrado, Ticket 51 `needs-info` |
| Command Code | Pendiente de investigación | Pendiente de investigación | Sin elegir | Gate | M9; Tickets 52–53 |
| Cline | Pendiente de investigación | Pendiente de investigación | Sin elegir | Gate | M9; Tickets 54–55 |
| Antigravity CLI | Bloqueada por política | Condicional, `.db` pasiva | `gen_metadata` local | Experimental + Bloqueado | M6B |
| Devin | No para self-serve | ACUs de organización | API v3 con service user manual | Experimental manual | M9; smoke pendiente |

La entrega indica orden, no fecha. Ningún estado `Gate` entra en estable hasta cerrar todos sus controles.

El término de entrada `Zcode` se resuelve como `ZCode`, el producto de
escritorio de ZCode Agent. Kimi Code y Command Code se conservan como términos
de entrada. Sus tickets de investigación deben fijar el producto canónico antes
de añadir IDs, iconos, rutas o claims al código.

## Gate de publicación

Cada proveedor necesita:

- [ ] fuente y precedencia documentadas;
- [ ] prueba Windows con rutas por defecto y variable de entorno;
- [ ] contrato de respuesta fijado con fixtures sanitizados;
- [ ] parser con límites de tamaño, timeout y cancelación;
- [ ] cuenta ausente, vencida, no apta, throttle y cambio de esquema cubiertos;
- [ ] varias cuentas y cambio de cuenta definidos;
- [ ] rotación de credencial sin carrera o sin escritura;
- [ ] logs y caché sin secretos;
- [ ] revisión de términos, política y marca;
- [ ] prueba dentro del MSIX firmado;
- [ ] prueba de regresión contra una versión real soportada;
- [ ] texto de UI que explica fuente, cobertura y límites.
- [ ] coste informado y estimado separados, con modelos sin precio visibles;
- [ ] diferencial de totales contra una referencia sobre el mismo fixture;
- [ ] prueba de que el lector no abre auth, prompt, respuesta, tarea o comando.

## Codex

### Fuente

- estado de cuenta: `account/read` con `refreshToken: false`, seleccionando solo
  tipo, plan y requisito de auth;
- cuota: `account/rateLimits/read`;
- tokens y buckets diarios: `account/usage/read`;
- detalle local opcional: `CODEX_HOME/sessions` y `archived_sessions`.

El [`app-server` oficial](https://github.com/openai/codex/blob/a26f219f6788c951dcb3bf435fab4c6d0f4d2f40/codex-rs/app-server/README.md) gestiona el login y la renovación. La app no lee `auth.json` en el MVP.

La lectura de estado no conserva ni muestra `email`, `codexHome`, token,
respuesta cruda o identificador de cuenta. Una sesión ChatGPT habilita cuota;
API key, Bedrock, modo local o auth futura quedan como cuenta no apta hasta que
un contrato de cuota diga lo contrario.

### Métricas

- ventana primaria;
- ventana secundaria;
- límites adicionales por modelo;
- próximo reinicio;
- plan;
- créditos y control de gasto cuando existan;
- tokens diarios y tendencia;
- gasto estimado por modelo en una fase posterior.

### Límites

- requiere un binario Codex compatible;
- API key sin cuenta ChatGPT puede carecer de cuota de suscripción;
- el método puede entregar límites adicionales nuevos;
- varias cuentas requieren un `CODEX_HOME` y proceso por instancia;
- consumir un crédito de reset queda fuera del MVP por ser una acción irreversible.

### Salida

MVP aprobado después de pruebas de proceso, contrato, paquete y cuenta no apta. La prueba local de investigación completó ambos métodos de lectura.

Fuente upstream de comparación: [provider Codex](https://github.com/robinebers/openusage/blob/9d2bf09f10e21f769494a525a9d65c84d7aeb1df/docs/providers/codex.md).

## Claude

### Fuente local

- `%USERPROFILE%\.claude\projects`;
- ruta equivalente bajo `CLAUDE_CONFIG_DIR`;
- logs de `pi` solo en una fase posterior;
- sesiones no persistidas quedan fuera de cobertura.

Se extraen tokens, modelo, fecha y costo registrado cuando existe. Los prompts y respuestas se omiten.

### Cuota

Claude Code guarda credenciales de Windows en `%USERPROFILE%\.claude\.credentials.json`, según su [documentación de autenticación](https://code.claude.com/docs/en/authentication). No documenta un comando de cuota de solo lectura. La implementación upstream llama un endpoint no público y puede rotar tokens.

La [guía legal de Claude Code](https://code.claude.com/docs/en/legal-and-compliance) limita el uso de OAuth de suscripciones por terceros. La cuota queda bloqueada hasta contar con interfaz pública o permiso. La app no escribe esa credencial.

### Métricas locales

- hoy, ayer y 30 días;
- tokens y tendencia;
- costo medido si el log lo trae;
- costo estimado con cobertura de precios;
- modelos omitidos y motivo.

### Salida

Vista local después del scanner y pruebas de cobertura. Cuota en vivo detrás de gate legal y técnico.

Fuente upstream de comparación: [provider Claude](https://github.com/robinebers/openusage/blob/9d2bf09f10e21f769494a525a9d65c84d7aeb1df/docs/providers/claude.md).

## OpenCode

### Fuente

OpenCode documenta `%USERPROFILE%\.local\share\opencode` en Windows y `~/.local/share/opencode` dentro de WSL. El lector admite `opencode.db` y el almacenamiento JSON `storage`. Omite `auth.json`.

El comando oficial [`opencode stats`](https://opencode.ai/docs/cli/) entrega estadísticas humanas de tokens y coste. Como no ofrece JSON en la versión observada, se usa como oráculo diferencial y no como formato del adaptador.

### Métricas

- uso observado en este equipo;
- tokens por periodo, agente y modelo;
- coste informado por OpenCode cuando exista;
- coste estimado solo para filas sin coste informado;
- tendencia;
- modelos y fuentes con cobertura.

### Límites

El dato local puede omitir otros equipos, sesiones eliminadas e instalaciones WSL. La UI lo llama `Uso local observado`. No afirma cuota restante porque OpenCode puede usar muchos proveedores y planes.

La base se abre en modo de solo lectura con consultas mínimas. No se copia: en la prueba local ocupa cerca de 2,5 GB. La primera beta cubre OpenCode nativo en Windows; cada distro WSL requiere detección y consentimiento aparte.

### Salida

Beta local después de fixtures para SQLite, JSON legado, WAL, modelo sin precio y deduplicación entre formatos. La instalación examinada tiene OpenCode `1.18.4`, `opencode.db`, `storage` y `opencode stats`.

Fuente upstream de comparación: [provider OpenCode](https://github.com/robinebers/openusage/blob/9d2bf09f10e21f769494a525a9d65c84d7aeb1df/docs/providers/opencode.md).

## Grok

### Fuente local

- `GROK_HOME/sessions` con `summary.json`, `signals.json` y `updates.jsonl`;
- `GROK_HOME/logs/unified.jsonl` como fallback;
- `params.update.usage`, desglose por modelo y `costUsdTicks` cuando existan;
- estimación por catálogo solo cuando la fuente no informa coste.

La fuente de sesión tiene prioridad. El fallback no suma eventos ya cubiertos por una sesión.

### Cuota

OpenUsage usa autenticación local y un endpoint de billing no documentado. xAI documenta `/usage` para su producto, pero no una salida de cuota apta para otra app. Su [política de uso aceptable](https://x.ai/legal/acceptable-use-policy) restringe el acceso automatizado. La build pública no lee `auth.json` ni llama el endpoint privado.

### Salida

Tokens y gasto local en beta tras fixtures de versiones y diferencial. Cuota y saldo solo después de una interfaz oficial apta o permiso escrito. La prueba Windows detectó Grok Build `0.2.106`, sesiones y el log unificado sin abrir la credencial.

Fuente upstream de comparación: [provider Grok](https://github.com/robinebers/openusage/blob/9d2bf09f10e21f769494a525a9d65c84d7aeb1df/docs/providers/grok.md).

## OpenRouter

### Fuente

Clave que el usuario agrega a esta app y que se guarda en Credential Locker. No se importa una clave de otra app sin confirmación.

### Métricas

- créditos y saldo;
- uso que entregue la API pública;
- hora y estado de la respuesta.

### Salida

Después de fijar el contrato de la API oficial, probar revocación, permisos mínimos y rate limits. Se marca como configuración manual.

Fuente upstream de comparación: [provider OpenRouter](https://github.com/robinebers/openusage/blob/9d2bf09f10e21f769494a525a9d65c84d7aeb1df/docs/providers/openrouter.md).

## Z.ai

### Fuente evaluada

Z.ai publica `glm-plan-usage`, un plugin de cuota para el plan Personal que se ejecuta dentro de Claude Code. Su repositorio oficial consulta `api.z.ai` para cuentas internacionales y `open.bigmodel.cn` para China. Los endpoints de monitor no aparecen en el OpenAPI general.

La política limita el GLM Coding Plan a herramientas soportadas. Las fuentes no conceden a una app Windows aparte un scope de solo lectura ni permiso para reutilizar esos endpoints.

### Salida

Bloqueado. La build pública no pide una key Z.ai, no invoca el plugin y no copia el cliente upstream. El gasto local de modelos Z.ai puede aparecer mediante logs de agents admitidos, con cobertura y procedencia.

Gate completo: [investigación Z.ai](research/2026-07-21-zai-gate.md).

Fuente upstream de comparación: [provider Z.ai](https://github.com/robinebers/openusage/blob/9d2bf09f10e21f769494a525a9d65c84d7aeb1df/docs/providers/zai.md).

## ZCode

### Fuente evaluada

ZCode es una app de escritorio con ZCode Agent integrado. Su UI muestra
`App Usage` desde registros de sesión locales y `Coding Plan` para la cuota y
el uso remotos de Z.ai o BigModel. La documentación revisada no publica la
ruta o el esquema de los registros, una exportación de métricas ni una API de
lectura para terceros.

La única ruta Windows publicada para datos propios es
`%USERPROFILE%\.zcode\logs`, destinada a soporte. La política confirma que las
conversaciones pueden incluir entradas, contenido generado, archivos, código y
comandos. Los términos prohíben extracción automática o no autorizada de datos.

### Salida

Bloqueado. La build pública no lee `.zcode`, logs, sesiones, configuración,
prompts o credenciales, ni llama endpoints privados. El provider se reabre con
una API pública de solo lectura autorizada o una exportación local documentada,
mínima y libre de contenido de sesión.

Gate completo: [investigación de fuente ZCode](research/2026-07-22-zcode-source-gate.md).

## Kimi Code

### Fuente evaluada

Kimi Code ofrece el CLI `kimi`, una extensión de VS Code, `/usage` dentro de
la TUI y una Console para cuota y Extra Usage. No publica una salida de
máquina, exportación de métricas ni API de solo lectura para cuota, tokens o
gasto de Kimi Code.

En Windows almacena configuración, OAuth, sesiones, logs e historial bajo
`%USERPROFILE%\.kimi-code` o `KIMI_CODE_HOME`. Sus sesiones incluyen
`lastPrompt`, comunicación completa y trazas de requests. La suscripción se
limita al uso interactivo y prohíbe automatización no interactiva.

Kimi Platform publica balance y uso con cuentas y facturación separadas. No se
mezcla con el provider Kimi Code.

### Salida

Bloqueado. La build pública puede detectar `kimi --version` en una fase de
diagnóstico, pero no lee datos, inicia la TUI, usa `kimi web`, toma tokens o
llama la Console. El provider se reabre con una fuente mínima, documentada y
autorizada para terceros.

Gate completo: [investigación de fuente Kimi Code](research/2026-07-22-kimi-code-source-gate.md).

## Cursor

### Fuente elegida

La Admin API pública de Cursor admite métricas, gasto y eventos de uso de Teams y Enterprise. Un administrador crea una clave y la entrega de forma manual. WOpenUsage la guarda en Windows Credential Locker y limita el cliente a `https://api.cursor.com`.

Los endpoints de gasto y eventos no publican el saldo de las dos bolsas de uso incluido que Cursor anunció para Teams en junio de 2026. La tarjeta muestra `Uso y gasto del equipo`, con procedencia y ciclo. No afirma cuota restante.

### Cobertura y política

- Individual: `Unsupported`; no hay una interfaz pública encontrada para uso o gasto de la cuenta.
- Teams: gasto, actividad, tokens y eventos según la Admin API.
- Business: nombre legado que puede aparecer en eventos; usa la semántica de Teams.
- Enterprise: mismo contrato por conexión configurada; varias conexiones no se mezclan por correo.

Quedan prohibidos `state.vscdb`, Credential Manager ajeno, refresh de token, cookies creadas desde JWT, RPC de `api2.cursor.sh`, rutas privadas de dashboard, Stripe y export CSV privado. Una base bloqueada, un export ausente o varias instalaciones no afectan el proveedor porque no se exploran.

Gate resuelto como `implement-subset`. La build pública sigue apagada hasta un smoke autorizado con una clave admin y el borrado posterior de la credencial.

Investigación: [fuente Cursor en Windows](research/2026-07-21-cursor-windows-source.md).

Fuente upstream de comparación: [provider Cursor](https://github.com/robinebers/openusage/blob/9d2bf09f10e21f769494a525a9d65c84d7aeb1df/docs/providers/cursor.md).

## GitHub Copilot

### Fuente elegida

La Billing REST API pública ofrece reportes dedicados de AI credits para una cuenta personal pagada y para una organización. WOpenUsage usa la versión `2026-03-10`, un fine-grained token entregado por el usuario y Windows Credential Locker.

La cuenta personal requiere `Plan: read`. La organización requiere `Administration: read` y un administrador. Cada conexión declara su scope. El resultado muestra créditos usados, descuento cubierto y cargo neto. La vista de organización se etiqueta como total de la entidad.

### Cobertura y política

- Personal pagado: uso y cargo de Pro, Pro+ o Max bajo billing por AI credits.
- Free y Student: `Unsupported` hasta validar una respuesta pública útil.
- Plan anual legado: fuera del primer subset.
- Business o Enterprise: total de organización para administradores; un miembro común recibe `InsufficientPermission`.

La API no devuelve la asignación efectiva o el saldo. La app no calcula cuota restante desde tablas de plan porque la parte flex cambia y los pools de organización dependen de licencias y presupuestos.

Quedan prohibidos `/copilot_internal/user`, identidad de editor simulada, archivos de extensiones, `hosts.yml`, Credential Manager ajeno, cookies y `gh auth`. El provider ignora una sesión existente de editor o GitHub CLI.

Gate resuelto como `implement-subset`. La build pública sigue apagada hasta un smoke autorizado y borrado de la credencial.

Investigación: [fuente GitHub Copilot](research/2026-07-21-copilot-source-gate.md).

Fuente upstream de comparación: [provider Copilot](https://github.com/robinebers/openusage/blob/9d2bf09f10e21f769494a525a9d65c84d7aeb1df/docs/providers/copilot.md).

## Antigravity

### Cuota

Antigravity documenta [`/usage`](https://antigravity.google/docs/cli/commands/usage) y [`/credits`](https://antigravity.google/docs/cli-credits) dentro de su TUI, sin una salida de máquina. Su [FAQ](https://antigravity.google/docs/faq) prohíbe usar el login de Antigravity desde software de terceros. La app no lee Windows Credential Manager, no automatiza el TUI y no llama Cloud Code, el language server o un RPC privado.

### Fuente local permitida

- conversaciones `.db` con `gen_metadata` y tokens por generación;
- apertura SQLite de solo lectura;
- una statusline futura solo si el usuario la instala de forma explícita y entrega datos mínimos.

Se excluyen `.pb` cifrados, descifrado, daemon auxiliar, token, CSRF y transcript. El binario `agy.exe` `1.1.5` está instalado en el equipo examinado, pero aún no existe una raíz de conversaciones CLI para formar fixtures.

### Salida

Spike experimental después de que exista una `.db` real. Puede entregar tokens y coste estimado con cobertura. Cuota y créditos quedan `Bloqueado` mientras rija el contrato actual.

Fuente upstream de comparación: [provider Antigravity](https://github.com/robinebers/openusage/blob/9d2bf09f10e21f769494a525a9d65c84d7aeb1df/docs/providers/antigravity.md).

## Devin

### Fuente elegida

La API v3 pública devuelve consumo diario de una organización. WOpenUsage admite un service user con scope de organización, permiso `ManageBilling`, ID manual y key en Credential Locker. El cliente fija `api.devin.ai` y llama solo `GET /v3/organizations/{org_id}/consumption/daily`.

La tarjeta muestra ACUs y desglose por producto durante un período explícito. No afirma cuota restante o dólares.

### Cobertura y política

- Organización: subset experimental con ACUs diarios y total.
- Self-serve: `Unsupported`; cuota y saldo siguen solo en el dashboard.
- Enterprise: agregado y ACU limits fuera del primer subset por el scope amplio de la key.
- Dedicated deployment: host personalizado fuera del primer subset.

Quedan prohibidos el archivo CLI, SQLite de la app, RPC de `server.codeium.com`, identidad simulada, host tomado de configuración y Session Insights. Este último devuelve ACUs, pero también material de sesión que el motor no necesita.

Gate resuelto como `implement-experimental-subset`. El permiso `ManageBilling` debe quedar acotado a una sola organización y pasar un smoke autorizado antes de activar la build pública.

Investigación: [fuente Devin](research/2026-07-21-devin-source-gate.md).

Fuente upstream de comparación: [provider Devin](https://github.com/robinebers/openusage/blob/9d2bf09f10e21f769494a525a9d65c84d7aeb1df/docs/providers/devin.md).

## Orden de implementación

1. Codex completo.
2. Motor propio de uso, gasto, precios y cobertura.
3. Claude local.
4. Grok Build y OpenCode local.
5. Spike pasivo Antigravity CLI con una `.db` real.
6. OpenRouter con clave manual.
7. Reabrir Z.ai solo con contrato público o permiso escrito.
8. Cursor Teams y Enterprise, y Copilot billing, con claves manuales y smoke autorizado.
9. Cuota Claude o Grok tras permiso o interfaz pública.
10. Devin experimental para ACUs de organización mediante API v3.

Este orden mantiene el objetivo de cuota restante con Codex y permite sumar valor local sin ampliar el manejo de credenciales ajenas.
