# Devin source gate

Fecha de corte: 2026-07-21

Decisión: `implement-experimental-subset`

## Respuesta

Devin publica una API v3 para consumo. TokenUsage puede mostrar ACUs diarios y totales de una organización mediante un service user creado para esa organización. La app pide el ID de organización y una key `cog_` de forma manual, y guarda la key en Windows Credential Locker.

El primer subset usa solo `GET /v3/organizations/{org_id}/consumption/daily` en `https://api.devin.ai`. No muestra cuota diaria, semanal, saldo de on-demand o dólares. Esos datos de self-serve solo aparecen en el dashboard; el adaptador upstream los obtiene de un RPC privado.

## Fuentes primarias

Consultadas el 2026-07-21:

| Fuente | Hecho que respalda |
|---|---|
| [API Overview](https://docs.devin.ai/api-reference/overview) | La API v3 separa scopes de organización y Enterprise y usa service users. |
| [Authentication](https://docs.devin.ai/api-reference/authentication) | Las integraciones usan keys `cog_`, Bearer, RBAC y auditoría. Los PAT personales siguen en beta cerrada. |
| [Organization daily consumption](https://docs.devin.ai/api-reference/v3/consumption/organizations-consumption-daily) | El endpoint público devuelve `total_acus` y desglose diario y por producto; requiere `ManageBilling` en la organización. |
| [Permissions and RBAC](https://docs.devin.ai/api-reference/v3/overview) | Los service users de organización quedan limitados a una organización; los de Enterprise heredan acceso entre organizaciones. |
| [Usage](https://docs.devin.ai/admin/billing/usage) | Self-serve muestra uso, cuota restante y saldo en Settings; Enterprise usa ACUs. |
| [Session Insights endpoint](https://docs.devin.ai/api-reference/v3/sessions/organizations-sessions-insights) | El endpoint read-only incluye ACUs por sesión, pero también prompts sugeridos, análisis y otros datos que TokenUsage no necesita. |
| [API release notes](https://docs.devin.ai/api-reference/release-notes) | Consumo de organización llegó a v3 y los límites de ACU se exponen en endpoints Enterprise con `ManageBilling`. |

## Contrato elegido

| Campo | Valor |
|---|---|
| Host | `https://api.devin.ai` |
| Método | `GET` |
| Ruta | `/v3/organizations/{org_id}/consumption/daily` |
| Auth | `Authorization: Bearer` con service user key de organización |
| Permiso | `ManageBilling` en scope de organización |
| Query | `time_after` y `time_before` como timestamps Unix |
| Respuesta | `total_acus`, `consumption_by_date[].date`, `acus` y `acus_by_product` |
| Día contable | medianoche PST, `08:00:00 UTC`, según la referencia |

La tarjeta dirá `Consumo de organización` y mostrará ACUs en un período explícito, primero `Últimos 30 días`. El API no entrega precio por ACU o gasto en dólares; cada contrato Enterprise puede tener condiciones propias.

El permiso se llama `ManageBilling`. Aunque el endpoint es `GET`, el mismo nombre de permiso cubre más billing que una lectura específica. Para reducir riesgo:

- la key debe pertenecer a un service user con scope de una sola organización;
- la configuración solo admite un ID y el endpoint de organización; una key Enterprise queda fuera del contrato;
- el host queda fijo;
- el feature flag nace apagado;
- el smoke debe confirmar el rol mínimo antes de activar una build pública.

Si Devin no permite crear ese rol acotado en una cuenta real, el provider sigue bloqueado.

## Fuentes rechazadas

### OpenUsage

El adaptador upstream:

- lee `windsurf_api_key` y `api_server_url` de `~/.local/share/devin/credentials.toml`;
- lee `apiKey` del estado SQLite de la app;
- llama `GetUserStatus` en `exa.seat_management_pb.SeatManagementService`;
- envía la key dentro de metadata y simula cliente Devin `1.108.2`;
- convierte porcentajes restantes en usados y micros en dólares;
- puede presentar una cuota diaria oculta como semanal.

No se copiará ese diseño. Además, aceptar cualquier host HTTPS desde una configuración ajena permitiría enviar la key a un servidor elegido por quien modifique el archivo. TokenUsage no lee ese archivo ni acepta el override.

### Session Insights

`GET /v3/organizations/{org_id}/sessions/insights` tiene permiso read-only y devuelve `acus_consumed`, pero su respuesta también incluye análisis, prompts sugeridos, identificadores, títulos y URLs. El producto no necesita ese material y no lo solicitará para sumar ACUs.

### Enterprise y self-serve

- Los endpoints Enterprise de consumo y ACU limits requieren un service user Enterprise con `ManageBilling`. Quedan fuera del primer subset por alcance y capacidad de gestión.
- Self-serve muestra cuota restante y saldo en el dashboard. La API pública no documenta una lectura equivalente para una app local. Los PAT personales siguen en beta cerrada.
- Dedicated deployments usan un dominio propio. El primer subset no acepta hosts personalizados.

## Sonda Windows

La sonda se limitó a comandos, rutas, procesos y registro. No abrió archivos ni hizo red:

| Prueba en este equipo | Resultado |
|---|---|
| comando `devin` | ausente |
| cinco rutas candidatas de CLI, credencial y app | 0 existentes |
| proceso Devin | 0 |
| entradas Devin o Cognition en uninstall registry | 0 |

La implementación no depende de una instalación local. La ausencia de CLI o app debe mostrar `No configurado` hasta que el usuario agregue una conexión de organización.

## Seguridad y errores

- La key vive en Credential Locker y no entra en logs, caché, diagnóstico o CLI.
- El ID de organización se valida con el formato documentado `org-...` y se codifica como un segmento.
- Los timestamps se normalizan al período pedido y sus límites se cubren con fixtures locales.
- El parser solo conserva fecha y ACUs; descarta campos nuevos.
- `401` pasa a `AuthRequired`; `403` a `InsufficientPermission`; `404` a `UnsupportedScope`; `429` conserva último valor válido como vencido.
- Quitar la conexión borra key y caché.

## Review de Grok Build

Grok Build hizo forense local sin web y terminó con `EndTurn`. El recibo está en `.scratch/agent-cli-delegation/grok-build/runs/2026-07-22T00-46-45-418Z-plan-bdbb16b5/result.json`: siete turnos y USD 0.2188196 reportados.

Grok clasificó el RPC, la cadena de credenciales y el override de host como inseguros, y propuso `block`. El parent review encontró la API v3 de consumo de organización. Ese contrato cambia el resultado a un subset experimental y elimina la instalación local de la arquitectura.

## Evidencia pendiente

No hubo cuenta, organización, service user, key ni llamada autenticada. No se probaron rol, límites de fecha, `200`, revocación o rate limits reales. El provider queda apagado hasta un smoke HITL con una key temporal de organización y borrado posterior.

## Decisión de producto

- Implementar ACUs de organización durante un período explícito mediante API v3.
- Admitir solo service user de organización y `api.devin.ai` en la primera versión.
- Mantener cuota self-serve, saldo, dólares, Enterprise agregado y hosts dedicados como `Unsupported`.
- Rechazar CLI, app DB, credenciales prestadas, RPC privado, identidad simulada y Session Insights.
- No afirmar cuota restante o gasto monetario.
- Reabrir otros scopes cuando exista un permiso de billing de solo lectura y una cuenta autorizada los pruebe.
