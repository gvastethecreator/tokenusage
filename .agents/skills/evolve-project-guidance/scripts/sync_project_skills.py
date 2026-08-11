#!/usr/bin/env python3
"""Safely materialize or verify portable skills under .agents/skills."""

from __future__ import annotations

import argparse
import hashlib
import json
import os
import re
import shutil
import stat
import subprocess
import sys
import tempfile
from pathlib import Path
from typing import Any, Iterable
from urllib.parse import urlsplit, urlunsplit


NAME_PATTERN = re.compile(r"^[a-z0-9]+(?:-[a-z0-9]+)*$")
SKIP_DIRECTORIES = {".git", ".hg", ".svn", ".venv", "__pycache__", "node_modules", "venv"}
SKIP_FILES = {".DS_Store", "Thumbs.db"}


def inside(path: Path, parent: Path) -> bool:
    try:
        path.resolve().relative_to(parent.resolve())
        return True
    except ValueError:
        return False


def is_link_like(path: Path) -> bool:
    try:
        metadata = path.lstat()
    except OSError:
        return False
    return stat.S_ISLNK(metadata.st_mode) or bool(getattr(metadata, "st_reparse_tag", 0))


def skill_files(root: Path) -> list[tuple[Path, Path]]:
    if is_link_like(root):
        raise ValueError(f"skill source must resolve to a physical directory: {root}")
    output: list[tuple[Path, Path]] = []
    for current, directories, names in os.walk(root, followlinks=False):
        current_path = Path(current)
        directories[:] = sorted(name for name in directories if name not in SKIP_DIRECTORIES)
        for directory in list(directories):
            candidate = current_path / directory
            if is_link_like(candidate):
                raise ValueError(f"skill contains a linked directory: {candidate}")
        for name in sorted(names):
            if name in SKIP_FILES:
                continue
            candidate = current_path / name
            if is_link_like(candidate):
                raise ValueError(f"skill contains a linked file: {candidate}")
            if candidate.is_file():
                output.append((candidate.relative_to(root), candidate))
    return output


def fingerprint(root: Path) -> tuple[str, int]:
    digest = hashlib.sha256()
    files = skill_files(root)
    for relative, source in files:
        digest.update(relative.as_posix().encode("utf-8"))
        digest.update(b"\0")
        with source.open("rb") as stream:
            for chunk in iter(lambda: stream.read(1024 * 1024), b""):
                digest.update(chunk)
        digest.update(b"\0")
    return digest.hexdigest(), len(files)


def copy_skill(source: Path, destination: Path) -> None:
    destination.mkdir(parents=True, exist_ok=False)
    for relative, file in skill_files(source):
        target = destination / relative
        target.parent.mkdir(parents=True, exist_ok=True)
        shutil.copy2(file, target)


def git_value(path: Path, *args: str) -> str | None:
    try:
        result = subprocess.run(
            ["git", "-C", str(path), *args],
            capture_output=True,
            check=False,
            encoding="utf-8",
            errors="replace",
            text=True,
            timeout=10,
        )
    except (OSError, subprocess.TimeoutExpired):
        return None
    return result.stdout.strip() if result.returncode == 0 and result.stdout.strip() else None


def sanitize_remote(value: str) -> str:
    if re.match(r"^[A-Za-z]:[\\/]", value) or value.startswith(("/", "\\\\")):
        local_name = re.split(r"[\\/]", value.rstrip("\\/"))[-1]
        return f"local:{local_name or 'repository'}"
    if "://" not in value:
        return value.split("?", 1)[0].split("#", 1)[0]
    parts = urlsplit(value)
    if parts.scheme.lower() == "file":
        return f"local:{Path(parts.path).name or 'repository'}"
    hostname = parts.hostname or ""
    if parts.port:
        hostname = f"{hostname}:{parts.port}"
    return urlunsplit((parts.scheme, hostname, parts.path, "", ""))


