# Release TokenUsage

Stable TokenUsage releases publish one signed MSIX package and one portable ZIP for each Windows architecture. Explicitly approved unsigned previews publish only a portable ZIP and must be marked as pre-releases.

The portable ZIP contains the WinUI app, the CLI, .NET, and the Windows App SDK runtime. It does not require installation.

## Build a local release candidate

1. Set the same release version in `Directory.Build.props`, `src/TokenUsage.App/app.manifest`, and `src/TokenUsage.Package/Package.appxmanifest`.
2. Run the release command from the repository root.

```powershell
.\scripts\release.ps1 -Platform x64 -Version 0.0.1
```

The command runs all tests and builds the MSIX package. Then it publishes the portable app and CLI.

Use a stable `major.minor.patch` version without leading zeros. Each component must fit a Windows package version field (`0` through `65535`). `Version` and `InformationalVersion` use three fields. `AssemblyVersion`, `FileVersion`, and both Windows manifests use the same version with a final `.0`. The command rejects mismatches before the build and checks the published app, CLI, and package again before it creates release assets.

The command writes these files to `artifacts\release`:

- `TokenUsage-<version>-win-<architecture>-portable.zip`
- A signed MSIX, or an MSIX with the `-unsigned` suffix
- `release-manifest.json`
- `SHA256SUMS.txt`

Use `-SkipTests` only after the same commit passes the complete release check.

## Unsigned portable preview

Use this channel when a signing certificate is not available and an unsigned
preview has been explicitly approved. Do not include an unsigned MSIX or change
Windows trust settings. A preview is not a signed or fully qualified release.

```powershell
.\scripts\release.ps1 -Platform x64 -Version 0.0.1 -UnsignedPreview
```

The full test and package gate still runs. Only the portable ZIP, provenance
manifest, and checksums go into `artifacts/release`. The ZIP name ends in
`-portable-unsigned-preview.zip`; its readme states that it is unsigned.
The provenance manifest records `channel: unsigned-preview`.

Use a tag such as `v0.0.1-preview.1`. The workflow skips certificate loading only
for this channel and creates a draft with `prerelease: true`. Verify the extracted
ZIP outside the checkout, CLI, native startup, source commit, and remote asset
digests before publication. Record missing clean-machine and lifecycle checks.
Never mark an unsigned preview as Latest. The app ignores these pre-releases;
users install them manually and can later update to a newer stable app version.

## Portable data

The portable folder contains `TokenUsage.portable` and `TokenUsage.files.json`. Keep both files beside the executable files. The JSON file lists files owned by the release, including itself, using paths relative to the portable root. The updater uses the old and new lists to replace release files without overwriting unrelated files.

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

Do not change the package identity or publisher for an update. Windows must accept the new signature and match the installed package publisher. The app does not install certificates or bypass Windows trust checks.

## Create the GitHub draft

1. Add `WINDOWS_CERTIFICATE_BASE64` to the GitHub repository secrets.
2. Add `WINDOWS_CERTIFICATE_PASSWORD` to the GitHub repository secrets.
3. Create and push the version tag.

```powershell
git tag v0.0.1
git push origin v0.0.1
```

Stable `vMAJOR.MINOR.PATCH` tags require a valid signed package. Tags ending in
`-preview.N` use the unsigned portable channel described above. The workflow
checks that the tag matches the source version and checked-out commit, runs the
complete x64 check, and creates a draft in the selected channel.

After upload, the workflow reads the draft assets through the GitHub API. Every uploaded asset must have the expected name, byte count, and `sha256:` digest. These digests must match the local files. Missing or different metadata fails the workflow; the draft stays unpublished. Manual runs without a certificate can create an unsigned candidate artifact, but cannot create a GitHub release.

Review the assets, checksums, notes, and installation results before you publish the draft.

An empty draft may be prepared before signing is available. Set its target to
the full source commit SHA, not a branch. The tag workflow can complete that
draft only when it is still unpublished, matches the selected channel, targets the exact
tag commit, and has no assets. It never replaces existing assets or a published
release. Candidate notes must keep signing and install checks marked as pending
until they pass; then update the notes before publication.

## Automatic update contract

The app reads stable public releases from `gvastethecreator/tokenusage`. It ignores drafts, prereleases, noncanonical tags, and versions that are not newer than the installed version. Publishing a reviewed draft makes it available to automatic-update clients. The workflow never publishes a draft itself.

Automatic download and installation are opt-in and off by default. The app checks once every 24 hours while it runs. It waits 30 seconds after startup, then checks only if at least 24 hours have passed since the last check; otherwise it waits for the remaining time. Users can check, download, and install from Options without waiting for the next scheduled check. Packaged background installation requires Windows 10 version 2004 (build 19041) or later; older Windows versions use manual installation. Store installs and development package registrations do not use this update path.

Keep these asset names exact:

