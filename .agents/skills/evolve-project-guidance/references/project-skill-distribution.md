# Project Skill Distribution

Choose one mode for each `.agents/skills/<name>/` destination. Do not mix physical and linked ownership for one skill.

## Portable Mode

Use portable mode for contributor-facing repositories.

- Store each required skill as a physical, versioned directory under `.agents/skills/`.
- Store copy provenance and content fingerprints in `.agents/skills.lock.json`.
- Include every runtime script, reference, asset, metadata file, and required sibling skill.
- Preserve license and source information when the source repository requires it.
- Review `source_dirty` in the preview. Prefer a committed source revision for reproducible contributor bundles.
- Reject private paths, credentials, hidden imports, and source-repository dependencies.
- Update only copies whose current fingerprint matches the prior lock entry.

Preview and apply a bundle from a source skill root:

```powershell
python <skill-root>\scripts\sync_project_skills.py --project . --source-root <source-skills-root> --skill evolve-project-guidance --skill writing-for-agents

python <skill-root>\scripts\sync_project_skills.py --project . --source-root <source-skills-root> --skill evolve-project-guidance --skill writing-for-agents --write

python .agents\skills\evolve-project-guidance\scripts\sync_project_skills.py --project . --verify
```

The first command is a dry run. The write command stops when a destination has unrecorded changes.

## Junction Mode

Use junction mode for a local project that consumes canonical skills without publishing them.

- Keep the destination untracked. Prefer `.git/info/exclude` for a personal loading surface.
- Point each junction directly at the canonical skill directory.
- Record the canonical source and expected revision in local project notes when drift matters.
- Verify both the immediate target and the resolved real path.
- Stop when a tracked physical `.agents/skills/` bundle already exists.

PowerShell creation pattern:

```powershell
$source = (Resolve-Path -LiteralPath '<canonical-skill>').Path
$destinationRoot = Join-Path (Resolve-Path -LiteralPath '.').Path '.agents\skills'
New-Item -ItemType Directory -Path $destinationRoot -Force | Out-Null
$destination = Join-Path $destinationRoot '<skill-name>'
if (Test-Path -LiteralPath $destination) { throw "Destination already exists: $destination" }
New-Item -ItemType Junction -Path $destination -Target $source | Out-Null
Get-Item -LiteralPath $destination | Select-Object FullName,LinkType,Target
```

Do not replace an existing destination during junction setup. Inspect ownership first.

## Owned Mode

Use owned mode when the project implements and maintains its own skill.

- Create the physical skill directly under `.agents/skills/<name>/`.
- Keep project-specific facts and scripts in that skill.
- Use the normal skill initializer, metadata, validator, and eval process.
- Do not sync the owned directory from an external source root.

## Minimal Bundle

Derive the bundle from active consumers:

1. Include `evolve-project-guidance` when contributors will maintain project guidance.
2. Include `writing-for-agents` when contributors can create or change skills and agent rules.
3. Include `simple-english` when a bundled skill writes task or plan text.
4. Include `maintain-code-map` only when project rules require the code-map workflow.
5. Include each domain skill named by active rules or required by another bundled skill.
6. Exclude global convenience skills that contributors do not need for project work.

Map every skill reference to `bundled | globally guaranteed | removed`. A clean checkout cannot depend on a private global skill.

## Contributor Gate

- Copy the project to a different path or use a clean checkout.
- Disable reliance on the author's global skill roots.
- Verify `.agents/skills.lock.json` and every skill folder.
- Run each bundled validator and script from outside its source repository.
- Run one project workflow that requires the bundle.
- Record any global dependency that remains. Treat it as a portability failure unless the project guarantees it.
