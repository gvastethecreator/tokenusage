# Revisión completa de arquitectura — 2026-08-13

Estado: implementación, verificación y revisión independiente completadas. Veredicto: `ACCEPT`.

Complemento HTML: `docs/architecture/architecture-review-2026-08-13.html`

Rendimiento: `docs/performance/performance-review-2026-08-13.md`

Tracker: GitHub Issues 6–25, Project 4, y espejo local 128–147.

## Resultado

Los diez cambios de arquitectura aprobados están implementados. El grafo publicado contiene 12 módulos, 33 dependencias verificadas y 5 flujos principales, sin relaciones desconocidas. La aplicación empaquetada Release x64 vuelve a leer la base local real y muestra los cinco proveedores.

La corrección que desbloqueó los datos no fue cosmética: el inicio cache-first intentaba leer una base schema v3 con código schema v4 antes de abrirla en modo escritura. `LocalUsageRefresh.ReadCachedAsync` ahora migra únicamente bases antiguas propiedad de TokenUsage y sigue rechazando schemas futuros.

## Diez resultados de arquitectura

| ID | Resultado | Evidencia principal |
| --- | --- | --- |
| ARCH-01 | `TokenUsage.Presentation` es `net10.0`, no referencia WinUI y concentra view models, proyectores y formatters portables. | Reglas de arquitectura y referencias de proyecto. |
| ARCH-02 | `CompactDashboardProjector` produce una sola proyección compartida para panel, selección y tray. | Prueba de identidad de snapshot y límites cacheados. |
| ARCH-03 | El informe separa filas, tabs de proveedor y transiciones del code-behind principal. | Archivos `UsageReportRows`, `UsageReportPage.ProviderTabs` y `UsageReportPage.Transitions`. |
| ARCH-04 | El dashboard compacto separa tabs y transiciones sin cambiar AutomationIds públicos. | Archivos `CompactUsageDashboard.ProviderTabs` y `.Transitions`; UIA empaquetada. |
| ARCH-05 | Los coordinadores Codex y Vercel delegan mediante registros públicos y no duplican la secuencia de refresh. | Pruebas Platform.Windows y búsqueda de composición. |
| ARCH-06 | Existe un solo adaptador Windows Credential Vault, propiedad de Platform.Windows. | Pruebas de credenciales y ausencia de implementaciones duplicadas. |
| ARCH-07 | Orden, nombre visible y marca de proveedor salen de `ProviderPresentationCatalog`. | Pruebas de catálogo y proyección. |
| ARCH-08 | Los wire types JSON estables permanecen en `TokenUsage.Cli`; Core conserva consultas de dominio. | 104 goldens/pruebas CLI y regla de frontera. |
| ARCH-09 | El escáner Codex está dividido en paths, scan y map sin cambiar `codex-jsonl/3`. | 19 pruebas Codex y mapa de módulos. |
| ARCH-10 | Los fakes Vercel de Debug salieron de `MainPage`; `AppComposition` sigue siendo la raíz. | Regla de composición y build Release x64. |

## Datos reales recuperados

Prueba empaquetada del 13 de agosto:

- `tokenusage doctor`, `usage` y `report` terminaron con código 0.
- SQLite quedó en schema 4, `quick_check=ok` y con los tres índices v4.
- El dashboard mostró Antigravity, Codex, Cursor, Grok Build y OpenCode.
- El informe global mostró 56.57B tokens, costo observado de $20,921.22 USD y cobertura 44.6% sin mezclar costo observado, estimado y tokens sin precio.
- La ruta Global → Provider fue recorrida con teclado: foco en Global, Tab a Provider y activación con Espacio.

Capturas locales:

- `.scratch/ui/arch-perf-2026-08-13/10-live-dashboard-final.png`
- `.scratch/ui/arch-perf-2026-08-13/09-live-report-top.png`
- `.scratch/ui/arch-perf-2026-08-13/11-live-report-provider.png`

## Accesibilidad afectada

| Estado | Evidencia | Alcance |
| --- | --- | --- |
| Teclado | UIA real sobre el paquete: Tab movió foco de Global a Provider; Espacio lo seleccionó. | Observación empaquetada. |
| Escala de texto | Las vistas afectadas conservan layout adaptativo a `UISettings.TextScaleFactor`; build y reglas pasan. | Verificación de código, no se cambió la escala global de Windows. |
| Alto contraste | Recursos HighContrast, brushes del sistema y fallbacks se mantienen; pruebas de paleta pasan. | Prueba automatizada, no observación manual nueva. |
| Movimiento reducido | Tabs, gráficos y transiciones consultan `MotionSettings.AreAnimationsEnabled()` y tienen salida inmediata. | Reglas automatizadas, no se cambió la preferencia global de Windows. |

## Puerta de integración

`scripts/check.ps1 -Platform x64 -Configuration Release` terminó correctamente en 166.4 s después de las reparaciones de la revisión independiente:

| Proyecto | Resultado |
| --- | ---: |
| Architecture | 97/97 |
| Core | 233/233 |
| CLI | 104/104 |
| Providers | 490/490 |
| Platform Windows | 157/157 |
| Total | 1081/1081 |
| Solución y MSIX x64 Release | correcto |

MSIX generado: `src/TokenUsage.Package/AppPackages/TokenUsage.Package_0.0.1.0_x64_Test/TokenUsage.Package_0.0.1.0_x64.msix`.

Un intento inicial tuvo un timeout accidental de un segundo y otro se interrumpió al llegar un hallazgo del revisor; ambos fueron inconclusos y no se cuentan como fallos del producto. El gate de 166.4 s es la repetición válida sobre el código final.

## Revisión independiente y autopsia

El revisor empezó con `REPAIR` y encontró dos ramas hermanas que las pruebas focales no cubrían:

- Cursor normalizaba timestamps de bubbles, pero no los tres timestamps de `composerData`; también faltaban numeric-text y real finito.
- Upsert/reconcile reconstruían las fechas entrantes, pero no el rollup anterior cuando una clave estable cambiaba de día.

Las reparaciones capturan fechas previas dentro de la transacción, reconstruyen `old ∪ new`, normalizan todos los formatos admitidos y mantienen blobs oversized fuera del parser JSON. Las nuevas regresiones pasan: Cursor 16/16 y UsageRepository 28/28. La re-revisión terminó en `ACCEPT` sin defecto residual concreto.

Objeción adversarial más fuerte: una optimización puede pasar sus pruebas y dejar una rama hermana más cara o incorrecta. Esa objeción quedó cerrada con casos composer/bubble, ISO/numeric-text/real, oversized y movimientos cross-day/upsert/reconcile.

## Mapa y límites

El mapa estático y su UI local se comprobaron en escritorio y 390×844: búsqueda, filtros, selección de flujo, detalle, zoom, fit y cierre con Escape. No hubo errores de consola ni desbordamiento horizontal. El intento de usar el validador Playwright local fue inconcluso porque Playwright no está instalado; no se añadió al producto.

No se modificaron credenciales ni bases de otros productos. Cursor y OpenCode siguen en lectura. No se hizo commit, push, publicación ni instalación de herramientas. Cambios previos ajenos en workflows, dependencias y `AGENTS.md` permanecen sin mezclar ni revertir.

## Veredicto

La implementación satisface los diez resultados de arquitectura y la ruta real que había dejado de cargar datos. `quality-obsessed` termina en `ACCEPT`: 20/20 tickets, gate final verde, mapa fresco y ningún P1 dentro del alcance.
