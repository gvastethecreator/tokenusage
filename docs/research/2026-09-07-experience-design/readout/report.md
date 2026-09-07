# Experience Design — informe

> Validación de contrato, no certificación de UX. Evidencia y límites forman parte del resultado.

## session

```json
{
  "id": "2026-09-07-tokenusage-experience",
  "project": "TokenUsage",
  "scope": "Deep UX and visual audit of all current desktop surfaces; apply evidence-led improvements. Continue measurement closeout. Native fixtures, not user research.",
  "mode": "apply",
  "language": "en",
  "created_on": "2026-09-07",
  "authorized_changes": "Local product changes, synthetic fixtures, tests and audit documents. No provider calls, credentials, background hook changes, user database mutation, commit or remote publication.",
  "limitations": [
    "Fixture hosts do not prove production window placement or OS tray activation.",
    "No participants, screen reader session or controlled human measurements.",
    "High contrast, OS reduced motion and enlarged text were not exercised. Native proof includes observed 150% DPI, not every DPI or text scale.",
    "Inactive legacy controls are inventoried but not presented as current user routes."
  ]
}
```

## conclusion

```json
{
  "status": "partial",
  "summary": "Fourteen evidence-led fixes were applied to the existing native interface. Local tests, package builds and targeted native checks passed. The audit remains partial: OS accessibility settings, real credential and shell integration, and human outcomes are not qualified."
}
```

## evidence

