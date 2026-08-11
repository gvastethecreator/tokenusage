# Recovery readiness incident evidence

## Repeated failures

- Incident 2026-05-14: agent queried `/health`, received HTTP 200, and declared recovery. Database connection remained unavailable for 11 minutes.
- Incident 2026-05-29: agent queried `/health`; background workers could not reach the database. Recovery report lacked command output.
- Incident 2026-06-07: agent ran an ad-hoc curl against `/health` and omitted the environment timestamp. Review could not establish which deployment was tested.
- Incident 2026-06-22: agent toggled the service twice before using the existing recovery script. The second toggle extended the outage.
- Incident 2026-07-03: `/health` passed while migrations were incomplete. `/ready` correctly returned failure until database and migrations recovered.

## Established workflow

- Full readiness endpoint: `/ready`.
- Existing deterministic check: `scripts/check-recovery.ps1`.
- Required command form: `scripts/check-recovery.ps1 -Environment <name> -OutputJson <path>`.
- The script checks application readiness, database connectivity, migrations, and worker heartbeat.
- A recovery is complete only when the script exits 0 and its JSON field `ready` is `true`.

## Evidence contract

Every incident review requires:

- UTC timestamp;
- environment and release version;
- exact recovery-check command;
- complete JSON output path;
- final exit code;
- any failed readiness component;
- operator-approved next action when readiness is false.

The skill may inspect and report without approval. Service restarts, toggles, deploys, rollbacks, traffic changes, or external incident comments require explicit user authorization.
