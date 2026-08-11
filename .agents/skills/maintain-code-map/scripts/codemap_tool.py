#!/usr/bin/env python3
"""Create fingerprints, render HTML, and validate synchronized code-map artifacts."""

from __future__ import annotations

import argparse
import hashlib
import html
import json
import os
from pathlib import Path, PurePosixPath
import re
import subprocess
import sys
from typing import Any


ALGORITHM = "sha256-path-content-v1"
ARTIFACTS = ("codemap.html", "codemap.json", "codemap.lock")
EDGE_TYPES = {"imports", "calls", "reads", "writes", "publishes", "subscribes"}
NODE_TYPES = {"module", "service", "database", "queue", "interface", "external"}
DEFAULT_EXCLUDES = (
    ".cache",
    ".git",
    ".hg",
    ".next",
    ".nuxt",
    ".svn",
    ".turbo",
    ".venv",
    "__pycache__",
    "build",
    "coverage",
    "dist",
    "docs/codemap",
    "generated",
    "node_modules",
    "out",
    "target",
    "vendor",
    "venv",
)


class CodemapError(RuntimeError):
    pass


def run_git(repo: Path, *args: str) -> str:
    result = subprocess.run(
        ["git", "-C", str(repo), *args],
        stdout=subprocess.PIPE,
        stderr=subprocess.PIPE,
        check=False,
    )
    if result.returncode:
        message = result.stderr.decode("utf-8", errors="replace").strip()
        raise CodemapError(message or f"git {' '.join(args)} failed")
    return result.stdout.decode("utf-8", errors="surrogateescape")


def repo_root(value: str) -> Path:
    candidate = Path(value).resolve()
    root = run_git(candidate, "rev-parse", "--show-toplevel").strip()
    return Path(root).resolve()


def normalize_rel(repo: Path, value: str) -> str:
    cleaned = value.strip().replace("\\", "/")
    if not cleaned or cleaned == ".":
        return "."
    pure = PurePosixPath(cleaned)
    if pure.is_absolute() or ".." in pure.parts:
        raise CodemapError(f"path must be repository-relative: {value}")
    resolved = (repo / Path(*pure.parts)).resolve()
    try:
        relative = resolved.relative_to(repo)
    except ValueError as error:
        raise CodemapError(f"path escapes repository: {value}") from error
    return relative.as_posix() or "."


def resolve_inside(repo: Path, value: str) -> Path:
    candidate = Path(value)
    resolved = candidate.resolve() if candidate.is_absolute() else (repo / candidate).resolve()
    try:
        resolved.relative_to(repo)
    except ValueError as error:
        raise CodemapError(f"path escapes repository: {value}") from error
    return resolved


def resolve_output(repo: Path, value: str) -> Path:
    resolved = resolve_inside(repo, value)
    codemap_root = (repo / "docs" / "codemap").resolve()
    try:
        resolved.relative_to(codemap_root)
    except ValueError as error:
        raise CodemapError(f"output must stay under docs/codemap: {value}") from error
    return resolved


def unique_paths(repo: Path, values: list[str] | None, defaults: tuple[str, ...] = ()) -> list[str]:
    normalized = {normalize_rel(repo, value) for value in (*defaults, *(values or []))}
    return sorted(normalized)


def tracked_files(repo: Path) -> list[str]:
    output = run_git(repo, "-c", "core.quotepath=false", "ls-files", "-z")
    return sorted(path for path in output.split("\0") if path)


def is_under(path: str, root: str) -> bool:
    return root == "." or path == root or path.startswith(f"{root}/")


def is_excluded(path: str, exclusions: list[str]) -> bool:
    parts = PurePosixPath(path).parts
    for exclusion in exclusions:
        exclusion_parts = PurePosixPath(exclusion).parts
        if len(exclusion_parts) == 1 and exclusion in parts:
            return True
        if is_under(path, exclusion):
            return True
    return False


