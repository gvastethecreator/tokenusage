## Linked issue

Closes #

Every pull request needs an issue opened before implementation.

## Result

Describe the user-visible problem and the result of this change.

## Scope

- Included:
- Not included:

## Provider data and privacy

Complete this section for provider, storage, pricing, or diagnostics changes.

- Provider and tested version:
- Source or local aggregate:
- Data fields read:
- Sensitive fields excluded:
- Cost provenance and coverage:
- Failure states checked:

## Verification

List every command and its result.

```text
command -> result
```

Add sanitized screenshots, CLI output, or other runtime evidence when required.

## Checklist

- [ ] This pull request links an issue opened before implementation.
- [ ] The diff stays within the agreed issue scope.
- [ ] Focused tests cover each changed behavior.
- [ ] `scripts/check.ps1 -Platform x64 -Configuration Release` passes, or the blocker is documented.
- [ ] UI changes include packaged runtime evidence and accessibility checks.
- [ ] Provider changes include real-source evidence or remain clearly unverified.
- [ ] Reported, estimated, unavailable, and unpriced costs remain separate.
- [ ] No credentials, customer content, identifiers, or private paths are present.
- [ ] Public behavior, provider status, and contracts have updated documentation.

## Remaining limits

List known gaps, unverified states, and follow-up work.
