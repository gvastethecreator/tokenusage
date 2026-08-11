# Writing Great Skills

A skill exists to wrangle determinism out of a stochastic system. **Predictability** — the agent taking the same _process_ every run, not producing the same output — is the root virtue; every lever below serves it.

**Bold terms** are defined in [`glossary.md`](glossary.md); look them up there for the full meaning.

## Skill shape

Pick the shape before writing details:

- Capability skill: thin wrapper over a deterministic tool, script, API, browser harness, or external service. `SKILL.md` should teach state check, common command shape, output shape, failure modes, and verification. Put fragile logic in scripts instead of prose.
- Process skill: discipline for judgment-heavy work. `SKILL.md` should own the decision loop, completion criteria, evidence gates, and handoff shape. Add scripts only for validation, extraction, or repeatable audits.
- Hybrid skill: allowed only when the capability and discipline cannot succeed separately. If one branch can stand alone, split it or route to it instead of bundling.

This prevents two common mistakes: wrapping a tool with pages of process theater, or making a method skill depend on a private tool the user may not have.

## Invocation

Two choices, trading different costs:

- A **model-invoked** skill can fire autonomously and other skills can reach it. It contributes to **context load** because its name and description participate in discovery. In Codex, omit `policy.allow_implicit_invocation` or set it to `true` in `agents/openai.yaml`; write a concise model-facing description with real trigger branches.
- An **explicit-only** skill fires when the user selects it with `$skill`. It reduces accidental activation and spends **cognitive load** because the user must remember it. In Codex, set `policy.allow_implicit_invocation: false` in `agents/openai.yaml`; keep the description useful for human selection. Other clients may expose a different policy mechanism.

Pick implicit invocation only when the agent must reach the skill on its own or another skill must route to it. Invocation policy is client configuration, not a portable custom `SKILL.md` field.

When explicit-only skills multiply past what you can remember, that piled-up cognitive load is cured by a **router skill**: one skill that names the others and when the human should select each.

## Writing the description

An implicitly invoked skill's **description** does two jobs — state what the skill is, and list the **branches** that should trigger it. Every word increases discovery **context load**, so a description earns even harder pruning than the body:

- **Front-load the skill's leading word** — the description is where it does its invocation work.
- **One trigger per branch.** Synonyms that rename a single branch are **duplication** — "build features using TDD … asks for test-first development" is one branch written twice. Collapse them; keep only genuinely distinct branches.
- **Cut identity that's already in the body.** Keep the description to triggers, plus any "when another skill needs…" reach clause.

## Information hierarchy

A skill is built from two content types — **steps** and **reference** — that mix freely: a skill can be all steps, all reference, or both. The core decision is which to use and where each sits on the **information hierarchy**, a ladder ranked by how immediately the agent needs the material:

1. **In-skill step** — an ordered action in `SKILL.md`, the primary tier: what the agent does, in order. Each step ends on a **completion criterion**, the condition that tells the agent the work is done. Make it _checkable_ (can the agent tell done from not-done?) and, where it matters, _exhaustive_ ("every modified model accounted for", not "produce a change list") — a vague criterion invites **premature completion**.
2. **In-skill reference** — a definition, rule, or fact in `SKILL.md`, consulted on demand. Often a legitimately flat peer-set (every rule of a review on one rung) — a fine arrangement, not a smell. _This skill is all reference._
3. **External reference** — reference pushed out of `SKILL.md` into a separate file, reached by a **context pointer**, loaded only when the pointer fires. (Spans _disclosed_ reference — a sibling file like `glossary.md`, still part of the skill — through fully **external reference** that lives outside the skill system and any skill can point at.)

A demanding completion criterion drives thorough **legwork** — the digging the agent does within the work — whether the skill has steps or not, since "every rule applied" binds flat reference just as "every step done" binds a sequence.

Push too little down and the top bloats; push too much and you hide material the agent actually needs. That tension is the whole decision.

**Progressive disclosure** is the move down the ladder — out of `SKILL.md` into a linked file — so the top stays legible. Mechanics: a linked `.md` file in the skill folder, named for what it holds (this skill discloses its full definitions to `glossary.md`). Some skills are used in more than one way, and each distinct way is a **branch** — different runs taking different paths through the skill. Branching is the cleanest disclosure test: inline what every branch needs, and push behind a pointer what only some branches reach. A **context pointer**'s _wording_, not its target, decides when and how reliably the agent reaches the material.

Where the ladder decides _how far down_ a piece sits, **co-location** decides _what sits beside it_ once there: keep a concept's definition, rules, and caveats under one heading rather than scattered, so reading one part brings its neighbours with it.

