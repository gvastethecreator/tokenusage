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

Both commands inspect every project in `TokenUsage.slnx`, including the Windows
packaging project. `deps-check.ps1` lists direct package updates and exits with
code 1 when updates are available. `audit.ps1` reports known vulnerabilities,
including transitive dependencies. Review its output; a successful command
does not mean that no vulnerabilities were reported.

## Apply an update

Keep updates tied to a compatibility, security, or maintenance need. Review
the package's release notes, update the owning project, and use the
[contributor testing guide](CONTRIBUTOR-TESTING.md) to select evidence. Run the
required repository check before review.

Keep the app's WinApp helper marked `PrivateAssets="all"` so it does not flow
into consumers' dependency graphs. Local research and build output are outside
the active solution and are not part of the dependency update.
