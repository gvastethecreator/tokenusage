# Ticket 20: gasto agregado real

Fecha: 2026-07-22

## Resultado

La tarjeta de uso local muestra cinco periodos civiles:

- Hoy;
- Ayer;
- últimos 7 días, desde `hoy - 6`;
- últimos 30 días, desde `hoy - 29`;
- mes actual, desde el día 1.

El coordinador consulta una sola ventana desde
`min(hoy - 29, inicio de mes)` hasta hoy. Los periodos filtran los rollups
diarios ya guardados; la UI no relee eventos ni conserva proyecto, sesión,
tarea o comando.

## Coste y cobertura

- Coste informado y estimado mantienen filas y etiquetas separadas.
- Cero informado cuenta como dato.
- Los tokens sin precio reducen la cobertura y nunca se convierten en dólares.
- Cada periodo expone tokens, cobertura y coste por millón.
- El aviso aclara que la estimación usa tarifas API y no es el cobro del plan.
- El anillo usa el gasto combinado solo para proporción. Su leyenda conserva
  las partes informada y estimada.

El detalle plegable de 30 días agrupa el mismo conjunto por agente y por
agente/modelo. Los modelos con gasto cero o sin precio siguen en la lista; el
anillo omite segmentos de valor cero.

## Estados y consistencia

- Ausencia de rollups muestra `Sin datos`; no rellena días con promedios.
- Una fuente `Complete` junto a otra `NoData` produce estado parcial.
- Fuentes con zonas de agrupación distintas fallan antes de escribir una foto
  combinada.
- Las sumas usan operaciones comprobadas; un overflow no queda oculto.
- El ID `openai/gpt-5` de OpenCode se normaliza a proveedor `openai` y modelo
  `gpt-5` cuando ambos prefijos coinciden.

## UI

La tarjeta conserva los AutomationIds previos para coste, tokens y cobertura.
Los cuatro periodos adicionales tienen IDs propios. El detalle usa un
`Expander`, anillo animado con movimiento reducido ya soportado, leyenda y un
`ListView` virtualizado de 180 DIPs. Todo texto nuevo usa recursos ES/EN,
ThemeResources, estilos WinUI y wrapping.

Capturas:

- [periodos compactos](../../artifacts/ticket-20/real-spend-collapsed.png)
- [desglose por agente y modelo](../../artifacts/ticket-20/real-spend-expanded.png)

## Grok Build y revisión local

Dos tareas Grok revisaron el contrato de periodos y la jerarquía compacta. La
revisión final leyó solo el projector y el coordinador. Se aceptaron sus riesgos
de zona horaria múltiple, estado mixto `Complete`/`NoData` y etiquetas del
anillo. El total central combinado se conserva como proporción del anillo: las
filas, leyenda, nombre accesible y aviso mantienen informado y estimado
separados y descartan cualquier claim de factura.

Resultados durables:

- `.scratch/agent-cli-delegation/grok-build/t20-period-contract/result.json`;
- `.scratch/agent-cli-delegation/grok-build/t20-compact-ui/result.json`;
- `.scratch/agent-cli-delegation/grok-build/t20-final-code-review/result.json`.

La revisión local WinUI eliminó avisos de bindings, localizó nombres accesibles,
ajustó medidas y comprobó el estado plegado y expandido a 400 x 900.

## Pruebas

- Pruebas focales de periodos, cobertura, cero, modelos y zonas: 12/12 dentro
  de `LocalUsageCoordinatorTests`.
- Focal conjunto con OpenCode: 25/25.
- Architecture: 26/26.
- Core: 60/60.
- CLI: 1/1.
- Providers: 165/165.
- Platform Windows: 52/52.
- Solución x64 Debug: 0 advertencias, 0 errores.
- Solución ARM64 Debug: 0 advertencias, 0 errores.
- UI Automation empaquetada: 9/9.
- Auditoría NuGet transitiva: sin paquetes vulnerables informados.
- `git diff --check`: sin errores.

## Límite

Este corte usa rollups locales. No afirma gasto de suscripción, consumo de otros
equipos ni cuota remota. El watcher, la frescura por origen y WSL siguen en
tareas posteriores.