- Portable: `TokenUsage-<version>-win-<architecture>-portable.zip`.
- Packaged: `TokenUsage-<version>-win-<architecture>` followed by `.msix`, `.msixbundle`, `.appx`, or `.appxbundle`.

Use lowercase `x64` or `arm64` in asset names. Do not rename an unsigned package to remove its suffix. The updater never selects the `-unsigned` asset. The ZIP keeps one root folder named `TokenUsage-<version>-win-<architecture>-portable`, with `TokenUsage.App.exe`, `TokenUsage.App.dll`, `TokenUsage.portable`, `TokenUsage.files.json`, and the self-contained CLI in `cli`. The release script generates the owned-file list after it assembles the ZIP contents. Do not add user data to the ZIP or the owned-file list.

The updater requires the GitHub release asset's SHA-256 digest and verifies the downloaded bytes before staging an update. `SHA256SUMS.txt` and `release-manifest.json` remain useful review records, but do not replace a missing GitHub digest. Portable updates trust the GitHub repository, HTTPS transport, and matching digest. This protects download integrity; it is not Authenticode signing or an independent publisher signature. A compromise of the GitHub release account can therefore compromise portable updates. Packaged updates also pass Windows signature and installed-publisher checks.

Automatic updates must not interrupt a running task or force the app to restart. The update is staged while the app runs and applied when the app can close normally. Portable updates keep `Data`; packaged updates keep Windows `LocalState`. Test this lifecycle before publication, not just the download.

Portable updates leave an owned file in place when its size and SHA-256 already match the release. Only changed or obsolete files need backups. Unchanged dependencies keep their timestamps; path and ownership validation still apply.

After a successful portable update, the next app launch removes its extracted staging files and copied worker. It retains the transaction journal and previous-file backup. Backup history is not pruned automatically. After you confirm the new version works, you may remove that completed transaction directory manually. Never remove a pending or failed transaction.

## Recover an interrupted portable update

A power loss or terminated update worker can leave a portable installation with files from two versions. The app might not start in this state. Recovery is not automatic before app startup: use the private CLI copy retained with the pending update. Do not delete the pending transaction, its `stage` folder, or its `backup` folder.

1. Close TokenUsage and its CLI normally. Do not run two recovery workers.
2. Read the recorded pending plan. The default path is `Data\updates\pending.v1.json` inside the portable folder. If TokenUsage uses a custom data folder, use that folder instead. Do not choose a transaction by its date or edit the plan.

```powershell
$pendingPath = 'C:\Apps\TokenUsage\Data\updates\pending.v1.json'
$pending = Get-Content -LiteralPath $pendingPath -Raw | ConvertFrom-Json
$planPath = $pending.Pending.PlanPath
$plan = Get-Content -LiteralPath $planPath -Raw | ConvertFrom-Json
$plan | Select-Object InstallDirectory, Version, ParentProcessId
```

3. Make sure `InstallDirectory` is the portable folder you intend to repair. If the pointer or plan is missing, unreadable, or refers to another installation, stop and keep the backup for investigation.
4. Run the copied worker with the parent process ID already stored in the plan. Do not substitute the current PowerShell process ID.

```powershell
$transactionPath = Split-Path -Parent $planPath
$workerPath = Join-Path $transactionPath 'worker\tokenusage.exe'
& $workerPath --tokenusage-update-worker $planPath ([string] $plan.ParentProcessId)
Write-Output "Recovery exit code: $LASTEXITCODE"
Get-Content -LiteralPath (Join-Path $transactionPath 'result.json') -Raw
```

The worker checks the recorded process identity and waits if that original app process still runs. It restores an incomplete transaction before it retries the verified staged release. A completed transaction is not rolled back. Exit code `0` means the update is installed. Exit code `1` means recovery or installation failed; read `result.json`. If it says `rollback-required`, keep all backup files and investigate the reported failure before another attempt. If no result can be written, do not assume success. Open TokenUsage after successful recovery.

## Release checks

Make sure that these results are valid before publication:

- The complete x64 check passes.
- The MSIX signature status is valid.
- The portable app starts without package identity.
- The portable CLI reads the same `Data` folder as the app.
- The ZIP and MSIX hashes match `SHA256SUMS.txt`.
- The GitHub asset sizes and SHA-256 digests match the tested files.
- The tag, asset names, app and CLI versions, and package version agree.
- The MSIX upgrade keeps the existing `LocalState` data.
- The portable update keeps the existing `Data` folder.
- A normal installation failure restores the previous files.
- The documented private-worker recovery handles an interrupted portable update and preserves `Data`.
- The update does not force a running app or task to restart.

A successful build or draft upload does not prove an upgrade. Record the exact artifact hashes, Windows version, starting app version, and observed result for portable and signed-package upgrade tests. Keep signing, clean-machine, rollback, or real-release checks marked as not run when the required environment is unavailable.