## When to split

**Granularity** is how finely you divide skills, and each cut spends one of the two loads, so split only when the cut earns it. Two cuts:

- **By invocation** — split off an implicitly invoked skill when you have a distinct **leading word** that should trigger it on its own, or another skill must reach it. You pay discovery **context load** for the new description, so that independent reach has to be worth it.
- **By sequence** — split a run of **steps** when the steps still ahead (a step's **post-completion steps**) tempt the agent to rush the one in front of it (**premature completion**). Keeping them out of view encourages the agent to do more **legwork** on the current task.

## Pruning

Keep each meaning in a **single source of truth**: one authoritative place, so changing the behaviour is a one-place edit.

The environment is also a source of truth. A skill that repeats scripts, configuration, directory layout, or cheap `--help` output is a **cache**. Keep a cache only when the lookup is costly. Cache unwritten conventions, decision reasons, and gotchas that the environment cannot show.

Check every line for **relevance**: does it still bear on what the skill does?

Then hunt **no-ops** sentence by sentence, not just line by line: run the no-op test on each sentence in isolation, and when one fails, delete the whole sentence rather than trim words from it. Be aggressive — most prose that fails should go, not be rewritten.

## Leading words

A **leading word** is a compact concept already living in the model's pretraining that the agent thinks with while running the skill (e.g. _lesson_, _fog of war_, _tracer bullets_). Repeated throughout the text (though not necessarily - a strong leading word might only be needed once), it accumulates a distributed definition and anchors a whole region of behaviour in the fewest tokens, by recruiting priors the model already holds.

It serves predictability twice. In the body it anchors _execution_: the agent reaches for the same behaviour every time the word appears. In the description it anchors _invocation_: when the same word lives in your prompts, docs, and code, the agent links that shared language to the skill and fires it more reliably.

Hunt for opportunities to refactor skills to use leading words. A triad spelled out at three sites (**duplication**), a description spending a sentence to gesture at one idea — each is a passage begging to **collapse** into a single token. Examples include:

- "fast, deterministic, low-overhead" -> _tight_ — one quality restated across a phase — into a single pretrained word (a _tight_ loop).
- "a loop you believe in" -> _red_ — converts a fuzzy gate into a binary observable state (the loop goes _red_ on the bug, or it doesn't).

You win twice over: fewer tokens, _and_ a sharper hook for the agent to hang its thinking on. Assume every skill is carrying restatements that leading words retire — go find them.

## Adopting outside skills

Treat third-party skills as untrusted input until audited:

- Read every file in the skill folder, including references, scripts, templates, and hidden setup assumptions.
- Check for outbound network calls, credential access, destructive filesystem commands, private paths, local-only tools, and prompt-injection language.
- Pin the source commit in a source note when copying or adapting material.
- Import portable patterns, not personal stack assumptions. Replace home-directory paths, vendor keys, OS-only commands, and private services with local repo conventions.
- Normalize to this repo's style: quoted descriptions, terse trigger text, progressive disclosure, completion criteria, and repo validator passing.

Do not copy a popular skill just because the workflow sounds clever. The adoption test is whether it changes future agent behavior in this repo under real verification.

## Failure modes

Use these to diagnose issues the user may be having with the skill.

- **Premature completion** — ending a step before it's genuinely done, attention slipping to _being done_. Defence, in order: sharpen the completion criterion first (cheap, local); only if it is irreducibly fuzzy _and_ you observe the rush, hide the post-completion steps by splitting (the sequence cut).
- **Negation** — steering by naming what not to do, which can activate the forbidden pattern. Prefer the positive target; keep prohibitions only for hard guardrails and pair them with the desired behavior.
- **Negative Space** — an omission silently delegates a decision to the model's priors. Review what the skill leaves unsaid: make the omission an intentional open branch or state the missing target explicitly.
- **Duplication** — the same meaning in more than one place. Costs maintenance and tokens, and inflates a meaning's prominence on the ladder past its real rank.
- **Sediment** — stale layers that settle because adding feels safe and removing feels risky. The default fate of any skill without a pruning discipline.
- **Sprawl** — a skill simply too long, even when every line is live and unique. Hurts readability and maintainability and wastes tokens. The cure is the ladder: disclose **reference** behind pointers, and split by **branch** or sequence so each path carries only what it needs.
- **No-op** — a line the model already obeys by default, so you pay load to say nothing. The test: does it change behaviour versus the default? A weak leading word (_be thorough_ when the agent is already thorough-ish) is a no-op; the fix is a stronger word (_relentless_), not a different technique.
