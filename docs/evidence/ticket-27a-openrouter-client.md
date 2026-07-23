# Ticket 27A: cliente OpenRouter

Fecha: 2026-07-23

## Resultado

El primer corte de OpenRouter agrega un cliente HTTP tipado y sin activación en
runtime. Tiene dos llamadas independientes:

- `GetCreditsAsync(managementKey)` para `/api/v1/credits`;
- `GetKeyUsageAsync(apiKey)` para `/api/v1/key`.

La separación evita tratar una clave común como si tuviera permiso para leer el
saldo de cuenta. El cliente conserva uso diario, semanal y mensual, límite,
saldo del límite, cadencia y nivel gratuito sin calcular valores remotos por su
cuenta.

## Seguridad

- host, esquema, puerto, path y query están fijados por capacidad;
- la respuesta se limita a 64 KiB;
- claves vacías o con caracteres inválidos se rechazan antes de la red;
- `401`, `403`, `429`, red, timeout, lectura y contrato tienen tipos estables;
- `Retry-After` acepta delta y fecha HTTP;
- mensajes y excepciones no incluyen clave, cuerpo remoto ni error interno;
- no se leen variables, archivos o credenciales de otras herramientas;
- no hay Credential Locker, caché, runtime, UI ni smoke real en este corte.

## Revisión

Grok Build produjo el plan inicial y revisó dos veces el código en modo de solo
lectura. La primera revisión rechazó el corte por validación de claves, endpoint
final y cobertura. Un revisor local independiente añadió la separación entre
management key y API key y los fallos de lectura. El parent corrigió los puntos
y amplió las pruebas. La segunda revisión local aceptó sin P0-P2; Grok aceptó el
corte y dejó cinco P2 de prueba/diagnóstico, que también se corrigieron salvo el
drenado de cuerpos de error. Ese cuerpo no se lee para evitar retener secretos;
al liberar la respuesta, `HttpClient` descarta o cierra la conexión si hace falta.

Artefactos Grok:

- `.scratch/agent-cli-delegation/grok-build/openrouter-client-plan/`;
- `.scratch/agent-cli-delegation/grok-build/openrouter-client-review/`;
- `.scratch/agent-cli-delegation/grok-build/openrouter-client-rereview/`.

## Pruebas

- `OpenRouterClientTests`: 37/37.
- Suite Providers: 328/328.
- MSIX Release x64: correcto.
- MSIX Release ARM64: correcto.
- `git diff --check`: sin errores.

Las pruebas usan handlers locales y secretos ficticios. Cubren ambos endpoints,
ceros medidos, campos futuros, cadencias, límites nulos, contratos inválidos,
estados HTTP, throttle, cancelación, fallo de stream, tamaño y cambios de
endpoint. Ninguna prueba abre red.

## Límite

Ticket 27 sigue abierto. Credential Locker, borrado ligado, runtime, caché,
copy i18n, opciones y smoke autorizado pertenecen a 27B–27E y dependen del
control de privacidad del Ticket 24.
