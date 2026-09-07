# GitHub auto-update
Date: 2026-09-07. Status: implementation and local checks complete; signed release qualification pending.

## Scope and decisions
- Support official Windows portable ZIPs and signed direct-install MSIX packages.
- Microsoft Store owns Store updates. Development packages never self-update.
- Use stable published releases from gvastethecreator/tokenusage only. Never
  install draft, pre-release, unsigned MSIX, older or equal versions.
- Automatic updates are opt-in. Settings disclose the GitHub network request.
  Check once every 24 hours while running. After startup, wait 30 seconds and
  check only if the last check was at least a day ago. Manual checks remain available.
- Download with size limits, cancellation, HTTPS origin checks and mandatory
  GitHub SHA-256 digest verification. Do not send provider data or credentials.
- Stage updates without forced shutdown. Apply portable updates after app exit;
  Windows defers MSIX registration while the app is in use. Portable can restart
  on request; MSIX asks the user to close and reopen. Preserve usage data,
  preferences and portable foreign files.
- Keep a portable rollback journal and backup. Report failure on next launch.
- Leave a file in place when its size and SHA-256 already match the release.
  Back up changed files only. Keep the same path and ownership checks.
- The follow-up request authorizes grouped commits, push, and a GitHub release.
  Certificate installation and secret changes still need explicit direction.

## Implementation steps
1. Release feed, strict version/asset policy, download verification and settings.
2. Portable staging, separate CLI worker, process binding, recovery and tests.
3. MSIX identity validation, trusted Windows deployment and distribution gating.
4. Options status, automatic toggle, manual commands, progress, cancellation and
   lifecycle integration without delaying the dashboard.
5. Release version/signature/digest contracts and release procedure.
6. Focused tests, repository x64 Release gate, native options checks, synthetic
   upgrade/failure checks, diff review and evidence.

## Acceptance
No unknown artifact can reach installation. Failed downloads leave the running
version intact. Portable preparation rejects unsafe ZIP entries and reparse
paths. Worker failures retain a recoverable old version and never remove Data.
The updater does not kill CLI tasks or restart the app after a normal user exit.

## Baseline and limits
The repository is public and Actions remains enabled. GitHub returned no releases
on 2026-09-07. The current local app is a development registration, not a signed
release installation. A real signed N-to-N+1 upgrade and public release smoke
therefore require release artifacts and a separate authorized test installation.
Existing report/options/compact/reset-marker changes remain untouched.

## Local verification
- The repository Release x64 gate was completed in stages: Architecture 106,
  Core 291, CLI 130, Providers 632 and Windows 216 tests passed (1,375 total).
  Initial analyzer failures CA1861 and CA2016 were corrected. The affected
  builds were rerun without repeating suites that had already passed.
- After the unchanged-file optimization, all 18 Core update tests passed again.
  The existing installation case now checks that an identical file keeps its
  timestamp and does not produce a backup.
- App and MSIX builds passed with analyzers enabled. The final build log is
  `.scratch/tokenusage-measurement/autoupdate-build-closeout.log`.
- Native WinUI checks covered the General options card at 460 DIP, dark and
  light themes, accessible control names, keyboard toggle, saved preferences,
  manual checking, available release, HTTP 404, HTTP 503, cancellation and a
  rejected SHA-256 mismatch. Development registration controls were disabled.
  The real MainWindow closed normally through the new shutdown path.
- Native testing found an uncaught InvalidDataException on a bad download.
  The update owner now handles it; repeating the failure kept the app open and
  left no installable download. No new unhandled exception was logged afterward.
- With a simulated clock 12 hours ahead, automatic updates stayed enabled but
  did not check again after the startup delay. With the clock 25 hours ahead,
  the automatic check ran after 30 seconds, saved its new check time and safely
  rejected the deliberately corrupt fixture download. Evidence:
  `updater-ui-daily-cooldown.png` and `updater-ui-daily-due.png` under
  `.scratch/tokenusage-measurement`.
