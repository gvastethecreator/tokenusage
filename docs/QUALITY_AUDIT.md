# Quality audit

Date: 2026-08-12

| Area | Result | Evidence |
| --- | --- | --- |
| Dependencies | PASS | `scripts/deps-check.ps1`, no outdated packages in active projects |
| Security | PASS | `scripts/audit.ps1`, no reported vulnerabilities |
| Build | PASS | WinUI app, net10, x64 Debug, 0 warnings/0 errors |
| Tests | PASS | 930 active tests (85 architecture, 204 core, 104 CLI, 394 providers, 143 Windows) |
| Packaging | DEFERRED | `wapproj` requires Visual Studio MSBuild and the DesktopBridge workload |
| Visual runtime | DEFERRED | requires interactive WinUI execution and a human capture |

`.gitignore` covers .NET outputs, packages, and test results. `.scratch`, `.reference`, `.snapshots`, and evidence artifacts are preserved. They are not product operating residue.
