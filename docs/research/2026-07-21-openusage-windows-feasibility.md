# Viabilidad de una implementación Windows de OpenUsage

Fecha: 2026-07-21

Estado: investigación cerrada para iniciar el diseño

Upstream fijado: `robinebers/openusage@9d2bf09f10e21f769494a525a9d65c84d7aeb1df`

## Pregunta

¿Podemos crear una app Windows nativa con la interfaz y funciones centrales de OpenUsage que muestre el uso restante desde las sesiones ya abiertas en el equipo, sin una cuenta propia ni un nuevo login?

## Respuesta

Sí para el producto y para Codex. La UI, el ciclo de refresco, la caché, el uso local, la CLI y una API local tienen equivalentes sólidos en Windows.

La ruta Codex quedó probada con la interfaz oficial `codex app-server`. Claude permite calcular uso desde logs locales, pero su cuota en vivo carece de una interfaz pública de solo lectura. Esa función queda sujeta a permiso o contrato público del proveedor. Los demás proveedores requieren una prueba aislada antes de prometer soporte.

La bandeja Windows no admite una tira persistente de texto y barras junto al icono. El diseño usará un icono de estado, un tooltip corto y un flyout nativo al hacer clic.

## Fuente estudiada

Se clonó OpenUsage en `.reference/openusage` y se fijó el análisis al SHA indicado. En esa revisión hay 237 archivos Swift y 138 archivos de prueba. El repo raíz ignora este clon.

