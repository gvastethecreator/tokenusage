# Ticket 33A: cliente GitHub Copilot

Fecha: 2026-08-12

## Resultado

El primer corte de Copilot agrega un cliente HTTP tipado y sin activación en
runtime. Detecta la cuenta y el uso público sin el endpoint interno del editor:

- `GetAuthenticatedUserAsync` y `GetPersonalAccountAsync` llaman `GET /user`
  y después `GET /users/{login}/settings/billing/ai_credit/usage`;
- `GetOrganizationSubscriptionAsync` llama `GET /orgs/{org}/copilot/billing`
  para `plan_type` Business o Enterprise y asientos;
- `GetOrganizationAiCreditUsageAsync` llama
  `GET /organizations/{org}/settings/billing/ai_credit/usage`.

Un `404` en billing personal u organización es `Unsupported`, no un plan
inventado. El cliente suma créditos y cargo reportados. No calcula cuota
restante ni nombra Pro, Pro+ o Max a partir de tablas de precio.

## Seguridad

- el host queda fijado a `https://api.github.com`;
- la versión REST es `2026-03-10`;
- `User-Agent` es `TokenUsage`; no hay headers de editor;
- logins inseguros se rechazan antes de la red;
- la respuesta se limita a 64 KiB;
- `401`, `403`, `404`, `429`, red, timeout y contrato tienen tipos estables;
- mensajes y excepciones no incluyen token, correo ni cuerpo remoto;
- no se leen `hosts.yml`, extensiones, cookies ni `gh auth`.

El popover de Configure pide un token fine-grained con `Plan: read` y, si
aplica, la organización administrada. El login personal no se escribe a mano.

## Límite

No hay refresh en runtime, caché, dashboard ni smoke autenticado. La build
pública sigue apagada hasta el smoke autorizado del Ticket 45.