def provenance(source: Path) -> dict[str, Any]:
    git_root_value = git_value(source, "rev-parse", "--show-toplevel")
    if not git_root_value:
        return {
            "source": f"local:{source.parent.name}",
            "revision": None,
            "skill_path": source.name,
            "source_dirty": None,
        }
    git_root = Path(git_root_value).resolve()
    remote = git_value(git_root, "remote", "get-url", "origin")
    revision = git_value(git_root, "rev-parse", "HEAD")
    relative = source.relative_to(git_root)
    dirty = git_value(git_root, "status", "--porcelain", "--", relative.as_posix()) is not None
    return {
        "source": sanitize_remote(remote) if remote else f"local:{git_root.name}",
        "revision": revision,
        "skill_path": relative.as_posix(),
        "source_dirty": dirty,
    }


def load_lock(path: Path) -> dict[str, Any]:
    if not path.exists():
        return {"version": 1, "skills": []}
    try:
        payload = json.loads(path.read_text(encoding="utf-8-sig"))
    except (OSError, UnicodeError, json.JSONDecodeError) as exc:
        raise ValueError(f"cannot parse lock file {path}: {exc}") from exc
    if not isinstance(payload, dict) or payload.get("version") != 1 or not isinstance(payload.get("skills"), list):
        raise ValueError(f"unsupported lock file: {path}")
    return payload


def lock_records(payload: dict[str, Any]) -> dict[str, dict[str, Any]]:
    records: dict[str, dict[str, Any]] = {}
    for item in payload.get("skills", []):
        if not isinstance(item, dict) or not isinstance(item.get("name"), str):
            raise ValueError("lock file contains an invalid skill record")
        if not NAME_PATTERN.fullmatch(item["name"]):
            raise ValueError(f"lock file contains an invalid skill name: {item['name']}")
        if item["name"] in records:
            raise ValueError(f"lock file contains a duplicate skill: {item['name']}")
        records[item["name"]] = item
    return records


def write_lock(path: Path, records: Iterable[dict[str, Any]]) -> None:
    payload = {
        "version": 1,
        "skills": sorted(records, key=lambda item: item["name"]),
    }
    path.parent.mkdir(parents=True, exist_ok=True)
    temporary = path.with_name(f".{path.name}.{os.getpid()}.tmp")
    temporary.write_text(json.dumps(payload, indent=2, sort_keys=True) + "\n", encoding="utf-8")
    os.replace(temporary, path)


def verify_project(project: Path, lock_path: Path) -> tuple[dict[str, Any], bool]:
    errors: list[str] = []
    if not lock_path.is_file():
        return {"mode": "verify", "errors": [f"lock file is missing: {lock_path}"], "skills": []}, False
    try:
        records = lock_records(load_lock(lock_path))
    except ValueError as exc:
        return {"mode": "verify", "errors": [str(exc)], "skills": []}, False
    skills_root = project / ".agents" / "skills"
    results: list[dict[str, Any]] = []
    for name, record in sorted(records.items()):
        destination = skills_root / name
        item: dict[str, Any] = {"name": name, "ok": False}
        if not destination.exists():
            item["error"] = "destination is missing"
        elif is_link_like(destination):
            item["error"] = "destination is linked, not portable"
        elif not destination.is_dir() or not (destination / "SKILL.md").is_file():
            item["error"] = "destination is not a valid skill directory"
        else:
            try:
                current, count = fingerprint(destination)
            except (OSError, ValueError) as exc:
                item["error"] = str(exc)
            else:
                item["fingerprint"] = current
                item["file_count"] = count
                if record.get("mode") != "portable-copy":
                    item["error"] = "lock mode is not portable-copy"
                elif current != record.get("fingerprint"):
                    item["error"] = "destination fingerprint differs from the lock"
                else:
                    item["ok"] = True
        if not item["ok"]:
            errors.append(f"{name}: {item.get('error', 'verification failed')}")
        results.append(item)
    return {"mode": "verify", "lock": str(lock_path), "skills": results, "errors": errors}, not errors


