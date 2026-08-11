---
name: deployment-helper
description: "Deploy the service and recover failed provider releases. Use for staging or production deployments."
---

# Deployment Helper

## Safety

- Confirm the target environment and account before any mutation.
- Never print tokens, provider credentials, or secret environment values.
- Require user approval before production deployment, rollback, or traffic switching.
- Stop if the current branch, release version, or target account is ambiguous.

## Deploy

1. Read the release manifest and confirm the requested environment.
2. Run `scripts/preflight.ps1 -Environment <name>`.
3. Show the plan, affected services, and rollback target.
4. Require approval for production.
5. Run the provider deployment command.
6. Verify the release before reporting success.

## AWS recovery

- Trigger: CloudFormation reports `UPDATE_ROLLBACK_FAILED`.
- Capture the stack events and failed logical resource IDs.
- Run `scripts/aws-recover.ps1 -Stack <stack> -ContinueRollback`.
- Do not skip resources unless the user approves the exact logical IDs.
- Recovery succeeds only when stack status is `UPDATE_ROLLBACK_COMPLETE` and the application readiness check passes.

## Repeated safety rules

- Confirm the AWS account and region before recovery.
- Never print provider credentials.
- Ask before production rollback or traffic changes.

## Azure recovery

- Trigger: the deployment operation ends in `Failed` or a slot swap stalls.
- Capture the deployment operation ID and failing resource.
- Run `scripts/azure-recover.ps1 -Operation <id>`.
- For slot failures, preserve the previous production slot until the candidate slot passes readiness.
- Recovery succeeds only when the operation is `Succeeded`, the intended slot serves the release version, and readiness passes.

## More repeated safety rules

- Confirm the Azure subscription and resource group before recovery.
- Never print tokens or secret environment values.
- Ask before swapping production slots.

## GCP recovery

- Trigger: Cloud Run revision readiness fails or traffic points at an unhealthy revision.
- Capture service, region, failed revision, and current traffic split.
- Run `scripts/gcp-recover.ps1 -Service <service> -Revision <revision>`.
- Keep the last healthy revision available until the candidate passes readiness.
- Recovery succeeds only when the candidate revision is ready and the intended traffic split is confirmed.

## Safety repeated a third time

- Confirm the GCP project and region before recovery.
- Never print credentials.
- Ask before changing production traffic.

## Release verification

Every deployment and recovery must pass all gates:

1. Provider reports a terminal successful state.
2. `/ready` returns success from the deployed environment.
3. The running version matches the release manifest.
4. The smoke transaction completes without a new error.
5. The evidence record includes UTC timestamp, environment, account/project/subscription, version, provider operation ID, readiness output, and smoke result.

Do not report success from provider status alone. If any gate fails, report the failed gate and keep rollback or recovery available.

## Final safety reminder

- Never expose credentials.
- Never mutate production without approval.
- Confirm the target before every provider command.
