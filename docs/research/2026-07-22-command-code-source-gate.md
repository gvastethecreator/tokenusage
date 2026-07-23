# Gate de fuente Command Code

Fecha de corte: 2026-07-22

Decisión: `block`

## Pregunta

¿Puede TokenUsage integrar Command Code en Windows para mostrar cuota, tokens o
gasto sin reutilizar login, credenciales, contenido de sesión o automatización
de la interfaz?

## Respuesta

Todavía no. Command Code muestra los límites de crédito y la cuota en `/usage`
dentro de la sesión interactiva. Su Studio muestra uso, tokens, coste e
historial por solicitud tras autenticarse. Las fuentes revisadas no definen una
API de lectura para cuota, saldo, gasto o historial de Command Code, ni una
exportación de métricas apta para terceros.

El JSON de `--output-format` corresponde a la respuesta de `cmd -p`; no cubre
`/usage`. La Provider API ofrece inferencia y listado de modelos con la misma
API key de la CLI. No publica un endpoint de saldo, cuota o historial, y no
convierte esa key en una credencial de monitor de solo lectura.

Los archivos locales mezclan conversaciones, credenciales y preferencias. La
build pública no puede invocar `/usage`, automatizar Studio, usar una API key ni
leer `.commandcode` o `~/.commandcode`.

## Identidad y soporte

- ID propuesto para un futuro descriptor: `command-code`.
- Nombre visible: `Command Code`.
- Publisher y responsable del servicio: `Langbase, Inc. d/b/a Command Code`.
- Cliente principal: CLI `command-code`, distribuido por npm como
  `command-code`; en Windows nativo se invoca `cmdc` porque `cmd` es un comando
  reservado del sistema.
- Versión observada: `1.0.1`.
- Windows: soporte nativo alpha en PowerShell, Windows Terminal y Git Bash;
  WSL es la vía recomendada por el editor.
- Editor: la documentación presenta integración de IDE, sin contrato de uso o
  gasto independiente de Studio y de la CLI.

## Fuentes primarias

Consultadas el 2026-07-22:

