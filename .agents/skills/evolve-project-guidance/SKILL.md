---
name: evolve-project-guidance
description: "Project guidance from codebase evidence. Use for repository-specific skill creation, project agent-rule updates, contributor .agents/skills setup, and evidence-led guidance improvement."
---

# Evolve Project Guidance

Turn current repository evidence into the narrowest useful agent guidance. Preserve shared guidance and unrelated local work.

Apply `writing-for-agents` when it is available. Before task or plan text, apply `simple-english` in pragmatic mode.

Apply `maintain-code-map` when the repository owns a stale or missing code map.

## Boundaries

- Use this skill for project evidence, artifact placement, and the improvement loop.
- Use `repo-rules-onboarding` for installation of a shared rules package.
- Use `codex-ruleset-builder` for changes to a shared rules package.
- Keep executable behavior in code or configuration. Do not replace product behavior with agent prose.

## Process

1. Build the evidence packet.
   - Resolve the repository root. Read every applicable instruction file before other project files.
   - Run `python <skill-root>/scripts/inspect_project_guidance.py --repo .`.
   - Inspect `git status -sb`, manifests, architecture documents, entrypoints, callers, tests, CI, and public commands.
   - Measure code-map freshness when the repository owns a code-map workflow. Refresh a stale map before impact analysis.
   - Trace existing skills, rules, pointers, and work documents through their loading or call chain.
   - Treat code, configuration, tests, runtime proof, and explicit user corrections as primary evidence.
   - Done when the packet identifies structure, consumers, verification seams, current guidance, and dirty work.

2. Select one durable owner.
   - Read [placement.md](references/placement.md) when the correct artifact is unclear.
   - Select `code | configuration | rule | skill | work instruction | reference | no change`.
   - Put always-on project constraints in the narrowest loaded rule file.
   - Put conditional, repeatable workflows or domain knowledge in a skill.
   - Put one effort's state, decisions, and evidence in its existing work document.
   - Cache only facts that are costly to find or easy to misread.
   - Done when one owner has clear consumers, scope, authority, and success evidence.

3. Select the project skill distribution.
   - Read [project-skill-distribution.md](references/project-skill-distribution.md) when the project needs `.agents/skills/`.
   - Default contributor-facing repositories to physical, versioned `portable` skills.
   - Use local `junction` skills only on an ignored and untracked loading surface.
   - Use `owned` for project-specific implementations that the project maintains directly.
   - Include only skills required by active project rules, workflows, and their runtime dependencies.
   - Preview portable changes with `sync_project_skills.py`. Apply them only after the preview has no blocked paths.
   - Done when a clean contributor checkout has every required skill without private machine paths.

4. Define the smallest complete delta.
   - Read [improvement-review.md](references/improvement-review.md) for an existing skill, rule set, or work instruction.
   - Record each symptom, source, affected consumer, cause, proposed change, and proof.
   - Account for every retained, changed, moved, and removed instruction.
   - Preserve inherited guidance. Replace duplication with a precise pointer to its canonical owner.
   - State write, network, secret, production, destructive, commit, and publish boundaries.
   - Done when the delta fixes the observed gap without creating a parallel source of truth.

5. Edit the source artifacts.
   - Preserve unrelated and concurrent changes.
   - Edit canonical sources before generated projections, catalogs, or synchronized copies.
   - For rules, keep project facts, verified commands, hard boundaries, and conditional context pointers.
   - For skills, use the installed initializer and validator. Add metadata, scripts, references, and evals only when justified.
   - Materialize contributor skills as physical directories under `.agents/skills/` and record `.agents/skills.lock.json`.
   - Keep local junction targets outside the tracked project. Never publish a junction as a portable skill.
   - Put deterministic or fragile repeated work in a script. Run every added script.
   - Update an existing work document in place. Do not create a second plan for the same effort.
   - Update code-map artifacts together when module boundaries, dependencies, routes, or major flows change.
   - Done when every changed artifact is complete, reachable, and owned at one durable location.

6. Verify the changed behavior.
   - Parse frontmatter and configuration. Verify local links, pointers, paths, and command names.
   - For rules, inspect the full loading hierarchy and test the cheapest representative command.
   - For skills, run the repository validator, each added script, and one realistic smoke task.
   - Verify portable bundles with `sync_project_skills.py --verify`. Run them from a clean or relocated checkout.
   - Verify that every contributor skill is physical and contains its runtime references, scripts, and assets.
   - Add trigger cases only for implicit invocation. Use comparative evals for complex or costly behavior.
   - For work instructions, verify status, decisions, dependencies, and evidence against the current repository.
   - Run a broad gate only when the changed surface or repository contract requires it.
   - Done when the proof can falsify the changed contract and every unrun gate has a reason.

7. Record the result and the next learning gate.
   - Report the evidence, owner decision, changed files, verification, and remaining uncertainty.
   - Promote a learning only from a verified outcome, explicit correction, repeated failure, or one costly failure.
   - Remove stale guidance when current evidence disproves it.
   - Do not commit, publish, or synchronize unless the request or repository workflow authorizes it.
   - Done when another agent can reproduce the decision without chat history.

## Resources

- [placement.md](references/placement.md): choose the durable owner and scope.
- [project-skill-distribution.md](references/project-skill-distribution.md): choose portable, junction, or owned project skills.
- [improvement-review.md](references/improvement-review.md): audit existing guidance and prove an improvement.
- `scripts/inspect_project_guidance.py`: produce a read-only repository evidence packet.
- `scripts/sync_project_skills.py`: preview, copy, update, lock, and verify portable contributor skills.
