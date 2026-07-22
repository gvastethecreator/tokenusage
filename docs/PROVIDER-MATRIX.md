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
| GitHub Copilot | API interna; billing org público | Limitado | editor o `gh` | Gate | M9 |
| Antigravity CLI | Bloqueada por política | Condicional, `.db` pasiva | `gen_metadata` local | Experimental + Bloqueado | M6B |
| Devin | RPC privado | Limitado | CLI o app local | Experimental | M9 |

La entrega indica orden, no fecha. Ningún estado `Gate` entra en estable hasta cerrar todos sus controles.

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

- cuota: `account/rateLimits/read`;
- tokens y buckets diarios: `account/usage/read`;
- detalle local opcional: `CODEX_HOME/sessions` y `archived_sessions`.

El [`app-server` oficial](https://github.com/openai/codex/blob/a26f219f6788c951dcb3bf435fab4c6d0f4d2f40/codex-rs/app-server/README.md) gestiona el login y la renovación. La app no lee `auth.json` en el MVP.

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

### Fuente candidata

- sesión del editor;
- `gh` y `hosts.yml`;
- endpoint interno para el usuario;
- API pública de billing de organización para dueños o responsables de facturación.

### Riesgos

- alcance distinto por token;
- cuotas por asiento no disponibles para miembros comunes;
- límite de organización y límite personal con semántica distinta;
- credencial en almacén del editor o Git Credential Manager.

### Salida

Primero soportar solo una fuente oficial documentada. La vista de billing de organización se separa de la cuota personal. El endpoint interno queda bloqueado por gate.

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

El upstream toma credenciales de CLI o app y llama un RPC no público. Se clasifica experimental. No entra en una promesa de paridad hasta validar Windows, política y estabilidad.

Fuente upstream de comparación: [provider Devin](https://github.com/robinebers/openusage/blob/9d2bf09f10e21f769494a525a9d65c84d7aeb1df/docs/providers/devin.md).

## Orden de implementación

1. Codex completo.
2. Motor propio de uso, gasto, precios y cobertura.
3. Claude local.
4. Grok Build y OpenCode local.
5. Spike pasivo Antigravity CLI con una `.db` real.
6. OpenRouter con clave manual.
7. Reabrir Z.ai solo con contrato público o permiso escrito.
8. Cursor Teams y Enterprise con Admin API; Copilot tras su gate.
9. Cuota Claude o Grok tras permiso o interfaz pública.
10. Devin experimental.

Este orden mantiene el objetivo de cuota restante con Codex y permite sumar valor local sin ampliar el manejo de credenciales ajenas.
