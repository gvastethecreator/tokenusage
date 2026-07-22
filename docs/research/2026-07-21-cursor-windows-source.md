# Cursor Windows source gate

Fecha de corte: 2026-07-21

Decisión: `implement-subset`

## Respuesta

WOpenUsage puede mostrar uso y gasto de Cursor para Teams y Enterprise mediante la Admin API pública. La conexión requiere una clave que crea un administrador de Cursor y entrega de forma manual. La app guardará esa clave en Windows Credential Locker.

No encontramos un contrato público equivalente para cuentas Individual. Esa variante queda como `Unsupported` para uso, gasto y cuota en vivo. El motor local podrá mostrar observaciones de modelos usados desde otros agents admitidos, con su procedencia, sin presentarlas como datos de Cursor.

WOpenUsage no leerá `state.vscdb`, Credential Manager ni archivos de perfil para obtener la sesión de Cursor. Tampoco llamará endpoints de dashboard, renovará tokens, creará cookies desde un JWT o automatizará el export CSV privado.

## Fuentes primarias

Consultadas el 2026-07-21:

| Fuente | Hecho que respalda |
|---|---|
| [Admin API](https://cursor.com/docs/account/teams/admin-api) | Los administradores de equipo pueden crear claves para `https://api.cursor.com`. La API ofrece miembros, uso diario, gasto y eventos de uso. |
| [Teams](https://cursor.com/en-US/business/teams) | Teams ofrece analítica detallada y cobro central. Individual no ofrece visibilidad de organización. |
| [Planes y precios](https://cursor.com/pricing) | Cursor separa planes Individual, Teams y Enterprise. Enterprise agrega uso agrupado y controles de organización. |
| [Mejoras de precios de Teams](https://cursor.com/blog/teams-pricing-june-2026) | Desde junio de 2026 el dashboard distingue dos bolsas de uso incluido: modelos propios y API de terceros. |
| [Organizaciones Enterprise](https://cursor.com/blog/organizations) | Enterprise puede agrupar varios equipos y mostrar uso agregado en su dashboard. |
| [Modelos y precios](https://cursor.com/docs/models-and-pricing) | Fuente pública de precios. No prueba consumo de una cuenta. |

La Admin API documenta una clave ligada a la organización y autenticación Basic con la clave como usuario. El contrato publicado incluye:

| Endpoint | Datos que admite WOpenUsage | Límite del claim |
|---|---|---|
| `POST /teams/spend` | gasto por miembro, límite de gasto configurado y comienzo de ciclo | gasto del mes; un límite individual no representa toda la cuota incluida |
| `POST /teams/filtered-usage-events` | modelo, clase de uso, tokens, coste en centavos, usuario y paginación | eventos facturados o incluidos; no saldo de las dos bolsas nuevas |
| `POST /teams/daily-usage-data` | solicitudes incluidas, por API y basadas en uso, además de actividad diaria | métrica de actividad; no factura ni cuota restante |
| `GET /teams/members` | miembros y roles | dato auxiliar; no consumo |

No encontramos campos públicos que expongan el saldo de las dos bolsas de Teams anunciadas en junio de 2026. La primera versión dirá `Uso y gasto del equipo`. No dirá `Cuota restante` para Cursor.

## Cobertura por plan

| Plan | Fuente aprobada | Cobertura |
|---|---|---|
| Individual | ninguna encontrada | `Unsupported`; sin lectura de sesión ni dashboard |
| Teams | Admin API con clave admin manual | gasto del ciclo, eventos, tokens y actividad según los endpoints publicados |
| Business | nombre legado que aún puede aparecer en el campo `kind` | se trata como datos de Teams, sin crear un cuarto plan |
| Enterprise | Admin API con clave admin manual | mismo contrato por scope configurado; no inferir un agregado de organización que la API no devuelva |

Una instalación podrá guardar varias conexiones con nombre. Cada clave conserva su scope y su procedencia. La app no unirá organizaciones o equipos por correo.

## Revisión de OpenUsage

OpenUsage sirve como comparación, no como contrato. Su adaptador Cursor:

- lee `cursorAuth/accessToken`, `cursorAuth/refreshToken` y el tipo de membresía desde estado local o Keychain;
- llama RPC privados en `api2.cursor.sh`;
- llama rutas privadas bajo `cursor.com/api` para uso, resumen, Stripe y export CSV;
- crea una cookie desde el JWT;
- renueva el token y escribe el nuevo valor;
- estima gasto desde tokens del CSV y un catálogo de precios.

Esas rutas quedan rechazadas. La base local solo contiene estado de autenticación para este caso; no aporta hechos de uso. El export de OpenUsage es una descarga remota autenticada, no un archivo local estable.

## Sonda Windows

La sonda se limitó a existencia, tamaño, fecha y conteos. No abrió la base ni leyó claves, valores, correo o contenido del usuario.

| Prueba en este equipo | Resultado |
|---|---|
| rutas de ejecutable por usuario, `%LOCALAPPDATA%`, `Program Files` y `Program Files (x86)` | 4 candidatas; 0 instalaciones encontradas |
| entradas de desinstalación | 0 |
| procesos Cursor | 0 |
| `%APPDATA%\Cursor\User\globalStorage\state.vscdb` | presente; 12.288 bytes; última escritura `2026-03-06T20:47:06Z` |
| sidecars WAL/SHM | ausentes |
| CSV cuyo nombre contiene `usage` bajo las dos raíces Cursor | 0 |

No fue posible observar una base bloqueada ni varias instalaciones reales porque Cursor no está instalado o en ejecución en este equipo. Esa falta no bloquea la fuente elegida: el cliente aprobado no toca el perfil local. Las pruebas del adaptador deben demostrar que una base bloqueada, un export ausente o varias instalaciones no cambian su resultado ni se exploran.

## Seguridad y datos

- La clave admin se solicita de forma explícita y se guarda en Credential Locker.
- Solo se permiten `https://api.cursor.com` y las rutas fijadas por el contrato.
- Los logs excluyen clave, header, correo, nombre y cuerpo de respuesta.
- La caché normaliza identificadores antes de persistir y guarda agregados mínimos.
- `401` y `403` pasan a `AuthRequired`; `429` conserva el último valor válido con estado vencido; un schema nuevo falla cerrado.
- El usuario puede borrar la conexión, la credencial y su caché desde Ajustes.

## Review de Grok Build

Grok Build hizo forense local sin web. La primera ejecución agotó diez turnos y quedó cancelada; la misma sesión se reanudó y terminó con `EndTurn`. El recibo válido está en `.scratch/agent-cli-delegation/grok-build/runs/2026-07-22T00-30-32-392Z-plan-d0d27de4/result.json`. Las dos invocaciones reportaron USD 0.3661820 en total.

Grok clasificó bien la base, el refresh, los RPC, el dashboard y el CSV como rutas privadas, y propuso `block` con la evidencia del checkout. El parent review encontró la Admin API oficial. Ese contrato cambia la decisión a `implement-subset` y elimina toda lectura local del diseño.

## Evidencia pendiente

No hubo login, clave admin ni llamada autenticada. Tampoco se probó una cuenta Teams o Enterprise real. La implementación se hará con fixtures sanitizados y quedará apagada en la build pública hasta completar un smoke autorizado con una clave de prueba. Un ticket HITL separado controla esa prueba y el borrado posterior de la credencial.

## Decisión de producto

- Implementar Teams y Enterprise con Admin API y clave manual.
- Mantener Individual en `Unsupported` para uso, gasto y cuota de Cursor.
- Mostrar gasto, eventos y cobertura; no prometer saldo de las bolsas incluidas.
- Rechazar DB local, secretos prestados, refresh, cookies, RPC privados, dashboard privado y CSV privado.
- Soportar varias conexiones nombradas sin mezclar scopes.
- Revisar el contrato antes de cada beta porque la Admin API se publica como una primera versión.