def build_plan(project: Path, source_root: Path, names: list[str], lock_path: Path) -> tuple[list[dict[str, Any]], dict[str, dict[str, Any]]]:
    existing_records = lock_records(load_lock(lock_path))
    skills_root = project / ".agents" / "skills"
    plans: list[dict[str, Any]] = []
    for name in names:
        if not NAME_PATTERN.fullmatch(name):
            plans.append({"name": name, "action": "blocked", "reason": "invalid skill name"})
            continue
        lexical_source = source_root / name
        if not lexical_source.is_dir() or not (lexical_source / "SKILL.md").is_file():
            plans.append({"name": name, "action": "blocked", "reason": "source skill is missing"})
            continue
        source = lexical_source.resolve()
        destination = skills_root / name
        if inside(source, destination) or inside(destination, source):
            plans.append({"name": name, "action": "blocked", "reason": "source and destination overlap"})
            continue
        try:
            source_fingerprint, file_count = fingerprint(source)
        except (OSError, ValueError) as exc:
            plans.append({"name": name, "action": "blocked", "reason": str(exc)})
            continue
        record = {
            "name": name,
            "mode": "portable-copy",
            "fingerprint": source_fingerprint,
            "file_count": file_count,
            **provenance(source),
        }
        plan: dict[str, Any] = {"name": name, "source": str(source), "record": record}
        if not destination.exists() and not is_link_like(destination):
            plan["action"] = "create"
        elif is_link_like(destination):
            plan.update(action="blocked", reason="destination is linked")
        elif not destination.is_dir() or not (destination / "SKILL.md").is_file():
            plan.update(action="blocked", reason="destination is not a valid skill directory")
        else:
            try:
                current_fingerprint, _ = fingerprint(destination)
            except (OSError, ValueError) as exc:
                plan.update(action="blocked", reason=str(exc))
            else:
                prior = existing_records.get(name)
                if current_fingerprint == source_fingerprint:
                    plan["action"] = "unchanged"
                elif prior and prior.get("mode") == "portable-copy" and prior.get("fingerprint") == current_fingerprint:
                    plan["action"] = "update"
                else:
                    plan.update(action="blocked", reason="destination has unrecorded changes")
        plans.append(plan)
    return plans, existing_records


