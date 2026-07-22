# Ticket 08D: Windows Codex process supervision

Date: 2026-07-22

Status: implemented and verified with a real synthetic child process. Factory
composition, a live Codex account call, cache wiring, and UI remain in 08E.

## Executable resolution

`CodexExecutableResolver` returns one closed result: resolved, missing, or
invalid explicit override.

- `WOPENUSAGE_CODEX_EXECUTABLE` accepts one existing, non-empty, absolute local
  `.exe` path.
- A set but invalid override fails closed. It never falls back to `PATH`.
- Normal discovery checks absolute `PATH` entries without a working-directory
  fallback, then local Bun, npm-native x64/ARM64, WinGet/WindowsApps, and program
  roots.
- Relative, UNC, device, quoted, argument-bearing, script, directory, missing,
  and empty-file candidates are rejected.
- Resolution results carry no raw override text or diagnostic reason.

## Process ownership

`CodexAppServerProcess` uses `CreateProcessW` with exact `app-server --stdio`
arguments, redirected pipes, no shell, no console window, and the executable
directory as its working directory.

`STARTUPINFOEX` carries a `PROC_THREAD_ATTRIBUTE_HANDLE_LIST`, so the child can
inherit only its three standard-stream handles. A real test creates a fourth
inheritable pipe and proves that the fake child cannot access it. Microsoft
recommends this list when a child needs inherited handles because unrestricted
inheritance can leak sensitive process resources:

- <https://learn.microsoft.com/windows/win32/api/processthreadsapi/nf-processthreadsapi-updateprocthreadattribute>
- <https://learn.microsoft.com/windows/win32/procthread/inheritance>

The child starts suspended. WOpenUsage configures a Job Object with
`JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE`, assigns the child, creates the parent
streams, and only then resumes its primary thread. Assignment failure stops the
still-suspended process. Microsoft documents that closing the last handle of a
job with this limit terminates all associated processes:

- <https://learn.microsoft.com/windows/win32/api/jobapi2/nf-jobapi2-createjobobjectw>
- <https://learn.microsoft.com/windows/win32/procthread/job-objects>
- <https://learn.microsoft.com/windows/win32/api/jobapi2/nf-jobapi2-assignprocesstojobobject>

Disposal is idempotent: close stdin, wait a short grace period, terminate and
close the job, wait again, then use `TerminateProcess` as a final bounded
fallback. The process handle remains available until the child exit is observed.

## Diagnostic privacy

Stderr is drained from process start so a full pipe cannot block app-server.
The public snapshot stores only sanitized text with a fixed character cap.
Email-like values, token and bearer patterns, secret assignments, and absolute
Windows paths are replaced. Overlong lines are discarded before storage. Raw
stderr never enters a public exception.

## Synthetic runtime proof

The test helper builds as a real `codex.exe`. It:

- accepts only `app-server --stdio`;
- echoes UTF-8 input to stdout;
- emits synthetic private-looking stderr and an overlong line;
- remains alive after stdin EOF, forcing Job Object shutdown.

Tests cover hostile resolver inputs, PATH/CWD boundaries, architecture-specific
npm paths, UTF-8 pipe polarity, diagnostic redaction and bounds, sticky-child
termination, idempotent disposal, missing executable privacy, and synthetic Job
assignment failure cleanup.

```text
Focused Platform.Windows tests, x64: 46/46 passed
scripts/check.ps1 -Platform x64:
  Architecture 22/22, Core 32/32, Providers 64/64,
  Platform.Windows 46/46, build 0 warnings/errors
scripts/check.ps1 -Platform ARM64:
  Architecture 22/22, Core 32/32, Providers 64/64,
  Platform.Windows 46/46 on the x64 test host, ARM64 build 0 warnings/errors
dotnet format WOpenUsage.slnx --verify-no-changes --no-restore: passed
git diff --check: passed
```

`scripts/check.ps1` now includes Platform.Windows tests. The ARM64 lane compiles
the production process types and fake child for ARM64; native runtime tests stay
on the x64 host, matching the repository's existing test policy.

## Review

Grok Build returned `accept` for the architecture plan and final read-only diff
review, with no P0/P1/P2 finding. Parent then found unrestricted handle
inheritance during the required adversarial autopsy, added the explicit handle
list and fourth-handle proof, and sent that delta back to Grok. The follow-up also
returned `accept`. Two bounded implementation attempts returned `Cancelled`
before edits, so no Grok code was accepted. Parent implemented and reviewed every
file, then ran all proof locally. Grok reported US$0.8957936 across the plan,
cancelled attempts, and reviews; the wrapper's first `--check` attempt failed
before inference because that flag conflicts with its no-subagent mode in the
installed CLI.

## Next

Ticket 08E adds the composition owner that binds this process to
`CodexAppServerClient`, `CodexProviderRuntime`, the cache-first coordinator, and
the real Codex dashboard card. No live account method ran in 08D.