La [licencia MIT](https://github.com/robinebers/openusage/blob/9d2bf09f10e21f769494a525a9d65c84d7aeb1df/LICENSE) permite copiar y cambiar el código si se conserva el aviso. La [política de marca](https://github.com/robinebers/openusage/blob/9d2bf09f10e21f769494a525a9d65c84d7aeb1df/TRADEMARK.md) reserva el nombre, el logo y la identidad visual. El producto necesita nombre, icono y texto legal propios.

## Qué hace OpenUsage

El [README fijado](https://github.com/robinebers/openusage/blob/9d2bf09f10e21f769494a525a9d65c84d7aeb1df/README.md) y sus documentos describen:

- panel por proveedor con porcentajes usados o restantes y cuenta regresiva;
- ritmo previsto frente al tiempo del periodo;
- gasto y tokens de hoy, ayer y 30 días;
- gráfico de tendencia y detalle por modelo;
- detección local de proveedores, orden, visibilidad y hasta dos métricas destacadas;
- caché persistente de cinco minutos y refresco paralelo;
- último resultado válido durante fallos, con aviso de datos vencidos;
- atajo global, inicio con el sistema, proxy, tema, densidad, avisos y actualizaciones;
- CLI de una ejecución y servidor HTTP local.

El detalle está en [dashboard](https://github.com/robinebers/openusage/blob/9d2bf09f10e21f769494a525a9d65c84d7aeb1df/docs/dashboard.md), [refresco](https://github.com/robinebers/openusage/blob/9d2bf09f10e21f769494a525a9d65c84d7aeb1df/docs/refreshing.md), [ajustes](https://github.com/robinebers/openusage/blob/9d2bf09f10e21f769494a525a9d65c84d7aeb1df/docs/settings.md), [CLI](https://github.com/robinebers/openusage/blob/9d2bf09f10e21f769494a525a9d65c84d7aeb1df/docs/cli.md) y [API local](https://github.com/robinebers/openusage/blob/9d2bf09f10e21f769494a525a9d65c84d7aeb1df/docs/local-http-api.md).

## Patrón de arquitectura upstream

El [documento de arquitectura](https://github.com/robinebers/openusage/blob/9d2bf09f10e21f769494a525a9d65c84d7aeb1df/docs/architecture.md) usa una raíz de composición, un runtime por proveedor y un modelo común consumido por la app, CLI y HTTP.

Cada proveedor sigue esta secuencia:

1. Detectar si existe una credencial local sin hacer red.
2. Leer una fuente de autenticación o una clave que pertenece a la app.
3. Consultar límites o uso con un cliente propio del proveedor.
4. Mapear la respuesta a un snapshot común.
5. Guardar solo el snapshot y el estado de refresco.

Los modelos fijados en [ProviderSnapshot](https://github.com/robinebers/openusage/blob/9d2bf09f10e21f769494a525a9d65c84d7aeb1df/Sources/OpenUsage/Models/ProviderSnapshot.swift) y [MetricLine](https://github.com/robinebers/openusage/blob/9d2bf09f10e21f769494a525a9d65c84d7aeb1df/Sources/OpenUsage/Models/MetricLine.swift) cubren progreso, valores, insignias, gráficos y texto. Este patrón sirve para Windows y evita que la UI conozca archivos, tokens o JSON de proveedores.

## Paridad en Windows

| Capacidad | Ruta Windows | Decisión |
|---|---|---|
| Flyout | WinUI `Window` + `AppWindow`, HWND y posición junto al área de trabajo | Una ventana sin marco, de tamaño fijo, que se oculta al perder foco |
| Bandeja | Win32 `Shell_NotifyIconW` | Interop interno; icono, tooltip, clic y menú accesible |
| Texto junto al icono | La API de bandeja no ofrece esa superficie | Estado por icono; resumen en tooltip y flyout |
| Instancia única | Windows App SDK AppLifecycle | Redirigir activaciones a la instancia principal |
| Avisos | `AppNotificationManager` | Alertas de cuota, ritmo, credencial y datos vencidos |
| Arranque | `StartupTask` con identidad de paquete | Ajuste explícito del usuario |
| Secretos propios | Windows Credential Locker | Solo claves que el usuario entrega a esta app |
| Distribución | MSIX de confianza plena | `x64` primero, `ARM64` antes de estable |
| Actualización | Store o App Installer firmado | Canal estable y beta separados |

Fuentes de Microsoft: [empaquetado](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/packaging/), [contenedor y confianza de MSIX](https://learn.microsoft.com/en-us/windows/msix/msix-containerization-overview), [ventanas con AppWindow](https://learn.microsoft.com/en-us/windows/apps/develop/ui/manage-app-windows), [obtención de HWND](https://learn.microsoft.com/en-us/windows/apps/develop/ui/retrieve-hwnd), [Shell_NotifyIconW](https://learn.microsoft.com/en-us/windows/win32/api/shellapi/nf-shellapi-shell_notifyiconw), [notificaciones](https://learn.microsoft.com/en-us/windows/apps/develop/notifications/), [instancia única](https://learn.microsoft.com/en-us/windows/apps/windows-app-sdk/applifecycle/applifecycle-instancing) y [Credential Locker](https://learn.microsoft.com/en-us/windows/apps/develop/security/credential-locker).

## Ruta Codex comprobada

El [`codex app-server`](https://github.com/openai/codex/blob/a26f219f6788c951dcb3bf435fab4c6d0f4d2f40/codex-rs/app-server/README.md) es la interfaz que Codex usa para clientes como su extensión de VS Code. Su transporte estable por defecto es JSONL sobre `stdio`.

El protocolo requiere:

1. iniciar `codex app-server --stdio`;
2. enviar `initialize` con nombre, título y versión del cliente;
3. enviar la notificación `initialized`;
4. pedir `account/rateLimits/read` para cuota y reinicios;
5. pedir `account/usage/read` para resumen y buckets diarios.

Codex conserva el login, el refresh token y la llamada remota. La app procesa campos tipados y tolera campos nuevos. La documentación marca esos dos métodos como parte estable de la superficie de cuenta.

### Prueba local

Se ejecutó un smoke test de solo esquema contra el `codex` instalado y la sesión existente. La prueba no imprimió tokens, correo ni cifras de uso.

Resultados:

- `initialize`: correcto;
- `account/rateLimits/read`: correcto;
- grupos vistos: `rateLimits`, `rateLimitsByLimitId`, `rateLimitResetCredits`;
- `account/usage/read`: correcto;
- grupos vistos: `summary`, `dailyUsageBuckets`.

La implementación debe mantener un proceso supervisado, poner timeout a cada solicitud, validar el ID de respuesta y reiniciar el proceso tras cierre, cambio de binario o error de protocolo. El cliente debe declarar un `clientInfo.name` propio. Para despliegues empresariales, la documentación de Codex pide contactar a OpenAI para registrar el cliente en sus logs de cumplimiento.

### Fuente local para gasto Codex

OpenUsage también lee `sessions` y `archived_sessions` bajo `CODEX_HOME` para medir tokens y estimar gasto. Su [documento Codex](https://github.com/robinebers/openusage/blob/9d2bf09f10e21f769494a525a9d65c84d7aeb1df/docs/providers/codex.md) explica zonas horarias, deduplicación de subagentes y precios.

El método oficial `account/usage/read` reduce el trabajo inicial. Los logs siguen siendo útiles para detalle por modelo y para comparar resultados. El MVP debe usar el método oficial para totales y añadir el scanner local cuando una prueba diferencial pruebe su valor.

## Ruta Claude y límite de lanzamiento

La [documentación de autenticación de Claude Code](https://code.claude.com/docs/en/authentication) indica que Windows guarda credenciales en `%USERPROFILE%\.claude\.credentials.json`, o bajo `CLAUDE_CONFIG_DIR`. También define el orden de fuentes de autenticación.

OpenUsage lee esa credencial, consulta un endpoint de cuota y rota tokens cuando hace falta. Además calcula gasto desde `projects`; su [documento Claude](https://github.com/robinebers/openusage/blob/9d2bf09f10e21f769494a525a9d65c84d7aeb1df/docs/providers/claude.md) describe ambas fuentes.

Claude Code no documenta un comando de cuota de solo lectura. Escribir una credencial rotada desde dos procesos puede invalidar la sesión. El changelog público también registra correcciones de carreras entre procesos.

La [guía legal de Claude Code](https://code.claude.com/docs/en/legal-and-compliance) reserva el OAuth de suscripciones para aplicaciones nativas de Anthropic y dirige a terceros hacia claves de API o proveedores cloud. Consultar cuota tiene menos alcance que enviar una solicitud de modelo, pero sigue usando una credencial de suscripción fuera del cliente oficial. Antes de distribuir esa función se requiere uno de estos contratos:

- interfaz pública de cuota de solo lectura;
- comando oficial de Claude Code que entregue la cuota;
- permiso escrito de Anthropic para este caso.

Mientras tanto, la app puede detectar logs Claude y mostrar uso local medido o costo estimado con una etiqueta clara. Esa vista no puede afirmar cuánto queda del plan.

## API local y privacidad

OpenUsage escucha en `127.0.0.1:6736` y publica CORS `*`. Su [nota de privacidad](https://github.com/robinebers/openusage/blob/9d2bf09f10e21f769494a525a9d65c84d7aeb1df/docs/local-http-api.md#cors-and-privacy) avisa que cualquier página abierta puede leer los snapshots mientras la app corre.

La versión Windows tendrá la API apagada al instalar. Al activarla:

- solo escuchará en `127.0.0.1`;
- exigirá un token aleatorio guardado en Credential Locker;
- rechazará peticiones con `Origin` salvo una lista explícita;
- tendrá límite de concurrencia, timeout y cuerpo máximo;
- excluirá rutas, tokens, correo y datos de cuenta no requeridos;
- registrará accesos sin incluir valores de cuota.

Un modo de compatibilidad con OpenUsage puede agregarse como opción con aviso de privacidad.

## Proveedores

El upstream anuncia Antigravity, Claude, Codex, Copilot, Cursor, Devin, Grok, OpenCode, OpenRouter y Z.ai. La investigación de sus adaptadores muestra tres tipos:

- interfaz oficial local: Codex;
- logs o bases locales: Claude, Codex, Cursor, Grok, OpenCode;
- endpoint privado con credencial reutilizada: Claude, Cursor, Copilot, Antigravity, Devin y Grok;
- clave manual: OpenRouter y Z.ai.

Cada proveedor privado necesita una prueba técnica, revisión de política y fixtures sanitizados. La [matriz de proveedores](../PROVIDER-MATRIX.md) fija el orden.

## Riesgos

| Riesgo | Efecto | Control |
|---|---|---|
| Endpoint privado cambia | Tarjeta sin datos | Adaptador aislado, contrato versionado, último valor válido y flag remoto |
| Dos procesos rotan un token | Cierre de sesión | Preferir interfaz oficial; no escribir credenciales ajenas en el MVP |
| Fuente local cambia de esquema | Gasto incompleto | Parser tolerante, fixtures por versión y aviso de cobertura |
| MSIX cambia rutas o permisos | Detección fallida | Pruebas dentro del paquete firmado, sin depender del directorio de trabajo |
| Explorer reinicia | Icono ausente | Volver a registrar el icono tras `TaskbarCreated` |
| Varias pantallas o escalas | Flyout fuera de pantalla | Posicionar por monitor, DPI y área de trabajo |
| Loopback accesible desde web | Fuga de cuota | API apagada, token, política de Origin y mínimo de datos |
| Marca upstream | Confusión o reclamo | Nombre, logo, paquete y avisos propios |
| Política del proveedor | Función no distribuible | Gate de lanzamiento por proveedor |

## Incertidumbre

- El protocolo Codex es estable hoy, pero el cliente debe comprobar la versión y tolerar campos extra.
- El aspecto exacto del flyout debe validarse en Windows 10 y 11, con tema oscuro, alto contraste y escalas 100–200%.
- No se leyó el contenido de ninguna credencial durante la investigación.
- Los proveedores privados quedan fuera de toda promesa de versión hasta cerrar su prueba y su revisión de uso permitido.

## Decisión

Iniciar un MVP Windows con shell WinUI, bandeja Win32, caché común y una integración Codex por `app-server`. Añadir uso local Claude como función separada si el scanner queda probado. Mantener la cuota Claude y los demás endpoints privados detrás de gates de proveedor. Empaquetar como MSIX, mantener telemetría y API local apagadas al instalar, y usar nombre e identidad propios.
