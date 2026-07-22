# Especificación de producto

Estado: base aprobada para implementación

Nombre formal aprobado: TokenUsage
Nombre interno transitorio: WOpenUsage
Plataforma: Windows 10 1903 o posterior, x64 y ARM64

La transición de nombre se hará después de cerrar el trabajo ya documentado con
`WOpenUsage`. Hasta entonces no cambian ensamblados, namespaces, identidad MSIX,
recursos, ejecutables ni textos del prototipo. El cambio final tendrá una tarea
propia para evitar una migración parcial.

## Objetivo

Dar a una persona una vista rápida y fiable de la cuota restante, el próximo reinicio, los tokens y el gasto reciente de sus herramientas de IA. La app usa sesiones ya abiertas y datos locales cuando existe un contrato seguro. No requiere una cuenta propia.

## Usuarios

- Persona que usa Codex, Claude Code u otras herramientas de IA a diario.
- Equipo de soporte que necesita saber qué cliente se quedó sin cuota y cuándo se restablece.
- Automatización local que consume un contrato JSON de solo lectura.

## Resultado principal

Desde el icono de bandeja, el usuario abre un panel y ve en menos de dos segundos:

- qué proveedores tienen datos;
- cuánto se usó o queda;
- cuándo se reinicia cada ventana;
- si el ritmo actual agotará la cuota antes del reinicio;
- cuánto uso y gasto local se observó cuando la cuota no está disponible;
- cuándo se tomó el dato y qué fuente lo produjo.

## Reglas de producto

1. La app no crea una cuenta ni ofrece un login de proveedor.
2. La detección local no hace red.
3. Cada llamada remota requiere una sesión existente o una clave que el usuario agregó de forma explícita.
4. La app no guarda copias de credenciales que pertenecen a otra herramienta.
5. Un dato estimado, local, parcial o vencido lleva una etiqueta visible.
6. Un fallo conserva el último valor válido y muestra su edad.
7. Un valor ausente aparece como `Sin datos`; no se inventa cero.
8. Un proveedor se publica solo tras cerrar pruebas técnicas, seguridad y uso permitido.
9. Cuota, uso observado y gasto son capacidades independientes; una tarjeta puede tener una, dos o tres.
10. Los lectores locales no abren archivos de autenticación ni indexan prompts, respuestas, herramientas o comandos.

## Superficies

### Bandeja

El icono resume el peor estado de las métricas elegidas:

| Estado | Tratamiento |
|---|---|
| Normal | Icono base |
| Cerca del límite | Marca ámbar |
| Agotado o error que requiere acción | Marca roja |
| Refrescando | Indicador breve y accesible |
| Sin datos | Icono neutro |

El tooltip muestra hasta tres líneas: proveedor, porcentaje restante y próximo reinicio. El clic principal abre o cierra el panel. El menú de contexto ofrece actualizar, ajustes y salir. Debe funcionar con mouse y teclado.

### Panel principal

Ventana sin marco, no redimensionable, alineada con el icono de bandeja y limitada al área visible del monitor. Ancho base: 320 DIPs. El alto sigue el contenido, con un mínimo de 200 DIPs y un máximo de 720 DIPs o 85 % del área de trabajo, el menor. El escalado a píxeles físicos usa el DPI del monitor.

Orden:

1. tarjeta `Gasto total` cuando haya una fuente apta;
2. tarjetas de proveedor;
3. pie fijo con identidad, antigüedad o actualización y acceso a opciones.

La ventana se oculta al perder foco. Ajustes y personalización se abren dentro del mismo panel. `Esc` vuelve una pantalla; otro `Esc` cierra.

El blanco visual elegido, sus fuentes y las correcciones obligatorias están en [Blanco visual del flyout](design/2026-07-21-selected-flyout.md).

### Tarjeta de proveedor

Cada tarjeta muestra:

- icono, nombre propio, plan y estado;
- métricas siempre visibles;
- bloque plegable para métricas secundarias;
- origen y hora del dato en tooltip o detalle;
- aviso corto de credencial, red, throttle, dato vencido o cobertura parcial;
- acción para actualizar solo ese proveedor.

La cabecera de detalle indica las capacidades disponibles:

- `Cuota`: límite, restante y reinicio informados por una fuente apta;
- `Uso local`: actividad observada solo en este equipo;
- `Gasto`: coste informado o estimado, con su etiqueta.

Tipos de fila:

- barra limitada con usado/restante y reinicio;
- valor simple para saldo, gasto o tokens;
- insignia para plan o estado;
- tendencia de 30 días;
- texto de diagnóstico corto.

