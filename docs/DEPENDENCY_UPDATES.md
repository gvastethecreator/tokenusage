# Dependency maintenance

TokenUsage uses .NET, WinUI, and NuGet. Package versions live in the project
files under `src/` and `tests/`. GitHub Actions revisions live in
`.github/workflows/`. Read those files for the configured versions.

## Check for updates

Run these commands from the repository root on Windows with the build tools
listed in [CONTRIBUTING.md](../CONTRIBUTING.md):

```powershell
.\scripts\deps-check.ps1
.\scripts\audit.ps1
```

Both commands inspect every managed project in `TokenUsage.slnx`.
`deps-check.ps1` lists direct package updates and exits with
code 1 when updates are available. The app owns the SDK packages used by the
native package builder. `audit.ps1` reports known vulnerabilities,
including transitive dependencies, and exits with code 1 if it finds one or
cannot complete a project audit.

## Apply an update

Keep updates tied to a compatibility, security, or maintenance need. Review
the package's release notes, update the owning project, and use the
[contributor testing guide](CONTRIBUTOR-TESTING.md) to select evidence. Run the
required repository check before review.

Keep the app's WinApp helper marked `PrivateAssets="all"` so it does not flow
into consumers' dependency graphs. Local research and build output are outside
the active solution and are not part of the dependency update.

The app references the Windows App SDK WinUI and Runtime packages at the same
version. `scripts/package.ps1` uses its restored SDK tools and framework metadata
to package both executables without a separate managed restore graph.
WinUI brings the Foundation and Interactive
Experiences components used by the app. Keep those references aligned; the
aggregate SDK package also includes unrelated AI, ML, Search, and Widgets
components. Portable releases include the exact restored .NET runtime pack
once, under `.dotnet`, for both executables.

## November 9 maintenance

The update check found five newer stable packages. The app and packaging project
now use Windows App SDK WinUI/Runtime `2.6.5` and Windows SDK BuildTools
`10.0.28000.2957`. The app's private WinApp helper is `0.8.0`, and Core uses
Microsoft.Data.Sqlite `10.0.14`. The installed .NET SDK was not changed.

