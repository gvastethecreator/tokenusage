# Reliable usage comparisons — second-pass review

**Review date:** 2026-09-06. **Status:** proposed work, not implemented product functionality.

**Repository:** `gvastethecreator/tokenusage`. **Reviewed main:** `27d36e2b95aeb8ef4111f02233143ca6e2e87aaf`.

The first audit and this second review resolve to the same main commit. A fresh GitHub comparison returned `identical`, zero commits ahead/behind, and no changed files. This is an additional investigation of that revision, not an invented review of new commits. At the initial check there were no open PRs. Other branches were listed, not adopted as the baseline. Recheck main before starting implementation.

This package consolidates the earlier nine measurement findings and fourteen proposed tickets, adds nine second-pass findings and eight tickets, and specifies a staged implementation. Competitor repositories are research references only; they are not authorized write targets. No unrelated repository is included by implication.

## Read in this order

1. [Audit](AUDIT.md): 18 findings, code evidence, impact, and test targets.
2. [RFC](RFC.md): source separation, observation contracts, eligibility, metrics, migration, privacy, and performance.
3. [Backlog](BACKLOG.md): 22 repository-local tickets with dependencies and acceptance criteria; these are not GitHub issue numbers.
4. [Validation](VALIDATION.md): executed checks versus required Windows/C# validation.
5. [Competitors and official contracts](COMPETITORS.md): specific ideas to adopt and constraints not to copy.

[Review manifest](review-manifest.json) records the baseline and evidence classification. The [counterexample script](reproduce_counterexamples.py) uses synthetic values and in-memory SQLite only.

## Decision

Retain C#, WinUI, SQLite, bounded readers, the CLI, and the local-first policy. Protect historical evidence and stop unsupported normalized conclusions before adding charts or more providers. Preserve the existing split between reported cost, catalog estimates, unavailable prices, and remote quota.

This PR changes documentation and adds a standalone research script. It does not migrate storage, change runtime behavior, install telemetry, consume reset credits, create paid workloads, or claim Windows validation. Merging it does not fix the defects described here.
