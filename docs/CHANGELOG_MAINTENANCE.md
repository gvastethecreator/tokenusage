# Changelog de mantenimiento

## 2026-08-13

- Actualizado `Microsoft.Windows.SDK.BuildTools.WinApp` a 0.6.0, con
  `PrivateAssets="all"` en la app.
- Actualizadas las GitHub Actions a checkout v7.0.1, setup-dotnet v6.0.0,
  setup-msbuild v3 y upload-artifact v7.0.1 (Node 24).

## 2026-08-12

- Actualizados BuildTools, SQLitePCLRaw, Microsoft.Data.Sqlite, Test SDK y
  adapter xUnit a las versiones disponibles.
- Añadidos scripts `deps-check.ps1` y `audit.ps1` para proyectos activos.
- Actualizados README, `.gitignore` y tasks con comandos cortos y emojis.
- Verificados 930 tests y build WinUI x64; packaging MSIX queda condicionado a
  MSBuild/Workload DesktopBridge de Visual Studio.
