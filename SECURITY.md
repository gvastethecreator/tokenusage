# Security policy

## Supported versions

TokenUsage is in pre-release development. Security fixes target the current `main` branch. No released version has a support promise yet.

## Report a vulnerability

Do not open a public issue for a suspected vulnerability. Use the repository's [private security advisory form](https://github.com/gvastethecreator/wopenusage/security/advisories/new).

Include:

- the affected commit or build;
- clear steps to reproduce the issue;
- the expected and observed result;
- the likely impact;
- a small test case when it contains no private data.

Do not send live credentials, session tokens, prompts, conversations, customer data, or unredacted local paths. Use placeholders and state what kind of value was removed.

The maintainer will confirm the report, assess its scope, and coordinate a fix before public disclosure. Response times may vary while the project remains pre-release.

## Security boundaries

TokenUsage must not copy credentials from another app, read another app's credential store, or add customer content to diagnostics. User-supplied provider keys belong in Windows Credential Locker. Logs and fixtures must use clear fake values.
