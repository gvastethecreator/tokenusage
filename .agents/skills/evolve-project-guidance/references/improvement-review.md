# Improvement Review

Use this review for an existing skill, rule set, prompt, plan, runbook, or work instruction.

## Evidence Ledger

Record these fields for each candidate:

- Symptom: the observable failure, friction, drift, or waste.
- Evidence: the exact output, correction, path, command, test, or trace.
- Consumer: the agent, workflow, tool, or project branch that receives the guidance.
- Cause: the instruction, omission, placement, script, or stale assumption that explains the symptom.
- Delta: the smallest change that removes the cause.
- Proof: the same case, focused command, or assertion that can falsify the delta.
- Reuse horizon: one task, one repository, or many repositories.

Do not promote a candidate that has no observable symptom or affected consumer.

## Review Dimensions

- Reach: the intended consumer can load the artifact at the correct time.
- Ownership: one source owns each meaning, command, and decision.
- Correctness: instructions match current code, configuration, and authority boundaries.
- Completion: each process step has a checkable end condition.
- Context cost: always-loaded text earns its space and optional branches stay behind pointers.
- Determinism: scripts own fragile repeated mechanics.
- Portability: published skills do not depend on private paths, hidden imports, credentials, or source-repository state.
- Evidence: claims distinguish static, focused, runtime, visual, integration, and human proof.
- Failure states: the artifact states safe behavior for observed partial or blocked outcomes.

## Change Loop

1. Capture the current artifact and one representative case.
2. Run the case before editing when safe and practical.
3. Identify the largest discriminating failure.
4. Change one instruction group, pointer, or helper.
5. Run the same case with the same inputs and permissions.
6. Compare correctness, work, context, time, and new failure modes.
7. Remove instructions that add work without improving the result.

For a refactor, map every old requirement to `keep | change | move | remove`. Give evidence for each removal.

## Promotion Gate

Promote guidance when one of these sources proves reusable value:

- an explicit user correction.
- a repeated failure across independent tasks.
- one costly or dangerous failure.
- verified code or configuration drift.
- a measured improvement against the previous artifact.

Do not promote speculation, stylistic preference, unverified summaries, or a successful result with no causal link to the candidate.

Stop when the new artifact beats the baseline, the leaner artifact preserves quality, or further edits add no material value.