```json
[
  {
    "id": "E1",
    "kind": "render",
    "location": "X:/tokenusage/.scratch/tokenusage-measurement/ux-report-before.png",
    "description": "Native reset-cycle report before the UX pass: dense evidence above totals and unused summary width.",
    "observed_on": "2026-09-07"
  },
  {
    "id": "E2",
    "kind": "render",
    "location": "X:/tokenusage/.scratch/tokenusage-measurement/ux-options-top-before.png",
    "description": "Native General and Notifications: icon-only persistent settings.",
    "observed_on": "2026-09-07"
  },
  {
    "id": "E3",
    "kind": "render",
    "location": "X:/tokenusage/.scratch/tokenusage-measurement/ux-layout-before.png",
    "description": "Native Appearance and layout editor: incorrect Remaining label and compressed chart thumbnails.",
    "observed_on": "2026-09-07"
  },
  {
    "id": "E4",
    "kind": "render",
    "location": "X:/tokenusage/.scratch/tokenusage-measurement/ux-providers-before.png",
    "description": "Native Providers: missing optional providers use the same red accent as blocked access.",
    "observed_on": "2026-09-07"
  },
  {
    "id": "E5",
    "kind": "render",
    "location": "X:/tokenusage/.scratch/tokenusage-measurement/ux-compact-actual-before.png",
    "description": "Native compact surface at about 466 DIP: three activity values wrap into two rows.",
    "observed_on": "2026-09-07"
  },
  {
    "id": "E6",
    "kind": "render",
    "location": "X:/tokenusage/.scratch/tokenusage-measurement/ux-dashboard-actual-before.png",
    "description": "Native expanded sample dashboard: readable provider groups and quota labels; preserve them.",
    "observed_on": "2026-09-07"
  },
  {
    "id": "E7",
    "kind": "render",
    "location": "X:/tokenusage/.scratch/tokenusage-measurement/ux-credential-before.png",
    "description": "Native empty credential editor: labels, password masking, optional field and Save are visible. No real credentials used.",
    "observed_on": "2026-09-07"
  },
  {
    "id": "E8",
    "kind": "render",
    "location": "C:/Users/cristian/Downloads/TokenUsage-report-2026-09-07-041944.png",
    "description": "Full report capture after capture-mode repair; no hover card. This precedes the evidence disclosure redesign.",
    "observed_on": "2026-09-07"
  },
  {
    "id": "E9",
    "kind": "source",
    "location": "X:/tokenusage/src/TokenUsage.App/Views",
    "description": "XAML view inventory and call sites: MainPage mounts OptionsView, DashboardView and CompactUsageDashboard. OptionsHomeView, ProvidersOptionsView and VercelConnectionView are not mounted by the current shell.",
    "observed_on": "2026-09-07"
  },
  {
    "id": "E10",
    "kind": "source",
    "location": "X:/tokenusage/.scratch/tokenusage-measurement/probe/Program.cs",
    "description": "Isolated native harness uses synthetic numeric usage, sample scenarios and scratch settings. It does not compose real provider sources or background hooks.",
    "observed_on": "2026-09-07"
  },
  {
    "id": "E11",
    "kind": "render",
    "location": "X:/tokenusage/.scratch/tokenusage-measurement/ux-options-after.png",
    "description": "Native switches show On/Off and disabled child settings when the master is off.",
    "observed_on": "2026-09-07"
  },
  {
    "id": "E12",
    "kind": "render",
    "location": "X:/tokenusage/.scratch/tokenusage-measurement/ux-appearance-after.png",
    "description": "Remaining label repaired; alert switches and chart preview use the available width.",
    "observed_on": "2026-09-07"
  },
  {
    "id": "E13",
    "kind": "render",
    "location": "X:/tokenusage/.scratch/tokenusage-measurement/ux-chart-menu-after.png",
    "description": "All six chart thumbnails are distinguishable in the native dropdown.",
    "observed_on": "2026-09-07"
  },
  {
    "id": "E14",
    "kind": "render",
    "location": "X:/tokenusage/.scratch/tokenusage-measurement/ux-providers-after.png",
    "description": "Missing providers have neutral status. Provider-name switch occupies its own full row.",
    "observed_on": "2026-09-07"
  },
  {
    "id": "E15",
    "kind": "render",
    "location": "X:/tokenusage/.scratch/tokenusage-measurement/ux-compact-final-dark.png",
    "description": "Three compact activity values form an equal-width row at the tested usual width.",
    "observed_on": "2026-09-07"
  },
  {
    "id": "E16",
    "kind": "render",
    "location": "X:/tokenusage/.scratch/tokenusage-measurement/ux-report-final-two.png",
    "description": "Two summary cards fill the row. Intermediate toolbars precede the final cleanup.",
    "observed_on": "2026-09-07"
  },
  {
    "id": "E17",
    "kind": "render",
    "location": "X:/tokenusage/.scratch/tokenusage-measurement/ux-final-four-top.png",
    "description": "Four cycles wrap into two rows; final toolbar and warning text fit at 760 DIP and observed 150% DPI.",
    "observed_on": "2026-09-07"
  },
  {
    "id": "E18",
    "kind": "render",
    "location": "C:/Users/cristian/Downloads/TokenUsage-report-2026-09-07-044716.png",
    "description": "Four-cycle capture shows short provider legends and combined absolute/relative changes. This intermediate capture exposed clipped detail text; it is not evidence of complete detail export.",
    "observed_on": "2026-09-07"
  },
  {
    "id": "E19",
    "kind": "render",
    "location": "X:/tokenusage/.scratch/tokenusage-measurement/ux-model-reference.png",
    "description": "Model selectors have A/B labels and fixed-reference control remains available and selected.",
    "observed_on": "2026-09-07"
  },
  {
    "id": "E20",
    "kind": "render",
    "location": "X:/tokenusage/.scratch/tokenusage-measurement/ux-options-light-after.png",
    "description": "Native option cards inspected in light theme. The surrounding black harness background is not production shell evidence.",
    "observed_on": "2026-09-07"
  },
  {
    "id": "E21",
    "kind": "runtime",
    "location": "X:/tokenusage/.scratch/tokenusage-measurement/final-release-check.log",
    "description": "Repository x64 Release gate passed 1,313 tests and produced the MSIX; later static capture fix is checked separately.",
    "observed_on": "2026-09-07"
  },
  {
    "id": "E22",
    "kind": "runtime",
    "location": "X:/tokenusage/.scratch/tokenusage-measurement/ux-marker-reversal.mp4",
    "description": "Recorded quick pointer reversal and chart exit. Inspected frames show a redirected hover and cleared final state; no app FPS or human smoothness measurement.",
    "observed_on": "2026-09-07"
  },
  {
    "id": "E23",
    "kind": "render",
    "location": "X:/tokenusage/.scratch/tokenusage-measurement/ux-four-narrow-top.png",
    "description": "Intermediate narrow capture revealed clipped caution text before the Grid layout repair.",
    "observed_on": "2026-09-07"
  },
  {
    "id": "E24",
    "kind": "render",
    "location": "C:/Users/cristian/Downloads/TokenUsage-report-2026-09-07-043632.png",
    "description": "Intermediate full four-cycle capture still has duplicate relative-change rows.",
    "observed_on": "2026-09-07"
  },
  {
    "id": "E25",
    "kind": "render",
    "location": "C:/Users/cristian/Downloads/TokenUsage-report-2026-09-07-045206.png",
    "description": "Final static detail block has its complete first and last lines below the title. Four-cycle values, short provider legends and combined changes are visible; no tooltip or action controls.",
    "observed_on": "2026-09-07"
  },
  {
    "id": "E26",
    "kind": "runtime",
    "location": "X:/tokenusage/.scratch/tokenusage-measurement/ux-final-runtime.json",
    "description": "Native UI Automation readback after full capture: capture enabled, cycle-count control enabled, detail expander restored collapsed.",
    "observed_on": "2026-09-07"
  },
  {
    "id": "E27",
    "kind": "render",
    "location": "C:/Users/cristian/Downloads/TokenUsage-report-2026-09-07-045135.png",
    "description": "Reproduced export during a pending cycle reload: data missing and progress line included. This is failed evidence, not a complete report.",
    "observed_on": "2026-09-07"
  },
  {
    "id": "E28",
    "kind": "runtime",
    "location": "X:/tokenusage/.scratch/tokenusage-measurement/ux-capture-gate-package.log",
    "description": "Final x64 Release package rebuild passed after static capture content and capture availability guard. Earlier 1,313 test results are reused for unchanged lower layers.",
    "observed_on": "2026-09-07"
  },
  {
    "id": "E29",
    "kind": "runtime",
    "location": "X:/tokenusage/.scratch/tokenusage-measurement/ux-capture-availability.json",
    "description": "Native capture IsEnabled False during a four-second synthetic refresh, then True after report completion.",
    "observed_on": "2026-09-07"
  }
]
```

