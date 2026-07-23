# Gate de fuente Kimi Code

Fecha de corte: 2026-07-22

Decisión: `block`

## Pregunta

¿Puede TokenUsage integrar Kimi Code en Windows para mostrar cuota, tokens o
gasto sin reutilizar login, credenciales, contenido de sesión o automatización
de la interfaz?

## Respuesta

Todavía no. Kimi Code ofrece el CLI `kimi` y una extensión de VS Code. El
comando interactivo `/usage` muestra tokens, contexto y cuota, y la consola
web muestra cuota y Extra Usage. Las fuentes revisadas no definen una salida de
máquina, exportación de métricas ni API de solo lectura para esos datos.

Los archivos locales contienen credenciales, historial y comunicación completa
del agente. La suscripción se limita al uso interactivo y sus términos prohíben
automatización que simule uso humano sin autorización escrita. TokenUsage no
puede invocar la TUI, usar `kimi web`, copiar su token ni leer `.kimi-code`.

## Identidad y soporte

- ID propuesto para un futuro descriptor: `kimi-code`.
- Nombre visible: `Kimi Code`.
- Publisher y responsable del servicio: `Moonshot AI PTE. LTD.`.
- Cliente principal: Kimi Code CLI, ejecutable `kimi`, distribuido como binario
  y paquete `@moonshot-ai/kimi-code`.
- Versión observada: `0.29.0`, publicada el 2026-07-22.
- Windows: el instalador oficial soporta PowerShell; requiere Git for Windows
  para su entorno Git Bash. La CLI TypeScript es el cliente actual; la variante
  Python queda como heredada.
- Editor: existe una extensión de VS Code, aunque nuevas instalaciones se
  limitan a usuarios de la CLI Python heredada.

## Fuentes primarias

Consultadas el 2026-07-22:

| Fuente | Hecho que respalda |
|---|---|
| [Overview de Kimi Code](https://www.kimi.com/code/docs/en/) | Define Kimi Code, sus productos CLI/VS Code y la separación de Kimi Platform. |
| [Kimi Code CLI en GitHub](https://github.com/MoonshotAI/kimi-code) | Repositorio oficial, distribución, soporte PowerShell y release `0.29.0`. |
| [Changelog del CLI](https://www.kimi.com/code/docs/en/kimi-code-cli/release-notes/changelog.html) | Publica `0.29.0` el 2026-07-22. |
| [Data locations](https://www.kimi.com/code/docs/en/kimi-code-cli/configuration/data-locations.html) | Fija la raíz Windows y el contenido de credenciales, sesiones, logs e historial. |
| [Sessions and context](https://www.kimi.com/code/docs/en/kimi-code-cli/guides/sessions.html) | Describe `wire.jsonl`, `state.json`, exportaciones y su contenido sensible. |
| [`kimi` command](https://www.kimi.com/code/docs/en/kimi-code-cli/reference/kimi-command.html) | Documenta CLI, exportaciones y `kimi web` local con bearer token. |
| [Slash commands](https://www.kimi.com/code/docs/en/kimi-code-cli/reference/slash-commands.html) | Define `/usage` como comando de la TUI; no como subcomando con JSON. |
| [Membership Benefits](https://www.kimi.com/code/docs/en/kimi-code/membership.html) | Explica cuota semanal, ventana de 5 horas, Extra Usage, saldo y consola. |
| [Community Guidelines](https://www.kimi.com/code/docs/en/kimi-code/community-guidelines.html) | Limita la suscripción al uso interactivo y prohíbe automatización no interactiva. |
| [Términos](https://www.kimi.com/user/agreement/modelUse?version=v2) | Identifica Moonshot AI PTE. LTD. y prohíbe automatización que simule uso humano sin autorización escrita. |
| [Kimi API: balance and usage](https://www.kimi.com/help/kimi-api/api-balance-and-usage) | Documenta balance y costes de Kimi Platform, producto separado de Kimi Code. |

## Prueba Windows aislada

La máquina no tenía `kimi` ni `%USERPROFILE%\.kimi-code`. Se instaló
`@moonshot-ai/kimi-code@0.29.0` dentro de
`.snapshots/kimi-code-t50-smoke` con `npm --prefix ... install --ignore-scripts
--no-save`; no se usó el instalador global, OAuth, API key ni datos locales.

`kimi --version` devolvió `0.29.0`. `kimi --help` confirmó los subcomandos
`export`, `provider`, `acp`, `web`, `server`, `login`, `doctor`, `vis`,
`migrate` y `upgrade`; no publica un subcomando de uso/cuota. La prueba no creó
`%USERPROFILE%\.kimi-code`.

## Clasificación de fuentes

| Dato | Fuente observada | Estado para TokenUsage | Decisión |
|---|---|---|---|
| Cuota restante | `/usage` de TUI y Kimi Code Console | Sin contrato de máquina ni permiso para automatizar | Bloqueada |
| Tokens y contexto | `/usage` de TUI | Sin salida estructurada ni exportación de métricas | Bloqueados |
| Gasto Extra Usage | Console y saldo visible | Sin API o exportación Kimi Code para terceros | Bloqueado |
| Datos locales | `sessions`, `wire.jsonl`, `state.json`, logs e historial | Incluyen prompts, respuestas, rutas, comandos y trazas | Bloqueados |
| API key Kimi Code | Endpoint de inferencia | Sin scope de solo lectura; TokenUsage no es un cliente de monitor autorizado | No se admite |
| Kimi Platform | Balance Query API oficial | Producto, cuentas, keys, endpoint y facturación separados | Fuera de este provider |

Kimi Platform puede ser objeto de una investigación manual separada. No se debe
mezclar su balance o gasto con cuota, tokens o Extra Usage de Kimi Code.

## Límite de privacidad y seguridad

TokenUsage puede detectar la presencia y versión de `kimi` con `kimi --version`.
No debe abrir, copiar ni usar:

- `%USERPROFILE%\.kimi-code`, `KIMI_CODE_HOME` ni un escaneo de sus
  subdirectorios;
- `config.toml`, `credentials`, `sessions`, `session_index.jsonl`,
  `user-history`, logs, exports, tareas, planes, MCP, skills o `AGENTS.md`;
- `state.json`, que incluye `lastPrompt`, ni `agents/*/wire.jsonl`, que guarda
  la comunicación completa y trazas de solicitudes;
- OAuth, API keys, bearer tokens de `kimi web`, cookies, Console, claves de
  Kimi Platform o estado de login;
- `kimi web`, ACP, `/usage`, la TUI o una automatización de consola;
- endpoints privados, tráfico observado, formatos deducidos o identidad de
  cliente modificada.

`kimi export` y `/export-md` también quedan fuera: la documentación advierte
que pueden contener código, prompts, comandos, rutas y logs.

## Decisión de producto

- No crear lector local, cliente remoto ni tarjeta de provider Kimi Code en
  esta fase.
- No pedir ni guardar credenciales Kimi Code o Kimi Platform para este
  provider.
- No automatizar `/usage`, el CLI, la web local o la Console.
- La detección de versión puede quedar como diagnóstico futuro sin promesa de
  cuota, tokens o gasto.
- Un formulario genérico de valores que el usuario escriba de forma explícita
  requeriría un ticket propio; no forma un adapter Kimi Code ni está en Ticket
  51.
- Mantener Ticket 51 en `needs-info`.
- Reabrir solo con una API o exportación de solo lectura, documentada para
  terceros, que entregue métricas mínimas sin sesiones ni credenciales y que
  autorice consultas automáticas.

## Revisión independiente

Grok Build revisó esta decisión con `Read` y `Grep` solamente y emitió
`accept`. No usó web, shell ni escritura. La revisión local confirmó que el
gate, la matriz y el plan coinciden. Esta revisión no sustituye las fuentes de
la tabla anterior.

## Incertidumbre restante

Kimi puede publicar una salida estructurada de `/usage`, una API de cuota o una
exportación de métricas. Antes de anunciar soporte, hay que repetir el gate y
probar la versión de Windows vigente con una cuenta de prueba autorizada.