def selected_scope(path: str, scopes: list[str]) -> str | None:
    matches = [scope for scope in scopes if is_under(path, scope)]
    if not matches:
        return None
    return max(matches, key=lambda item: len(PurePosixPath(item).parts))


def module_id(path: str, scope: str) -> str:
    path_parts = PurePosixPath(path).parts
    scope_parts = () if scope == "." else PurePosixPath(scope).parts
    remainder = path_parts[len(scope_parts) :]
    if len(remainder) <= 1:
        return scope
    first_child = remainder[0]
    return first_child if scope == "." else f"{scope}/{first_child}"


def module_fingerprint(repo: Path, files: list[str]) -> str:
    digest = hashlib.sha256()
    digest.update(f"{ALGORITHM}\0".encode())
    for relative in sorted(files):
        digest.update(relative.encode("utf-8", errors="surrogateescape"))
        digest.update(b"\0")
        path = repo / Path(*PurePosixPath(relative).parts)
        if path.is_file():
            content = path.read_bytes()
            digest.update(str(len(content)).encode())
            digest.update(b"\0")
            digest.update(content)
        else:
            digest.update(b"MISSING")
        digest.update(b"\0")
    return digest.hexdigest()


def snapshot_modules(repo: Path, scopes: list[str], exclusions: list[str]) -> list[dict[str, Any]]:
    grouped: dict[str, list[str]] = {}
    for relative in tracked_files(repo):
        if is_excluded(relative, exclusions):
            continue
        scope = selected_scope(relative, scopes)
        if scope is None:
            continue
        grouped.setdefault(module_id(relative, scope), []).append(relative)
    return [
        {
            "id": identifier,
            "path": identifier,
            "file_count": len(files),
            "fingerprint": module_fingerprint(repo, files),
        }
        for identifier, files in sorted(grouped.items())
    ]


def current_commit(repo: Path) -> str:
    return run_git(repo, "rev-parse", "HEAD").strip()


def working_tree_dirty(repo: Path) -> bool:
    return bool(run_git(repo, "status", "--porcelain=v1", "--untracked-files=all").strip())


def load_json(path: Path) -> Any:
    try:
        return json.loads(path.read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError) as error:
        raise CodemapError(f"cannot parse {path}: {error}") from error


def write_text_atomic(path: Path, content: str) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    temporary = path.with_name(f".{path.name}.tmp")
    temporary.write_text(content, encoding="utf-8", newline="\n")
    os.replace(temporary, path)


def write_json_atomic(path: Path, value: Any) -> None:
    write_text_atomic(path, f"{json.dumps(value, indent=2, ensure_ascii=False)}\n")


def require(condition: bool, message: str, errors: list[str]) -> None:
    if not condition:
        errors.append(message)


def verify_source_path(repo: Path, value: Any, label: str, errors: list[str], *, file_only: bool = False) -> Path | None:
    if not isinstance(value, str) or not value:
        errors.append(f"{label} must be a non-empty path")
        return None
    try:
        resolved = resolve_inside(repo, value)
    except CodemapError as error:
        errors.append(f"{label}: {error}")
        return None
    exists = resolved.is_file() if file_only else resolved.exists()
    require(exists, f"{label} does not exist: {value}", errors)
    return resolved if exists else None


def verify_evidence(
    repo: Path,
    evidence: Any,
    label: str,
    errors: list[str],
    *,
    allow_unknown: bool,
) -> str | None:
    if not isinstance(evidence, dict):
        errors.append(f"{label} evidence must be an object")
        return None
    status = evidence.get("status")
    locations = evidence.get("locations")
    require(status in {"verified", "unknown"}, f"{label} has invalid evidence status", errors)
    require(isinstance(locations, list), f"{label} evidence locations must be a list", errors)
    if not isinstance(locations, list):
        return status if isinstance(status, str) else None
    if status == "unknown":
        require(allow_unknown, f"{label} cannot use unknown evidence", errors)
        require(not locations, f"{label} unknown evidence must not contain locations", errors)
        return status
    require(bool(locations), f"{label} verified evidence needs a location", errors)
    for index, location in enumerate(locations):
        location_label = f"{label} evidence[{index}]"
        if not isinstance(location, dict):
            errors.append(f"{location_label} must be an object")
            continue
        source = verify_source_path(repo, location.get("path"), location_label, errors, file_only=True)
        symbol = location.get("symbol")
        require(isinstance(symbol, str) and bool(symbol), f"{location_label} needs a literal symbol", errors)
        if source and isinstance(symbol, str) and symbol:
            content = source.read_text(encoding="utf-8", errors="replace")
            require(symbol in content, f"{location_label} symbol not found: {symbol}", errors)
    return status if isinstance(status, str) else None


