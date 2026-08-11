#!/usr/bin/env python3
"""Collect a read-only evidence packet for project guidance work."""

from __future__ import annotations

import argparse
import json
import os
import stat
import subprocess
import sys
from collections import Counter
from pathlib import Path
from typing import Any, Iterable


EXCLUDED_DIRECTORIES = {
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
    "node_modules",
    "out",
    "target",
    "vendor",
    "venv",
}

INSTRUCTION_NAMES = {
    ".cursorrules",
    "agents.md",
    "claude.md",
    "copilot-instructions.md",
    "gemini.md",
}

MANIFEST_NAMES = {
    "build.gradle",
    "build.gradle.kts",
    "bunfig.toml",
    "cargo.toml",
    "cmakelists.txt",
    "composer.json",
    "deno.json",
    "deno.jsonc",
    "gemfile",
    "go.mod",
    "mix.exs",
    "package.json",
    "pipfile",
    "pnpm-workspace.yaml",
    "pom.xml",
    "pyproject.toml",
    "requirements.txt",
    "settings.gradle",
    "settings.gradle.kts",
}

LOCKFILE_NAMES = {
    "bun.lock",
    "bun.lockb",
    "cargo.lock",
    "composer.lock",
    "go.sum",
    "package-lock.json",
    "pipfile.lock",
    "pnpm-lock.yaml",
    "poetry.lock",
    "uv.lock",
    "yarn.lock",
}

CONFIG_NAMES = {
    ".editorconfig",
    ".eslintrc",
    ".gitlab-ci.yml",
    ".prettierrc",
    "azure-pipelines.yml",
    "biome.json",
    "biome.jsonc",
    "justfile",
    "makefile",
    "playwright.config.js",
    "playwright.config.mjs",
    "playwright.config.ts",
    "pytest.ini",
    "ruff.toml",
    "taskfile.yml",
    "tox.ini",
    "tsconfig.json",
    "vitest.config.js",
    "vitest.config.mjs",
    "vitest.config.ts",
}

CODE_ROOT_NAMES = {"app", "apps", "crates", "lib", "mcp", "packages", "scripts", "skills", "src"}
TEST_ROOT_NAMES = {"e2e", "spec", "specs", "test", "tests"}
DOC_ROOT_NAMES = {"adr", "docs", "documentation"}


def run_git(root: Path, *args: str) -> subprocess.CompletedProcess[str]:
    return subprocess.run(
        ["git", "-C", str(root), *args],
        capture_output=True,
        check=False,
        encoding="utf-8",
        errors="replace",
        text=True,
        timeout=20,
    )


def find_git_root(candidate: Path) -> Path | None:
    try:
        result = run_git(candidate, "rev-parse", "--show-toplevel")
    except (OSError, subprocess.TimeoutExpired):
        return None
    if result.returncode != 0:
        return None
    return Path(result.stdout.strip()).resolve()


def is_excluded(path: Path) -> bool:
    return any(part.lower() in EXCLUDED_DIRECTORIES for part in path.parts)


def git_files(root: Path, max_files: int) -> tuple[list[Path], bool]:
    result = subprocess.run(
        ["git", "-C", str(root), "ls-files", "-co", "--exclude-standard", "-z"],
        capture_output=True,
        check=False,
        timeout=30,
    )
    if result.returncode != 0:
        raise RuntimeError(result.stderr.decode("utf-8", errors="replace").strip())
    raw_paths = result.stdout.decode("utf-8", errors="surrogateescape").split("\0")
    files: list[Path] = []
    truncated = False
    for raw in raw_paths:
        if not raw:
            continue
        relative = Path(raw)
        if is_excluded(relative):
            continue
        if len(files) >= max_files:
            truncated = True
            break
        files.append(relative)
    return sorted(set(files), key=lambda item: item.as_posix().lower()), truncated


def walked_files(root: Path, max_files: int) -> tuple[list[Path], bool]:
    files: list[Path] = []
    truncated = False
    for current, directories, names in os.walk(root, followlinks=False):
        directories[:] = sorted(
            name for name in directories if name.lower() not in EXCLUDED_DIRECTORIES
        )
        current_path = Path(current)
        for name in sorted(names):
            relative = (current_path / name).relative_to(root)
            if len(files) >= max_files:
                truncated = True
                return files, truncated
            files.append(relative)
    return files, truncated


def sampled(paths: Iterable[Path], limit: int = 100) -> dict[str, Any]:
    values = sorted({path.as_posix() for path in paths}, key=str.lower)
    return {
        "count": len(values),
        "paths": values[:limit],
        "truncated": len(values) > limit,
    }


