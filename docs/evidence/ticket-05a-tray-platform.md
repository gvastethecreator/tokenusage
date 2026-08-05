# Ticket 05A: plataforma de bandeja y posición

Fecha: 2026-07-21
Estado: aceptado como corte de plataforma; falta prueba runtime en Ticket 05C.

## Alcance

Este corte añade:

- host de bandeja Win32 propio con `Shell_NotifyIconW`;
- menú nativo con comandos tipados y textos entregados por la app;
- activación por mouse y teclado mediante `NOTIFYICON_VERSION_4`;
- cálculo puro de posición según área de trabajo, DPI y borde de taskbar;
- liberación explícita del icono, menú y subclass de ventana.

No conecta aún el host con la ventana WinUI. Tampoco cubre recuperación tras reinicio de Explorer; Ticket 06 posee `TaskbarCreated`.

## Delegación y revisión

Dos ejecuciones Grok Build en modo edición terminaron en `cancelled` antes de cambiar archivos por el límite de permiso de edición en este host Windows. La misma tarea se reanudó en modo de solo lectura y produjo una propuesta completa:

- resultado: `.scratch/agent-cli-delegation/grok-build/runs/2026-07-22T01-28-10-122Z-plan-ce2273f7/result.json`;
- estado: `EndTurn`;
- turnos: 4;
- costo registrado: USD 0.2451008.

La revisión del padre no aplicó ese texto de forma directa. Corrigió:

- expectativas izquierda/arriba que contradecían el área visible;
- huecos entre la ventana y el borde útil del monitor;
- desborde cuando la ventana pedida era mayor que el área de trabajo;
- duplicación de activación por mezclar mensajes antiguos con versión 4;
- riesgo de ejecutar el subclass fuera del hilo dueño de la ventana;
- búsqueda de DLL nativa fuera de `System32`.

La revisión de firmas se contrastó con la documentación vigente de Microsoft para [Shell_NotifyIcon](https://learn.microsoft.com/windows/win32/api/shellapi/nf-shellapi-shell_notifyiconw), [Shell_NotifyIconGetRect](https://learn.microsoft.com/windows/win32/api/shellapi/nf-shellapi-shell_notifyicongetrect) y [RemoveWindowSubclass](https://learn.microsoft.com/windows/win32/api/commctrl/nf-commctrl-removewindowsubclass).

## Pruebas

```text
dotnet test tests/TokenUsage.Platform.Windows.Tests/TokenUsage.Platform.Windows.Tests.csproj -p:Platform=x64 --nologo
15 superadas, 0 fallidas

dotnet test tests/TokenUsage.Architecture.Tests/TokenUsage.Architecture.Tests.csproj -p:Platform=x64 --nologo
3 superadas, 0 fallidas

dotnet build src/TokenUsage.Platform.Windows/TokenUsage.Platform.Windows.csproj -p:Platform=ARM64 --nologo
0 advertencias, 0 errores

dotnet build TokenUsage.slnx -p:Platform=x64 --nologo
0 advertencias, 0 errores
```

Las pruebas de posición cubren los cuatro bordes, DPI 150 %, tamaño mayor que el monitor, fallback sin rectángulo de icono, coordenadas negativas y entradas inválidas.

## Límite del claim

La biblioteca compila en x64 y ARM64 y el cálculo tiene pruebas. Aún no se ha probado un icono visible, el menú real, el foco ni la ventana en ejecución. Ticket 05C debe aportar ese recibo antes de cerrar Ticket 05.