def apply_plan(project: Path, plans: list[dict[str, Any]], existing_records: dict[str, dict[str, Any]], lock_path: Path) -> None:
    blocked = [plan for plan in plans if plan["action"] == "blocked"]
    if blocked:
        raise ValueError("plan contains blocked skills")
    agents_root = project / ".agents"
    skills_root = agents_root / "skills"
    agents_root.mkdir(parents=True, exist_ok=True)
    skills_root.mkdir(parents=True, exist_ok=True)
    stage_root = Path(tempfile.mkdtemp(prefix=".skills-stage-", dir=agents_root))
    backup_root = Path(tempfile.mkdtemp(prefix=".skills-backup-", dir=agents_root))
    installed: list[tuple[Path, Path | None]] = []
    success = False
    try:
        for plan in plans:
            if plan["action"] == "blocked":
                continue
            current_source, _ = fingerprint(Path(plan["source"]))
            if current_source != plan["record"]["fingerprint"]:
                raise RuntimeError(f"source changed after planning: {plan['name']}")
            if plan["action"] == "unchanged":
                current_destination, _ = fingerprint(skills_root / plan["name"])
                if current_destination != plan["record"]["fingerprint"]:
                    raise RuntimeError(f"destination changed after planning: {plan['name']}")

        for plan in plans:
            if plan["action"] not in {"create", "update"}:
                continue
            source = Path(plan["source"])
            staged = stage_root / plan["name"]
            copy_skill(source, staged)
            staged_fingerprint, _ = fingerprint(staged)
            if staged_fingerprint != plan["record"]["fingerprint"]:
                raise RuntimeError(f"staged fingerprint mismatch: {plan['name']}")

        for plan in plans:
            if plan["action"] not in {"create", "update"}:
                continue
            destination = skills_root / plan["name"]
            backup: Path | None = None
            if plan["action"] == "update":
                current_fingerprint, _ = fingerprint(destination)
                prior = existing_records.get(plan["name"], {})
                if current_fingerprint != prior.get("fingerprint"):
                    raise RuntimeError(f"destination changed after planning: {plan['name']}")
                backup = backup_root / plan["name"]
                os.replace(destination, backup)
            elif destination.exists() or is_link_like(destination):
                raise RuntimeError(f"destination appeared after planning: {plan['name']}")
            try:
                os.replace(stage_root / plan["name"], destination)
            except Exception:
                if backup is not None and backup.exists() and not destination.exists():
                    os.replace(backup, destination)
                raise
            installed.append((destination, backup))

        merged = dict(existing_records)
        for plan in plans:
            merged[plan["name"]] = plan["record"]
        write_lock(lock_path, merged.values())
        success = True
    except Exception as original:
        rollback_errors: list[str] = []
        for destination, backup in reversed(installed):
            try:
                if destination.exists() and not is_link_like(destination):
                    shutil.rmtree(destination)
                if backup is not None and backup.exists():
                    os.replace(backup, destination)
            except OSError as exc:
                rollback_errors.append(f"{destination}: {exc}")
        if rollback_errors:
            details = "; ".join(rollback_errors)
            raise RuntimeError(f"rollback failed; backups remain at {backup_root}: {details}") from original
        raise
    finally:
        shutil.rmtree(stage_root, ignore_errors=True)
        if success:
            shutil.rmtree(backup_root, ignore_errors=True)
        else:
            try:
                backup_root.rmdir()
            except OSError:
                pass


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--project", default=".", help="Target project root")
    parser.add_argument("--source-root", help="Directory that contains source skill folders")
    parser.add_argument("--skill", action="append", default=[], help="Skill name to materialize; repeat this option")
    parser.add_argument("--lock", default=".agents/skills.lock.json", help="Project-relative lock path")
    parser.add_argument("--write", action="store_true", help="Apply a clean plan")
    parser.add_argument("--verify", action="store_true", help="Verify the current portable bundle against its lock")
    return parser.parse_args()


def main() -> int:
    args = parse_args()
    project = Path(args.project).expanduser().resolve()
    if not project.is_dir():
        print(f"project path is not a directory: {project}", file=sys.stderr)
        return 2
    lock_path = (project / args.lock).resolve()
    if not inside(lock_path, project):
        print(f"lock path escapes the project: {lock_path}", file=sys.stderr)
        return 2

    if args.verify:
        if args.write or args.source_root or args.skill:
            print("--verify cannot be combined with --write, --source-root, or --skill", file=sys.stderr)
            return 2
        result, ok = verify_project(project, lock_path)
        print(json.dumps(result, indent=2, sort_keys=True))
        return 0 if ok else 1

    if not args.source_root or not args.skill:
        print("--source-root and at least one --skill are required", file=sys.stderr)
        return 2
    names = sorted(set(args.skill))
    source_root = Path(args.source_root).expanduser().resolve()
    if not source_root.is_dir():
        print(f"source root is not a directory: {source_root}", file=sys.stderr)
        return 2
    for boundary in (project / ".agents", project / ".agents" / "skills"):
        if is_link_like(boundary):
            print(f"portable destination root is linked: {boundary}", file=sys.stderr)
            return 2

    try:
        plans, existing_records = build_plan(project, source_root, names, lock_path)
    except (OSError, ValueError) as exc:
        print(f"skill sync planning failed: {exc}", file=sys.stderr)
        return 1
    public_plan = [
        {key: value for key, value in plan.items() if key not in {"record", "source"}}
        | {"record": plan.get("record")}
        for plan in plans
    ]
    blocked = any(plan["action"] == "blocked" for plan in plans)
    print(json.dumps({"mode": "write" if args.write else "dry-run", "skills": public_plan}, indent=2, sort_keys=True))
    if blocked:
        return 1
    if not args.write:
        return 0
    try:
        apply_plan(project, plans, existing_records, lock_path)
    except (OSError, RuntimeError, ValueError) as exc:
        print(f"skill sync failed: {exc}", file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
