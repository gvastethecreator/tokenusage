# Auditoría de calidad

Fecha: 2026-08-12

| Área | Resultado | Evidencia |
| --- | --- | --- |
| Dependencias | PASS | `scripts/deps-check.ps1`, sin outdated en proyectos activos |
| Seguridad | PASS | `scripts/audit.ps1`, sin vulnerabilidades reportadas |
| Compilación | PASS | App WinUI net10 x64 Debug, 0 warnings/0 errors |
| Tests | PASS | 930 tests activos (85 arquitectura, 204 core, 104 CLI, 394 providers, 143 Windows) |
| Packaging | DIFERIDO | `wapproj` requiere MSBuild/Workload DesktopBridge de Visual Studio |
| Runtime visual | DIFERIDO | requiere ejecución interactiva de WinUI y captura humana |

`.gitignore` cubre salidas .NET, paquetes y resultados de test. Se preservan
`.scratch`, `.reference`, `.snapshots` y artefactos de evidencia; no son residuos
operativos del producto.
