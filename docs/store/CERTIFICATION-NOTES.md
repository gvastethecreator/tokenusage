# TokenUsage certification notes

Copy and adapt this document in Partner Center immediately before submitting. Replace every bracketed placeholder and update the date.

## Notes for certification

**Notes date:** `[YYYY-MM-DD]`  
**Product:** TokenUsage  
**Store ID:** `9NWX6M53B36K`  
**Package version:** `[MAJOR.MINOR.BUILD.REVISION]`  
**Submission commit:** `[GIT SHA]`

TokenUsage is a local-first Windows tray application and command-line utility that summarizes quota, token usage, estimated or reported cost, activity, and reset-cycle data exposed by supported AI developer tools.

No TokenUsage account or test credentials are required.

### Basic test path

1. Install the submitted package.
2. Launch **TokenUsage** from the Start menu.
3. The tray application opens even when none of the supported provider tools is installed.
4. In an environment without provider data, the application intentionally displays unavailable, empty, partial, or unsupported states. This is expected behavior and does not indicate an incomplete application.
5. Open the detailed report to review the provider matrix, range controls, status explanations, and local report surfaces.
6. Open **Settings** to switch between English and Spanish.
7. Open PowerShell after installation and run:

   ```powershell
   tokenusage doctor --format human
   tokenusage providers --format human
   tokenusage usage --days 7 --format human
   ```

8. The execution alias is registered by the package. A newly opened terminal may be required after installation.

### Data behavior

- TokenUsage reads only bounded, documented local usage sources for installed developer tools.
- It does not intentionally store prompts, responses, conversations, command contents, source files, emails, or credentials owned by another application.
- Provider-reported values, locally estimated values, partial data, stale data, unavailable values, and unpriced tokens are presented as distinct states.
- Network-enabled provider connections are opt-in and remain disabled unless the user explicitly configures them.
- The application functions without network access, with the expected limitation that remote or opt-in data cannot refresh.

### Environment-dependent behavior

The available providers and metrics depend on which supported developer tools and source versions are installed on the certification machine. A provider catalog entry does not imply that a working reader is available. TokenUsage intentionally does not generate sample activity for missing providers.

### Hidden or gated functionality

There are no paid, hidden, or account-gated TokenUsage features in this submission. Provider-specific data appears only when an approved local source is present or the user explicitly enables a documented connection.

### Update behavior

This Store package relies on Microsoft Store for package updates. The portable GitHub distribution is a separate channel and does not share the package-local data directory.

### Support and privacy

- Privacy policy: `[PUBLIC PRIVACY POLICY URL]`
- Product website: `[PUBLIC PRODUCT URL]`
- Support: `[PUBLIC SUPPORT URL OR EMAIL]`

## Restricted capability: `runFullTrust`

TokenUsage is a native desktop application and packaged CLI. It requires `runFullTrust` to perform the following desktop operations:

1. Read bounded local files and databases exposed by installed AI developer tools using ordinary Win32 and .NET file/database APIs.
2. Communicate with approved local services, such as an official provider-owned local server, when that reader is enabled.
3. Use Windows desktop integrations and package-local storage needed by the tray application.
4. Protect user-supplied opt-in credentials with Windows credential facilities where supported.
5. execute the packaged `tokenusage.exe` command-line application through the declared App Execution Alias.

TokenUsage does not use `runFullTrust` to bypass Windows security boundaries, elevate silently, copy another application's private credentials, inspect prompt or response content, or transmit local customer content without an explicitly enabled and documented integration.

## Reviewer troubleshooting

### The app shows no usage

This is expected if the certification environment does not contain a supported provider or approved local data source. Verify the UI still explains the unavailable state and remains navigable.

### The CLI alias is not found

Close the existing terminal, open a new PowerShell window, and retry `tokenusage doctor --format human` so Windows refreshes the execution-alias path.

### A provider is marked partial, unavailable, or blocked

These labels are deliberate product states. They communicate the limits of the available evidence instead of fabricating activity or quota values.

### An opt-in connection is not configured

No external credentials are supplied for certification. The app must still be fully testable in its local and unavailable-data states.