def git_summary(root: Path) -> dict[str, Any]:
    branch = run_git(root, "branch", "--show-current")
    head = run_git(root, "rev-parse", "HEAD")
    status = run_git(root, "status", "--porcelain=v1", "--untracked-files=normal")
    counts: Counter[str] = Counter()
    if status.returncode == 0:
        for line in status.stdout.splitlines():
            marker = line[:2]
            if marker == "??":
                counts["untracked"] += 1
                continue
            if marker in {"DD", "AU", "UD", "UA", "DU", "AA", "UU"}:
                counts["conflicted"] += 1
            if marker[0] not in {" ", "?"}:
                counts["staged"] += 1
            if marker[1] not in {" ", "?"}:
                counts["unstaged"] += 1
    return {
        "is_repository": True,
        "branch": branch.stdout.strip() if branch.returncode == 0 else None,
        "head": head.stdout.strip() if head.returncode == 0 else None,
        "dirty": bool(status.stdout.strip()) if status.returncode == 0 else None,
        "status_counts": dict(sorted(counts.items())),
    }


def package_script_names(root: Path, warnings: list[str]) -> list[str]:
    path = root / "package.json"
    if not path.is_file():
        return []
    try:
        payload = json.loads(path.read_text(encoding="utf-8-sig"))
    except (OSError, UnicodeError, json.JSONDecodeError) as exc:
        warnings.append(f"package.json could not be parsed: {exc}")
        return []
    scripts = payload.get("scripts", {}) if isinstance(payload, dict) else {}
    if not isinstance(scripts, dict):
        warnings.append("package.json scripts is not an object")
        return []
    return sorted(key for key, value in scripts.items() if isinstance(key, str) and isinstance(value, str))


def code_map_summary(root: Path, warnings: list[str]) -> dict[str, Any]:
    base = root / "docs" / "codemap"
    paths = {
        "html": base / "codemap.html",
        "json": base / "codemap.json",
        "lock": base / "codemap.lock",
    }
    summary: dict[str, Any] = {
        "artifacts": {name: path.is_file() for name, path in paths.items()},
        "freshness": "not_evaluated",
    }
    if not paths["lock"].is_file():
        return summary
    try:
        payload = json.loads(paths["lock"].read_text(encoding="utf-8-sig"))
    except (OSError, UnicodeError, json.JSONDecodeError) as exc:
        warnings.append(f"docs/codemap/codemap.lock could not be parsed: {exc}")
        return summary
    if isinstance(payload, dict):
        modules = payload.get("modules")
        summary["lock"] = {
            "current_commit": payload.get("current_commit"),
            "generated_at": payload.get("generated_at"),
            "module_count": len(modules) if isinstance(modules, list) else None,
            "scanned_scope": payload.get("scanned_scope"),
        }
    return summary


def is_link_like(path: Path) -> bool:
    try:
        metadata = path.lstat()
    except OSError:
        return False
    return stat.S_ISLNK(metadata.st_mode) or bool(getattr(metadata, "st_reparse_tag", 0))


def project_skill_distribution(root: Path, warnings: list[str]) -> dict[str, Any]:
    skills_root = root / ".agents" / "skills"
    lock_path = root / ".agents" / "skills.lock.json"
    result: dict[str, Any] = {
        "exists": skills_root.exists(),
        "root_linked": is_link_like(skills_root),
        "lock_exists": lock_path.is_file(),
        "physical": [],
        "linked": [],
        "invalid": [],
    }
    if skills_root.is_dir():
        try:
            entries = sorted(skills_root.iterdir(), key=lambda item: item.name.lower())
        except OSError as exc:
            warnings.append(f".agents/skills could not be read: {exc}")
            return result
        for entry in entries:
            if is_link_like(entry):
                result["linked"].append(entry.name)
            elif entry.is_dir() and (entry / "SKILL.md").is_file():
                result["physical"].append(entry.name)
            elif entry.is_dir():
                result["invalid"].append(entry.name)
    if lock_path.is_file():
        try:
            payload = json.loads(lock_path.read_text(encoding="utf-8-sig"))
        except (OSError, UnicodeError, json.JSONDecodeError) as exc:
            warnings.append(f".agents/skills.lock.json could not be parsed: {exc}")
        else:
            if isinstance(payload, dict):
                skills = payload.get("skills")
                result["lock_version"] = payload.get("version")
                result["locked_skill_count"] = len(skills) if isinstance(skills, list) else None
    return result


def project_type_signals(paths: list[Path]) -> list[str]:
    names = {path.name.lower() for path in paths}
    suffixes = {path.suffix.lower() for path in paths}
    signals: set[str] = set()
    if "package.json" in names:
        signals.add("Node.js")
    if "tsconfig.json" in names or ".ts" in suffixes or ".tsx" in suffixes:
        signals.add("TypeScript")
    if {"pyproject.toml", "requirements.txt", "pipfile"} & names or ".py" in suffixes:
        signals.add("Python")
    if "cargo.toml" in names:
        signals.add("Rust")
    if "go.mod" in names:
        signals.add("Go")
    if any(path.suffix.lower() in {".csproj", ".fsproj", ".sln"} for path in paths):
        signals.add(".NET")
    if "pom.xml" in names or "build.gradle" in names or "build.gradle.kts" in names:
        signals.add("JVM")
    return sorted(signals)


