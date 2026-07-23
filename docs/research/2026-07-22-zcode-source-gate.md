# Gate de fuente ZCode

Fecha de corte: 2026-07-22

Decisión: `block`

## Pregunta

¿Puede TokenUsage integrar ZCode en Windows para mostrar cuota restante, uso
local o gasto sin reutilizar login, credenciales o contenido de sesión?

## Respuesta

Todavía no. ZCode muestra ambos grupos de datos dentro de su app: `App Usage`
para registros locales de sesión y `Coding Plan` para cuota y uso remotos de
Z.ai o BigModel. Las fuentes oficiales revisadas no publican una API de lectura
para terceros, una exportación de métricas ni la ruta y el esquema de los
registros locales. La política confirma que las conversaciones registran
entradas y contenido generado, y los términos prohíben extraer datos o acceder
de forma automatizada sin autorización.

TokenUsage no añadirá un adaptador ZCode mientras falte una fuente pública
apta. El gate se puede reabrir con una API de solo lectura autorizada o con una
exportación local documentada y segura.

## Identidad y soporte

- ID propuesto para un futuro descriptor: `zcode`.
- Nombre visible: `ZCode`.
- Producto: app de escritorio con ZCode Agent integrado; la documentación
  describe un espacio de trabajo de escritorio, tareas, terminal y revisión.
- Publisher y responsable del servicio: `JINGSHENG HENGXING TECHNOLOGY
  PTE.LTD`.
- Versión observada: `3.4.2`, publicada el 2026-07-22.
- Windows x64: instalación documentada. La página también publica un enlace de
  descarga Windows ARM64, aunque su guía y su frase de soporte solo detallan
  x64. ARM64 requiere smoke real antes de anunciarlo como soporte del
  proveedor.
- Las fuentes revisadas no describen un ejecutable ni un contrato CLI de
  ZCode. `/goal`, `/compact` y los comandos personalizados viven dentro de
  ZCode Agent.

## Fuentes primarias

Consultadas el 2026-07-22:

| Fuente | Hecho que respalda |
|---|---|
| [Términos de ZCode](https://zcode.z.ai/en/terms) | Define el producto y el proveedor legal; exige login y prohíbe bots, scraping, extracción de datos e ingeniería inversa. |
| [Política de privacidad](https://zcode.z.ai/en/privacy) | Identifica al controller; registra conversaciones, entradas, archivos, código, comandos y contenido generado. |
| [Instalación](https://zcode.z.ai/en/docs/install) | Describe la app de escritorio, Windows x64 y enlaces Windows x64/ARM64. |
| [Changelog](https://zcode.z.ai/en/changelog) | Publica ZCode `3.4.2` el 2026-07-22. |
| [ZCode Agent](https://zcode.z.ai/en/docs/agent-framework) | Describe el agente propio dentro del espacio de trabajo de escritorio. |
| [Comandos](https://zcode.z.ai/en/docs/commands) | Fija `/goal`, `/compact` y comandos Markdown como funciones del agente. |
| [Usage Stats](https://zcode.z.ai/en/docs/usage-stats) | Separa `App Usage` local de `Coding Plan` remoto y enumera sus métricas. |
| [Connect Models & Plans](https://zcode.z.ai/en/docs/configuration) | Documenta endpoints de inferencia para API key; no un contrato de cuota, uso acumulado o facturación para terceros. |
| [Feedback & Support](https://zcode.z.ai/en/docs/feedback) | Documenta `%USERPROFILE%\\.zcode\\logs` en Windows para soporte. |

## Clasificación de fuentes

| Dato | Fuente observada | Estado para TokenUsage | Decisión |
|---|---|---|---|
| Cuota restante | `Coding Plan` en la UI de ZCode | No hay API pública, exportación ni permiso de terceros | Bloqueada |
| Uso histórico | `App Usage` lee registros locales | Ruta y esquema no publicados; los registros pueden contener contenido de sesión | Bloqueado |
| Tokens por modelo | `App Usage` y `Coding Plan` | Sin contrato de máquina ni fuente mínima documentada | Bloqueados |
| Gasto o facturación | Suscripción y plan dentro del servicio | Sin API o exportación de gasto para terceros | Bloqueado |
| API key manual | ZCode permite una key para invocar modelos | No representa un permiso de lectura de cuota o gasto | No se admite |

Los endpoints OpenAI y Anthropic descritos para Z.ai o BigModel sirven para
invocar modelos. TokenUsage no los llamará para medir consumo, inferir saldo ni
probar rutas no documentadas.

## Límite de privacidad y seguridad

TokenUsage no debe abrir, indexar ni usar:

- `%USERPROFILE%\\.zcode\\logs` ni un escaneo amplio de `.zcode`;
- `AGENTS.md`, comandos, skills, subagentes, configuración de MCP o archivos
  `.zcode` de un workspace;
- conversaciones, prompts, respuestas, adjuntos, archivos, código, comandos de
  shell, resultados de herramientas, tareas o historial de sesión;
- cookies, tokens, estado de login, credenciales de ZCode, Z.ai o BigModel, ni
  API keys de otro producto;
- endpoints privados, tráfico observado, automatización de la interfaz o
  mecanismos deducidos de binarios y logs.

La ruta de logs es la única ruta Windows de datos de ZCode que la documentación
revisada publica. Su finalidad es soporte y puede incluir material de tarea; no
es una fuente de métricas apta.

## Decisión de producto

- No crear código, descriptor, lector local ni cliente remoto para ZCode en
  esta fase.
- No pedir ni guardar una API key, cookie, token o credencial ZCode/Z.ai para
  este proveedor.
- No anunciar cuota, uso, gasto o soporte ARM64 de ZCode en la UI pública.
- Mantener el Ticket 49 en `needs-info`.
- Reabrir cuando ZCode publique una de estas opciones:
  1. API pública de lectura con endpoint, versión, autenticación de mínimo
     privilegio, alcance de cuenta/región y permiso expreso para terceros.
  2. Exportación o archivo local con ruta, esquema, licencia y garantía de que
     contiene solo marca de tiempo, modelo, tokens y coste, sin contenido de
     sesión ni credenciales.

## Revisión independiente

Grok Build revisó este gate en modo de solo lectura. Tuvo acceso únicamente a
esta nota, la matriz, el plan y el README; no usó web, shell ni permisos de
edición. Su veredicto fue `accept`: la matriz conserva los mismos límites y no
convierte el comportamiento de la UI en un contrato para terceros. El parent
verificó después el diff local. Esta revisión no sustituye las fuentes
primarias de la tabla anterior.

## Incertidumbre restante

La descarga ARM64 visible requiere prueba de instalación y lectura segura antes
de cualquier claim. ZCode puede publicar una exportación, una API o cambios de
política después de este corte; el gate debe revisarse antes de una beta que
prometa soporte.