- A full isolated portable replacement passed using a copied external CLI:
  exact-parent wait, natural parent exit, installation, obsolete-file removal,
  preservation of Data and foreign files, retained backup and cleanup of the
  completed staging/worker folders. Receipt:
  `.scratch/tokenusage-measurement/portable-smoke-run-20260907-1813/proof.json`.
  This used version 0.0.1.0 on both sides with changed fixture components, not
  a published N-to-N+1 release. The optimized replay passed too, using a fresh
  copied CLI and a separate transaction outside the first test's installation.
  It verified that an identical App.dll kept its timestamp and had no backup.
  Receipt: `portable-smoke-run-20260907-optimized/proof.json` in the same evidence
  directory. Both workers ended naturally and their completed stage/worker
  folders were removed; no installed user app was replaced.
- The X: filesystem showed high per-file open/copy/flush latency. The first
  full replacement exposed redundant copies of identical dependencies, which
  led to the size-plus-hash skip. No durability flushes or path checks were
  removed. Measurements are in
  `.scratch/tokenusage-measurement/portable-smoke-latency-20260907/results.json`.
  The isolated comparison went from 533 journal entries/backups and 1,253.52
  seconds to 4 entries/backups and 11.32 seconds from worker start to result.
  These figures describe this mostly-identical, same-version fixture only;
  they exclude download/preparation and do not predict real-release timings.
  Comparison: `.scratch/tokenusage-measurement/portable-smoke-comparison.json`.
- Final diff review passed. No test fixture windows or worker processes remain.

## Release qualification still required
No signed MSIX N-to-N+1 installation, clean-machine install, real GitHub release
download, ARM64 runtime check or hard-power-loss recovery drill was performed.
MSIX tests cover manifest rejection; Windows signature trust still needs a real
signed release test. The installed development app was not replaced. No release,
commit or push was created during implementation. The release workflow retains its manual publication
gate. A prior build without the updater needs one manual bootstrap installation.

## Release preparation follow-up

The user authorized commits, push, and release preparation on 2026-09-07.
Version 0.0.1 remains the first release: GitHub has no releases or tags.
The public repository keeps standard GitHub Actions enabled.

No repository signing secrets, signing environment variables, or certificate
matching the package publisher were found in the Windows personal stores.
Do not create a public unsigned MSIX or push a release tag that cannot pass
the signing gate. Prepare the changelog and a draft while signing is unresolved.
The existing local development installation stays unchanged.

Release notes now separate highlights, measurement changes, update safety,
installation, and known limits. `CHANGELOG.md` provides a shorter version index.
The release workflow can complete an empty draft for the exact source commit;
it rejects a published release, a different commit, a pre-release, or a draft
that already contains assets. Six simulated GitHub cases passed, including
fresh creation and empty-draft completion. These checks did not upload files.
All four workflow PowerShell blocks and `scripts/release.ps1` parsed successfully.

The first four grouped commits were pushed through `4324e66`. An empty GitHub
draft for v0.0.1 was created with release ID `384337345`. A remote check confirmed
that REST lookup by published tag returned 404 for this pending tag. The workflow
now resolves a draft through GraphQL and reads it by release ID; its final asset
check uses `gh release view`, which supports pending tags. No release tag or
installer was published. Signing and final install checks remain blocked.
The corrected lookup passed the same six simulated cases plus a rejected remote
digest mismatch. Live read-only checks found the draft through GraphQL and its
release ID. The seven existing packaging contract tests also passed.

## References
- [GitHub releases API](https://docs.github.com/en/rest/releases/releases)
- [GitHub asset digests](https://github.blog/changelog/2025-06-03-releases-now-expose-digests-for-release-assets/)
- [Windows package signature kind](https://learn.microsoft.com/en-us/uwp/api/windows.applicationmodel.package.signaturekind)
- [Windows package deployment options](https://learn.microsoft.com/en-us/uwp/api/windows.management.deployment.addpackageoptions)
