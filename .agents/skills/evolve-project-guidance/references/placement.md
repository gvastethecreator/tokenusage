# Placement Rules

Choose one durable owner before editing. Use the narrowest owner that every affected consumer can load.

## Artifact Choice

1. Choose code when runtime behavior must change.
2. Choose configuration when a tool already owns the behavior through a supported setting.
3. Choose a rule for an always-on project constraint, safety boundary, or stable local convention.
4. Choose a skill for a conditional and repeatable workflow, tool integration, or non-obvious domain process.
5. Choose a work instruction for one effort's state, decisions, dependencies, and evidence.
6. Choose a reference for branch-specific facts that an owning rule or skill can reach.
7. Choose no change when current guidance already works or evidence does not prove a gap.

## Scope Choice

- Put cross-project invariants in the shared rules owner.
- Put repository-wide facts in the root project rule file.
- Put subtree facts in a nested rule file only when the active client loads that scope.
- Put optional branches behind a pointer that names both the content and its trigger.
- Put contributor-required project skills in physical `.agents/skills/<name>/` directories.
- Put local-only canonical links in `.agents/skills/` only when that loading surface is untracked.
- Keep task state out of always-loaded rules.
- Keep global behavior out of project-local copies.

Inspect the real loading chain before placement. Do not assume that clients load the same filenames, casing, or nested scopes.

## Evidence Order

Prefer evidence in this order:

1. Reproduced runtime behavior or a verified user correction.
2. Executable configuration, tests, schemas, and public commands.
3. Callers, imports, entrypoints, and current source behavior.
4. Current architecture decisions and maintained documentation.
5. Version history and old task records.
6. Names, folder proximity, guesses, or generic best practice.

Lower evidence can guide inspection. It cannot override contradictory executable evidence.

## Conflict Rules

- Preserve the more specific loaded rule unless it conflicts with a higher-authority instruction.
- Replace repeated meanings with one canonical statement and precise pointers.
- Keep a prohibition only for a real safety or correctness boundary. Pair it with the required behavior.
- Do not store secrets, credentials, personal data, transient paths, or generated output in guidance.
- Do not make cheap repository facts stale by copying them from manifests or command help.

Placement is complete when one owner reaches every intended consumer without duplicate authority.
