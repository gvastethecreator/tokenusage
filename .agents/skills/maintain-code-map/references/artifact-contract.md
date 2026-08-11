# Artifact Contract

Create all three files under `docs/codemap/`. Generate them from one analysis and one repository state.

## `codemap.json`

Use this top-level shape:

```json
{
  "generated_at": "2026-08-04T15:04:05Z",
  "generated_from_commit": "<full commit>",
  "scope": ["src", "tests"],
  "nodes": [],
  "edges": [],
  "flows": []
}
```

Use repository-relative POSIX paths. Sort nodes and edges by stable IDs. Keep flow steps in runtime order.

### Nodes

Each node must contain these fields:

```json
{
  "id": "api",
  "path": "src/api",
  "role": "Accepts HTTP requests and coordinates use cases.",
  "type": "interface",
  "boundary": "Application",
  "entrypoints": ["src/api/server.ts:createServer"],
  "tests": ["tests/api/server.test.ts"],
  "constraints": ["Does not access storage directly."],
  "evidence": {
    "status": "verified",
    "locations": [
      {"path": "src/api/server.ts", "symbol": "createServer"}
    ]
  }
}
```

Use one of these node types: `module`, `service`, `database`, `queue`, `interface`, or `external`.

The node path can name a file or directory. Every test path must exist. A node must have verified evidence.

### Edges

Each edge must contain these fields:

```json
{
  "from": "api",
  "to": "orders",
  "type": "calls",
  "evidence": {
    "status": "verified",
    "locations": [
      {"path": "src/api/routes/orders.ts", "symbol": "createOrder"}
    ]
  }
}
```

Use only these edge types:

- `imports`
- `calls`
- `reads`
- `writes`
- `publishes`
- `subscribes`

Use this evidence object when the source does not prove the relationship:

```json
{
  "status": "unknown",
  "locations": []
}
```

Do not attach invented paths or symbols to an unknown edge.

### Flows

Each flow must contain a trigger, ordered node IDs, and an outcome:

```json
{
  "trigger": "POST /orders",
  "steps": ["api", "orders", "database", "events"],
  "outcome": "The order is stored and OrderCreated is published."
}
```

Each consecutive flow step must have a matching directed edge. Use three to five flows.

## `codemap.html`

Generate the HTML with `codemap_tool.py render`. The renderer embeds the exact JSON payload in the template.

The template provides:

- a dark theme and visible repository metadata;
- at most 20 primary nodes and labeled system boundaries;
- a layered layout with crossing-reduction passes;
- type colors and a legend;
- upstream, downstream, test, and flow highlights;
- flow selection, search, type filters, zoom, pan, and node drag;
- no network, package, font, image, script, or stylesheet dependency.

Do not replace the bundled interaction model with a static diagram.

## `codemap.lock`

Generate the lock with `codemap_tool.py lock`. The lock contains:

```json
{
  "current_commit": "<full commit>",
  "working_tree_dirty": true,
  "generated_at": "2026-08-04T15:04:05Z",
  "scanned_scope": ["src", "tests"],
  "excluded_directories": ["docs/codemap", "node_modules"],
  "fingerprint_algorithm": "sha256-path-content-v1",
  "modules": [
    {
      "id": "src/api",
      "path": "src/api",
      "file_count": 4,
      "fingerprint": "<sha256>"
    }
  ]
}
```

The fingerprint hashes sorted tracked paths and their current bytes. Missing tracked files use a deterministic marker.

The dirty flag records the generation state. A later dirty-state change is metadata drift. It does not make unchanged module fingerprints stale.

## Validation

The validator must pass these checks:

- `codemap.json` parses.
- The node count is 20 or less.
- Every node path and test path exists.
- Every verified evidence path exists and contains its literal symbol.
- Every edge endpoint and flow step references a node.
- Every consecutive flow step has a directed edge.
- Every edge type uses the allowed vocabulary.
- Every unproved edge uses `status: unknown` with no locations.
- The HTML embeds the same nodes, edges, and flows as the JSON.
- The lock commit, dirty state, scope, exclusions, and fingerprints match the generation state.

Then open the HTML in a browser. Static validation does not prove the interaction behavior.

## Final Report

Report these items in this order:

1. Files created or modified.
2. Stale modules before regeneration.
3. Remaining unknown relationships.
4. Static and browser validation results.
5. The complete tracked and untracked artifact diff.

Before publication, compare each current artifact with its staged replacement by using `git diff --no-index`.

Use `NUL` on Windows or `/dev/null` on POSIX when the current artifact does not exist. Preserve this output for the final report.

After publication, use `git diff -- docs/codemap` for tracked artifacts. Do not stage untracked artifacts only to make Git show them.