def embedded_html_data(content: str) -> Any:
    match = re.search(
        r'<script\s+id=["\']codemap-data["\']\s+type=["\']application/json["\']>(.*?)</script>',
        content,
        flags=re.DOTALL,
    )
    if not match:
        raise CodemapError("codemap.html does not contain the codemap-data payload")
    try:
        return json.loads(match.group(1))
    except json.JSONDecodeError as error:
        raise CodemapError(f"codemap.html contains invalid embedded JSON: {error}") from error


def validate_directory(repo: Path, directory: Path) -> dict[str, Any]:
    errors: list[str] = []
    paths = {name: directory / name for name in ARTIFACTS}
    for name, path in paths.items():
        require(path.is_file(), f"missing {name}", errors)
    if errors:
        raise CodemapError("; ".join(errors))

    data = load_json(paths["codemap.json"])
    lock = load_json(paths["codemap.lock"])
    require(isinstance(data, dict), "codemap.json must contain an object", errors)
    require(isinstance(lock, dict), "codemap.lock must contain an object", errors)
    if not isinstance(data, dict) or not isinstance(lock, dict):
        raise CodemapError("; ".join(errors))

    required_top = {"generated_at", "generated_from_commit", "scope", "nodes", "edges", "flows"}
    require(required_top.issubset(data), "codemap.json is missing required top-level fields", errors)
    nodes = data.get("nodes")
    edges = data.get("edges")
    flows = data.get("flows")
    require(isinstance(nodes, list), "nodes must be a list", errors)
    require(isinstance(edges, list), "edges must be a list", errors)
    require(isinstance(flows, list), "flows must be a list", errors)
    if not all(isinstance(value, list) for value in (nodes, edges, flows)):
        raise CodemapError("; ".join(errors))

    require(0 < len(nodes) <= 20, "node count must be between 1 and 20", errors)
    require(3 <= len(flows) <= 5, "flow count must be between 3 and 5", errors)
    node_ids: set[str] = set()
    for index, node in enumerate(nodes):
        label = f"node[{index}]"
        if not isinstance(node, dict):
            errors.append(f"{label} must be an object")
            continue
        required = {"id", "path", "role", "type", "boundary", "entrypoints", "tests", "constraints", "evidence"}
        require(required.issubset(node), f"{label} is missing required fields", errors)
        identifier = node.get("id")
        require(isinstance(identifier, str) and bool(identifier), f"{label} id must be a non-empty string", errors)
        if isinstance(identifier, str):
            require(identifier not in node_ids, f"duplicate node id: {identifier}", errors)
            node_ids.add(identifier)
        verify_source_path(repo, node.get("path"), f"{label} path", errors)
        require(node.get("type") in NODE_TYPES, f"{label} has invalid type", errors)
        require(isinstance(node.get("role"), str) and bool(node.get("role")), f"{label} role is required", errors)
        require(isinstance(node.get("boundary"), str) and bool(node.get("boundary")), f"{label} boundary is required", errors)
        for field in ("entrypoints", "tests", "constraints"):
            require(isinstance(node.get(field), list), f"{label} {field} must be a list", errors)
        if isinstance(node.get("tests"), list):
            for test_index, test_path in enumerate(node["tests"]):
                verify_source_path(repo, test_path, f"{label} tests[{test_index}]", errors, file_only=True)
        verify_evidence(repo, node.get("evidence"), label, errors, allow_unknown=False)

    directed_edges: set[tuple[str, str]] = set()
    unknown_edges: list[str] = []
    for index, edge in enumerate(edges):
        label = f"edge[{index}]"
        if not isinstance(edge, dict):
            errors.append(f"{label} must be an object")
            continue
        required = {"from", "to", "type", "evidence"}
        require(required.issubset(edge), f"{label} is missing required fields", errors)
        source_id = edge.get("from")
        target_id = edge.get("to")
        require(source_id in node_ids, f"{label} references missing source node: {source_id}", errors)
        require(target_id in node_ids, f"{label} references missing target node: {target_id}", errors)
        require(edge.get("type") in EDGE_TYPES, f"{label} has invalid type: {edge.get('type')}", errors)
        if isinstance(source_id, str) and isinstance(target_id, str):
            directed_edges.add((source_id, target_id))
        status = verify_evidence(repo, edge.get("evidence"), label, errors, allow_unknown=True)
        if status == "unknown":
            unknown_edges.append(f"{source_id} -> {target_id}")

    for index, flow in enumerate(flows):
        label = f"flow[{index}]"
        if not isinstance(flow, dict):
            errors.append(f"{label} must be an object")
            continue
        require({"trigger", "steps", "outcome"}.issubset(flow), f"{label} is missing required fields", errors)
        require(isinstance(flow.get("trigger"), str) and bool(flow.get("trigger")), f"{label} trigger is required", errors)
        require(isinstance(flow.get("outcome"), str) and bool(flow.get("outcome")), f"{label} outcome is required", errors)
        steps = flow.get("steps")
        require(isinstance(steps, list) and len(steps) >= 2, f"{label} needs at least two steps", errors)
        if isinstance(steps, list):
            for step in steps:
                require(step in node_ids, f"{label} references missing node: {step}", errors)
            for source_id, target_id in zip(steps, steps[1:]):
                require((source_id, target_id) in directed_edges, f"{label} has no edge for {source_id} -> {target_id}", errors)

    html_content = paths["codemap.html"].read_text(encoding="utf-8")
    try:
        html_data = embedded_html_data(html_content)
    except CodemapError as error:
        errors.append(str(error))
        html_data = {}
    for key in ("nodes", "edges", "flows"):
        require(html_data.get(key) == data.get(key), f"HTML and JSON differ for {key}", errors)
    for marker in ("repo-name", "generated-at", "generated-commit", "flow-list", "type-filters", "map-svg"):
        require(f'id="{marker}"' in html_content, f"HTML is missing #{marker}", errors)

    required_lock = {
        "current_commit",
        "working_tree_dirty",
        "generated_at",
        "scanned_scope",
        "excluded_directories",
        "fingerprint_algorithm",
        "modules",
    }
    require(required_lock.issubset(lock), "codemap.lock is missing required fields", errors)
    scopes = lock.get("scanned_scope")
    exclusions = lock.get("excluded_directories")
    require(isinstance(scopes, list) and bool(scopes), "lock scanned_scope must be a non-empty list", errors)
    require(isinstance(exclusions, list), "lock excluded_directories must be a list", errors)
    require(lock.get("fingerprint_algorithm") == ALGORITHM, "lock fingerprint algorithm does not match", errors)
    commit = current_commit(repo)
    require(lock.get("current_commit") == commit, "lock commit does not match HEAD", errors)
    require(data.get("generated_from_commit") == commit, "JSON commit does not match HEAD", errors)
    require(lock.get("generated_at") == data.get("generated_at"), "JSON and lock generation times differ", errors)
    require(lock.get("scanned_scope") == data.get("scope"), "JSON and lock scopes differ", errors)
    require(lock.get("working_tree_dirty") == working_tree_dirty(repo), "lock dirty state does not match the working tree", errors)
    if isinstance(scopes, list) and isinstance(exclusions, list):
        expected_modules = snapshot_modules(repo, scopes, exclusions)
        require(lock.get("modules") == expected_modules, "lock module fingerprints do not match", errors)

    if errors:
        raise CodemapError("\n".join(f"- {error}" for error in errors))
    return {
        "ok": True,
        "nodes": len(nodes),
        "edges": len(edges),
        "flows": len(flows),
        "unknown_edges": unknown_edges,
        "commit": commit,
        "working_tree_dirty": lock["working_tree_dirty"],
    }


