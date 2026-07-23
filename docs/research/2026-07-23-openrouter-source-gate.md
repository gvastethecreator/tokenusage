# Gate de fuente OpenRouter

Fecha: 2026-07-23

## Decisión

TokenUsage puede integrar OpenRouter mediante una clave que el usuario entrega
de forma explícita. La app no buscará `OPENROUTER_API_KEY`, archivos de otras
apps ni credenciales del navegador. La clave vivirá en Windows Credential
Locker en un corte posterior.

El contrato oficial actual separa dos capacidades:

- `GET https://openrouter.ai/api/v1/key` informa uso de la clave activa,
  incluidos los periodos diario, semanal y mensual, límite opcional y cadencia;
- `GET https://openrouter.ai/api/v1/credits` informa créditos comprados y uso
  total, pero exige una management key.

Por ese cambio, una clave común puede mostrar uso y límite sin poder mostrar el
saldo global. La UI futura debe presentar `Permiso insuficiente` para créditos
sin ocultar un resultado válido de `/key`.

Fuentes primarias:

- [créditos restantes](https://openrouter.ai/docs/api/api-reference/credits/get-credits);
- [clave activa](https://openrouter.ai/docs/api/api-reference/api-keys/get-current-key);
- [referencia y OpenAPI](https://openrouter.ai/docs/api/reference/overview).

## Contrato fijado

Ambas llamadas usan `GET`, host fijo `openrouter.ai` y
`Authorization: Bearer <clave manual>`. El cliente no envía cuerpos ni admite
hosts configurables.

`/credits`:

```json
{
  "data": {
    "total_credits": 100.5,
    "total_usage": 25.75
  }
}
```

`/key` usa los campos `usage`, `usage_daily`, `usage_weekly`,
`usage_monthly`, `limit`, `limit_remaining`, `limit_reset` e `is_free_tier`.
Los importes observados deben ser finitos y no negativos. `limit` y
`limit_remaining` pueden ser nulos. La cadencia aceptada es `daily`, `weekly`,
`monthly` o nula.

## Fallos

- `401`: clave inválida o revocada;
- `403`: permiso insuficiente para esa capacidad;
- `429`: throttle con `Retry-After` opcional;
- red, timeout y otros estados: fallo transitorio;
- JSON, origen, tamaño o esquema: fallo de contrato.

Los mensajes no incluyen la clave, el cuerpo remoto ni excepciones internas.
Las respuestas se limitan a 64 KiB y se valida el origen final antes de leer el
cuerpo.

## Entrega por cortes

1. `27A`: cliente HTTP tipado y pruebas offline.
2. `27B`: Credential Locker y borrado ligado a la cuenta, después del control
   de privacidad del Ticket 24.
3. `27C`: runtime, caché y resultados parciales por capacidad.
4. `27D`: UI i18n, configuración manual y prueba empaquetada.
5. `27E`: smoke autorizado con una clave descartable, sin imprimirla ni
   guardarla fuera de Credential Locker.

El cliente 27A no activa red en la app y no completa Ticket 27.