| Fuente | Hecho que respalda |
|---|---|
| [Documentación principal](https://commandcode.ai/docs) | Identifica Command Code y su CLI oficial. |
| [CLI Reference](https://commandcode.ai/docs/reference/cli) | Fija `cmd`, sesiones, `--output-format` para `-p`, subcomandos y comandos interactivos. |
| [Windows Guide](https://commandcode.ai/docs/troubleshooting/windows) | Fija `cmdc` para Windows nativo, que sigue en alpha, y recomienda WSL. |
| [Usage Limits](https://commandcode.ai/docs/resources/usage-limits) | Define `/usage`, balance y límites de 5 horas y semanales dentro del CLI. |
| [Pricing & Limits](https://commandcode.ai/docs/resources/pricing-limits) | Sitúa el historial de solicitudes y los costes en Studio. |
| [Studio](https://commandcode.ai/docs/studio) | Confirma los datos por solicitud y que API keys se obtienen tras login. |
| [Provider API](https://commandcode.ai/docs/provider) | Enumera solo inferencia y modelos; la misma key autentica CLI y API. |
| [Security & Privacy](https://commandcode.ai/docs/resources/security) | Documenta `auth.json`, conversaciones locales y `.commandcode/taste/`. |
| [Privacy Policy](https://commandcode.ai/privacy) | Identifica Langbase, Inc. y clasifica prompts, salidas, metadatos y Taste. |
| [Terms of Service](https://commandcode.ai/terms) | Confirma el servicio, su cuenta y las obligaciones de credenciales. |
| [Repositorio oficial](https://github.com/CommandCodeAI/command-code) | Confirma el proyecto público, npm y el flujo de CLI. |

## Prueba Windows aislada

La máquina no tenía `cmdc` global. Se instaló `command-code@latest` sin scripts
de ciclo de vida dentro de `.snapshots/command-code-t52-smoke`. El paquete
resuelto fue `1.0.1`.

La prueba usó un perfil temporal bajo ese mismo snapshot, sin login, API key ni
datos del usuario. `cmdc --version --no-auto-update` devolvió `1.0.1`. `cmdc
--help --no-auto-update` confirmó `cmdc`, `/usage`, `/session-file`, sesiones
`.jsonl` y `--output-format json` solo para `-p`; no mostró un subcomando de
cuota, uso, saldo, facturación o exportación de métricas. Incluso la ayuda creó
un perfil `.commandcode` dentro del perfil temporal, por lo que futuras pruebas
deben mantener el aislamiento.

## Clasificación de fuentes

| Dato | Fuente observada | Estado para TokenUsage | Decisión |
|---|---|---|---|
| Cuota restante | `/usage` interactivo | Sin contrato de máquina | Bloqueada |
| Créditos y límites | `/usage` | Solo sesión interactiva; sin JSON de métricas | Bloqueados |
| Tokens y gasto | Studio Usage | Interfaz autenticada; sin API o exportación pública | Bloqueados |
| Provider API | Respuestas de inferencia por petición | No expone estado ni historial de la cuenta; requiere key de uso | Fuera de este provider |
| Datos locales | `projects`, sesiones `.jsonl`, `auth.json` y Taste | Mezclan prompts, respuestas, credenciales y reglas | Bloqueados |

Una integración futura del Provider API, si se autoriza, sería un producto
distinto de Command Code: mediría solo solicitudes que TokenUsage instrumente,
no el consumo existente del agente ni sus límites de suscripción.

## Límite de privacidad y seguridad

TokenUsage puede detectar la presencia y versión de `cmdc` con `cmdc --version`
en un diagnóstico futuro. No debe abrir, copiar ni usar:

- `~/.commandcode/projects/`, sesiones `.jsonl`, checkpoints, exports,
  conversaciones, prompts, respuestas, adjuntos, archivos, código o comandos;
- `~/.commandcode/auth.json`, `COMMAND_CODE_API_KEY`, cookies, OAuth, perfiles
  de Studio, tokens de MCP ni estado de login;
- `.commandcode/taste/`, `AGENTS.md`, skills, mods, MCP, memoria, planes o
  reglas de proyecto;
- `/usage`, `/session-file`, `/export`, la TUI, Studio, tráfico observado o
  endpoints privados;
- Provider API, porque la key no tiene un scope de monitor de solo lectura y
  sus datos no cubren el uso existente de Command Code.

## Decisión de producto

- No crear lector local, cliente remoto ni tarjeta de provider Command Code en
  esta fase.
- No pedir ni guardar credenciales Command Code o Provider API para este
  provider.
- No automatizar `/usage`, el CLI, Studio o la Provider API.
- La detección de versión puede quedar como diagnóstico futuro sin promesa de
  cuota, tokens o gasto; debe respetar el estado alpha de Windows nativo.
- Un formulario genérico de valores escritos por el usuario requeriría un
  ticket propio; no forma un adapter Command Code ni está en Ticket 53.
- Mantener Ticket 53 en `needs-info`.
- Reabrir solo con una API o exportación de solo lectura, documentada para
  terceros, que entregue métricas mínimas sin sesiones ni credenciales y que
  autorice consultas automáticas.

## Revisión independiente

Grok Build revisó esta decisión con `Read` y `Grep` solamente y emitió
`accept`. No usó web, shell ni escritura. La revisión local confirmó que el
gate, la matriz y el plan coinciden. Esta revisión no sustituye las fuentes de
la tabla anterior.

## Incertidumbre restante

Command Code puede publicar una API de cuenta, un export de métricas o un
subcomando estructurado de `/usage`. Antes de anunciar soporte, hay que repetir
el gate y probar la versión de Windows vigente con una cuenta de prueba
autorizada.
