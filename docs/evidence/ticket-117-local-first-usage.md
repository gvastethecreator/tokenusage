# Ticket 117: local usage first

Date: 2026-07-28

## Result

Codex, Grok Build, and OpenCode now feed the live 30-day spend ring from local sources. Vercel AI Gateway remains in the codebase but stays outside active composition and visible options.

## Reader path

- Grok follows the pinned OpenUsage reader: `GROK_HOME/logs/unified.jsonl` is primary, model changes are tracked per process, and inference rows receive a catalog estimate. Current session snapshots remain a bounded fallback.
- OpenCode opens `opencode*.db` read-only and queries the current aggregate schema by date. Legacy message and JSON formats remain supported.
- Codex uses official `account/usage/read` daily totals. It samples bounded rollout tails for model and token-category mix and estimates known models from a versioned price table.
- TokenUsage stores normalized events and daily rollups in its own SQLite database. Startup publishes these cached rollups before provider refresh begins.

No reader stores prompts, responses, tool calls, commands, auth data, or raw provider rows.

## Runtime proof

The aggregate-only local probe returned complete, priced data for Codex, Grok Build, and OpenCode. Grok's primary read completed in about 30 ms on the inspected installation.

A packaged x64 run launched by AUMID with the UI test hook produced this UI Automation proof:

- `UsageProductCard` appeared at startup in about 3 seconds without a manual refresh.
- `UsageProductCard.AgentRing30Days` appeared after expanding details and the breakdown.
- The visible ring data included Codex, Grok Build, and OpenCode.
- A search for Vercel AI Gateway returned zero matches.

No screenshot was saved because the run used the owner's real usage data.

## Automated proof

`scripts/check.ps1 -Platform x64 -Configuration Release` passed:

- architecture: 67 tests;
- core: 191 tests;
- CLI: 82 tests;
- providers: 361 tests;
- Windows platform: 116 tests;
- Visual Studio MSBuild solution and packaged x64 Release build.

The final Release build ran without `EnableUiTestFixtures`; the hook was used only for the UIA proof build.
