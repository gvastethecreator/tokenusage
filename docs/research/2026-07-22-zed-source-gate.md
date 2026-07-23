# Gate de fuente Zed

Fecha de corte: 2026-07-22

Decisión: `block`

## Pregunta

¿Puede TokenUsage integrar Zed en Windows para mostrar cuota, tokens o gasto
sin reutilizar login, credenciales, contenido de hilo o automatización de la
interfaz?

## Respuesta

Todavía no. Zed muestra tokens para el hilo activo del agente nativo dentro del
Agent Panel. Los agentes externos y los hilos de terminal conservan su propia
autenticación, y la documentación advierte que la disponibilidad de tokens y
restauración varía según la integración.

El código oficial persiste en el mismo `DbThread` los mensajes, resultados de
herramientas, modelo, uso acumulado y uso por solicitud. Luego comprime el
objeto y lo guarda en `threads.db`. Un lector local tendría que descomprimir
datos que incluyen prompts, respuestas y herramientas para alcanzar los
contadores. Esa ruta cruza el límite de privacidad de TokenUsage.

No se encontró una API, CLI o exportación pública de métricas agregadas que una
app de terceros pueda consultar.

## Identidad y soporte

- ID propuesto para un futuro descriptor: `zed`.
- Nombre visible: `Zed`.
- Alcance evaluado: Zed Agent nativo.
- Fuera de este descriptor: agentes externos ACP y Terminal Threads; su uso
  sigue perteneciendo al agente o proveedor que ejecutan.
- El CLI `zed` no está instalado en esta máquina; no se instaló ni se inició el
  editor durante el gate.

## Fuentes primarias

Consultadas el 2026-07-22:

| Fuente | Hecho que respalda |
|---|---|
| [Agents](https://zed.dev/docs/ai/agents) | Distingue Zed Agent, agentes externos y Terminal Threads. |
| [Agent Panel](https://zed.dev/docs/ai/agent-panel) | Muestra tokens del hilo activo y advierte diferencias para agentes externos. |
| [API access](https://zed.dev/docs/ai/use-api-access) | Separa las credenciales de modelos de Zed Agent de los agentes externos y de terminal. |
| [Código de base de hilos](https://github.com/zed-industries/zed/blob/aba12fc8a0fe44a0742acc0d096e843d07385962/crates/agent/src/db.rs) | SHA consultado durante el gate; define `DbThread`, sus mensajes y contadores; comprime el blob y crea `threads.db`. |

## Prueba Windows mínima

`Get-Command zed` no encontró el CLI en la máquina. La prueba no instala Zed,
no crea hilos, no abre el panel y no examina directorios de datos del usuario.
La decisión se apoya en las fuentes primarias y en el límite de datos del
producto.

## Clasificación de fuentes

| Dato | Fuente observada | Estado para TokenUsage | Decisión |
|---|---|---|---|
| Cuota restante | Agent Panel y proveedor del modelo | Sin API de cuota Zed para terceros | Bloqueada |
| Tokens del Zed Agent | Indicador del hilo y `threads.db` | El almacén incluye transcripción y herramientas | Bloqueados |
| Coste | Cuenta del proveedor de modelo o Zed-hosted models | Pertenece al proveedor configurado; no hay exportación Zed agregada | Bloqueado |
| Agentes externos y terminal | ACP o CLI propio | Deben atribuirse al proveedor que los ejecuta | Fuera de este provider |
| API keys | Keychain o variables por proveedor | No son credenciales de monitor y no se reutilizan | No se admite |

## Límite de privacidad y seguridad

TokenUsage no debe:

- abrir, copiar, consultar o descomprimir `threads.db`, sus WAL/SHM ni una base
  de hilos futura;
- leer mensajes, resúmenes, títulos, rutas, resultados de herramientas,
  sandbox grants, configuración, settings, keychain o variables de proveedor;
- automatizar Agent Panel, Threads Sidebar, Terminal Threads, agentes ACP,
  feedback, dashboard o exportación Markdown;
- tomar keys de Anthropic, OpenAI, Google, xAI, OpenCode u otro proveedor que
  Zed use para un hilo;
- sumar uso de un agente externo como uso nativo de Zed.

## Decisión de producto

- No crear código, descriptor, scanner local, cliente remoto ni tarjeta Zed en
  esta fase.
- Mantener Ticket 59 en `needs-info`.
- Reabrir solo cuando Zed publique una API o exportación de solo lectura,
  agregada, mínima y autorizada para terceros, con unidades y cobertura claras.
- Una futura integración debe separar el agente nativo de los agentes externos
  y conservar el coste bajo el proveedor de modelo cuando corresponda.

## Revisión independiente

Grok Build revisó matriz, plan, tickets y gates con `Read` y `Grep` solamente.
Confirmó la separación entre Zed Agent, agentes externos y Terminal Threads, y
no encontró una ruta aprobada que lea hilos o credenciales. La revisión no
certifica fuentes externas ni comportamiento del editor en vivo.

## Incertidumbre restante

Zed puede publicar métricas de cuenta, exportación agregada o cambios de
persistencia. Antes de anunciar soporte hay que repetir el gate con la versión
de Windows vigente y un fixture aprobado que no contenga contenido de hilo.
