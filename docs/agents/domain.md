# Domain context

Use one shared product context. Read only the provider research needed by the current ticket.

## Read order

1. `README.md`
2. `docs/PRODUCT-SPEC.md`
3. `docs/PROVIDER-MATRIX.md`
4. Relevant ADR under `docs/architecture/`
5. Relevant note under `docs/research/`
6. `docs/IMPLEMENTATION-PLAN.md`
7. Current issue under `.scratch/wopenusage/issues/`

## Terms

- **provider:** source adapter that produces quota or observed-usage outcomes.
- **agent:** local or remote coding tool whose activity can be measured.
- **quota:** provider-authoritative allowance and reset window.
- **observed usage:** locally measured activity with stated coverage.
- **spend:** reported or estimated cost with source and pricing basis.
- **snapshot:** cached provider result with observation time and freshness.
- **outcome:** typed success, stale, unavailable, policy-blocked, or error result.
- **coverage:** fraction and limits of data observed by a source.
- **policy blocked:** data path withheld because authorization or public contract is missing.

Report contradictions with the ADR. Do not change architecture or provider policy silently.