Un clic sobre `Usado` o `Restante` cambia el modo en toda la app. Un clic sobre el tiempo alterna cuenta regresiva y fecha exacta.

### Uso y gasto

Se muestra cuando al menos un proveedor ofrece tokens o gasto con cobertura conocida.

- periodos rápidos: hoy, ayer, 7 días, 30 días y mes actual;
- métricas: costo, costo por millón de tokens y tokens;
- anillo por agente, total y leyenda;
- desglose por agente y modelo;
- coste informado separado de coste estimado;
- modelos sin precio y porcentaje cubierto;
- detalle del origen y de estimaciones;
- estado vacío cuando el periodo no tiene datos.

El gasto estimado a tarifas API no se presenta como factura de una suscripción.

La primera versión no agrupa por proyecto, sesión, tarea o comando. Esas vistas exigirían guardar más metadatos y quedan fuera del motor pequeño.

### Personalización

- activar o desactivar proveedores;
- ordenar proveedores;
- ordenar métricas;
- mover métricas entre siempre visible y bajo demanda;
- ocultar una métrica;
- elegir hasta dos métricas para el resumen de bandeja;
- deshacer cambios durante la sesión;
- restablecer un proveedor o todo, con confirmación para todo.

### Ajustes

| Grupo | Opciones MVP |
|---|---|
| General | iniciar con Windows, atajo global, refresco manual |
| Apariencia | sistema/claro/oscuro, densidad, transparencia, usado/restante, hora relativa/exacta |
| Proveedores | detección, activación, estado y fuente |
| Avisos | umbrales, ritmo, datos vencidos y fallo de credencial |
| Red | proxy del sistema y prueba de conexión |
| Privacidad | API local, acceso por Origin, telemetría, exportar o borrar datos |
| Diagnóstico | versión, logs, caché, copiar informe sin secretos |
| Actualización | canal, versión y buscar actualización |

La telemetría queda apagada al instalar. El usuario debe confirmar cualquier futura opción de métricas.

### CLI

Ejecutable propio, por ejemplo `wusage.exe` tras cerrar el nombre:

```text
wusage limits
wusage limits codex
wusage limits --force --format json
wusage usage --days 30 --format json
wusage providers
wusage doctor
```

La CLI comparte proveedores, caché y modelos con la app. Puede leer datos sin que el panel esté abierto. Códigos de salida:

- `0`: respuesta válida, incluso con datos vencidos marcados;
- `2`: uso o argumentos inválidos;
- `4`: no se obtuvo ningún dato útil.

### API local

Apagada al instalar. Al activarla expone:

- `GET /v1/limits`;
- `GET /v1/limits/{providerId}`;
- `GET /v1/usage?days=30`;
- `GET /v1/usage/{providerId}?days=30`;
- `GET /v1/health`.

Requiere `Authorization: Bearer <token>`. El token se crea al activar la función, se guarda en Credential Locker y puede rotarse. No se expone en pantalla salvo una acción con confirmación.

## Modelo de estado visible

Cada proveedor está en uno de estos estados:

| Estado | UI | Acción |
|---|---|---|
| Detectando | Skeleton breve | Esperar |
| Disponible | Datos y hora | Ninguna |
| Refrescando con caché | Datos anteriores + progreso | Esperar o cancelar lote |
| Sin credencial | Explicación y ruta para abrir la herramienta | Iniciar sesión en la herramienta original |
| Tipo de cuenta no apto | Explicación | Ver detalle |
| Sin datos | Filas con `Sin datos` | Actualizar o abrir ayuda |
| Parcial | Datos + aviso de cobertura | Ver fuente |
| Vencido | Último valor + edad | Actualizar |
| Throttle | Último valor + próximo intento | Esperar |
| Sin red | Último valor + estado de red | Reintentar |
| Error de formato | Último valor + informe | Copiar diagnóstico |
| Bloqueado por política | Tarjeta informativa sin acceso | Consultar estado del proveedor |

## Ritmo de uso

Para una métrica limitada:

- `fracciónUsada = usado / límite`;
- `fracciónTiempo = tiempoTranscurrido / duraciónVentana`;
- la evaluación comienza tras una muestra mínima y un tiempo mínimo;
- azul: uso con margen;
- ámbar: uso cerca del ritmo de agotamiento;
- rojo: proyección de agotamiento antes del reinicio.

El cálculo, los umbrales y el reloj se prueban con una fuente de tiempo inyectable. La UI evita una predicción cuando faltan duración, inicio o límite.

## Refresco y caché

