# Issue tracker contract

GitHub Issues and the linked GitHub Project hold live work state. Local Markdown files hold synchronized context, decisions, evidence, and handoffs.

## Identity

- Repository: `gvastethecreator/tokenusage`
- Project owner: `gvastethecreator`
- Project number: `4`
- Project title: `TokenUsage`
- Project URL: `https://github.com/users/gvastethecreator/projects/4`
- Local root: `.scratch/tokenusage/`

## Checkout modes

- Public contributors use the linked GitHub Issue and Project item as the complete live state.
- Maintainers can mirror expanded context and evidence under the ignored `.scratch/tokenusage/` path.
- If the local root is absent, do not create it for an ordinary code change.
- Never commit local mirrors, readiness reports, or private working notes.

## Authority

- GitHub owns issue state, assignees, comments, relationships, labels, and Project fields.
- Local files own expanded context, decisions, verification evidence, and offline handoffs.
- Shared fields must match on both surfaces.
- Do not copy the full GitHub comment history into local files.

## Local layout

- Product specification: `.scratch/tokenusage/spec.md`
- Ticket index: `.scratch/tokenusage/tickets.md`
- Ticket mirrors: `.scratch/tokenusage/issues/<NN>-<slug>.md`
- Rejected requests: `.scratch/tokenusage/out-of-scope/<concept>.md`
- Execution plans: `.scratch/planning/`
- Decision maps: `.scratch/wayfinder/<effort-slug>/`
- Delegated handoffs: `.scratch/agent-cli-delegation/`

Each ticket mirror records these fields:

```markdown
# <NN>: <title>

GitHub issue: <url-or-pending>
GitHub project: https://github.com/users/gvastethecreator/projects/4
Sync: pending | synced | conflict
Last synced: <ISO-8601-or-never>
Remote updated: <ISO-8601-or-unknown>
Category: bug | enhancement
Status: needs-triage | needs-info | ready-for-agent | ready-for-human | wontfix
Project status: Todo | In Progress | Done
Execution: queued | active | blocked | finished
Type: AFK | HITL
Source: <spec path, issue URL, or conversation>
Blocked by: <GitHub issue numbers or None>
```

## Maintainer sync protocol

Use this protocol only when the maintainer checkout already owns a local mirror.

1. Read the Issue, Project item, and local mirror before a mutation.
2. If both surfaces changed after `Last synced`, set `Sync: conflict` and stop.
3. Write the local draft with `Sync: pending` before remote creation.
4. Create or update the GitHub Issue.
5. Add the Issue to Project 4 under `gvastethecreator`.
6. Set the Project `Status` field.
7. Update identifiers, shared fields, timestamps, and `Sync: synced` locally.
8. If a step fails, record the failed step under `## Sync log`.

Retry from the stored Issue URL. Never create a second Issue after a partial failure.

## GitHub commands

```powershell
gh issue view <number> -R gvastethecreator/tokenusage --json number,title,state,body,labels,assignees,comments,updatedAt,url
gh project view 4 --owner gvastethecreator --format json
gh project field-list 4 --owner gvastethecreator --format json
gh project item-list 4 --owner gvastethecreator --limit 200 --format json
gh project item-add 4 --owner gvastethecreator --url <issue-url>
```

Use the installed `gh project item-edit --help` contract before a field update. GitHub CLI field flags can change between versions.

## Triage and implementation

- Triage updates one category label, one triage label, and their local fields.
- Starting work sets Project status to `In Progress` and local `Execution:` to `active`.
- Verified completion closes the Issue and sets Project status to `Done`.
- A blocker keeps the Issue open and sets local `Execution:` to `blocked`.

## Wayfinding

- Use `wayfinder:map` for the parent decision map.
- Use native sub-issues for decision tickets.
- Mirror maps under `.scratch/wayfinder/<effort-slug>/`.
- Mirror native blocking relationships in `Blocked by:`.