def command_status(args: argparse.Namespace) -> dict[str, Any]:
    repo = repo_root(args.repo)
    lock_path = resolve_inside(repo, args.lock)
    if not lock_path.is_file():
        scopes = unique_paths(repo, args.scope or ["."])
        exclusions = unique_paths(repo, args.exclude, DEFAULT_EXCLUDES)
        modules = snapshot_modules(repo, scopes, exclusions)
        identifiers = [module["id"] for module in modules]
        return {
            "lock_found": False,
            "stale": True,
            "changed_modules": [],
            "new_modules": identifiers,
            "removed_modules": [],
            "stale_modules": identifiers,
            "commit_changed": True,
            "dirty_state_changed": True,
        }
    lock = load_json(lock_path)
    if not isinstance(lock, dict):
        raise CodemapError("codemap.lock must contain an object")
    scopes = lock.get("scanned_scope")
    exclusions = lock.get("excluded_directories")
    if not isinstance(scopes, list) or not scopes or not isinstance(exclusions, list):
        raise CodemapError("codemap.lock has invalid scope or exclusions")
    current = snapshot_modules(repo, scopes, exclusions)
    previous = lock.get("modules")
    if not isinstance(previous, list):
        raise CodemapError("codemap.lock modules must be a list")
    current_by_id = {module["id"]: module for module in current}
    previous_by_id = {module.get("id"): module for module in previous if isinstance(module, dict)}
    shared = current_by_id.keys() & previous_by_id.keys()
    changed = sorted(
        identifier
        for identifier in shared
        if current_by_id[identifier].get("fingerprint") != previous_by_id[identifier].get("fingerprint")
    )
    new = sorted(current_by_id.keys() - previous_by_id.keys())
    removed = sorted(previous_by_id.keys() - current_by_id.keys())
    commit_changed = lock.get("current_commit") != current_commit(repo)
    dirty_changed = lock.get("working_tree_dirty") != working_tree_dirty(repo)
    stale_modules = sorted({*changed, *new, *removed})
    return {
        "lock_found": True,
        "stale": bool(stale_modules or commit_changed),
        "changed_modules": changed,
        "new_modules": new,
        "removed_modules": removed,
        "stale_modules": stale_modules,
        "commit_changed": commit_changed,
        "dirty_state_changed": dirty_changed,
    }