Windows App SDK `2.6.5` includes rendering, window composition, backdrop, and
package installation fixes. See the [release notes](https://github.com/microsoft/WindowsAppSDK/releases/tag/v2.6.5).
The [WinApp release notes](https://github.com/microsoft/winappCli/releases/tag/v0.8.0)
describe build and tooling fixes. The stable
[BuildTools](https://www.nuget.org/packages/Microsoft.Windows.SDK.BuildTools/10.0.28000.2957)
and [SQLite](https://www.nuget.org/packages/Microsoft.Data.Sqlite/10.0.14)
packages keep the existing target frameworks. The SQLite source tags `v10.0.13`
and `v10.0.14` resolve to the same source commit; this is a package servicing
update, not a TokenUsage storage format change.

Workflow pins now use [setup-dotnet `v6.1.0`](https://github.com/actions/setup-dotnet/releases/tag/v6.1.0)
and [upload-artifact `v7.0.2`](https://github.com/actions/upload-artifact/releases/tag/v7.0.2),
with commit hashes resolved from the official tags. Job structure and permissions
are unchanged. Local checks do not establish that a hosted workflow has run.

## November 12 maintenance

[Microsoft.Data.Sqlite `11.0.0`](https://www.nuget.org/packages/Microsoft.Data.Sqlite/11.0.0)
passed the managed tests but failed the required DesktopBridge package restore
with `NU1701`. Its [documented compatibility changes](https://learn.microsoft.com/en-us/ef/core/what-is-new/ef-core-11.0/breaking-changes)
remove .NET Standard support. The managed projects target .NET 10, but the
packaging restore targets `UAP10.0.17763` and cannot use the new `net10.0` assets
without fallback warnings. Core therefore retains `10.0.14`, whose .NET Standard
assets support the existing package restore. No warning was suppressed, and no
SDK, target framework, native SQLite bundle, or storage format was changed.

NuGet also reports `Microsoft.NET.Test.Sdk` `18.12.0`. Its package metadata is
available, but the official repository did not expose its release/tag or the
source commit named by that metadata during this review. The five test projects
retain `18.11.0` until those changes can be reviewed. This is a deferred update,
not a vulnerability finding. The seven GitHub Actions releases still match the
verified pins from the previous pass.

## November 13 maintenance

All five test projects now use Microsoft.NET.Test.Sdk `18.12.0`. The
[official release](https://github.com/microsoft/vstest/releases/tag/v18.12.0)
is available and includes discovery and test-event fixes without changing the
default test ID algorithm. Its .NET 8 assets support the existing .NET 10 test
targets. The package metadata still names a source commit that GitHub does not
expose, so the exact package-to-tag source correspondence remains unverified.
The release and package compatibility review support this update. SQLite
`11.0.0` remains deferred for the packaging failure recorded above.

The installed .NET SDK `10.0.403` contains VSTest CLI `18.7.0`. Microsoft's
[CVE-2026-105544 advisory](https://github.com/dotnet/announcements/issues/517)
lists this SDK as affected and `10.0.404` as its patched servicing version.
Updating Test SDK packages does not replace the SDK's VSTest runner. The NuGet
project audit does not cover this installed toolchain vulnerability. SDK
installation requires separate authorization and was not performed in this pass.

## November 14 maintenance

The user's follow-up authorized the SDK update. The official Microsoft installer
applied .NET SDK `10.0.404`; the repository now selects it, and its VSTest runner
reports `18.7.1-hotfix-26529-119`, the patched version listed in the advisory.
WinGet reported success, while the installer returned `3010`: Windows still
requires a restart to finish installation. No restart was performed. Older SDK
installations remain present; this check covers the SDK selected for TokenUsage.

The initial scan found only SQLite `11.0.0` pending. Further inspection
confirmed a DesktopBridge restore-model conflict, not a demonstrated SQLite
runtime failure: the package copies SQLite from the .NET 10 app and CLI publish
outputs, but its own restore uses UAP and rejects the new assets with `NU1701`.
Updating the SDK does not change those Visual Studio targets.

The authorized migration replaces the DesktopBridge project with
`scripts/package.ps1` and updates Microsoft.Data.Sqlite to `11.0.0`. The script
publishes the app and CLI, then uses MakePri, MakeAppx, and SignTool from the
app's restored Windows SDK BuildTools package. The source manifest now points
both the app and its notification COM server to the packaged app subfolder.
The CLI alias and package identity remain the same. Store uploads contain a
signed bundle and symbols; portable releases retain their shared private runtime.
No warning suppression or fallback restore target was added.

The required gate passed with 1240 tests and an x64 Release package. The obsolete
DesktopBridge import test was removed; the retained manifest test now also checks
the notification COM server. Final x64 Release/Store and Debug artifacts passed
content inspection. Both executables include .NET `10.0.15` and SQLite `11.0.0`.
The packaged PRI is byte-identical to the earlier package, and every referenced
resource path exists. Store bundle and package signatures passed cryptographic
checks with a temporary construction certificate. This does not establish public
certificate trust, Store acceptance, or installed application behavior.

ARM64 app and CLI compilation passed, but packaging stopped because this machine
lacks both VCLibs ARM64 framework packages. The required Visual Studio component
is documented in [CONTRIBUTING.md](../CONTRIBUTING.md). Installing that component
requires authorization; no framework dependency was omitted to bypass the failure.

## November 17 maintenance

NuGet reports no newer stable direct dependencies across the 13 managed projects.
Windows rebooted after the SDK update. The selected SDK remains `10.0.404`,
and the .NET host and patched VSTest runner match their recorded binary hashes.

## November 18 maintenance

Update the WinUI and Runtime package references together to
[Windows App SDK 2.7.0](https://github.com/microsoft/WindowsAppSDK/releases/tag/v2.7.0).
This release fixes WinUI input, style and dispatcher behavior. The project keeps
its split SDK references, and the package builder derives the minimum runtime
version from the restored framework package.

The first hosted x64 package build with WinUI 2.7.0 reported
[`CsWinRT1032`](https://github.com/microsoft/CsWinRT/blob/master/src/Authoring/WinRT.SourceGenerator/AnalyzerReleases.Shipped.md)
for collection expressions with interface targets. The affected app call
sites now pass concrete arrays. The report keeps its read-only periods.
