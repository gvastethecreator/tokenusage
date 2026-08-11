---
name: maintain-code-map
description: "Generate or refresh an evidence-backed interactive repository code map and lock. Use for codemap creation, code-change preflight, stale-map checks, or architecture and data-flow changes."
---

# Maintain Code Map

Create one evidence-backed view of the current repository. Keep the map useful for change-impact checks, not as a file inventory.

Read [references/artifact-contract.md](references/artifact-contract.md) before you create or validate artifacts.

## Process

1. Set the boundary.
   - Resolve the repository root and read its instructions and architecture documents.
   - Inspect `git status -sb`, the current commit, tracked files, build manifests, entrypoints, and tests.
   - Exclude vendor, dependency, build, distribution, cache, generated, and coverage directories.
   - Do not modify product code during this process.
   - Write only under `docs/codemap/` in the target repository.
   - Done when the scan scope and exclusions are explicit.

2. Measure freshness before analysis.
   - If `docs/codemap/codemap.lock` exists, run `codemap_tool.py status` before regeneration.
   - Record each changed, new, and removed module from the status output.
   - Treat all scanned modules as new when the lock does not exist.
   - Treat fingerprint or commit drift as stale.
   - Report dirty-state drift, but do not call the map stale when module fingerprints and the commit still match.
   - Done when the stale-module list is saved for the final report.

3. Build the evidence model.
   - Use tracked source, configuration, migrations, schemas, and tests as evidence.
   - Select no more than 20 primary nodes.
   - Group low-level files under the module that owns them.
   - Include major modules, services, databases, queues, interfaces, and external dependencies.
   - Trace calls and data movement through imports, calls, reads, writes, publishes, and subscriptions.
   - Add three to five important end-to-end flows.
   - Use exact source paths and literal symbols for verified evidence.
   - Mark an edge `unknown` when source evidence does not prove the relationship.
   - Do not infer a relationship from names, folder proximity, or architecture prose alone.
   - For large batches, run `generate_repository_map.py` to create a conservative baseline.
   - For a drive-wide first pass, run `generate_drive_maps.py` without `--write` to inspect scope, then repeat with `--write`.
   - The drive command skips reference clones, repositories without `HEAD`, metadata-only roots, and nested repositories.
   - It validates and checks freshness for repositories that already own all three artifacts.
   - It prints one JSON event per repository and never publishes a failed staging set.
   - Review automatic roles and flows against the repository entrypoints before publication.
   - If the analyzer reports insufficient evidence, leave the repo unchanged and report it as blocked.
   - Done when every node and verified edge has source evidence.

4. Generate the three artifacts together.
   - Create `docs/codemap/.staging/` for the new artifact set.
   - Write `codemap.json` first, according to the artifact contract.
   - Use one UTC generation time and the current `HEAD` commit in all artifacts.
   - Run `codemap_tool.py render` to create the self-contained HTML from the JSON.
   - Run `codemap_tool.py lock` with the same scan scope, exclusions, and generation time.
   - Run `codemap_tool.py validate` against the staging directory.
   - Capture a no-index diff from each current artifact to its staged replacement before publishing.
   - Use the platform null device as the old file when a current artifact does not exist.
   - Run `codemap_tool.py publish` only after staging validation passes.
   - Do not hand-edit one published artifact without regenerating all three.
   - Done when the published HTML, JSON, and lock describe the same repository state.

5. Check the browser artifact.
   - Open `docs/codemap/codemap.html` directly, without a server.
   - Check the initial fit, boundaries, edge labels, legend, and three to five flows.
   - Check module selection, upstream and downstream highlights, tests, flow membership, search, filters, zoom, pan, and node drag.
   - Done when the static file works with network access disabled.

6. Report the result.
   - List created or modified files.
   - List stale modules from the pre-generation status.
   - List all remaining `unknown` edges.
   - List each validation result and any check that did not run.
   - Show the tracked Git diff and the captured no-index diff for untracked artifacts.
   - Do not stage artifacts only to make Git show a diff.
   - Do not claim runtime or browser proof from static validation.
   - Done when the report separates verified facts, unknowns, and unrun checks.

## Commands

Set `<skill-root>` to this skill directory.

```powershell
python <skill-root>/scripts/codemap_tool.py status --repo . --lock docs/codemap/codemap.lock

python <skill-root>/scripts/generate_repository_map.py --repo . --output docs/codemap/.staging/codemap.json --generated-at <utc-time>

python <skill-root>/scripts/generate_drive_maps.py --root X:\

python <skill-root>/scripts/generate_drive_maps.py --root X:\ --write

python <skill-root>/scripts/generate_drive_maps.py --root X:\ --write --refresh-stale

python <skill-root>/scripts/generate_drive_maps.py --root X:\ --write --refresh-stale --rerender-existing

python <skill-root>/scripts/codemap_tool.py render --repo . --json docs/codemap/.staging/codemap.json --output docs/codemap/.staging/codemap.html

python <skill-root>/scripts/codemap_tool.py lock --repo . --scope src --scope tests --exclude docs/codemap --generated-at <utc-time> --output docs/codemap/.staging/codemap.lock

python <skill-root>/scripts/codemap_tool.py validate --repo . --dir docs/codemap/.staging

python <skill-root>/scripts/codemap_tool.py publish --repo . --staging docs/codemap/.staging --target docs/codemap

node <skill-root>/scripts/verify_codemap_browser.cjs docs/codemap/codemap.html
```

Repeat `--scope` and `--exclude` for the repository. Use `--scope .` only when root-level grouping gives useful module fingerprints.

## Resources

- `references/artifact-contract.md`: JSON, evidence, lock, HTML, and report contracts.
- `scripts/codemap_tool.py`: freshness, fingerprint, rendering, publishing, and validation utility.
- `scripts/generate_repository_map.py`: conservative tracked-source baseline for large repository batches.
- `scripts/generate_drive_maps.py`: Git-root inventory and validated drive-wide publication.
- `scripts/verify_codemap_browser.cjs`: desktop/mobile interaction and rendering smoke.
- `assets/codemap-template.html`: self-contained interactive browser template.
