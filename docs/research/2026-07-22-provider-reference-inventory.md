# Inventario de cobertura de proveedores de referencia

Fecha de corte: 2026-07-23

Decisión: ampliar el alcance con Kilo Code y Zed; conservar Kimi Code y Cursor
en sus gates existentes; abrir una siguiente ola de investigación, sin crear
adaptadores todavía.

## Pregunta

¿Qué proveedores aparecen en las referencias locales y faltan en el alcance de
TokenUsage, y cuáles justifican un gate de fuente antes de crear código?

## Método y límite

Se revisaron clones locales fijados de OpenUsage, CodeBurn y AgentsView. Son
inventario y comparación, no autorización para leer sesiones, transcripciones,
credenciales o formatos internos. Toda decisión de integración usa fuentes
primarias separadas.

| Referencia | Commit local | Uso en esta revisión |
|---|---|---|
| `robinebers/openusage` | `9d2bf09f10e21f769494a525a9d65c84d7aeb1df` | Comparar el conjunto de proveedores de cuota. |
| `getagentseal/codeburn` | `6e3c57a9ff95a624f1d9affa7384d32a67f359b7` | Detectar proveedores de gasto local y rutas que requieren gate. |
| `kenn-io/agentsview` | `1ee2de88e2dae54326d8b47aeb2de2f58b5944f9` | Contrastar familias de agentes y evitar duplicar fuentes. |

## Resultado de la petición

| Proveedor pedido | Estado antes de esta revisión | Acción |
|---|---|---|
| Kilo Code | Ausente | Añadido a M9; Ticket 56 cerró el gate inicial y Ticket 57 queda en `needs-info`. |
| Kimi Code | Ticket 50 cerrado como bloqueado | Se conserva el gate y Ticket 51; no se duplica. |
| Cursor | Gate público para Teams y Enterprise | Se conserva Tickets 30, 31 y 44; Individual sigue fuera de alcance. |
| Zed | Ausente | Añadido a M9; Ticket 58 cerró el gate inicial y Ticket 59 queda en `needs-info`. |

## Hallazgos de las referencias

OpenUsage documenta Antigravity, Claude, Codex, Copilot, Cursor, Devin, Grok,
OpenCode, OpenRouter y Z.ai. Todos ya están en la matriz. CodeBurn añade una
lista más amplia de fuentes de gasto local. Entre ellas están Gemini CLI,
Kiro, Roo Code, Goose, Kimi CLI y Cursor Agent. También incluye Kilo Code,
Kimi Code, Zed y los proveedores que TokenUsage ya tenía.

AgentsView confirma la presencia de Gemini, Kiro, Roo Code, Kilo, Kimi y Zed.
La coincidencia entre referencias sube la prioridad de los tres primeros, pero
no valida sus rutas ni su política.

La comparación de los índices completos encontró seis coincidencias que no
estaban en el alcance: Forge, Hermes Agent, OpenClaw, Pi, Qwen y Warp. AgentsView
también tiene muchos parsers que CodeBurn no enumera. Estos quedan fuera por
ahora porque una sola implementación de referencia aporta poca señal de demanda
y ninguna prueba de uso permitido.

CodeBurn y AgentsView obtienen gasto desde datos locales de sesión. Ese enfoque
puede contener prompts, respuestas, comandos, rutas y credenciales. TokenUsage
no adopta esas rutas solo por aparecer en una referencia.

El registro ejecutable de CodeBurn contiene 38 adaptadores: 27 cargados de
forma directa y 11 bajo carga diferida. Su índice de documentación queda atrás
y omite Codebuff, Mux, Open Design y Zed. Para contar cobertura se usa
`.reference/codeburn/src/providers/index.ts`, no el total declarado en el
README. Esta diferencia no cambia la prioridad: esos adaptadores siguen sujetos
al mismo gate de fuente y privacidad.

## Prioridad siguiente

Ticket 60 se divide en gates pequeños:

1. Gemini CLI, Ticket 61, por presencia en ambas referencias y uso Windows conocido.
2. Kiro, Ticket 62, por presencia en ambas referencias y una familia de CLI/IDE distinta.
3. Roo Code, Ticket 63, por frecuencia de la familia Cline y necesidad de separar su
   almacenamiento de tareas del uso agregado.
4. Goose, Ticket 64, por presencia en CodeBurn y posible ruta local multiplataforma.
5. Kimi CLI, Ticket 65, porque CodeBurn lo trata como fuente distinta de Kimi Code.
6. Cursor Agent, Ticket 66, porque CodeBurn lo separa del editor Cursor.
7. Forge, Ticket 67, presente en ambas referencias.
8. Hermes Agent, Ticket 68, presente en ambas referencias.
9. OpenClaw, Ticket 69, presente en ambas referencias.
10. Pi, Ticket 70, presente en ambas referencias y relacionado con OMP.
11. Qwen, Ticket 71, presente en ambas referencias y distinto del proveedor de modelos.
12. Warp, Ticket 72, presente en ambas referencias y con datos de terminal sensibles.

Aider, Amp, Windsurf, Codebuff, Crush, CodeWhale, Droid, IBM Bob, LingTai TUI,
Mistral Vibe, Mux, OMP, Open Design, Quick Desktop, Zerostack y otros agentes
de referencia quedan como candidatos posteriores. Antes de sumarlos a M9 se
debe evaluar demanda, soporte Windows y una fuente que no cruce el límite de
privacidad.

## Decisión de producto

- Kilo Code y Zed quedan representados en la matriz y M9 con estado de gate.
- Kimi Code y Cursor quedan cubiertos por sus tickets actuales.
- Kimi CLI y Cursor Agent se investigan por separado; no heredan los contratos
  de Kimi Code ni Cursor Admin API.
- Forge, Hermes Agent, OpenClaw, Pi, Qwen y Warp reciben gates propios por su
  presencia en ambas referencias.
- Ningún candidato de la siguiente ola recibe descriptor, logo, scanner,
  credencial o tarjeta antes de su gate de fuente.
- La investigación futura debe mantener separado el uso del agente, la cuota
  de su suscripción y el gasto del proveedor de modelo.

## Incertidumbre restante

Las referencias cambian rápido y sus parsers no prueban términos ni contratos.
Antes de anunciar soporte de cualquier candidato, hay que repetir el gate con
documentación primaria vigente, una prueba Windows aislada y datos mínimos
autorizados cuando hagan falta.
