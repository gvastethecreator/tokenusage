# Report animation plans

These plans correct the report motion without changing data, layout, or automation contracts.

| Plan | Title | Severity | Status | Dependencies |
| --- | --- | --- | --- | --- |
| 001 | Tighten the report motion tokens | MEDIUM | DONE | None |
| 002 | Stabilize the report tabs | HIGH | DONE | None |
| 003 | Make report transitions interruptible | HIGH | DONE | 001 |
| 004 | Animate only report leaf content | HIGH | DONE | 003 |
| 005 | Coalesce report refresh motion | HIGH | DONE | 003, 004 |

## Recommended execution order

1. Run plan 001 to establish durations and easing.
2. Run plan 002 to remove the tab indicator conflict.
3. Run plan 003 to make rapid interactions safe.
4. Run plan 004 to keep stable containers fixed.
5. Run plan 005 to remove duplicate refresh entries.

Run the focused architecture test after each plan. Run the full Release x64 gate after plan 005.

Do the packaged-app feel check after plans 002, 004, and 005. Source inspection and a successful build do not prove motion quality.
