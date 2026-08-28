# M1 readiness on this machine

Date: 2026-07-21

Status: ready to run after the tracker and tickets are approved

## Question

Can this machine create, compile, and launch the first packaged WinUI 3 scaffold without installing or changing tools before M1?

## Answer

Yes. .NET 10, the WinUI templates, `winapp`, Developer Mode, and Visual Studio with MSBuild are present. M1 can start with `winui-mvvm`, an `x64` build, and launch with package identity.

## Sources

- [Get started with WinUI 3 from the CLI](https://learn.microsoft.com/en-us/windows/apps/winui/winui3/create-your-first-winui3-app)
- [WinUI start paths](https://learn.microsoft.com/en-us/windows/apps/get-started/winui-get-started-overview)
- [Windows App SDK and its channels](https://learn.microsoft.com/en-us/windows/apps/windows-app-sdk/)
- [Windows App SDK versions](https://learn.microsoft.com/en-us/windows/apps/get-started/versioning-overview)
- [Using `winapp` with .NET](https://learn.microsoft.com/en-us/windows/apps/dev-tools/winapp-cli/guides/dotnet)

## Local evidence

| Check | Observed result |
|---|---|
| `.NET SDK` | `10.0.301` |
| template | `winui-mvvm` available |
| `winapp` | `0.4.0` |
| Developer Mode | enabled |
| Visual Studio | Community 18 with MSBuild detected |
| initial architecture | `x64` |

No tool was installed or updated during the check.

## M1 decisions

- Create the app with `dotnet new winui-mvvm -n TokenUsage.App`.
- Keep `Package.appxmanifest` and the package identity.
- Use the stable Windows App SDK channel that the template resolves; do not pin Preview or Experimental.
- Build and launch with `BuildAndRun.ps1` or the `winapp` package path; never open the packaged executable directly.
- Compile for a concrete architecture. The first test is `x64`; `ARM64` is validated before stable.
- Add packages without a manual version and check restore when each dependency is added.

## Uncertainty

- The presence of the tools does not prove that a new scaffold compiles; that is the first M1 acceptance criterion.
- Production signing and Publisher ID remain human decisions. A development identity is enough for local smoke.
- The template can resolve a newer stable version than the one cited today. The created `.csproj` will be the repo's reproducible baseline.

## Plan change

M1 no longer needs an install task. It starts with scaffold, build, and launch. If any of those steps fails, record the exact error and stop before adding architecture or providers.