def command_lock(args: argparse.Namespace) -> dict[str, Any]:
    repo = repo_root(args.repo)
    scopes = unique_paths(repo, args.scope or ["."])
    exclusions = unique_paths(repo, args.exclude, DEFAULT_EXCLUDES)
    value = {
        "current_commit": current_commit(repo),
        "working_tree_dirty": working_tree_dirty(repo),
        "generated_at": args.generated_at,
        "scanned_scope": scopes,
        "excluded_directories": exclusions,
        "fingerprint_algorithm": ALGORITHM,
        "modules": snapshot_modules(repo, scopes, exclusions),
    }
    output = resolve_output(repo, args.output)
    write_json_atomic(output, value)
    return {"ok": True, "output": output.relative_to(repo).as_posix(), "modules": len(value["modules"])}


def command_render(args: argparse.Namespace) -> dict[str, Any]:
    repo = repo_root(args.repo)
    json_path = resolve_inside(repo, args.json)
    output = resolve_output(repo, args.output)
    template = Path(args.template).resolve() if args.template else Path(__file__).resolve().parents[1] / "assets" / "codemap-template.html"
    if not template.is_file():
        raise CodemapError(f"template does not exist: {template}")
    data = load_json(json_path)
    compact = json.dumps(data, ensure_ascii=False, separators=(",", ":")).replace("</", "<\\/")
    content = template.read_text(encoding="utf-8")
    if "__CODEMAP_DATA__" not in content or "__REPO_NAME__" not in content:
        raise CodemapError("template placeholders are missing")
    content = content.replace("__CODEMAP_DATA__", compact).replace("__REPO_NAME__", html.escape(repo.name))
    write_text_atomic(output, content)
    return {"ok": True, "output": output.relative_to(repo).as_posix()}