- detección local en paralelo al primer inicio;
- último snapshot válido cargado antes de la primera llamada de red;
- refresco remoto al iniciar cada sesión de la app;
- cadencia base de cinco minutos;
- actualización manual que ignora TTL;
- timeout y cancelación por proveedor;
- backoff con jitter para throttle y fallos transitorios;
- un proveedor lento no bloquea la publicación de los demás;
- escritura atómica del snapshot tras validarlo;
- datos con más de diez minutos marcados como vencidos por defecto.

El intervalo podrá cambiar tras medir carga y contratos. Nunca se acorta por debajo de la política del proveedor.

## Detección inicial

La primera ejecución revisa en local, sin leer el contenido secreto:

- ejecutables conocidos en `PATH` y rutas de instalación;
- carpetas de datos conocidas;
- variables de entorno que cambian la ruta;
- presencia de archivos o bases de credenciales;
- claves manuales propias ya guardadas.

Activa solo los proveedores con una ruta apta. Si ninguno aparece, muestra Codex y Claude como opciones guiadas, sin afirmar que están conectados.

## Codex MVP

Requisitos:

- localizar `codex` de forma segura;
- iniciar `codex app-server --stdio` sin ventana de consola;
- completar el handshake;
- leer límites y uso con métodos estables;
- no invocar login, logout, consumo de reset ni una solicitud de modelo;
- no leer ni copiar el token;
- parar el proceso hijo al salir;
- tolerar actualización del CLI y campos nuevos;
- explicar API-key-only o cuenta sin límites ChatGPT;
- usar logs locales solo para detalle que el método oficial no entregue.

## Claude inicial

Alcance permitido en la primera versión:

- detectar `CLAUDE_CONFIG_DIR` y el directorio por defecto;
- leer logs de sesiones para tokens y tendencia;
- calcular gasto estimado con catálogo versionado;
- marcar sesiones no persistidas como fuera de cobertura;
- no leer ni usar el OAuth de suscripción para una llamada remota distribuida.

La cuota en vivo se activa después del gate definido en la matriz de proveedores.

## Accesibilidad

- navegación completa por teclado;
- orden de foco estable;
- nombres y estados para lector de pantalla;
- alto contraste sin depender de color;
- mínimo de 44 DIPs en acciones principales;
- texto a 200% sin corte de valores críticos;
- animación reducida según el sistema;
- tooltip accesible por foco, no solo por hover.

## Rendimiento

- panel visible desde caché en menos de 500 ms en hardware de referencia;
- refresco no bloquea el hilo UI;
- uso inactivo menor a 150 MB como meta inicial;
- CPU inactiva cercana a cero;
- sin polling de archivos continuo; usar lote o watcher con debounce;
- inicio del proceso Codex bajo demanda y reutilización mientras la app corre.

Las cifras se convierten en gates tras medir el primer vertical slice.

## Privacidad y datos

Persistidos:

- configuración y orden;
- snapshots sin credenciales;
- índices de scanner y agregados diarios;
- eventos normalizados sin contenido durante el periodo de retención;
- versión y tasas del catálogo usadas para cada estimación;
- token propio de API local en Credential Locker;
- logs rotados sin valores de cuota por defecto.

No persistidos:

- tokens de Codex, Claude u otra herramienta;
- contenido de prompts o respuestas;
- nombres de herramientas, comandos, tareas o archivos;
- nombre de proyecto y ruta de trabajo en la primera versión;
- correo de cuenta salvo que una función futura lo requiera y el usuario la acepte;
- rutas de proyecto en informes normales.

## Fuera del MVP

- login propio;
- sincronización cloud entre equipos;
- consumo de créditos de reinicio Codex;
- soporte de endpoints privados sin gate;
- panel web;
- widget siempre visible en el escritorio;
- instalación sin paquete;
- importación automática de secretos de navegadores.
- índice de transcripciones, tareas, herramientas o comandos.
- cuota Grok o Antigravity mediante endpoints, tokens o TUI privados.

## Criterio de éxito del MVP

- instalación y desinstalación MSIX limpias;
- icono de bandeja fiable tras reinicio de Explorer;
- flyout correcto en varias pantallas y DPI;
- Codex muestra cuota, reinicio y uso con sesión existente;
- cero login o copia de token;
- caché y estados de fallo comprobados;
- tema claro, oscuro y alto contraste;
- teclado y lector de pantalla cubren el flujo principal;
- suite de unidad, contrato, integración y UI verde;
- paquete x64 firmado en beta y prueba ARM64 antes de estable;
- licencia MIT y avisos de tercero incluidos;
- nombre e identidad final aprobados.

La beta de gasto agrega Claude, Grok Build y OpenCode con fixtures, totales diferenciales y cobertura visible. Antigravity CLI requiere primero una base local real, fixtures sanitizados y un parser que no use su login.
