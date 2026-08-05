# GitHub Copilot source gate

Fecha de corte: 2026-07-21

Decisión: `implement-subset`

## Respuesta

GitHub publica endpoints REST dedicados al uso de AI credits. TokenUsage puede mostrar consumo y cargo adicional para una cuenta personal pagada o una organización administrada mediante un fine-grained personal access token que entrega el usuario. La app guarda el token en Windows Credential Locker.

La API pública no entrega el saldo de la asignación incluida. TokenUsage mostrará `AI credits usados` y `Cargo adicional`. No mostrará `Cuota restante` para Copilot hasta que un contrato público devuelva el límite efectivo de la cuenta.

Quedan fuera el endpoint interno `/copilot_internal/user`, los headers que imitan un editor, los tokens de extensiones, `hosts.yml`, GitHub CLI Credential Manager y cookies.

## Fuentes primarias

Consultadas el 2026-07-21:

| Fuente | Hecho que respalda |
|---|---|
| [Billing usage REST API](https://docs.github.com/en/rest/billing/usage?apiVersion=2026-03-10) | GitHub publica reportes de AI credits para usuarios y organizaciones, con schemas y permisos. |
| [Usage-based billing for individuals](https://docs.github.com/en/copilot/concepts/billing/usage-based-billing-for-individuals) | Los planes personales usan AI credits, reinician el primer día UTC y tienen una asignación por plan. La parte flex puede variar. |
| [Usage-based billing for organizations and enterprises](https://docs.github.com/en/copilot/concepts/billing/usage-based-billing-for-organizations-and-enterprises) | Business y Enterprise agrupan los créditos incluidos en la entidad que paga. Un total de organización no representa la cuota de un miembro. |
| [Copilot usage metrics REST API](https://docs.github.com/en/rest/copilot/copilot-usage-metrics?apiVersion=2026-03-10) | GitHub ofrece reportes de actividad de organización y empresa con permisos propios; sirven para adopción, no para saldo. |
| [Copilot plans](https://docs.github.com/en/copilot/get-started/plans) | Separa Free, Student, Pro, Pro+, Max, Business y Enterprise. |
| [GitHub CLI `gh copilot`](https://cli.github.com/manual/gh_copilot) | Ejecuta Copilot CLI; no documenta un comando de cuota o billing. |

## Contrato elegido

Versión REST: `2026-03-10`.

| Scope | Endpoint | Permiso fine-grained | Cobertura |
|---|---|---|---|
| cuenta personal pagada | `GET /users/{username}/settings/billing/ai_credit/usage` | `Plan: read` del usuario | AI credits, modelo, precio, uso bruto, descuento incluido y cargo neto |
| organización | `GET /organizations/{org}/settings/billing/ai_credit/usage` | `Administration: read` de la organización | los mismos campos para el total de la organización; admite filtro por usuario |

El endpoint de usuario solo incluye Copilot comprado y facturado por esa cuenta personal. Si la licencia la paga una organización o empresa, se debe usar el endpoint de esa entidad. El endpoint de organización exige un administrador.

Los campos admitidos son hechos de uso y facturación:

- `grossQuantity`: AI credits consumidos;
- `grossAmount`: valor bruto;
- `discountQuantity` y `discountAmount`: parte cubierta;
- `netQuantity` y `netAmount`: uso y cargo netos;
- `model`, `product`, `sku`, `unitType` y período.

No hay un campo de asignación total, saldo o plan efectivo. La asignación publicada no se mezcla con la respuesta porque la parte flex de los planes personales puede cambiar, existen planes anuales legados y los pools de organización dependen de licencias y reglas de presupuesto. Un cálculo local de saldo podría quedar mal aunque el uso sea correcto.

## Cobertura por cuenta y rol

| Cuenta | Resultado |
|---|---|
| Free o Student | `Unsupported` hasta probar que el endpoint público devuelve datos útiles; no usar el endpoint interno para chat o completions |
| Pro, Pro+ o Max pagado por el usuario | uso y cargo mediante endpoint personal y token con `Plan: read` |
| Pro o Pro+ anual con billing legado | fuera del primer subset; GitHub mantiene un endpoint separado de premium requests |
| Business o Enterprise, miembro común | `InsufficientPermission`; no se presenta el total de la organización como propio |
| Business o Enterprise, administrador | uso y cargo de la organización; texto visible `Total de la organización` |
| Enterprise con varios scopes | conexiones nombradas por entidad; sin unir usuarios u organizaciones por login |

Los reportes públicos de Copilot usage metrics agregan actividad diaria y adopción. No entran en el primer adaptador: amplían datos personales y permisos sin mejorar el objetivo de gasto.

## Revisión de OpenUsage

OpenUsage:

- busca tokens en archivos del editor, `gh hosts.yml` y Keychain;
- llama `GET /copilot_internal/user` con identidad de VS Code y Copilot Chat;
- usa esa respuesta privada para plan y porcentajes personales;
- lista organizaciones y llama un summary general de billing;
- degrada a datos de organización cuando el endpoint privado marca un asiento administrado.

La clasificación local de Grok fue correcta. El parent review encontró contratos públicos más nuevos y específicos que el corte upstream:

- el endpoint personal de AI credits;
- el endpoint de organización bajo `/organizations/{org}`;
- la versión `2026-03-10`;
- permisos fine-grained precisos.

TokenUsage no copia la cadena upstream. El usuario escribe cuenta u organización y entrega una credencial creada para esta app. El cliente llama el endpoint dedicado de AI credits sin consultar primero un endpoint privado.

## Sonda Windows

La sonda no abrió archivos, no ejecutó `gh auth status` y no consultó GitHub:

| Prueba en este equipo | Resultado |
|---|---|
| GitHub CLI | `gh 2.92.0` instalado |
| cinco archivos candidatos de `gh`, Copilot y VS Code | dos existen |
| `%APPDATA%\GitHub CLI\hosts.yml` | existe; 100 bytes; solo se leyó metadata |
| base global de VS Code | existe; 6.496.256 bytes; solo se leyó metadata |
| directorios de extensión `github.copilot*` bajo la raíz estándar de VS Code | 0 |
| procesos VS Code | 0 |

La presencia de `gh` o estado de editor no configura el provider. La app ignora esas fuentes. Esto evita enviar un token Enterprise Server a `api.github.com` y mantiene el permiso bajo control del usuario.

## Seguridad y errores

- Cada conexión declara `Personal` u `Organization` y un nombre visible.
- El token vive en Credential Locker y nunca entra en logs, caché, diagnóstico o CLI de TokenUsage.
- El host queda fijado a `https://api.github.com`; GitHub Enterprise Server no se redirige al host público.
- `401` pasa a `AuthRequired`; `403` a `InsufficientPermission`; `404` a `UnsupportedScope`; `429` conserva el último valor válido como vencido.
- La caché guarda agregados mínimos y elimina login, modelo si el usuario desactiva detalle, y cualquier cuerpo remoto.
- Quitar una conexión borra token y caché.

## Review de Grok Build

Grok Build hizo forense local sin web y terminó con `EndTurn`. El recibo está en `.scratch/agent-cli-delegation/grok-build/runs/2026-07-22T00-38-11-898Z-plan-e0785714/result.json`: siete turnos y USD 0.2205448 reportados.

Grok propuso bloquear cuota personal y habilitar de forma condicional el billing de organización. El parent review mantuvo fuera la cuota privada, encontró el endpoint oficial de usuario y reemplazó el summary genérico por los dos reportes dedicados de AI credits.

## Evidencia pendiente

No hubo token, cuenta, organización ni llamada autenticada. No se validaron `200`, filtros, descuentos, revocación o rate limits con una cuenta real. El adaptador se implementará con fixtures sanitizados y quedará apagado en la build pública hasta un smoke HITL que borre la credencial al terminar.

## Decisión de producto

- Implementar uso y gasto para cuentas personales pagadas y organizaciones administradas.
- Usar solo endpoints públicos de AI credits y una credencial entregada para TokenUsage.
- Mantener Free, Student, planes legados y miembros sin permiso en estados honestos.
- No prometer cuota restante, aunque GitHub publique asignaciones por plan.
- No leer ni invocar tokens, sesiones o endpoints internos de Copilot, editor o `gh`.
- Reabrir cuota cuando el REST público entregue el límite efectivo o un saldo estable.