## coverage

```json
[
  {
    "id": "C1",
    "dimension": "current-view inventory",
    "claim_kind": "implementation",
    "scope": "Report, compact, expanded dashboard, General, Notifications, Appearance, personalization, Providers, credentials, tray and shell.",
    "status": "passed",
    "reason": "Current XAML routes were inventoried. Report, options, expanded/compact dashboard and empty credential editor were rendered in native fixtures. Shell and tray activation are source inventory only; unmounted legacy controls are not claimed as current routes.",
    "evidence_ids": [
      "E9",
      "E10"
    ]
  },
  {
    "id": "C2",
    "dimension": "visual craft",
    "claim_kind": "visual",
    "scope": "Changed native surfaces after edits.",
    "status": "passed",
    "reason": "Changed options, previews, provider status, compact activity and report layouts were inspected. Final capture details receive separate evidence.",
    "evidence_ids": [
      "E11",
      "E12",
      "E13",
      "E14",
      "E15",
      "E16",
      "E17",
      "E19",
      "E20"
    ]
  },
  {
    "id": "C3",
    "dimension": "export",
    "claim_kind": "visual",
    "scope": "Report tooltip exclusion.",
    "status": "passed",
    "reason": "Actual full PNG inspected without a chart tooltip.",
    "evidence_ids": [
      "E25"
    ]
  },
  {
    "id": "C4",
    "dimension": "assistive technology and OS settings",
    "claim_kind": "interaction",
    "scope": "Screen reader, high contrast, reduced motion, enlarged text.",
    "status": "unknown",
    "reason": "No executed session for these conditions. Static use of theme/focus/motion settings is not certification.",
    "evidence_ids": []
  },
  {
    "id": "C5",
    "dimension": "human task outcomes",
    "claim_kind": "human",
    "scope": "Reading speed, error rate and perceived polish.",
    "status": "unknown",
    "reason": "No participant study was requested or performed; improvements are design decisions, not measured human outcomes.",
    "evidence_ids": []
  },
  {
    "id": "C6",
    "dimension": "primary and difficult data",
    "claim_kind": "visual",
    "scope": "Two/four cycles, model comparison, long labels, partial pricing and narrow report.",
    "status": "passed",
    "reason": "Synthetic native states show explicit provider identity, a shared baseline, wrapped caveats and readable values.",
    "evidence_ids": [
      "E16",
      "E17",
      "E25",
      "E19"
    ]
  },
  {
    "id": "C7",
    "dimension": "empty and unavailable",
    "claim_kind": "visual",
    "scope": "Empty credential editor, missing providers and disabled notification children.",
    "status": "passed",
    "reason": "These states were rendered without real credentials or provider calls.",
    "evidence_ids": [
      "E7",
      "E11",
      "E14"
    ]
  },
  {
    "id": "C8",
    "dimension": "refresh failure",
    "claim_kind": "interaction",
    "scope": "Preserving an existing report after refresh failure.",
    "status": "unknown",
    "reason": "Earlier native fixture readback showed a failure message and retained values; this audit has no durable runtime transcript for that interaction. Unit coverage is not a substitute for that transcript.",
    "evidence_ids": []
  },
  {
    "id": "C9",
    "dimension": "pending and interruption",
    "claim_kind": "interaction",
    "scope": "Loading, rapid view changes, capture failure and close during capture.",
    "status": "unknown",
    "reason": "No complete executed state matrix or injected file-write failure. Finally restoration is source evidence only.",
    "evidence_ids": []
  },
  {
    "id": "C10",
    "dimension": "marker reversal and exit",
    "claim_kind": "interaction",
    "scope": "Rapid pointer reversal inside a chart followed by exit.",
    "status": "passed",
    "reason": "A recording and inspected intermediate/final frames show redirection and cleared hover. Frame pacing is not measured.",
    "evidence_ids": [
      "E22"
    ]
  },
  {
    "id": "C11",
    "dimension": "return and persistence",
    "claim_kind": "interaction",
    "scope": "Saved four-cycle revision, notification settings and post-export restoration.",
    "status": "unknown",
    "reason": "Earlier fixture interactions exercised saved revisions and settings; final capture state readback will be recorded separately. This does not prove live data migration.",
    "evidence_ids": []
  },
  {
    "id": "C12",
    "dimension": "permission and native shell",
    "claim_kind": "interaction",
    "scope": "Real credential save, OS tray, window activation and installed main app.",
    "status": "unknown",
    "reason": "No real credential writes, provider connections or production-app replacement were authorized or performed.",
    "evidence_ids": []
  },
  {
    "id": "C13",
    "dimension": "release gate",
    "claim_kind": "implementation",
    "scope": "Core, provider, CLI, architecture, Windows tests and x64 MSIX.",
    "status": "passed",
    "reason": "All 1,313 tests and repository Release gate passed. The final capture-only repair is followed by a package build and native export, not a duplicate full suite.",
    "evidence_ids": [
      "E21",
      "E28"
    ]
  },
  {
    "id": "C14",
    "dimension": "capture return",
    "claim_kind": "interaction",
    "scope": "Normal state after full PNG capture.",
    "status": "passed",
    "reason": "Native UI Automation readback shows capture re-enabled and the detail expander still collapsed.",
    "evidence_ids": [
      "E26"
    ]
  },
  {
    "id": "C15",
    "dimension": "pending capture availability",
    "claim_kind": "interaction",
    "scope": "Share during report refresh.",
    "status": "passed",
    "reason": "A four-second synthetic refresh shows IsEnabled False during loading and True after completion.",
    "evidence_ids": [
      "E29"
    ]
  }
]
```

