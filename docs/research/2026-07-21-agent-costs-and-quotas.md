# Cuotas y gasto local de Grok, Antigravity y OpenCode

Fecha: 2026-07-21

Estado: decisión lista para incorporar al plan

## Pregunta

¿Podemos sumar Grok Build, Antigravity CLI y OpenCode, mostrar gasto cuando no haya una cuota restante apta y hacerlo sin pedir otro login ni usar credenciales ajenas?

## Respuesta

Sí para un motor local de tokens y gasto. Grok Build y OpenCode tienen rutas locales útiles en Windows. Antigravity CLI puede entrar por lectura pasiva de bases locales, pero necesita un spike con datos reales antes de prometer cobertura.

La cuota en vivo tiene otro límite. Grok ofrece `/usage` en su producto, pero no documenta una salida de cuota apta para otra app. Antigravity ofrece `/usage` y `/credits` dentro de su TUI, y su FAQ prohíbe usar el login de Antigravity desde herramientas de terceros. WOpenUsage no automatizará esas rutas privadas.

## Referencias locales fijadas

Los clones están bajo `.reference/` y el repo raíz los ignora:

| Proyecto | SHA | Uso en esta investigación |
|---|---|---|
| [OpenUsage](https://github.com/robinebers/openusage) | `9d2bf09f10e21f769494a525a9d65c84d7aeb1df` | paridad de cuota, tarjeta y proveedores |
| [CodeBurn](https://github.com/getagentseal/codeburn) | `6e3c57a9ff95a624f1d9affa7384d32a67f359b7` | lectores de sesiones y agregación local |
| [AgentsView](https://github.com/kenn-io/agentsview) | `1ee2de88e2dae54326d8b47aeb2de2f58b5944f9` | contratos de cobertura, deduplicación y precios |

CodeBurn y AgentsView usan licencia MIT. Sirven como comparación y corpus de diseño. WOpenUsage tendrá contratos y código propios, con un alcance menor: tokens, coste, modelo, fecha y cobertura. No indexará transcripciones, comandos, herramientas, tareas ni resultados.

## Modelo común elegido

Cada agente expone tres capacidades independientes:

1. `Quota`: porcentaje usado o restante, límite y reinicio cuando existe una interfaz apta.
2. `ObservedUsage`: tokens observados en este equipo, agrupados por día, agente y modelo.
3. `Spend`: coste informado por la fuente o estimado con un catálogo versionado.

Una tarjeta puede mostrar cualquier subconjunto. La UI siempre declara fuente, edad y cobertura. `Gasto estimado` representa el valor a tarifas conocidas; no afirma el cargo de una suscripción.

El motor propio normaliza solo:

- agente y proveedor de modelo cuando se conocen;
- modelo;
- fecha y zona horaria;
- tokens de entrada, salida, razonamiento, lectura de caché y escritura de caché;
- coste USD informado o estimado;
- clave de evento para deduplicar;
- procedencia, versión de parser y cobertura.

La primera entrega agrega por día, agente y modelo. Proyecto y sesión quedan fuera para no guardar nombres ni crear un índice de conversaciones.

## Grok Build

### Fuentes oficiales

[Grok Build](https://docs.x.ai/build/overview) soporta Windows, login por navegador, API key, modo headless y Agent Client Protocol. Su [referencia de CLI](https://docs.x.ai/build/cli/reference) documenta `grok agent stdio`. El [changelog](https://x.ai/build/changelog) registra `/usage`, porcentajes de uso, créditos prepagos y coste/tokens en salida headless.

La [FAQ de Grok](https://docs.x.ai/grok/faq) describe un pool semanal compartido y una vista de uso con porcentaje, reinicio y créditos. No ofrece un contrato JSON de cuota para otra app. La [política de uso aceptable de xAI](https://x.ai/legal/acceptable-use-policy) restringe el acceso automatizado. Por eso, el endpoint privado de billing que usa OpenUsage no se activa en una build pública.

### Ruta local

Las referencias muestran dos generaciones de datos locales:

- `GROK_HOME/sessions/.../summary.json`, `signals.json` y `updates.jsonl`;
- `GROK_HOME/logs/unified.jsonl`.

La fuente de sesión tiene prioridad. Algunas versiones incluyen `params.update.usage`, desglose por modelo, tokens y `costUsdTicks`. Si el coste informado no existe, se estima desde tokens y se etiqueta. El log unificado queda como fallback y nunca se suma sobre una sesión ya contada.

### Prueba Windows

- binario: `C:\Users\cristian\.grok\bin\grok.exe`;
- versión observada: `0.2.106`;
- sesiones y `unified.jsonl`: presentes;
- últimas 2.000 líneas examinadas solo por esquema: 1.992 JSON válidos;
- no se leyó ni imprimió el contenido de `auth.json`, prompts, modelos, costes o tokens.

### Decisión

- tokens y gasto local: beta;
- cuota y saldo en vivo: `PolicyBlocked` hasta tener salida oficial apta o permiso escrito;
- nunca iniciar login ni leer `auth.json` para el scanner local.

## OpenCode

### Fuentes oficiales

La [CLI de OpenCode](https://opencode.ai/docs/cli/) ofrece `opencode stats` para tokens y costes, filtros por días, modelos y proyecto, además de export de sesiones. La [guía de solución de problemas](https://opencode.ai/docs/troubleshooting/) documenta `%USERPROFILE%\.local\share\opencode` en Windows. La [guía de Windows y WSL](https://opencode.ai/docs/windows-wsl/) fija `~/.local/share/opencode` dentro de cada distro WSL.

`opencode stats` no ofrece JSON en la versión examinada. Se usará como oráculo diferencial, no como texto para parsear.

### Ruta local

OpenCode ha usado dos formatos que pueden coexistir:

- `opencode.db`, con sesión, mensaje y parte;
- `storage/session`, `storage/message` y `storage/part` en JSON.

Los mensajes o partes `step-finish` incluyen modelo, `cost` y `tokens` con entrada, salida, razonamiento y caché. El coste informado tiene prioridad. El catálogo solo cubre filas sin coste.

El lector abre SQLite en modo de solo lectura, consulta columnas mínimas y usa `busy_timeout` corto. No copia una base grande. Procesa el WAL sin crear o cambiar archivos. Un bloqueo conserva el último agregado y marca cobertura parcial.

### Prueba Windows

- binario detectado: `C:\Users\cristian\.bun\bin\opencode.exe`;
- versión observada: `1.18.4`;
- `opencode stats --help`: correcto y con flags de días, modelos y proyecto;
- `opencode.db` y `storage`: presentes;
- tamaño observado de la base: cerca de 2,5 GB, motivo para evitar copia completa y escaneo de transcripciones;
- no se abrió `auth.json` ni se ejecutó un informe con cifras del usuario.

OpenCode también puede correr dentro de WSL. La detección WSL requiere consentimiento porque una app Windows debe enumerar distros y abrir archivos mediante `\\wsl$`. La primera beta cubre la instalación Windows nativa; WSL queda como tarea separada.

### Decisión

- tokens y gasto local: beta, junto con Grok Build;
- `opencode stats`: prueba diferencial de totales en fixtures y smoke opt-in;
- cuota común: no aplica, porque OpenCode puede usar varios proveedores y planes;
- `auth.json`: fuera del lector.

## Antigravity CLI

### Fuentes oficiales

La [instalación de Antigravity CLI](https://antigravity.google/docs/cli-install) admite Windows y guarda el login silencioso en Windows Credential Manager. `/usage` muestra cuota dentro del TUI según su [documentación](https://antigravity.google/docs/cli/commands/usage); `/credits` tiene una [vista propia](https://antigravity.google/docs/cli-credits). La [statusline](https://antigravity.google/docs/cli-statusline) expone uso del contexto activo, pero no la cuota global de suscripción.

La [FAQ de Antigravity](https://antigravity.google/docs/faq) indica que usar software de terceros para acceder a Antigravity con el login de Antigravity viola sus términos y puede suspender la cuenta. Ese límite descarta leer Credential Manager, llamar Cloud Code, consultar un language server privado o automatizar `/usage`.

### Ruta local permitida

Las referencias detectan conversaciones `.db` con una tabla `gen_metadata`. Sus filas contienen modelo y contadores de tokens. Un lector pasivo puede extraer esos campos sin usar el login. Se aceptan solo formatos locales claros y sin descifrado:

- `.db` SQLite en modo de solo lectura;
- eventos de statusline que el usuario configure de forma explícita en el futuro;
- ningún `.pb` cifrado, daemon auxiliar, token, CSRF o RPC local privado.

La instalación examinada tiene `agy.exe` `1.1.5`, pero aún no existe `%USERPROFILE%\.gemini\antigravity-cli`. No hay corpus local para verificar el parser.

### Decisión

- cuota y créditos: bloqueados por política;
- tokens y coste local: experimental hasta probar una `.db` real y sanitizar fixtures;
- una build pública debe fallar cerrada si solo encuentra datos cifrados o una fuente privada.

## Catálogo de precios

Orden de precedencia:

1. coste informado por el agente;
2. precio fijado por proveedor y modelo cuando el contrato es claro;
3. snapshot embebido y fechado del [catálogo de LiteLLM](https://github.com/BerriAI/litellm/blob/main/model_prices_and_context_window.json);
4. sin coste cuando el modelo no tiene una coincidencia exacta.

El motor no hace coincidencias por subcadena. Cada estimación guarda versión del catálogo y tasa aplicada. Las actualizaciones de catálogo se revisan en build; no se descarga una tabla sin firma durante el uso normal.

## Incertidumbre

- Los formatos locales no son API estable y requieren fixtures por versión.
- La cuota Grok podría ganar una salida oficial; se debe revisar antes de cada beta.
- Antigravity aún carece de un fixture Windows real en este equipo.
- OpenCode nativo y OpenCode en WSL usan raíces distintas.
- Un coste informado puede representar tarifa API, precio de router o cero promocional; la UI conserva la procedencia.

## Decisión final

Construir un motor local propio y pequeño. La beta incluirá Claude, Grok Build y OpenCode sobre el mismo contrato. Antigravity CLI entra después como parser pasivo experimental. Codex conserva su fuente oficial para cuota y uso. Cada proveedor puede entregar cuota, uso o gasto de forma independiente, y la tarjeta se adapta al conjunto disponible.
