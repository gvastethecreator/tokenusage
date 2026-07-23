# Inventario de cobertura de proveedores de referencia

Fecha de corte: 2026-07-22

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

OpenUsage enumera Claude, Codex, Copilot, Cursor, Devin, Grok y OpenCode, con
otros proveedores documentados por separado. CodeBurn enumera Kilo Code, Kimi
Code CLI y Zed junto con Gemini CLI, Kiro, Roo Code y Goose. AgentsView también
registra Kilo, Kimi, Zed, Gemini, Kiro y Roo Code.

CodeBurn y AgentsView obtienen gasto desde datos locales de sesión. Ese enfoque
puede contener prompts, respuestas, comandos, rutas y credenciales. TokenUsage
no adopta esas rutas solo por aparecer en una referencia.

## Prioridad siguiente

La siguiente ola de gates queda en Ticket 60:

1. Gemini CLI, por presencia en ambas referencias y uso Windows conocido.
2. Kiro, por presencia en ambas referencias y una familia de CLI/IDE distinta.
3. Roo Code, por frecuencia de la familia Cline y necesidad de separar su
   almacenamiento de tareas del uso agregado.
4. Goose, por presencia en CodeBurn y posible ruta local multiplataforma.

Aider, Amp, Cursor Agent, Windsurf, Crush, Forge, OpenClaw, Pi y otros agentes
de referencia quedan como candidatos posteriores. Antes de sumarlos a M9 se
debe evaluar demanda, soporte Windows y una fuente que no cruce el límite de
privacidad.

## Decisión de producto

- Kilo Code y Zed quedan representados en la matriz y M9 con estado de gate.
- Kimi Code y Cursor quedan cubiertos por sus tickets actuales.
- Ningún candidato de la siguiente ola recibe descriptor, logo, scanner,
  credencial o tarjeta antes de su gate de fuente.
- La investigación futura debe mantener separado el uso del agente, la cuota
  de su suscripción y el gasto del proveedor de modelo.

## Incertidumbre restante

Las referencias cambian rápido y sus parsers no prueban términos ni contratos.
Antes de anunciar soporte de cualquier candidato, hay que repetir el gate con
documentación primaria vigente, una prueba Windows aislada y datos mínimos
autorizados cuando hagan falta.
