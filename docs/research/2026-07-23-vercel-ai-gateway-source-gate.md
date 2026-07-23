# Vercel AI Gateway: gate de fuente para Windows

Fecha: 2026-07-23
Estado: aprobado con límites
Ticket: 73

## Decisión

TokenUsage puede integrar Vercel AI Gateway como proveedor manual y experimental.
La fuente aprobada cubre gasto y tokens que pasaron por AI Gateway. También puede
mostrar la cuota restante de una clave con presupuesto cuando el usuario entrega
el ID público de esa clave.

La integración no representa todo el uso de un agente ni la factura directa de
un proveedor de modelos. Tampoco debe prometer acceso de solo lectura: Vercel no
documenta una clave exclusiva para reportes.

## Contrato aprobado

### Gasto agregado

- Endpoint fijo: `GET https://ai-gateway.vercel.sh/v1/report`.
- Autenticación: `Authorization: Bearer <AI_GATEWAY_API_KEY>`.
- Parámetros requeridos: `start_date` y `end_date`, fechas UTC inclusivas.
- Planes: Pro y Enterprise. Hobby y Pro Trial quedan fuera.
- Estado: beta.
- Precio del endpoint: USD 5 por cada 1.000 consultas.
- Alcance: toda la cuenta asociada a la clave.
- Retraso: las solicitudes pueden tardar unos minutos en aparecer.

El endpoint devuelve `results`. Cada fila incluye una sola dimensión, elegida
con `group_by`, más las métricas agregadas. No existe paginación documentada.
Vercel tampoco fija en esta página un rango máximo, un máximo de filas o una
tasa límite. TokenUsage debe limitar cada consulta a 31 días, usar agregación
diaria y aplicar caché local para controlar costo y tamaño.

Una consulta no puede devolver día y modelo como dimensiones a la vez. El MVP
hará una consulta con `group_by=day&date_part=day`. Un desglose por modelo
requiere otra consulta cobrada y queda fuera del primer corte.

Métricas que podemos mostrar:

- `total_cost`: importe cobrado por Vercel AI Gateway, en USD; vale cero para
  BYOK;
- `market_cost`: valor de mercado estimado por el gateway para tráfico BYOK y
  no BYOK; no equivale a una factura externa;
- `gateway_cost` y `surcharge_cost`: partes del costo del gateway;
- tokens de entrada, salida, caché, creación de caché y razonamiento;
- cantidad de solicitudes.

### Cuota de una clave

Vercel permite un presupuesto opcional por clave. El mínimo documentado es USD
1 y los períodos son diario, semanal, mensual o sin reinicio. El límite es
blando: la solicitud que lo cruza termina y puede dejar un gasto algo mayor.

TokenUsage puede consultar:

`GET https://ai-gateway.vercel.sh/v1/quotas?quotaEntityId=api_key_id_<key-id>`

La llamada usa la misma clave manual. Una clave con presupuesto devuelve límite,
gasto actual, período y estado. Una clave sin presupuesto devuelve `404` con
`Quota not found`. El restante se calcula como `max(0, limitAmount-currentSpend)`.
El ID de clave no se puede deducir de forma documentada desde el secreto, por lo
que el usuario debe copiar también ese ID desde Vercel.

## Seguridad y permisos

Una clave de AI Gateway sirve para inferencia y para reportes. Vercel no publica
un scope de solo lectura. La API de reportes también declara alcance de cuenta.
Esto impide presentar la conexión como una credencial de bajo privilegio.

La integración debe cumplir estas reglas:

1. pedir una clave nueva creada para TokenUsage;
2. recomendar vencimiento y presupuesto de USD 1;
3. aceptar la clave solo por una acción manual del usuario;
4. guardarla en Windows Credential Locker;
5. no leer variables de entorno, archivos o claves de otros agentes;
6. fijar ambos hosts y rechazar redirecciones a otro origen;
7. borrar credencial y caché de cuenta al desconectar;
8. mostrar antes de guardar que la clave puede ejecutar modelos y que el reporte
   cubre toda la cuenta;
9. mantener el proveedor como experimental hasta un smoke autorizado.

Vercel permite crear claves con `projectId`, fecha de vencimiento y presupuesto.
El documento del reporte sigue indicando alcance de cuenta, por lo que
TokenUsage no afirmará que `projectId` reduzca los datos del reporte.

## Estados y errores

El cliente debe tipar al menos estos estados. Solo el `401` quedó observado en
este gate. Los demás son manejo defensivo hasta un smoke autorizado:

- `401`: clave ausente, inválida o revocada;
- `403`: posible plan o permiso no admitido, sin contrato de error confirmado;
- `404` en cuotas: el contrato confirma `Quota not found` para una clave sin
  presupuesto; un ID incorrecto puede ser indistinguible hasta el smoke;
- `429`: respuesta HTTP defensiva; Vercel no publica una tasa límite en la
  página del reporte;
- error de red o tiempo agotado;
- JSON inválido o contrato desconocido;
- reporte vacío válido;
- datos guardados con aviso de retraso.

Una llamada sin `Authorization` devuelve `401` y
`authentication_error`. Esta comprobación se hizo sin credenciales el
2026-07-23 contra ambos endpoints.

Los nombres y el sentido de las métricas, los campos de cuota, el presupuesto
mínimo, los períodos y el límite blando provienen de la documentación primaria
citada. Los fixtures deben fijar su forma para el cliente. El smoke real debe
confirmar esa forma antes de quitar la marca experimental.

## Separación de claims

| Dato | Claim permitido |
| --- | --- |
| `total_cost` | Gasto cobrado por Vercel AI Gateway en el período |
| `market_cost` | Valor de mercado informado por el gateway |
| tokens del reporte | Tokens procesados por AI Gateway |
| cuota de clave | Presupuesto restante de esa clave de AI Gateway |
| actividad del agente | No se deduce del reporte |
| factura BYOK externa | No se deduce del reporte |
| cuota de Cursor, Codex u otro agente | No se deduce del reporte |

## Comparación local

CodeBurn implementa `GET /v1/report` con una clave manual, pero combina
`group_by=model` con `date_part=day`. Según el contrato actual, `date_part` solo
se aplica cuando `group_by=day`; por eso TokenUsage no copiará esa consulta.
Además, CodeBurn lee variables de entorno. TokenUsage pedirá una clave propia y
usará Credential Locker.

## Fuentes primarias

- [Custom Reporting](https://vercel.com/docs/ai-gateway/observability-and-spend/custom-reporting)
- [API Keys](https://vercel.com/docs/ai-gateway/authentication-and-byok/api-keys)
- [API Key Budgets](https://vercel.com/docs/ai-gateway/observability-and-spend/api-key-budgets)
- [Authentication and BYOK](https://vercel.com/docs/ai-gateway/authentication-and-byok)
- [AI Gateway pricing](https://vercel.com/docs/ai-gateway/pricing)
- [Coding agents](https://vercel.com/docs/ai-gateway/coding-agents)
- [Custom Reporting changelog](https://vercel.com/changelog/custom-reporting-ai-gateway)

## Gate de implementación

Ticket 74 puede empezar con cliente, mapeo, fixtures y UI experimental. El smoke
real sigue bloqueado hasta que el usuario autorice una clave de prueba con
presupuesto. Ninguna prueba automática debe buscar o usar credenciales locales.

La revisión adversaria de Grok aceptó el gate con revisión humana. Su objeción
principal fue separar hechos documentados, respuestas observadas y manejo
defensivo. El padre incorporó esa separación. La objeción sobre campos de costo
y cuota no bloquea el contrato: esos campos sí constan en las fuentes primarias.
