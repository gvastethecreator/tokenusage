# Paridad con OpenUsage y base ampliada

Fecha de corte: 2026-08-11

## Dos proyectos homónimos

TokenUsage fija como referencia histórica y de producto a
[`robinebers/openusage`](https://github.com/robinebers/openusage). Esta revisión
usa su etiqueta
[`v0.7.8`](https://github.com/robinebers/openusage/releases/tag/v0.7.8),
en el commit
[`487cc8f19a9a28676f6924aafa48dee79ad7a7f6`](https://github.com/robinebers/openusage/tree/487cc8f19a9a28676f6924aafa48dee79ad7a7f6).
Ese commit era `HEAD` durante la revisión y registra diez tarjetas.

Existe un proyecto diferente llamado OpenUsage.sh:
[`janekbaraniewski/openusage`](https://github.com/janekbaraniewski/openusage).
Su rama `main` estaba en
[`ddc05f24b159bfd1a24bbf641dcfb841410a77ab`](https://github.com/janekbaraniewski/openusage/commit/ddc05f24b159bfd1a24bbf641dcfb841410a77ab).
La última release era
[`v0.24.2`](https://github.com/janekbaraniewski/openusage/releases/tag/v0.24.2),
en el commit `89d33d30c48b9a36b343a0ee4105c0b956385763`.

El primer proyecto define el cierre inmediato de paridad. El segundo sirve como
inventario ampliado para preparar módulos futuros. Sus IDs y fuentes no se deben
mezclar.

## Cierre inmediato de la paridad de diez

La [lista pública](https://github.com/robinebers/openusage/blob/487cc8f19a9a28676f6924aafa48dee79ad7a7f6/README.md#supported-providers)
y el [catálogo ejecutable](https://github.com/robinebers/openusage/blob/487cc8f19a9a28676f6924aafa48dee79ad7a7f6/Sources/OpenUsage/Providers/ProviderCatalog.swift)
coinciden en diez providers.

| ID | Capacidades de OpenUsage | Estado seguro en TokenUsage |
|---|---|---|
| `antigravity` | Cuotas Gemini y no-Gemini de cinco horas y semanales. | **Active, local.** Mantener tokens y gasto local. La cuota remota sigue bloqueada. |
| `claude` | Sesión, semanal, modelos, extra usage y gasto local. | **Implementable ahora, local.** El lector ya existe. No reutilizar OAuth. |
| `codex` | Sesión, semanal, Spark, reset credits, extra usage y gasto local. | **Active.** Mantener `codex app-server` y los lectores locales. |
| `copilot` | AI credits, extra usage, organización, chat y completions. | **Deferred.** Añadir módulo e icono. Implementar después Billing REST API con token manual. |
| `cursor` | Uso total, Auto, API, extra usage, créditos y gasto. | **Active, local parcial.** Mantener la proyección allowlist. |
| `devin` | Cuotas diaria y semanal, más saldo de extra usage. | **Deferred.** Añadir módulo e icono. La futura ruta usa API v3 con service user manual. |
| `grok` | Pool semanal, pay-as-you-go y gasto local. | **Active, local.** La cuota remota sigue bloqueada. |
| `opencode` | Topes Go y gasto local de Go y Zen. | **Active, local.** No presentar los topes observados como saldo oficial. |
| `openrouter` | Créditos, balance, gasto por período y límite de key. | **Manual, implementable ahora.** El cliente ya existe. Faltan Credential Locker, composición, UI y smoke. |
| `zai` | Ventanas de tokens y búsquedas web. | **Blocked.** Añadir identidad y estado. No pedir key ni crear un cliente de endpoints internos. |

Cinco superficies siguen sin cierre: `claude`, `copilot`, `devin`, `openrouter`
y `zai`. Claude ya tiene una entrada deferred, un lector y un icono local.
OpenRouter ya tiene un cliente HTTP. El trabajo nuevo de identidad se concentra
en Copilot, Devin, OpenRouter y Z.ai.

Los recursos de referencia son:

- [`copilot.svg`](https://github.com/robinebers/openusage/blob/487cc8f19a9a28676f6924aafa48dee79ad7a7f6/Sources/OpenUsage/Resources/ProviderIcons/copilot.svg)
- [`devin.svg`](https://github.com/robinebers/openusage/blob/487cc8f19a9a28676f6924aafa48dee79ad7a7f6/Sources/OpenUsage/Resources/ProviderIcons/devin.svg)
- [`openrouter.svg`](https://github.com/robinebers/openusage/blob/487cc8f19a9a28676f6924aafa48dee79ad7a7f6/Sources/OpenUsage/Resources/ProviderIcons/openrouter.svg)
- [`zai.svg`](https://github.com/robinebers/openusage/blob/487cc8f19a9a28676f6924aafa48dee79ad7a7f6/Sources/OpenUsage/Resources/ProviderIcons/zai.svg)

El repositorio contiene las fuentes de cada provider bajo
[`Sources/OpenUsage/Providers`](https://github.com/robinebers/openusage/tree/487cc8f19a9a28676f6924aafa48dee79ad7a7f6/Sources/OpenUsage/Providers).
Su código confirma una frontera importante: varias cuotas usan credenciales
existentes y endpoints privados. TokenUsage no debe copiar esas rutas.

## Base ampliada opcional de 35

El [registro ejecutable de OpenUsage.sh](https://github.com/janekbaraniewski/openusage/blob/ddc05f24b159bfd1a24bbf641dcfb841410a77ab/internal/providers/registry.go#L45-L82)
contiene 35 providers. `docs/providers.md` enumera 17 y está desactualizado. El
registro y los módulos tienen prioridad sobre esa página.

La base ampliada puede registrar las 35 identidades sin activar lectores
inseguros, pero no forma parte del cierre inmediato de diez. Si se adopta en una
fase posterior, el catálogo debe separar cuatro estados:

- `active`: existe una fuente real aprobada.
- `manual`: existe una fuente pública que requiere una credencial entregada por el usuario.
- `deferred`: la identidad y las capacidades existen, pero no hay un lector aprobado.
- `blocked`: la única ruta observada usa cookies, credenciales ajenas o un contrato privado.

Las fuentes locales de agentes pueden contener prompts, respuestas, comandos y
rutas. Su presencia en OpenUsage.sh no autoriza su lectura en TokenUsage.

La conciliación de identidades debe obedecer estas reglas:

- El ID actual `claude` puede declarar `claude_code` como alias de referencia. No debe crear dos tarjetas.
- El ID actual `grok` significa Grok Build. No equivale a `xai`.
- Antigravity no equivale a `gemini_cli` ni a `gemini_api`.
- `moonshot` es la API de Moonshot. No equivale a `kimi_cli`.
- `alibaba_cloud` no equivale a `qwen_cli`.
- Devin pertenece al cierre de diez, aunque OpenUsage.sh no lo registre.

## Providers de API o cuenta

| ID y nombre | Capacidades de OpenUsage | Fuente upstream | Estado seguro en TokenUsage |
|---|---|---|---|
| `openai` · OpenAI | Límites obtenidos de headers. | [`internal/providers/openai`](https://github.com/janekbaraniewski/openusage/tree/ddc05f24b159bfd1a24bbf641dcfb841410a77ab/internal/providers/openai) | **Deferred.** Un monitor independiente no recibe esos headers sin hacer una solicitud. |
| `anthropic` · Anthropic | Límites obtenidos de headers. | [`internal/providers/anthropic`](https://github.com/janekbaraniewski/openusage/tree/ddc05f24b159bfd1a24bbf641dcfb841410a77ab/internal/providers/anthropic) | **Deferred.** No reutilizar claves ni OAuth de Claude Code. |
| `azure_openai` · Azure OpenAI | Límites por recurso y deployment, obtenidos de headers. | [`internal/providers/azure_openai`](https://github.com/janekbaraniewski/openusage/tree/ddc05f24b159bfd1a24bbf641dcfb841410a77ab/internal/providers/azure_openai) | **Deferred.** Necesita configuración manual y un gate de scopes y coste. |
| `alibaba_cloud` · Alibaba Cloud Model Studios | Cuota, créditos, gasto, tokens, rate limits y modelos. | [`internal/providers/alibaba_cloud`](https://github.com/janekbaraniewski/openusage/tree/ddc05f24b159bfd1a24bbf641dcfb841410a77ab/internal/providers/alibaba_cloud) | **Deferred.** Añadir catálogo. Validar contrato público antes del cliente. |
| `openrouter` · OpenRouter | Créditos, balance, gasto, actividad, generaciones y modelos. | [`internal/providers/openrouter`](https://github.com/janekbaraniewski/openusage/tree/ddc05f24b159bfd1a24bbf641dcfb841410a77ab/internal/providers/openrouter) | **Manual, implementable ahora.** El cliente de TokenUsage ya existe. Faltan Credential Locker, composición, UI y smoke autorizado. |
| `perplexity` · Perplexity | Billing, analytics y tier de la consola. | [`internal/providers/perplexity`](https://github.com/janekbaraniewski/openusage/tree/ddc05f24b159bfd1a24bbf641dcfb841410a77ab/internal/providers/perplexity) | **Blocked.** Upstream usa una cookie de navegador. TokenUsage no debe copiarla. |
| `groq` · Groq | Rate limits y límites diarios. | [`internal/providers/groq`](https://github.com/janekbaraniewski/openusage/tree/ddc05f24b159bfd1a24bbf641dcfb841410a77ab/internal/providers/groq) | **Deferred.** No equivale a Grok Build. Requiere un gate propio. |
| `mistral` · Mistral AI | Headers, suscripción y uso. | [`internal/providers/mistral`](https://github.com/janekbaraniewski/openusage/tree/ddc05f24b159bfd1a24bbf641dcfb841410a77ab/internal/providers/mistral) | **Deferred.** Mantener separado de Mistral Vibe. |
| `moonshot` · Moonshot | Balance y datos de cuenta. | [`internal/providers/moonshot`](https://github.com/janekbaraniewski/openusage/tree/ddc05f24b159bfd1a24bbf641dcfb841410a77ab/internal/providers/moonshot) | **Deferred.** No equivale a Kimi CLI. |
| `deepseek` · DeepSeek | Headers y balance. | [`internal/providers/deepseek`](https://github.com/janekbaraniewski/openusage/tree/ddc05f24b159bfd1a24bbf641dcfb841410a77ab/internal/providers/deepseek) | **Deferred.** Añadir shell. Validar la API de billing antes del cliente. |
| `xai` · xAI (Grok) | Headers y datos de API key. | [`internal/providers/xai`](https://github.com/janekbaraniewski/openusage/tree/ddc05f24b159bfd1a24bbf641dcfb841410a77ab/internal/providers/xai) | **Deferred.** No equivale a Grok Build local. |
| `zai` · Z.AI | Modelos, cuotas, créditos y uso por modelo o herramienta. | [`internal/providers/zai`](https://github.com/janekbaraniewski/openusage/tree/ddc05f24b159bfd1a24bbf641dcfb841410a77ab/internal/providers/zai) | **Blocked.** El gate actual de TokenUsage no autoriza los endpoints internos de cuota. |
| `gemini_api` · Google Gemini API | Headers, límites por modelo y estado de autenticación. | [`internal/providers/gemini_api`](https://github.com/janekbaraniewski/openusage/tree/ddc05f24b159bfd1a24bbf641dcfb841410a77ab/internal/providers/gemini_api) | **Deferred.** No equivale a Gemini CLI ni a Antigravity. |

## Herramientas locales o híbridas

| ID y nombre | Capacidades de OpenUsage | Fuente upstream | Estado seguro en TokenUsage |
|---|---|---|---|
| `opencode` · OpenCode | Telemetría local, gasto y modelos. También puede consultar la consola Zen. | [`internal/providers/opencode`](https://github.com/janekbaraniewski/openusage/tree/ddc05f24b159bfd1a24bbf641dcfb841410a77ab/internal/providers/opencode) | **Active, local.** Mantener el lector SQLite actual. Bloquear cookies y credenciales de consola. |
| `gemini_cli` · Gemini CLI | Sesiones locales, tokens, coste y cuota OAuth. | [`internal/providers/gemini_cli`](https://github.com/janekbaraniewski/openusage/tree/ddc05f24b159bfd1a24bbf641dcfb841410a77ab/internal/providers/gemini_cli) | **Deferred.** Preparar módulo. El lector local necesita un gate de contenido y formato. Bloquear OAuth ajeno. |
| `ollama` · Ollama | API local, SQLite, logs, tokens, modelos y nube opcional. | [`internal/providers/ollama`](https://github.com/janekbaraniewski/openusage/tree/ddc05f24b159bfd1a24bbf641dcfb841410a77ab/internal/providers/ollama) | **Implementable ahora, local.** Prioridad alta porque la API local no necesita credenciales. La nube queda deferred. |
| `copilot` · GitHub Copilot | Cuenta, cuota y telemetría local por sesión. | [`internal/providers/copilot`](https://github.com/janekbaraniewski/openusage/tree/ddc05f24b159bfd1a24bbf641dcfb841410a77ab/internal/providers/copilot) | **Deferred.** Preparar módulo. Implementar después la Billing REST API con token manual. Bloquear token del editor y endpoint privado. |
| `cursor` · Cursor IDE | SQLite, CSV, telemetría local y uso remoto. | [`internal/providers/cursor`](https://github.com/janekbaraniewski/openusage/tree/ddc05f24b159bfd1a24bbf641dcfb841410a77ab/internal/providers/cursor) | **Active, local parcial.** Mantener la proyección allowlist actual. Bloquear token, RPC, Stripe y CSV privado. |
| `claude_code` · Claude Code CLI | JSONL, stats, tokens, gasto y cuota remota. | [`internal/providers/claude_code`](https://github.com/janekbaraniewski/openusage/tree/ddc05f24b159bfd1a24bbf641dcfb841410a77ab/internal/providers/claude_code) | **Implementable ahora, local.** El lector ya existe. Mantener la cuota remota bloqueada. |
| `codex` · OpenAI Codex CLI | JSONL, tokens, gasto, límites y créditos. | [`internal/providers/codex`](https://github.com/janekbaraniewski/openusage/tree/ddc05f24b159bfd1a24bbf641dcfb841410a77ab/internal/providers/codex) | **Active.** Conservar `codex app-server` y los lectores locales. No copiar `auth.json` ni endpoints privados. |

## Agentes con almacenamiento local

| ID y nombre | Fuente y datos que OpenUsage lee | Estado seguro en TokenUsage |
|---|---|---|
| `amp` · Amp | [Threads JSON y `ledger.jsonl`](https://github.com/janekbaraniewski/openusage/tree/ddc05f24b159bfd1a24bbf641dcfb841410a77ab/internal/providers/amp). Tokens y coste en créditos. | **Deferred.** El ledger es candidato. Los threads requieren un gate de contenido. |
| `goose` · Goose | [`sessions.db`](https://github.com/janekbaraniewski/openusage/tree/ddc05f24b159bfd1a24bbf641dcfb841410a77ab/internal/providers/goose). Tokens, coste y sesiones. | **Deferred.** No abrir la base hasta fijar una proyección sin conversación. |
| `hermes` · Hermes Agent | [`state.db`](https://github.com/janekbaraniewski/openusage/tree/ddc05f24b159bfd1a24bbf641dcfb841410a77ab/internal/providers/hermes). Tokens, coste y modelos. | **Deferred.** Requiere un gate de esquema y privacidad. |
| `mux` · Mux | [`session-usage.json`](https://github.com/janekbaraniewski/openusage/tree/ddc05f24b159bfd1a24bbf641dcfb841410a77ab/internal/providers/mux). Uso agregado por sesión. | **Deferred.** Buen candidato si el archivo contiene solo métricas. |
| `droid` · Droid | [Settings de sesión y JSONL auxiliar](https://github.com/janekbaraniewski/openusage/tree/ddc05f24b159bfd1a24bbf641dcfb841410a77ab/internal/providers/droid). Tokens y coste. | **Deferred.** Excluir transcript y comandos antes del reader. |
| `crush` · Crush | [`crush.db` por proyecto](https://github.com/janekbaraniewski/openusage/tree/ddc05f24b159bfd1a24bbf641dcfb841410a77ab/internal/providers/crush). Tokens y coste. | **Deferred.** Falta un icono dedicado y un gate de tablas. |
| `roocode` · Roo Code | [Global storage de VS Code y forks compatibles](https://github.com/janekbaraniewski/openusage/tree/ddc05f24b159bfd1a24bbf641dcfb841410a77ab/internal/providers/roocode). Tokens y coste. | **Deferred.** No leer tareas ni mensajes. También hay riesgo de doble conteo. |
| `kilo_code` · Kilo Code | [Formato local compatible con Roo Code](https://github.com/janekbaraniewski/openusage/tree/ddc05f24b159bfd1a24bbf641dcfb841410a77ab/internal/providers/kilocode). Tokens y coste. | **Deferred.** Mantener ID separado. El gate actual no autoriza la base. |
| `kiro_cli` · Kiro CLI | [`data.sqlite3`](https://github.com/janekbaraniewski/openusage/tree/ddc05f24b159bfd1a24bbf641dcfb841410a77ab/internal/providers/kiro). Sesiones y tokens cuando existen. | **Deferred, experimental.** Upstream marca baja confianza y tokens ausentes en algunos datos. |
| `zed` · Zed | [`threads.db`](https://github.com/janekbaraniewski/openusage/tree/ddc05f24b159bfd1a24bbf641dcfb841410a77ab/internal/providers/zed). Tokens por thread. | **Deferred.** La base mezcla métricas y conversación. El gate actual bloquea su lectura. |
| `codebuff` · Codebuff | [`chat-messages.json`](https://github.com/janekbaraniewski/openusage/tree/ddc05f24b159bfd1a24bbf641dcfb841410a77ab/internal/providers/codebuff). Tokens, coste y modelos. | **Deferred.** No leer mensajes. Falta un icono dedicado. |
| `kimi_cli` · Kimi CLI | [`wire.jsonl`](https://github.com/janekbaraniewski/openusage/tree/ddc05f24b159bfd1a24bbf641dcfb841410a77ab/internal/providers/kimi_cli). Tokens y coste. | **Deferred.** No equivale a Moonshot API. El gate actual bloquea sesiones. |
| `openclaw` · OpenClaw | [JSONL por agente y aliases](https://github.com/janekbaraniewski/openusage/tree/ddc05f24b159bfd1a24bbf641dcfb841410a77ab/internal/providers/openclaw). Tokens y coste. | **Deferred.** Requiere deduplicación y una proyección sin contenido. |
| `pi` · Pi | [Sesiones JSONL de Pi y OMP](https://github.com/janekbaraniewski/openusage/tree/ddc05f24b159bfd1a24bbf641dcfb841410a77ab/internal/providers/pi). Tokens, coste y modelos. | **Deferred.** Separar agente y proveedor de modelo antes de contar. |
| `qwen_cli` · Qwen CLI | [Chats JSONL por proyecto](https://github.com/janekbaraniewski/openusage/tree/ddc05f24b159bfd1a24bbf641dcfb841410a77ab/internal/providers/qwen_cli). Tokens y coste. | **Deferred.** No leer chats. Mantener separado de Alibaba Cloud. |

## Iconos de la base ampliada

El manifiesto oficial es
[`internal/tmux/assets/icons.json`](https://github.com/janekbaraniewski/openusage/blob/ddc05f24b159bfd1a24bbf641dcfb841410a77ab/internal/tmux/assets/icons.json#L1-L40).
Declara 32 iconos para 35 providers. Los archivos viven en
[`website/public/icons`](https://github.com/janekbaraniewski/openusage/tree/ddc05f24b159bfd1a24bbf641dcfb841410a77ab/website/public/icons).

| Provider | Recurso upstream |
|---|---|
| `openai` | [`openai.svg`](https://github.com/janekbaraniewski/openusage/blob/ddc05f24b159bfd1a24bbf641dcfb841410a77ab/website/public/icons/openai.svg) |
| `anthropic` | [`anthropic.svg`](https://github.com/janekbaraniewski/openusage/blob/ddc05f24b159bfd1a24bbf641dcfb841410a77ab/website/public/icons/anthropic.svg) |
| `azure_openai` | Sin recurso dedicado. Usa fallback. |
| `alibaba_cloud` | [`alibabacloud.svg`](https://github.com/janekbaraniewski/openusage/blob/ddc05f24b159bfd1a24bbf641dcfb841410a77ab/website/public/icons/alibabacloud.svg) |
| `openrouter` | [`openrouter.svg`](https://github.com/janekbaraniewski/openusage/blob/ddc05f24b159bfd1a24bbf641dcfb841410a77ab/website/public/icons/openrouter.svg) |
| `perplexity` | [`perplexity.svg`](https://github.com/janekbaraniewski/openusage/blob/ddc05f24b159bfd1a24bbf641dcfb841410a77ab/website/public/icons/perplexity.svg) |
| `groq` | [`groq.svg`](https://github.com/janekbaraniewski/openusage/blob/ddc05f24b159bfd1a24bbf641dcfb841410a77ab/website/public/icons/groq.svg) |
| `mistral` | [`mistral.svg`](https://github.com/janekbaraniewski/openusage/blob/ddc05f24b159bfd1a24bbf641dcfb841410a77ab/website/public/icons/mistral.svg) |
| `moonshot` | [`moonshot.svg`](https://github.com/janekbaraniewski/openusage/blob/ddc05f24b159bfd1a24bbf641dcfb841410a77ab/website/public/icons/moonshot.svg) |
| `deepseek` | [`deepseek.svg`](https://github.com/janekbaraniewski/openusage/blob/ddc05f24b159bfd1a24bbf641dcfb841410a77ab/website/public/icons/deepseek.svg) |
| `xai` | [`xai.svg`](https://github.com/janekbaraniewski/openusage/blob/ddc05f24b159bfd1a24bbf641dcfb841410a77ab/website/public/icons/xai.svg) |
| `zai` | [`zai.svg`](https://github.com/janekbaraniewski/openusage/blob/ddc05f24b159bfd1a24bbf641dcfb841410a77ab/website/public/icons/zai.svg) |
| `opencode` | [`opencode.svg`](https://github.com/janekbaraniewski/openusage/blob/ddc05f24b159bfd1a24bbf641dcfb841410a77ab/website/public/icons/opencode.svg) |
| `gemini_api` | [`gemini.svg`](https://github.com/janekbaraniewski/openusage/blob/ddc05f24b159bfd1a24bbf641dcfb841410a77ab/website/public/icons/gemini.svg) |
| `gemini_cli` | [`geminicli.svg`](https://github.com/janekbaraniewski/openusage/blob/ddc05f24b159bfd1a24bbf641dcfb841410a77ab/website/public/icons/geminicli.svg) |
| `ollama` | [`ollama.svg`](https://github.com/janekbaraniewski/openusage/blob/ddc05f24b159bfd1a24bbf641dcfb841410a77ab/website/public/icons/ollama.svg) |
| `copilot` | [`copilot.svg`](https://github.com/janekbaraniewski/openusage/blob/ddc05f24b159bfd1a24bbf641dcfb841410a77ab/website/public/icons/copilot.svg) |
| `cursor` | [`cursor.svg`](https://github.com/janekbaraniewski/openusage/blob/ddc05f24b159bfd1a24bbf641dcfb841410a77ab/website/public/icons/cursor.svg) |
| `claude_code` | [`claude.svg`](https://github.com/janekbaraniewski/openusage/blob/ddc05f24b159bfd1a24bbf641dcfb841410a77ab/website/public/icons/claude.svg) |
| `codex` | [`codex.svg`](https://github.com/janekbaraniewski/openusage/blob/ddc05f24b159bfd1a24bbf641dcfb841410a77ab/website/public/icons/codex.svg) |
| `amp` | [`amp.svg`](https://github.com/janekbaraniewski/openusage/blob/ddc05f24b159bfd1a24bbf641dcfb841410a77ab/website/public/icons/amp.svg) |
| `goose` | [`goose.svg`](https://github.com/janekbaraniewski/openusage/blob/ddc05f24b159bfd1a24bbf641dcfb841410a77ab/website/public/icons/goose.svg) |
| `hermes` | [`hermes.svg`](https://github.com/janekbaraniewski/openusage/blob/ddc05f24b159bfd1a24bbf641dcfb841410a77ab/website/public/icons/hermes.svg) |
| `mux` | [`mux.svg`](https://github.com/janekbaraniewski/openusage/blob/ddc05f24b159bfd1a24bbf641dcfb841410a77ab/website/public/icons/mux.svg) |
| `droid` | [`droid.svg`](https://github.com/janekbaraniewski/openusage/blob/ddc05f24b159bfd1a24bbf641dcfb841410a77ab/website/public/icons/droid.svg) |
| `crush` | Sin recurso dedicado. Usa fallback. |
| `roocode` | [`roocode.svg`](https://github.com/janekbaraniewski/openusage/blob/ddc05f24b159bfd1a24bbf641dcfb841410a77ab/website/public/icons/roocode.svg) |
| `kilo_code` | [`kilocode.svg`](https://github.com/janekbaraniewski/openusage/blob/ddc05f24b159bfd1a24bbf641dcfb841410a77ab/website/public/icons/kilocode.svg) |
| `kiro_cli` | [`kiro.svg`](https://github.com/janekbaraniewski/openusage/blob/ddc05f24b159bfd1a24bbf641dcfb841410a77ab/website/public/icons/kiro.svg) |
| `zed` | [`zed.svg`](https://github.com/janekbaraniewski/openusage/blob/ddc05f24b159bfd1a24bbf641dcfb841410a77ab/website/public/icons/zed.svg) |
| `codebuff` | Sin recurso dedicado. Usa fallback. |
| `kimi_cli` | [`kimi.svg`](https://github.com/janekbaraniewski/openusage/blob/ddc05f24b159bfd1a24bbf641dcfb841410a77ab/website/public/icons/kimi.svg) |
| `openclaw` | [`openclaw.svg`](https://github.com/janekbaraniewski/openusage/blob/ddc05f24b159bfd1a24bbf641dcfb841410a77ab/website/public/icons/openclaw.svg) |
| `pi` | [`pi.svg`](https://github.com/janekbaraniewski/openusage/blob/ddc05f24b159bfd1a24bbf641dcfb841410a77ab/website/public/icons/pi.svg) |
| `qwen_cli` | [`qwen.svg`](https://github.com/janekbaraniewski/openusage/blob/ddc05f24b159bfd1a24bbf641dcfb841410a77ab/website/public/icons/qwen.svg) |

Los SVG de ambos proyectos sirven para confirmar identidad y geometría.
TokenUsage debe guardar procedencia y licencia propias antes de distribuirlos.
Los tres fallbacks necesitan un glyph local explícito para evitar iconos rotos.

## Base recomendada

La primera implementación debe añadir catálogo, no 35 lectores.

1. Cerrar las cinco superficies pendientes de la paridad de diez.
2. Añadir Copilot, Devin, OpenRouter y Z.ai al sistema de iconos.
3. Conservar Claude como una sola identidad con alias `claude_code`.
4. Registrar después los IDs ampliados, sus grupos, capacidades y estados.
5. Añadir los 32 iconos ampliados con un fallback para los tres restantes.
6. Activar solo las fuentes reales aprobadas.
7. Mantener el resto como `deferred` o `blocked`, sin factories de red o disco.
8. Añadir búsqueda y filtros para evitar 35 tabs en la vista compacta.

La vista simulada debe usar el catálogo completo. El demo upstream solo crea
siete providers:
[`cmd/demo/provider.go`](https://github.com/janekbaraniewski/openusage/blob/ddc05f24b159bfd1a24bbf641dcfb841410a77ab/cmd/demo/provider.go).
Por tanto, TokenUsage necesita fixtures propios para los 35.

Los datos simulados deben cumplir estas reglas:

- Mostrar `Datos simulados` en el encabezado y en cada tarjeta.
- No escribir en el almacén durable ni sumar en informes reales.
- No aparecer en capturas o exports sin la marca de simulación.
- Incluir estados con datos, sin datos, error, deferred y blocked.
- Ocultar acciones de conexión para providers bloqueados.

## Verificación realizada

Se leyó el registro, cada módulo de provider, el manifiesto de iconos y el demo
del commit fijado. No se ejecutaron endpoints. No se leyeron cookies,
credenciales o datos de sesión.
