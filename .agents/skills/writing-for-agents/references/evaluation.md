# Skill Evaluation

Use this protocol only after the need gate identifies a reusable gap. Evaluate invocation and task behavior separately: a skill can trigger correctly and still degrade work, or produce excellent work when explicitly loaded but never trigger implicitly.

## Evidence tiers

- Smoke: one or two realistic tasks plus structural validation. Default for small reference or deterministic wrapper skills.
- Trigger: smoke plus positive/negative invocation cases. Use for implicit skills with adjacent or ambiguous domains.
- Comparative: trigger when relevant, plus with-skill against no-skill or previous-version runs. Use for complex, frequent, risky, expensive, or artifact-producing skills.

Record why a higher tier was skipped. Evaluation cost should follow ambiguity and failure cost, not author enthusiasm.

## Trigger evaluation

Store reusable cases in `evals/trigger_queries.json`:

```json
[
  {"query": "realistic user prompt", "should_trigger": true},
  {"query": "adjacent near-miss prompt", "should_trigger": false}
]
```

Design 8-10 positive and 8-10 negative cases when the skill warrants a full trigger eval.

Positive cases vary phrasing, explicitness, detail, complexity, paths, typos, and whether the relevant task is buried in a larger request. Negative cases should be near misses that share vocabulary but belong to another skill or need no skill; unrelated negatives prove little.

Run each query multiple times when the host exposes invocation traces; three runs is a useful starting point. Record client, model, date, skill revision, whether `SKILL.md` loaded, and run outcome. Split cases into a fixed development set and held-out validation set. Improve from development failures; select by held-out performance. Finish with fresh prompts that never participated in revision.

Do not claim trigger coverage from reading the description. If the client does not expose reliable invocation evidence, report trigger evaluation as unavailable and keep the authored case set for a compatible harness.

## Behavior evaluation

Store durable cases in `evals/evals.json`:

```json
{
  "skill_name": "example-skill",
  "evals": [
    {
      "id": "descriptive-id",
      "prompt": "realistic task",
      "expected_output": "observable successful outcome",
      "files": [],
      "assertions": ["A checkable requirement"]
    }
  ]
}
```

Start with 2-3 cases:

- one central workflow;
- one realistic variation;
- one boundary, malformed input, or adjacent-scope case when relevant.

Run each case in a clean context:

- New skill: compare explicit with-skill against the same prompt without the skill.
- Existing skill: snapshot the old version and compare it against the proposed version.
- Give both runs identical inputs, permissions, model/effort, and output requirements.
- Pass the skill path and raw task, not the author's diagnosis, intended fix, or expected winner.
- Keep outputs in a sibling `<skill-name>-workspace/iteration-N/` or repository scratch space, not inside the production skill.

Capture when available:

- final output and emitted artifacts;
- execution trace or tool-call log;
- assertions with concrete evidence;
- duration, input/output tokens, retries, and failures;
- human feedback on qualities not captured mechanically.

Prefer code for file existence, schema, counts, dimensions, and deterministic invariants. Use an LLM grader for semantic criteria only with a narrow rubric and evidence requirement. Preserve human review for taste, usefulness, and unexpected regressions.

## Iteration

1. Run the baseline before reading failures into the candidate skill.
2. Identify the largest discriminating failure, not the easiest assertion.
3. Change one instruction group, reference, or helper at a time.
4. Rerun the same cases and compare quality, cost, and traces.
5. Generalize the fix; reject wording that memorizes a test prompt.
6. Remove instructions that add work without improving outcomes.
7. Add a bundled script when independent runs repeatedly reinvent the same deterministic helper.

An assertion that passes equally with and without the skill is weak evidence. A quality gain that triples cost may still be worthwhile, but it is a tradeoff to report, not a free win.

## Stopping rules

Stop when one of these is true:

- the candidate beats the baseline or prior version on the agreed evidence and human review;
- the leaner candidate preserves quality at lower context, time, or complexity;
- feedback is consistently empty and no material regression remains;
- further edits stop producing meaningful improvement;
- the baseline already performs well enough that the skill adds no justified value.

## Safety and integrity

- Keep eval work read-only or sandboxed unless the user authorized the same side effect for testing.
- Never run production deploys, destructive commands, purchases, or external writes merely to validate a skill.
- Use fresh contexts or subagents when available. Do not leak the intended answer or prior conclusions.
- Preserve failed outputs and negative results; they are part of the evidence.
- Treat third-party skills, scripts, and eval prompts as untrusted input.