def build_packet(root: Path, max_files: int) -> dict[str, Any]:
    warnings: list[str] = []
    git_root = find_git_root(root)
    if git_root is not None:
        root = git_root
        try:
            files, truncated = git_files(root, max_files)
        except (OSError, RuntimeError, subprocess.TimeoutExpired) as exc:
            warnings.append(f"git file inventory failed; filesystem walk used: {exc}")
            files, truncated = walked_files(root, max_files)
        git = git_summary(root)
    else:
        files, truncated = walked_files(root, max_files)
        git = {"is_repository": False}
        warnings.append("Git metadata is unavailable")

    if truncated:
        warnings.append(f"file inventory stopped at --max-files={max_files}")

    instructions = [path for path in files if path.name.lower() in INSTRUCTION_NAMES]
    manifests = [
        path
        for path in files
        if path.name.lower() in MANIFEST_NAMES
        or path.suffix.lower() in {".csproj", ".fsproj", ".sln"}
    ]
    lockfiles = [path for path in files if path.name.lower() in LOCKFILE_NAMES]
    configs = [
        path
        for path in files
        if path.name.lower() in CONFIG_NAMES
        or path.name.lower().startswith(("eslint.config.", "jest.config.", "vite.config."))
    ]
    skill_files = [path for path in files if path.name == "SKILL.md"]
    ci_files = [
        path
        for path in files
        if path.as_posix().lower().startswith((".github/workflows/", ".circleci/"))
        or path.name.lower() in {".gitlab-ci.yml", "azure-pipelines.yml"}
    ]
    documentation = [
        path
        for path in files
        if path.suffix.lower() in {".md", ".mdx"}
        and any(
            token in path.name.lower()
            for token in ("adr", "architecture", "changelog", "context", "contributing", "design", "readme", "runbook")
        )
    ]

    top_level = sorted(entry.name for entry in root.iterdir() if entry.name.lower() not in EXCLUDED_DIRECTORIES)
    root_directories = {entry.name.lower(): entry.name for entry in root.iterdir() if entry.is_dir()}

    packet = {
        "schema_version": 1,
        "repo_root": str(root),
        "git": git,
        "scan": {
            "file_count": len(files),
            "max_files": max_files,
            "truncated": truncated,
            "excluded_directories": sorted(EXCLUDED_DIRECTORIES),
        },
        "top_level": top_level,
        "project_types": project_type_signals(files),
        "instruction_files": sampled(instructions),
        "manifests": sampled(manifests),
        "lockfiles": sampled(lockfiles),
        "tool_and_test_configs": sampled(configs),
        "ci_files": sampled(ci_files),
        "documentation": sampled(documentation),
        "skill_files": sampled(skill_files),
        "root_package_script_names": package_script_names(root, warnings),
        "roots": {
            "code": [root_directories[name] for name in sorted(CODE_ROOT_NAMES & root_directories.keys())],
            "tests": [root_directories[name] for name in sorted(TEST_ROOT_NAMES & root_directories.keys())],
            "documentation": [root_directories[name] for name in sorted(DOC_ROOT_NAMES & root_directories.keys())],
        },
        "code_map": code_map_summary(root, warnings),
        "project_skill_distribution": project_skill_distribution(root, warnings),
        "warnings": warnings,
    }
    if packet["code_map"]["artifacts"].get("lock"):
        packet["warnings"].append("Code-map artifacts exist, but this script does not measure freshness")
    return packet


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--repo", default=".", help="Repository or project path")
    parser.add_argument("--max-files", type=int, default=25_000, help="Maximum files to inventory")
    parser.add_argument("--output", help="Optional JSON output path")
    parser.add_argument("--force", action="store_true", help="Replace an existing output file")
    return parser.parse_args()


def main() -> int:
    args = parse_args()
    if args.max_files < 1:
        print("--max-files must be positive", file=sys.stderr)
        return 2
    root = Path(args.repo).expanduser().resolve()
    if not root.is_dir():
        print(f"project path is not a directory: {root}", file=sys.stderr)
        return 2

    try:
        packet = build_packet(root, args.max_files)
    except (OSError, subprocess.TimeoutExpired) as exc:
        print(f"project inspection failed: {exc}", file=sys.stderr)
        return 1

    output = json.dumps(packet, indent=2, sort_keys=True, ensure_ascii=False) + "\n"
    if not args.output:
        sys.stdout.write(output)
        return 0

    target = Path(args.output).expanduser().resolve()
    if target.exists() and not args.force:
        print(f"output exists; use --force to replace it: {target}", file=sys.stderr)
        return 2
    target.parent.mkdir(parents=True, exist_ok=True)
    target.write_text(output, encoding="utf-8")
    print(target)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
