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

## Unsigned preview authorization

The user approved release without signing because no certificate is available.
Publish `v0.0.1-preview.1` as an unsigned x64 portable pre-release, not as a stable
or trusted package. Omit the MSIX installer. Keep size and digest checks, preserve
the signed stable lane, and leave the updater's stable-only policy unchanged.
The preview must pass artifact and extracted-app checks before publication.
The existing installed development application and its data stay unchanged.

The first candidate passed the full 1,375-test x64 gate and produced a portable
ZIP with a complete owned-file inventory. Extracted CLI help passed, but the
native app crashed at AppNotificationManager.Register because
`Microsoft.WindowsAppRuntime.Insights.Resource.dll` was absent. This matches
[Windows App SDK #6071](https://github.com/microsoft/WindowsAppSDK/issues/6071).
The resource is present in the pinned runtime NuGet package's framework MSIX,
but not in component publish output. Adding that exact resource to the isolated
copy restored native startup without installing anything or suppressing errors.
The release script now bundles the matching restored resource and runtime license.
Discard the unpublished preview.1 assets and qualify preview.2 from a fresh ZIP.

## Tray settings follow-up

The tray context menu now exposes all 11 switches from Options. It also includes
provider visibility, taskbar-preview selection, and each metric's visibility,
highlight, and on-demand state. Native submenus group the settings by category.
Account-connection consent stays in its explanatory form, not in the tray menu.

The menu reads the existing option owners each time it opens. Selection uses the
same setters and save methods as Options. Disabled states and highlight limits
are checked again before a change. No second settings store was added.

The x64 Release app build passed without warnings. The existing architecture
and Windows suites passed: 106 and 216 tests. An isolated native fixture covered
four categories and 63 switches, including 52 provider/metric switches from
sample data. It confirmed unique IDs, refreshed checks, and blocked alert rules
when the master switch is off. A native menu action enabled alerts and saved
that change in the fixture's settings file.

Native menus were inspected at 96 DPI on Windows 11 with English app labels.
General, Notifications, and Dashboard layout had no clipped labels. The system
menu renderer supplies check marks and disabled text. Evidence is in
`.scratch/tokenusage-measurement/tray-*-submenu.png` and `tray-options-menu.png`.
Keyboard verification is inconclusive: injected keys did not produce a confirmed
setting change. Another DPI, contrast theme, and installed-package pass remain
untested. The initial probe lacked XAML resources; the working probe uses the
repository's `portable-x64` publish profile and a separate temporary data folder.

At the end of the tray change, local replacement and draft publication were
blocked: 187,471 Codex observations exceeded the 32 MiB checkpoint document
limit, and the collector reported that failure as AccessBlocked. The installed
database retained its existing Codex rows and passed the read-only integrity
check. The repair and local replacement are tracked below.

## Logo theme switch follow-up

The compact and report logos now toggle the saved light/dark theme. Both use a
40 DIP button, a 32 DIP logo, and the same centered 16 DIP wordmark. The button
requests the hand cursor and keeps space around the rotating image. The report
logo is outside the title-bar drag region.

Rotation and color blending share a 480 ms cubic ease-out. New clicks supersede
the current transition, including while settings are saving. Reduced motion and
high contrast skip the animation. Temporary brushes live in the animation layer;
stopping restores the original XAML theme-resource expressions. Animating shared
resource brushes directly caused a native exception in the first probe and was
removed. Explicit page foreground resources fix inherited white text on light
backgrounds after switching themes.

The final x64 portable publish passed. The existing serial-save test now covers
a second theme selection before awaiting persistence and passed. The focused
automation-ID contract also passed. Native isolated probes showed readable text
in both themes and full, aligned logos in compact and report views. Two compact
invocations 120 ms apart persisted the final dark theme. Report invocations
switched both ways and persisted light. No native exception was recorded in the
final probe. Evidence: `theme-compact-interruption.mp4`,
`theme-compact-light-verified.png`, and `theme-report-verified.mp4` under
`.scratch/tokenusage-measurement/`.

Frame captures do not establish a 60 FPS performance claim. Reduced-motion,
high-contrast, alternate DPI, and keyboard runtime checks remain untested.
Physical hover verification was blocked when the test window lost foreground;
the cursor assignment is source-verified only. The desktop installation was not
replaced during the logo work.

## Codex collection repair

The checkpoint now streams its existing JSON schema. It no longer allocates a
second whole-document byte buffer or applies the 32 MiB preferences-document
limit to observation replay. Writes still use a locked temporary file, disk
flush, and atomic replacement. A failed write preserves the original file.

A second cause kept old installations stale even after that fix. The obsolete
tail reader could mark the source partial when a redundant last-token counter
was invalid despite a valid cumulative counter. That blocked parser-version
reconciliation. The numeric observation scanner is now the only reader. It uses
the valid cumulative count with unknown attribution/precision when needed.
Oversized message content is ignored only after reading its non-usage envelope;
unrecognized or malformed usage still reports a partial scan. The reconciliation
guard remains in place. Internal read failures now report `read-failed`, not a
false access-permission error.

Final source verification covered 1,379 tests: architecture 106, core 293, CLI
130, providers 634, and Windows 216. The provider cases include a checkpoint
larger than 32 MiB, restart replay, both message envelope types, and explicit
unknown attribution for a valid cumulative count. A stream-write interruption
test checks original-file preservation and temporary-file cleanup. The first
test run found a syntax error in the new test; it was fixed before these results.

The final real-source probe opened the installed database read-only and used
SQLite backup to make a test copy. Its first scan was Complete/None with 190,092
observations and a 54,633,921-byte checkpoint. Restart was also Complete/None.
Collection then reconciled the copied database successfully. In the preceding
equivalent probe, Codex increased from 60 to 70 daily/model rows and from
30,145,286,537 to 32,246,233,422 tokens across retained history. Every other
provider's row count and token total stayed unchanged. Live totals continue to
change as Codex writes new events. Source logs and the original database were
not cleared or replaced by test data.

Local evidence is in `.scratch/tokenusage-measurement/codex-final-repro.log`,
`codex-final-provider-tests.log`, and `codex-backup.log`. A consistent pre-repair
SQLite backup and a LocalState copy are stored at the backup path in that log.

The x64 Release solution/package build passed. The tested unsigned development
MSIX has SHA-256 `D71CA76F74A2711537CCA254CF76E5211791476733EC72ED4FDE0F4E69E10DF9`.
Re-registering the same identity did not relocate Windows' existing installation.
The original `.scratch/local-brand-header` directory was therefore backed up to
`.scratch/local-brand-header-before-codex-repair-20260907` and updated in place.
Every extracted package file matched after copying. The installed provider DLL
hash is `5514586B91F7F6255777A0F87C4B4727FA25C5CD000B2E907A6C2B700BB77959`.

Windows reports the existing package as OK. Its installed `tokenusage.exe`
execution alias completed a real refresh with Codex `complete` / `none`, against
the original packaged LocalState. The database passed integrity checking and
contained 70 Codex daily/model rows, 32,262,806,448 retained-history tokens, and a
successful collection timestamp of `2026-09-08T00:31:04.2021397+00:00`. It was not
replaced by a portable or sample database. Native capture of the auto-hiding
flyout was inconclusive because foreground activation did not hold; no rendered
data claim is made from those black captures. The installed app remains running.
Clean-machine installation and notification activation were not tested. No
public release, commit, or push was made as part of this repair.

## References
- [GitHub releases API](https://docs.github.com/en/rest/releases/releases)
- [GitHub asset digests](https://github.blog/changelog/2025-06-03-releases-now-expose-digests-for-release-assets/)
- [Windows package signature kind](https://learn.microsoft.com/en-us/uwp/api/windows.applicationmodel.package.signaturekind)
- [Windows package deployment options](https://learn.microsoft.com/en-us/uwp/api/windows.management.deployment.addpackageoptions)
