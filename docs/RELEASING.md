# Release TokenUsage

TokenUsage publishes one signed MSIX package and one portable ZIP for each Windows architecture.

The portable ZIP contains the WinUI app, the CLI, .NET, and the Windows App SDK runtime. It does not require installation.

## Build a local release candidate

1. Make sure that `Directory.Build.props` contains the release version.
2. Run the release command from the repository root.

```powershell
.\scripts\release.ps1 -Platform x64 -Version 0.0.1
```

The command runs all tests and builds the MSIX package. Then it publishes the portable app and CLI.

The command writes these files to `artifacts\release`:

- `TokenUsage-<version>-win-<architecture>-portable.zip`
- A signed MSIX, or an MSIX with the `-unsigned` suffix
- `release-manifest.json`
- `SHA256SUMS.txt`

Use `-SkipTests` only after the same commit passes the complete release check.

## Portable data

The portable folder contains `TokenUsage.portable`. Keep this file beside the executable files.

The app and CLI store their data in the `Data` folder. The CLI executable is in `cli`.

Run `tokenusage.cmd` from the portable root to use the CLI. Move the complete TokenUsage folder to move its data.

The MSIX package uses its Windows `LocalState` folder. The portable build does not change or import the MSIX data.

## Sign the MSIX package

Set these environment variables before you run the release command:

```powershell
$env:TOKENUSAGE_CERTIFICATE_PATH = 'C:\secure\TokenUsage-release.pfx'
$env:TOKENUSAGE_CERTIFICATE_PASSWORD = '<certificate-password>'
.\scripts\release.ps1 -Platform x64 -Version 0.0.1
```

The certificate subject must match the publisher in `Package.appxmanifest`. Do not publish an asset with the `-unsigned` suffix.

## Create the GitHub draft

1. Add `WINDOWS_CERTIFICATE_BASE64` to the GitHub repository secrets.
2. Add `WINDOWS_CERTIFICATE_PASSWORD` to the GitHub repository secrets.
3. Create and push the version tag.

```powershell
git tag v0.0.1
git push origin v0.0.1
```

The release workflow runs the complete x64 check. Then it creates a draft GitHub release.

Review the assets, checksums, notes, and installation results before you publish the draft.

## Release checks

Make sure that these results are valid before publication:

- The complete x64 check passes.
- The MSIX signature status is valid.
- The portable app starts without package identity.
- The portable CLI reads the same `Data` folder as the app.
- The ZIP and MSIX hashes match `SHA256SUMS.txt`.
- The MSIX upgrade keeps the existing `LocalState` data.
- The portable update keeps the existing `Data` folder.
