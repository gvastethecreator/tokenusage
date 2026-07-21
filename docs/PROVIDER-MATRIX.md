# Matriz de proveedores

Fecha de corte: 2026-07-21

Upstream: `robinebers/openusage@9d2bf09f10e21f769494a525a9d65c84d7aeb1df`

## Estados

- `MVP`: ruta técnica y de producto elegida.
- `Local`: se puede publicar una vista basada solo en datos locales.
- `Gate`: falta prueba, contrato público o revisión de uso permitido.
- `Manual`: requiere una clave que el usuario entrega a la app.
- `Experimental`: fuente frágil; sin promesa de soporte.

## Resumen

| Proveedor | Cuota restante | Uso local | Fuente de sesión | Estado | Entrega |
|---|---|---|---|---|---|
| Codex | Sí, interfaz oficial local | Sí, API oficial y logs | `codex app-server` | MVP | M1 |
| Claude | Endpoint no público | Sí, logs | Archivo Claude Code | Local + Gate | M2 local; cuota pendiente |
| OpenCode | Sin cuota remota común | Sí, base local | Datos propios de OpenCode | Local | M5 |
| Grok | Endpoint privado | Sí, JSONL | Archivo de auth Grok | Local + Gate | M5 local; cuota pendiente |
| OpenRouter | Sí, API con clave | Depende de API | Clave manual propia | Manual | M6 |
| Z.ai | Sí, API con clave | Depende de API | Clave manual propia | Manual + Gate | M6 |
| Cursor | APIs privadas y export | Sí, DB/export | Estado local de Cursor | Gate | M7 |
| GitHub Copilot | API interna; billing org público | Limitado | Editor o `gh` | Gate | M7 |
| Antigravity | Servicio local o API privada | Limitado | Servicio/almacén local | Experimental | M8 |
| Devin | RPC privado | Limitado | CLI o app local | Experimental | M8 |

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

Base de datos y autenticación local de OpenCode. La ubicación exacta en Windows se debe obtener desde la configuración de la versión instalada, sin asumir una ruta Unix.

### Métricas

- uso observado en este equipo;
- tokens y costo por periodo;
- tendencia;
- modelos y fuentes con cobertura.

### Límites

El dato local puede omitir otros equipos y servicios. La UI lo llama `Uso local observado`. No afirma cuota restante cuando no existe un límite común del proveedor.

### Salida

Fase local después de una prueba Windows con base real y fixtures mínimos.

Fuente upstream de comparación: [provider OpenCode](https://github.com/robinebers/openusage/blob/9d2bf09f10e21f769494a525a9d65c84d7aeb1df/docs/providers/opencode.md).

## Grok

### Fuente local

- `GROK_HOME/logs/unified.jsonl` o ruta por defecto;
- tokens y modelos registrados por el CLI.

### Cuota

El upstream usa autenticación local y endpoints de billing no documentados. Esa ruta queda en gate.

### Salida

Logs locales después de prueba Windows. Cuota y saldo tras contrato y revisión.

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

### Fuente

Clave manual en Credential Locker. El adaptador debe confirmar que el endpoint y su uso por terceros estén documentados para la región y el tipo de cuenta elegidos.

### Salida

Después de OpenRouter. Se bloquea si depende de un endpoint interno.

Fuente upstream de comparación: [provider Z.ai](https://github.com/robinebers/openusage/blob/9d2bf09f10e21f769494a525a9d65c84d7aeb1df/docs/providers/zai.md).

## Cursor

### Fuente candidata

- base de estado local de Cursor en Windows;
- endpoints de dashboard;
- export CSV de uso para gasto y modelo.

### Riesgos

- token y esquema internos;
- diferencias Individual, Business y Enterprise;
- endpoint o export sin contrato estable;
- base SQLite bloqueada mientras Cursor corre;
- varias instalaciones y perfiles.

### Salida

Gate completo. El scanner debe copiar o abrir la base en modo solo lectura sin bloquear Cursor. La app no rota ni escribe el token.

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

El upstream prueba un servicio local y recurre a almacén de credenciales y API inversa. La disponibilidad cambia por versión. Se clasifica experimental hasta contar con un contrato estable, una matriz de versiones y un fallo seguro.

Fuente upstream de comparación: [provider Antigravity](https://github.com/robinebers/openusage/blob/9d2bf09f10e21f769494a525a9d65c84d7aeb1df/docs/providers/antigravity.md).

## Devin

El upstream toma credenciales de CLI o app y llama un RPC no público. Se clasifica experimental. No entra en una promesa de paridad hasta validar Windows, política y estabilidad.

Fuente upstream de comparación: [provider Devin](https://github.com/robinebers/openusage/blob/9d2bf09f10e21f769494a525a9d65c84d7aeb1df/docs/providers/devin.md).

## Orden de implementación

1. Codex completo.
2. Claude local.
3. Core de scanners y precios.
4. OpenCode y Grok local.
5. OpenRouter con clave manual.
6. Z.ai si su contrato queda aprobado.
7. Cursor y Copilot tras sus gates.
8. Cuota Claude tras permiso o interfaz pública.
9. Antigravity y Devin como experimentales.

Este orden mantiene el objetivo de cuota restante con Codex y permite sumar valor local sin ampliar el manejo de credenciales ajenas.
