# MSIX CLI execution alias

Cutoff date: 2026-07-23

## Question

Can the current single-project MSIX publish `tokenusage.exe` as a real CLI without turning the WinUI app into a console process?

## Answer

Not with the current model. Single-project MSIX supports one executable.
TokenUsage needs two: `TokenUsage.App.exe` for WinUI and `tokenusage.exe` for
stdout, stderr, and exit codes. The cut must move packaging to a Windows
Application Packaging Project. Keep both application projects as x64/ARM64
references.

The alias must use `windows.appExecutionAlias`. It must point at the payload
`TokenUsage.Cli\tokenusage.exe`, declare `Windows.FullTrustApplication`, and
register `tokenusage.exe`. The WinUI executable stays the visual entry. A
`WinExe` does not meet the console contract.

## Primary sources

| Source | Fact used |
|---|---|
| [Single-project MSIX](https://learn.microsoft.com/en-us/windows/apps/windows-app-sdk/single-project-msix) | The single-project model supports one executable. Several executables need a Windows Application Packaging Project. |
| [Packaging extensions](https://learn.microsoft.com/en-us/windows/apps/desktop/modernize/desktop-to-uwp-extensions) | `windows.appExecutionAlias` accepts `Executable`, `EntryPoint="Windows.FullTrustApplication"`, and an alias that ends in `.exe`. |
| [Packaging project](https://learn.microsoft.com/en-us/windows/msix/desktop/desktop-to-uwp-packaging-dot-net) | A Windows Application Packaging Project can include several desktop apps. Reference platforms must match. |
| [Windows App SDK packaged deployment](https://learn.microsoft.com/en-us/windows/apps/windows-app-sdk/deploy-packaged-apps) | A separate `.wapproj` must declare the Windows App SDK package reference that produces the framework dependency. |

## Local state

- The current `Package.appxmanifest` belongs to `TokenUsage.App` and does not declare an alias.
- `TokenUsage.App` uses single-project MSIX.
- `TokenUsage.Cli` is a separate `Exe`, but its assembly is still named `TokenUsage.Cli`.
- Visual Studio Community 18 is installed and contains `Microsoft.DesktopBridge.props` and x64 MSBuild. The technical gate to create a `.wapproj` is available.

## Implementation decision

1. `25D1`: Test concurrent app and CLI reads against the cache and SQLite, including an active writer, cancellation, and intact files.
2. `25D2`: Create the packaging project, move the manifest there, include the app and the CLI, and register `tokenusage.exe`.
3. Validate the manifest and build x64/ARM64. Install and run the alias only through a signed package or a development identity. Never start the packaged executable by a direct path.

## Implemented result

`TokenUsage.Package` owns the manifest and references App and CLI. Debug x64,
Debug ARM64, and Release x64 builds produced both executables. A registered
dev Release started the app by AUMID and ran `tokenusage providers --format json`
through the Windows alias.
