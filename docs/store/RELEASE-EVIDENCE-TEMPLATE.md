# TokenUsage Microsoft Store release evidence

Copy this file for each submission. Do not overwrite evidence from an earlier published version.

Suggested private or repository-safe file name:

```text
TokenUsage-<version>-store-evidence.md
```

Do not commit credentials, Partner Center session data, private account screenshots, certificate private keys, or user/provider content.

## Submission identity

| Field | Value |
| --- | --- |
| Product | TokenUsage |
| Store ID | `9NWX6M53B36K` |
| Package identity name | `GVASTETHECREATOR.TokenUsage` |
| Publisher | `CN=DB97CC4C-CCCD-41DF-8D43-C67641CBBC92` |
| Publisher display name | `GVASTETHECREATOR` |
| Package version | `[0.0.0.0]` |
| Product version | `[0.0.0]` |
| Architecture(s) | `[x64 / ARM64]` |
| Source commit | `[SHA]` |
| Source tag | `[tag or n/a]` |
| Build date UTC | `[timestamp]` |
| Builder/workflow | `[local command or workflow run]` |

## Artifact evidence

| Field | Value |
| --- | --- |
| Upload file | `[TokenUsage_... .msixupload]` |
| File size | `[bytes]` |
| SHA-256 | `[hash]` |
| Package/bundle inside upload | `[name]` |
| Manifest target family | `Windows.Desktop` |
| Minimum OS | `10.0.17763.0` |
| Languages | `en-US`, `es-ES` |
| Restricted capabilities | `runFullTrust` |
| App execution alias | `tokenusage.exe` |
| Symbols included | `[yes/no and location]` |

## Automated validation

| Check | Command or run | Result | Evidence |
| --- | --- | --- | --- |
| Store identity | `.\scripts\store\Test-StoreReadiness.ps1` | `[pass/fail]` | `[log]` |
| Full repository check | `.\scripts\check.ps1 -Platform x64 -Configuration Release` | `[pass/fail]` | `[log/run]` |
| Store upload build | `.\scripts\store\Build-StoreUpload.ps1 -Platform x64` | `[pass/fail]` | `[artifact]` |
| Signature/package inspection | `[command]` | `[pass/fail]` | `[log]` |
| Malware/security scan | `[tool]` | `[pass/fail]` | `[report]` |

## Lifecycle qualification

Record the Windows version/build, architecture, account type, and whether the machine had development tools installed.

| Scenario | Environment | Result | Notes/evidence |
| --- | --- | --- | --- |
| Clean install | `[VM/profile]` | `[pass/fail]` | |
| First launch | | `[pass/fail]` | |
| Empty/no-provider state | | `[pass/fail]` | |
| Supported local provider | | `[pass/fail]` | |
| CLI alias in new terminal | | `[pass/fail]` | |
| English UI | | `[pass/fail]` | |
| Spanish UI | | `[pass/fail]` | |
| Offline launch | | `[pass/fail]` | |
| Upgrade from prior Store version | | `[pass/fail/n-a]` | |
| LocalState preserved | | `[pass/fail/n-a]` | |
| Uninstall | | `[pass/fail]` | |
| Reinstall | | `[pass/fail]` | |
| Standard user/no elevation | | `[pass/fail]` | |

## Privacy and security review

- [ ] Public privacy-policy URL loads without authentication.
- [ ] Policy matches the submitted build's local sources, network behavior, storage, and credential handling.
- [ ] Logs contain no API keys, tokens, prompts, responses, conversations, emails, customer content, or unnecessary identifiers.
- [ ] Screenshots contain no personal or provider-account data.
- [ ] Test fixtures are synthetic and clearly non-production.
- [ ] No `.pfx`, private key, certificate password, or Partner Center secret is inside the artifact or repository.
- [ ] The Store build does not advertise a GitHub self-updater as the package update mechanism.

## Partner Center checklist

### Pricing and availability

- Markets: `[selection]`
- Audience: `[selection]`
- Discoverability: `[selection]`
- Schedule: `[selection]`
- Base price: `[selection]`
- Publishing hold: `[manual/date/immediate]`

### Properties

- Primary category: `[value]`
- Secondary category: `[value/n-a]`
- Privacy policy URL: `[url]`
- Website: `[url]`
- Support: `[url/email]`
- Company contact details reviewed: `[yes/no]`
- System requirements reviewed: `[yes/no]`

### Age ratings

- Questionnaire completed: `[yes/no]`
- Assigned rating(s): `[values]`
- Unexpected answers reviewed: `[yes/no]`

### Packages

- Upload status: `[validated/error]`
- Package section status: `[complete/incomplete]`
- Device-family availability: `[Windows Desktop only]`
- Package warnings: `[none/list]`
- Package details match evidence: `[yes/no]`

### Store listings

- English description reviewed: `[yes/no]`
- Spanish description reviewed: `[yes/no]`
- What's new updated: `[yes/no]`
- Screenshot count: `[number]`
- Logo/assets reviewed: `[yes/no]`
- Trademark wording reviewed: `[yes/no]`

### Submission options

- Certification notes date: `[date]`
- `runFullTrust` justification entered: `[yes/no]`
- Tester instructions entered: `[yes/no]`
- Notification audience reviewed: `[yes/no]`
- Publishing hold confirmed: `[yes/no]`

## Certification outcome

| Field | Value |
| --- | --- |
| Submitted at | `[timestamp]` |
| Certification result | `[passed/failed/cancelled]` |
| Certification report ID | `[reference]` |
| Findings | `[summary]` |
| Remediation commit/submission | `[reference]` |
| Approved at | `[timestamp]` |
| Published at | `[timestamp or held]` |
| Live Store URL | `[url once available]` |
| Store deep link | `[value once available]` |

## Final approval

- Engineering: `[name/date]`
- Product/listing: `[name/date]`
- Privacy/security: `[name/date]`
- Publisher account owner: `[name/date]`
- Release decision: `[publish/hold/reject]`