def command_validate(args: argparse.Namespace) -> dict[str, Any]:
    repo = repo_root(args.repo)
    directory = resolve_inside(repo, args.dir)
    return validate_directory(repo, directory)


def command_publish(args: argparse.Namespace) -> dict[str, Any]:
    repo = repo_root(args.repo)
    staging = resolve_output(repo, args.staging)
    target = resolve_output(repo, args.target)
    if staging == target:
        raise CodemapError("staging and target must differ")
    result = validate_directory(repo, staging)
    extras = sorted(path.name for path in staging.iterdir() if path.name not in ARTIFACTS)
    if extras:
        raise CodemapError(f"staging contains unexpected files: {', '.join(extras)}")
    target.mkdir(parents=True, exist_ok=True)
    prepared: list[tuple[Path, Path]] = []
    for name in ARTIFACTS:
        destination = target / name
        temporary = target / f".{name}.publish"
        temporary.write_bytes((staging / name).read_bytes())
        prepared.append((temporary, destination))
    for temporary, destination in prepared:
        os.replace(temporary, destination)
    for name in ARTIFACTS:
        (staging / name).unlink()
    staging.rmdir()
    final_result = validate_directory(repo, target)
    final_result["published"] = [str((target / name).relative_to(repo).as_posix()) for name in ARTIFACTS]
    final_result["staging_validation"] = result["ok"]
    return final_result


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(description=__doc__)
    subparsers = parser.add_subparsers(dest="command", required=True)

    status = subparsers.add_parser("status", help="compare a lock with the current repository")
    status.add_argument("--repo", default=".")
    status.add_argument("--lock", default="docs/codemap/codemap.lock")
    status.add_argument("--scope", action="append")
    status.add_argument("--exclude", action="append")
    status.set_defaults(handler=command_status)

    lock = subparsers.add_parser("lock", help="write deterministic module fingerprints")
    lock.add_argument("--repo", default=".")
    lock.add_argument("--scope", action="append")
    lock.add_argument("--exclude", action="append")
    lock.add_argument("--generated-at", required=True)
    lock.add_argument("--output", required=True)
    lock.set_defaults(handler=command_lock)

    render = subparsers.add_parser("render", help="render the self-contained HTML artifact")
    render.add_argument("--repo", default=".")
    render.add_argument("--json", required=True)
    render.add_argument("--output", required=True)
    render.add_argument("--template")
    render.set_defaults(handler=command_render)

    validate = subparsers.add_parser("validate", help="validate an artifact directory")
    validate.add_argument("--repo", default=".")
    validate.add_argument("--dir", required=True)
    validate.set_defaults(handler=command_validate)

    publish = subparsers.add_parser("publish", help="validate and publish one staged artifact set")
    publish.add_argument("--repo", default=".")
    publish.add_argument("--staging", required=True)
    publish.add_argument("--target", default="docs/codemap")
    publish.set_defaults(handler=command_publish)
    return parser


def main() -> int:
    args = build_parser().parse_args()
    try:
        result = args.handler(args)
    except CodemapError as error:
        print(json.dumps({"ok": False, "error": str(error)}, ensure_ascii=False, indent=2), file=sys.stderr)
        return 1
    print(json.dumps(result, ensure_ascii=False, indent=2))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