## findings

```json
[
  {
    "id": "UX01",
    "title": "Measurement details displace the result",
    "task": "Compare reset cycles",
    "state": "Loaded, incomplete evidence",
    "observation": "The report puts a dense technical paragraph before the totals and chart.",
    "classification": "observed",
    "claim_kind": "visual",
    "impact": "Measurement details displace the result. This obscures a decision or weakens state interpretation.",
    "severity": "P1",
    "confidence": "high",
    "reach": "Users of the affected native surface; no population estimate.",
    "evidence_ids": [
      "E1"
    ],
    "principle_ids": [],
    "source_ids": [],
    "proposal": "Move the full details to a disclosure after the results; keep the short cost caveat and cycle warning visible. Expand details for exported reports and restore state afterwards.",
    "tradeoff": "Details need one extra action in the interactive report.",
    "acceptance_test": "Totals and chart are visible earlier; details remain accessible, selectable and present in capture.",
    "status": "verified",
    "resolution": "Inspected native post-change rendering in the cited scope; broader OS and production integration limits remain in coverage.",
    "verification_evidence_ids": [
      "E25"
    ]
  },
  {
    "id": "UX02",
    "title": "Remaining preference has a comparison label",
    "task": "Choose how quota is displayed",
    "state": "Appearance",
    "observation": "Show usage as displays A · Baseline for the Remaining enum.",
    "classification": "observed",
    "claim_kind": "visual",
    "impact": "Remaining preference has a comparison label. This obscures a decision or weakens state interpretation.",
    "severity": "P1",
    "confidence": "high",
    "reach": "Users of the affected native surface; no population estimate.",
    "evidence_ids": [
      "E3"
    ],
    "principle_ids": [],
    "source_ids": [],
    "proposal": "Restore the label to Remaining without changing its stored value.",
    "tradeoff": "None; repairs an unrelated resource replacement.",
    "acceptance_test": "Appearance selector shows Remaining and Used, not comparison roles.",
    "status": "verified",
    "resolution": "Inspected native post-change rendering in the cited scope; broader OS and production integration limits remain in coverage.",
    "verification_evidence_ids": [
      "E12"
    ]
  },
  {
    "id": "UX03",
    "title": "Persistent settings look like actions",
    "task": "Configure closure, notifications and tray",
    "state": "Enabled and disabled options",
    "observation": "The close preference uses an X and notification settings use icon-only buttons.",
    "classification": "observed",
    "claim_kind": "visual",
    "impact": "Persistent settings look like actions. This obscures a decision or weakens state interpretation.",
    "severity": "P2",
    "confidence": "high",
    "reach": "Users of the affected native surface; no population estimate.",
    "evidence_ids": [
      "E2"
    ],
    "principle_ids": [],
    "source_ids": [],
    "proposal": "Use native switches with explicit On/Off state and preserve all bindings, labels and automation identities.",
    "tradeoff": "Switches use more width; stack the provider-name setting across both columns.",
    "acceptance_test": "Keyboard changes state; alerts master gates child controls; states persist in the existing store.",
    "status": "verified",
    "resolution": "Inspected native post-change rendering in the cited scope; broader OS and production integration limits remain in coverage.",
    "verification_evidence_ids": [
      "E11",
      "E12",
      "E14"
    ]
  },
  {
    "id": "UX04",
    "title": "Chart thumbnail loses most of its drawing space",
    "task": "Choose report chart style",
    "state": "Collapsed and expanded selector",
    "observation": "The daily bars preview looks like small arcs because full-chart top and bottom padding consumes 18 of 24 DIP.",
    "classification": "observed",
    "claim_kind": "visual",
    "impact": "Chart thumbnail loses most of its drawing space. This obscures a decision or weakens state interpretation.",
    "severity": "P2",
    "confidence": "high",
    "reach": "Users of the affected native surface; no population estimate.",
    "evidence_ids": [
      "E3"
    ],
    "principle_ids": [],
    "source_ids": [],
    "proposal": "Use preview-specific padding and a proportionate corner radius, without changing report geometry.",
    "tradeoff": "Small thumbnails simplify detail.",
    "acceptance_test": "Bars, steps, line, smooth line, area and two-hour bars can be distinguished in the native selector.",
    "status": "verified",
    "resolution": "Inspected native post-change rendering in the cited scope; broader OS and production integration limits remain in coverage.",
    "verification_evidence_ids": [
      "E13"
    ]
  },
  {
    "id": "UX05",
    "title": "Two-cycle summary leaves half the row empty",
    "task": "Compare two to four cycles",
    "state": "Loaded",
    "observation": "Two summary cards use two of four fixed-width slots.",
    "classification": "observed",
    "claim_kind": "visual",
    "impact": "Two-cycle summary leaves half the row empty. This obscures a decision or weakens state interpretation.",
    "severity": "P2",
    "confidence": "high",
    "reach": "Users of the affected native surface; no population estimate.",
    "evidence_ids": [
      "E1"
    ],
    "principle_ids": [],
    "source_ids": [
      "S1"
    ],
    "proposal": "Fit the number of columns to the selected cycles and available width; spread cards across the row.",
    "tradeoff": "Four cards wrap on narrow layouts.",
    "acceptance_test": "Two cards fill the wide row; four cards wrap into two rows at 760 DIP without clipping.",
    "status": "verified",
    "resolution": "Inspected native post-change rendering in the cited scope; broader OS and production integration limits remain in coverage.",
    "verification_evidence_ids": [
      "E16",
      "E17"
    ]
  },
  {
    "id": "UX06",
    "title": "Unavailable price controls add clutter",
    "task": "Compare reset cycles",
    "state": "Cycles selected",
    "observation": "The fixed-price checkbox and date picker are disabled but remain above the cycle selectors.",
    "classification": "observed",
    "claim_kind": "visual",
    "impact": "Unavailable price controls add clutter. This obscures a decision or weakens state interpretation.",
    "severity": "P2",
    "confidence": "high",
    "reach": "Users of the affected native surface; no population estimate.",
    "evidence_ids": [
      "E1"
    ],
    "principle_ids": [],
    "source_ids": [],
    "proposal": "Show them only for supported pair comparisons; add visible A/B headers to model selectors.",
    "tradeoff": "Price features are not visible in cycle mode, where they cannot run.",
    "acceptance_test": "Cycle mode omits inactive controls; model and period modes retain them.",
    "status": "verified",
    "resolution": "Inspected native post-change rendering in the cited scope; broader OS and production integration limits remain in coverage.",
    "verification_evidence_ids": [
      "E17",
      "E19"
    ]
  },
  {
    "id": "UX07",
    "title": "Cycle legend repeats long metadata",
    "task": "Identify chart series",
    "state": "Long cycle names",
    "observation": "Full labels compete with values in narrow legend slots and get truncated.",
    "classification": "observed",
    "claim_kind": "visual",
    "impact": "Cycle legend repeats long metadata. This obscures a decision or weakens state interpretation.",
    "severity": "P2",
    "confidence": "high",
    "reach": "Users of the affected native surface; no population estimate.",
    "evidence_ids": [
      "E8"
    ],
    "principle_ids": [],
    "source_ids": [],
    "proposal": "Use an explicit short legend label with A/B/C/D, provider and start date. Keep full labels in tooltips and summary cards.",
    "tradeoff": "The legend alone omits the pool and boundary classification.",
    "acceptance_test": "Series remain distinguishable without relying only on color; full metadata is still available.",
    "status": "verified",
    "resolution": "Inspected native post-change rendering in the cited scope; broader OS and production integration limits remain in coverage.",
    "verification_evidence_ids": [
      "E25"
    ]
  },
  {
    "id": "UX08",
    "title": "Missing optional providers look like errors",
    "task": "Inspect provider availability",
    "state": "Not installed",
    "observation": "Missing and blocked providers use the same red brush.",
    "classification": "observed",
    "claim_kind": "visual",
    "impact": "Missing optional providers look like errors. This obscures a decision or weakens state interpretation.",
    "severity": "P2",
    "confidence": "high",
    "reach": "Users of the affected native surface; no population estimate.",
    "evidence_ids": [
      "E4"
    ],
    "principle_ids": [],
    "source_ids": [],
    "proposal": "Use a neutral color for Missing and retain red for Blocked.",
    "tradeoff": "Some missing providers may require user action; their text and details remain.",
    "acceptance_test": "Missing is visibly neutral and status text stays visible. The unchanged Blocked branch remains red by source inspection; its live state is not exercised.",
    "status": "verified",
    "resolution": "Inspected native post-change rendering in the cited scope; broader OS and production integration limits remain in coverage.",
    "verification_evidence_ids": [
      "E14"
    ]
  },
  {
    "id": "UX09",
    "title": "Compact activity values break into an uneven row",
    "task": "Scan recent token activity",
    "state": "Compact global view",
    "observation": "At the usual narrow width the third activity item sits alone on a second row.",
    "classification": "observed",
    "claim_kind": "visual",
    "impact": "Compact activity values break into an uneven row. This obscures a decision or weakens state interpretation.",
    "severity": "P2",
    "confidence": "high",
    "reach": "Users of the affected native surface; no population estimate.",
    "evidence_ids": [
      "E5"
    ],
    "principle_ids": [],
    "source_ids": [],
    "proposal": "Use a smaller minimum cell width, equal stretch and consistent gaps.",
    "tradeoff": "Large text can still require wrapping; verify the supported layout without fixing the height.",
    "acceptance_test": "The three values align in one row at the tested compact width without clipping.",
    "status": "verified",
    "resolution": "Inspected native post-change rendering in the cited scope; broader OS and production integration limits remain in coverage.",
    "verification_evidence_ids": [
      "E15"
    ]
  },
  {
    "id": "UX10",
    "title": "Export can include keyboard-induced hover",
    "task": "Share a report",
    "state": "Capture after chart focus",
    "observation": "A previous export included the hover card; disabling pointer input did not prevent focus from recreating it.",
    "classification": "observed",
    "claim_kind": "visual",
    "impact": "Export can include keyboard-induced hover. This obscures a decision or weakens state interpretation.",
    "severity": "P1",
    "confidence": "high",
    "reach": "Users of the affected native surface; no population estimate.",
    "evidence_ids": [
      "E8"
    ],
    "principle_ids": [],
    "source_ids": [],
    "proposal": "Suppress hover creation for the complete report capture and restore the previous capture/input state in finally.",
    "tradeoff": "No hover interaction during export.",
    "acceptance_test": "Export excludes the hover card. Post-export interaction restoration is tracked separately in coverage.",
    "status": "verified",
    "resolution": "Native PNG inspected without the hover card. Capture-mode restoration was read back separately; no injected file-write failure was tested.",
    "verification_evidence_ids": [
      "E25"
    ]
  },
  {
    "id": "UX11",
    "title": "Cycle caveat clips on a narrow window",
    "task": "Read comparison limits",
    "state": "Four cycles at 760 DIP",
    "observation": "A horizontal StackPanel measures the wrapping caution text without a width limit.",
    "classification": "observed",
    "claim_kind": "visual",
    "impact": "Users can miss the warning about incomplete evidence.",
    "severity": "P2",
    "confidence": "high",
    "reach": "Users of the affected report state; no population estimate.",
    "evidence_ids": [
      "E23"
    ],
    "principle_ids": [],
    "source_ids": [],
    "proposal": "Use an Auto/* Grid so the caution text wraps inside the available width.",
    "tradeoff": "The warning grows by one line.",
    "acceptance_test": "The complete caution text is visible without horizontal clipping.",
    "status": "verified",
    "resolution": "Native final rendering inspected.",
    "verification_evidence_ids": [
      "E17"
    ]
  },
  {
    "id": "UX12",
    "title": "Separate percentage rows repeat the same totals",
    "task": "Compare token and cost changes",
    "state": "Loaded multi-cycle table",
    "observation": "Relative-change rows repeat totals already displayed by the main token and cost rows.",
    "classification": "observed",
    "claim_kind": "visual",
    "impact": "Extra rows make scanning harder without adding another metric.",
    "severity": "P2",
    "confidence": "high",
    "reach": "Users of the affected report state; no population estimate.",
    "evidence_ids": [
      "E24"
    ],
    "principle_ids": [],
    "source_ids": [],
    "proposal": "Show the absolute change and relative percentage together below each main value.",
    "tradeoff": "Narrow cells may wrap the change onto two lines.",
    "acceptance_test": "Token and cost rows show both changes, with no duplicate totals row and no invented percentage for a zero baseline.",
    "status": "verified",
    "resolution": "Native final rendering inspected.",
    "verification_evidence_ids": [
      "E25"
    ]
  },
  {
    "id": "UX13",
    "title": "Capture catches the detail expander mid-animation",
    "task": "Export all measurement evidence",
    "state": "Full PNG export from collapsed details",
    "observation": "The expander header overlaps the first lines of technical evidence in the export.",
    "classification": "observed",
    "claim_kind": "visual",
    "impact": "The shared report omits part of the measurement explanation.",
    "severity": "P2",
    "confidence": "high",
    "reach": "Users of the affected report state; no population estimate.",
    "evidence_ids": [
      "E18"
    ],
    "principle_ids": [],
    "source_ids": [],
    "proposal": "Use static capture-only detail content; leave the interactive expander state unchanged.",
    "tradeoff": "The capture has a static detail block rather than a disclosure control.",
    "acceptance_test": "The full evidence starts below its title in the PNG; the normal expander state remains unchanged.",
    "status": "verified",
    "resolution": "Native full PNG inspected: the complete detail text is below the static title. UI Automation readback confirms collapsed expander and enabled capture after export.",
    "verification_evidence_ids": [
      "E25",
      "E26"
    ]
  },
  {
    "id": "UX14",
    "title": "Share stays available while the report reloads",
    "task": "Export all measurement evidence",
    "state": "Immediately after changing cycle count",
    "observation": "A share action during reload produced a PNG with no report data.",
    "classification": "observed",
    "claim_kind": "interaction",
    "impact": "A partial loading state can be mistaken for a complete report.",
    "severity": "P2",
    "confidence": "high",
    "reach": "Users of the affected report state; no population estimate.",
    "evidence_ids": [
      "E27",
      "E29"
    ],
    "principle_ids": [],
    "source_ids": [],
    "proposal": "Disable capture while loading or without data; also guard the capture handler and restore availability from the current view model.",
    "tradeoff": "Users wait until the selected report is ready before sharing.",
    "acceptance_test": "Capture is disabled during a delayed refresh and enabled after a populated report completes.",
    "status": "verified",
    "resolution": "Native UI Automation confirms disabled capture during delayed refresh and enabled capture after completion.",
    "verification_evidence_ids": [
      "E29"
    ]
  }
]
```

## metrics

```json
[]
```

## decisions

```json
[
  {
    "id": "D1",
    "question": "What visual direction should the audit preserve?",
    "choice": "A restrained native utility: clear numeric hierarchy, provider colors, compact spacing, explicit controls and complete evidence on demand.",
    "rationale": "The current dashboard grouping works. The observed problems are information density, misleading labels and uneven allocation of space, not absence of decoration.",
    "alternatives": [
      "Replace the visual system with new cards, gradients and custom controls."
    ],
    "risk": "Over-compressing can hide measurement caveats; preserve a short visible caveat and full capture details.",
    "owner": "Current implementation task",
    "review_trigger": "Any missing caveat, clipped control or failed native interaction."
  }
]
```

## additional_sources

```json
[
  {
    "id": "S1",
    "title": "Microsoft UniformGridLayout",
    "url": "https://learn.microsoft.com/en-us/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.controls.uniformgridlayout?view=windows-app-sdk-2.0",
    "kind": "primary documentation",
    "reviewed_on": "2026-09-07",
    "scope_note": "Native ItemsStretch and MaximumRowsOrColumns behavior used for cycle summary layout; not usability outcome evidence."
  }
]
```
