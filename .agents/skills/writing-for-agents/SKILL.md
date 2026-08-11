---
name: writing-for-agents
description: "Agent-facing documentation and skill authoring. Use when creating or editing skills, AGENTS.md, CLAUDE.md, or documents reached through agent context pointers."
---

# Writing For Agents

Aim: make agent-consumed documents drive a predictable process with minimum live context.

Apply these rules to skills, `AGENTS.md`, `CLAUDE.md`, and documents reached through their links:

- **Context pointers**: state what the target contains and the distinct branches that should load it. Front-load the leading trigger word. Remove synonyms that repeat one branch.
- **Information hierarchy**: keep required ordered actions as in-file steps, nearby rules as in-file reference, and branch-only material behind conditional links.
- **Completion criteria**: end each step with a checkable and appropriately exhaustive done condition.
- **Environment cache**: document conventions, reasons, and hidden gotchas. Let scripts, configuration, directory layout, and `--help` remain their own source of truth when lookup is cheap.
- **Pruning**: keep each meaning in one authoritative place. Remove stale lines, default behavior, repeated meanings, and prohibitions that can be stated as a positive target.

For an instruction document, inspect its loading and ownership chain before editing. Put a rule at the narrowest durable scope that needs it, sharpen weak pointers before inlining their targets, and validate that required branches can reach their material.

For a skill, read [references/skill-design.md](references/skill-design.md) for invocation design, granularity, progressive disclosure, pruning, adoption, and failure modes. Read [references/evaluation.md](references/evaluation.md) when it needs trigger or behavior evaluation beyond a smoke test, then follow the process below.

## Skill Process

1. Prove the need.
   - Start from a working task, repeated failure, user correction, domain artifact, or costly non-obvious rule.
   - Inspect existing skills and their descriptions before adding another entrypoint.
   - When value is uncertain, run one representative task without the proposed skill.
   - Choose `create | update | reference-only | no skill`.
   - Done when the missing capability or process is observable and no existing skill already owns it.

2. Set the contract.
   - Pick capability, process, or hybrid shape.
   - Name scope, inputs, outputs, dependencies, action boundaries, and success evidence.
   - Choose invocation deliberately:
     - Codex implicit: keep a concise trigger description; omit `policy.allow_implicit_invocation` or set it to `true` in `agents/openai.yaml`.
     - Codex explicit-only: set `policy.allow_implicit_invocation: false`; keep `description` useful for humans and explicit `$skill` selection.
     - Other clients: use their documented invocation policy; do not invent portable frontmatter fields.
   - Choose repo, user, or plugin distribution only after the ownership boundary is clear.
   - Done when a future agent can tell when to enter, what it may do, and what proves completion.

3. Draft the smallest useful skill.
   - Put only universal steps and high-value gotchas in `SKILL.md`.
   - State each instruction once. Explain why for judgment-heavy rules; use hard constraints for actual safety or correctness boundaries.
   - End ordered steps with checkable completion criteria.
   - Move branch-specific facts, examples, provider notes, and long commands behind context pointers.
   - Add scripts only for deterministic, fragile, or repeatedly reinvented work. Document inputs, outputs, dependencies, and failure shape.
   - Keep the published skill self-contained: runtime code, assets, and imports must not depend on its source repository.
   - Keep maintainer research, history, build inputs, and rejected artifacts outside the published skill root.
   - For executable skills, smoke-test a relocated copy or junction from a different working directory; write generated output outside the skill.
   - Done when every line changes routing, execution, verification, or reference lookup.

4. Validate structure.
   - Run `python <writing-for-agents-path>/scripts/validate_skill.py <skill-path> --skills-root <skills-root>`.
   - Run `skills-ref validate <skill-path>` when `skills-ref` is available.
   - Run the repository validator and each added script's focused tests.
   - Fix errors; review warnings rather than silencing them mechanically.
   - Done when schema, naming, references, metadata, eval fixtures, scripts, and repo conventions are green.

5. Choose the evidence tier.
   - Simple reference/process skill: static validation plus one or two realistic smoke tasks.
   - Ambiguous implicit invocation: add trigger evals with positive and near-miss negative prompts.
   - Complex, frequent, risky, or artifact-producing skill: compare with-skill against no-skill or the previous version in clean contexts.
   - Do not force expensive evals onto a tiny deterministic wrapper; record the skip reason.
   - Done when evaluation cost matches the skill's ambiguity and failure cost.

6. Iterate from evidence.
   - Inspect outputs, artifacts, traces, failed assertions, user feedback, duration, and token cost where available.
   - Generalize from failures; avoid patches that only memorize eval wording.
   - Remove one instruction group at a time and rerun the same cases. Preserve the leaner version when quality holds.
   - Bundle repeated helper work revealed across runs.
   - Done when the skill beats its baseline or prior version without unjustified context, latency, or complexity.

7. Hand back the result.
   - Report the decision, invocation scope, files changed, validation, eval delta, remaining uncertainty, and distribution target.
   - If evidence did not justify a skill, return the candidate and missing evidence instead of scaffolding one.
   - Done when another agent or maintainer can reproduce the proof and continue iteration.

## Minimal Template

```md
---
name: skill-name
description: "Leading intent plus real trigger branches. Use when ..."
---

# Skill Name

## Process

1. First action.
   - Done when ...

2. Verify the outcome.
   - Done when ...

## Reference Files

- `references/<branch>.md`: read when ...
- `scripts/<check>.py`: run when ...
```

For an explicit-only Codex skill, add:

```yaml
# agents/openai.yaml
policy:
  allow_implicit_invocation: false
```

## Review Gates

- Need: measured gap, not generic knowledge or a one-off preference.
- Reach: no duplicate name, trigger branch, or ownership surface.
- Contract: explicit inputs, outputs, evidence, dependencies, and action boundaries.
- Context: universal steps inline; optional branches disclosed.
- Control: freedom matches fragility; defaults beat menus.
- Proof: validation plus the smallest representative eval tier.
- Pruning: no no-ops, sediment, repeated meanings, or stale examples.

## Resources

- [references/skill-design.md](references/skill-design.md): design vocabulary and failure-mode diagnosis.
- [references/glossary.md](references/glossary.md): exact definitions for bold terms in the design guide.
- [references/evaluation.md](references/evaluation.md): trigger and behavior eval protocol, schemas, isolation, grading, and stopping rules.
- [scripts/validate_skill.py](scripts/validate_skill.py): spec-aware cross-file validator.
- [scripts/test_validate_skill.py](scripts/test_validate_skill.py): focused regression tests for the validator.
- [evals/trigger_queries.json](evals/trigger_queries.json): invocation regression set for this skill.
- [evals/evals.json](evals/evals.json): behavior regression cases for this skill.
