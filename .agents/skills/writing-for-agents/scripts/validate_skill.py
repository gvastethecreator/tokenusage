#!/usr/bin/env python3
"""Validate one Agent Skill or a complete skills root."""

from __future__ import annotations

import argparse
import json
import re
import sys
from pathlib import Path
from typing import Any
from urllib.parse import unquote

for _stream in (sys.stdout, sys.stderr):
    if hasattr(_stream, "reconfigure"):
        _stream.reconfigure(errors="backslashreplace")

try:
    import yaml
except ImportError as exc:  # pragma: no cover - exercised by the CLI environment
    raise SystemExit("PyYAML is required: install pyyaml or run skills-ref validate") from exc


ALLOWED_FRONTMATTER = {
    "name",
    "description",
    "license",
    "compatibility",
    "metadata",
    "allowed-tools",
}
NAME_RE = re.compile(r"^[a-z0-9]+(?:-[a-z0-9]+)*$")
FRONTMATTER_RE = re.compile(r"\A---[ \t]*\n(.*?)\n---[ \t]*(?:\n|\Z)", re.DOTALL)
MARKDOWN_LINK_RE = re.compile(r"\[[^\]]*\]\(([^)]+)\)")
FENCED_BLOCK_RE = re.compile(r"```.*?```|~~~.*?~~~", re.DOTALL)
EXTRANEOUS_FILES = {
    "README.md",
    "CHANGELOG.md",
    "INSTALLATION_GUIDE.md",
    "QUICK_REFERENCE.md",
}
IGNORED_DIRECTORY_NAMES = {".git", ".venv", "__pycache__", "node_modules", "venv"}


def _read_text(path: Path) -> str:
    return path.read_text(encoding="utf-8-sig").replace("\r\n", "\n")


def _frontmatter(text: str) -> tuple[dict[str, Any] | None, str, str]:
    match = FRONTMATTER_RE.match(text)
    if not match:
        return None, "", ""
    raw = match.group(1)
    try:
        parsed = yaml.safe_load(raw)
    except yaml.YAMLError:
        return None, raw, text[match.end() :]
    return parsed if isinstance(parsed, dict) else None, raw, text[match.end() :]


def _inside(path: Path, root: Path) -> bool:
    try:
        path.resolve().relative_to(root.resolve())
        return True
    except ValueError:
        return False


def _inside_ignored_directory(path: Path, root: Path) -> bool:
    try:
        relative = path.relative_to(root)
    except ValueError:
        return False
    return any(part in IGNORED_DIRECTORY_NAMES for part in relative.parts[:-1])


def _relative_target(raw_target: str) -> str | None:
    target = raw_target.strip()
    if target.startswith("<") and ">" in target:
        target = target[1 : target.index(">")]
    else:
        target = target.split(maxsplit=1)[0]
    target = unquote(target).split("#", 1)[0]
    if not target or target.startswith("#") or "://" in target or target.startswith("mailto:"):
        return None
    return target


def _validate_resource_path(
    skill_dir: Path,
    raw_target: str,
    errors: list[str],
) -> None:
    target = _relative_target(raw_target)
    if target is None:
        return
    candidate = (skill_dir / target).resolve()
    if not _inside(candidate, skill_dir):
        errors.append(f"resource path escapes skill directory: {target}")
    elif not candidate.exists():
        errors.append(f"referenced resource does not exist: {target}")


def _validate_markdown_links(skill_dir: Path, errors: list[str]) -> None:
    for source in skill_dir.rglob("*.md"):
        if _inside_ignored_directory(source, skill_dir):
            continue
        try:
            body = FENCED_BLOCK_RE.sub("", _read_text(source))
        except (OSError, UnicodeError) as exc:
            errors.append(f"cannot read markdown resource {source.relative_to(skill_dir)}: {exc}")
            continue
        for match in MARKDOWN_LINK_RE.finditer(body):
            target = _relative_target(match.group(1))
            if target is None:
                continue
            candidate = (source.parent / target).resolve()
            display = f"{source.relative_to(skill_dir)} -> {target}"
            if not _inside(candidate, skill_dir):
                errors.append(f"markdown link escapes skill directory: {display}")
            elif not candidate.exists():
                errors.append(f"markdown link target does not exist: {display}")


def _validate_openai_yaml(
    skill_dir: Path,
    skill_name: str,
    errors: list[str],
    warnings: list[str],
) -> None:
    path = skill_dir / "agents" / "openai.yaml"
    if not path.exists():
        return
    try:
        data = yaml.safe_load(_read_text(path))
    except (OSError, UnicodeError, yaml.YAMLError) as exc:
        errors.append(f"agents/openai.yaml is invalid YAML: {exc}")
        return
    if not isinstance(data, dict):
        errors.append("agents/openai.yaml must be a mapping")
        return

    unknown = set(data) - {"interface", "dependencies", "policy"}
    if unknown:
        warnings.append(f"agents/openai.yaml has unknown top-level keys: {', '.join(sorted(unknown))}")

    interface = data.get("interface", {})
    if interface is not None and not isinstance(interface, dict):
        errors.append("agents/openai.yaml interface must be a mapping")
    elif isinstance(interface, dict):
        string_fields = {
            "display_name",
            "short_description",
            "icon_small",
            "icon_large",
            "brand_color",
            "default_prompt",
        }
        unknown_interface = set(interface) - string_fields
        if unknown_interface:
            errors.append(
                "agents/openai.yaml interface has unsupported keys: "
                + ", ".join(sorted(unknown_interface))
            )
        for field in string_fields & set(interface):
            if not isinstance(interface[field], str) or not interface[field].strip():
                errors.append(f"agents/openai.yaml interface.{field} must be a non-empty string")
        short = interface.get("short_description")
        if isinstance(short, str) and not 25 <= len(short) <= 64:
            errors.append("agents/openai.yaml interface.short_description must be 25-64 characters")
        prompt = interface.get("default_prompt")
        if isinstance(prompt, str) and f"${skill_name}" not in prompt:
            errors.append(f"agents/openai.yaml interface.default_prompt must mention ${skill_name}")
        color = interface.get("brand_color")
        if isinstance(color, str) and not re.fullmatch(r"#[0-9A-Fa-f]{6}", color):
            errors.append("agents/openai.yaml interface.brand_color must be a six-digit hex color")
        for field in ("icon_small", "icon_large"):
            icon = interface.get(field)
            if isinstance(icon, str):
                _validate_resource_path(skill_dir, icon, errors)

    policy = data.get("policy", {})
    if policy is not None and not isinstance(policy, dict):
        errors.append("agents/openai.yaml policy must be a mapping")
    elif isinstance(policy, dict):
        unknown_policy = set(policy) - {"allow_implicit_invocation"}
        if unknown_policy:
            errors.append(
                "agents/openai.yaml policy has unsupported keys: "
                + ", ".join(sorted(unknown_policy))
            )
        if "allow_implicit_invocation" in policy and not isinstance(
            policy["allow_implicit_invocation"], bool
        ):
            errors.append("agents/openai.yaml policy.allow_implicit_invocation must be boolean")

    dependencies = data.get("dependencies", {})
    if dependencies is not None and not isinstance(dependencies, dict):
        errors.append("agents/openai.yaml dependencies must be a mapping")
    elif isinstance(dependencies, dict) and "tools" in dependencies:
        tools = dependencies["tools"]
        if not isinstance(tools, list):
            errors.append("agents/openai.yaml dependencies.tools must be a list")
        else:
            for index, tool in enumerate(tools):
                if not isinstance(tool, dict):
                    errors.append(f"agents/openai.yaml dependencies.tools[{index}] must be a mapping")
                    continue
                for required in ("type", "value", "description"):
                    if not isinstance(tool.get(required), str) or not tool[required].strip():
                        errors.append(
                            f"agents/openai.yaml dependencies.tools[{index}].{required} must be a non-empty string"
                        )


def _load_json(path: Path, errors: list[str]) -> Any | None:
    try:
        return json.loads(_read_text(path))
    except (OSError, UnicodeError, json.JSONDecodeError) as exc:
        errors.append(f"{path.name} is invalid JSON: {exc}")
        return None


def _validate_evals(
    skill_dir: Path,
    skill_name: str,
    errors: list[str],
    warnings: list[str],
) -> None:
    evals_path = skill_dir / "evals" / "evals.json"
    if evals_path.exists():
        data = _load_json(evals_path, errors)
        if data is not None:
            if not isinstance(data, dict):
                errors.append("evals/evals.json must be an object")
            else:
                if data.get("skill_name") != skill_name:
                    errors.append("evals/evals.json skill_name must match SKILL.md name")
                cases = data.get("evals")
                if not isinstance(cases, list) or not cases:
                    errors.append("evals/evals.json evals must be a non-empty list")
                else:
                    seen: set[str] = set()
                    for index, case in enumerate(cases):
                        label = f"evals/evals.json evals[{index}]"
                        if not isinstance(case, dict):
                            errors.append(f"{label} must be an object")
                            continue
                        case_id = case.get("id")
                        if not isinstance(case_id, (str, int)) or str(case_id).strip() == "":
                            errors.append(f"{label}.id must be a non-empty string or integer")
                        elif str(case_id) in seen:
                            errors.append(f"{label}.id is duplicated: {case_id}")
                        else:
                            seen.add(str(case_id))
                        for field in ("prompt", "expected_output"):
                            if not isinstance(case.get(field), str) or not case[field].strip():
                                errors.append(f"{label}.{field} must be a non-empty string")
                        files = case.get("files", [])
                        if not isinstance(files, list) or not all(isinstance(item, str) for item in files):
                            errors.append(f"{label}.files must be a list of paths")
                        else:
                            for item in files:
                                _validate_resource_path(skill_dir, item, errors)
                        assertions = case.get("assertions", [])
                        if not isinstance(assertions, list) or not all(
                            isinstance(item, str) and item.strip() for item in assertions
                        ):
                            errors.append(f"{label}.assertions must be a list of non-empty strings")

    triggers_path = skill_dir / "evals" / "trigger_queries.json"
    if triggers_path.exists():
        data = _load_json(triggers_path, errors)
        if data is not None:
            if not isinstance(data, list) or not data:
                errors.append("evals/trigger_queries.json must be a non-empty list")
            else:
                positives = 0
                negatives = 0
                seen_queries: set[str] = set()
                for index, case in enumerate(data):
                    label = f"evals/trigger_queries.json[{index}]"
                    if not isinstance(case, dict):
                        errors.append(f"{label} must be an object")
                        continue
                    if not isinstance(case.get("query"), str) or not case["query"].strip():
                        errors.append(f"{label}.query must be a non-empty string")
                    else:
                        normalized_query = " ".join(case["query"].casefold().split())
                        if normalized_query in seen_queries:
                            errors.append(f"{label}.query duplicates another trigger case")
                        seen_queries.add(normalized_query)
                    if not isinstance(case.get("should_trigger"), bool):
                        errors.append(f"{label}.should_trigger must be boolean")
                    elif case["should_trigger"]:
                        positives += 1
                    else:
                        negatives += 1
                if not positives or not negatives:
                    errors.append("evals/trigger_queries.json must contain positive and negative cases")
                if positives < 8:
                    warnings.append("trigger eval has fewer than 8 positive cases")
                if negatives < 8:
                    warnings.append("trigger eval has fewer than 8 negative cases")


def _default_skills_root(skill_dir: Path) -> Path | None:
    for candidate in skill_dir.parents:
        if candidate.name == "skills":
            return candidate
    return None


def _validate_unique_name(
    skill_dir: Path,
    skill_name: str,
    skills_root: Path | None,
    errors: list[str],
) -> None:
    if skills_root is None or not skills_root.exists():
        return
    target = (skill_dir / "SKILL.md").resolve()
    for path in skills_root.rglob("SKILL.md"):
        if _inside_ignored_directory(path, skills_root):
            continue
        try:
            if path.resolve() == target:
                continue
            data, _, _ = _frontmatter(_read_text(path))
        except (OSError, UnicodeError):
            continue
        if isinstance(data, dict) and data.get("name") == skill_name:
            errors.append(f"duplicate skill name {skill_name!r}: {path}")


def validate_skill(
    skill_path: str | Path,
    skills_root: str | Path | None = None,
    *,
    check_unique_name: bool = True,
) -> dict[str, Any]:
    # Keep the invoked alias for identity checks. Resolving here makes a junction
    # inherit its canonical source folder name instead of its published skill name.
    skill_dir = Path(skill_path).expanduser().absolute()
    errors: list[str] = []
    warnings: list[str] = []
    skill_md = skill_dir / "SKILL.md"

    if not skill_dir.is_dir():
        return {"ok": False, "skill": str(skill_dir), "errors": ["skill path is not a directory"], "warnings": []}
    if not skill_md.is_file():
        return {"ok": False, "skill": str(skill_dir), "errors": ["SKILL.md not found"], "warnings": []}

    try:
        text = _read_text(skill_md)
    except (OSError, UnicodeError) as exc:
        return {"ok": False, "skill": str(skill_dir), "errors": [f"cannot read SKILL.md: {exc}"], "warnings": []}

    data, raw_frontmatter, body = _frontmatter(text)
    if data is None:
        errors.append("SKILL.md must start with valid YAML frontmatter")
        data = {}

    unknown = set(data) - ALLOWED_FRONTMATTER
    if unknown:
        errors.append(f"unsupported frontmatter fields: {', '.join(sorted(unknown))}")

    name = data.get("name")
    if not isinstance(name, str) or not name.strip():
        errors.append("frontmatter name must be a non-empty string")
        name = ""
    else:
        name = name.strip()
        if len(name) > 64 or not NAME_RE.fullmatch(name):
            errors.append("frontmatter name must be <=64 chars of lowercase letters, digits, and single hyphens")
        if name != skill_dir.name:
            errors.append(f"frontmatter name {name!r} must match parent directory {skill_dir.name!r}")

    description = data.get("description")
    if not isinstance(description, str) or not description.strip():
        errors.append("frontmatter description must be a non-empty string")
    elif len(description.strip()) > 1024:
        errors.append("frontmatter description must be <=1024 characters")
    for field in ("license", "compatibility", "allowed-tools"):
        if field in data and (not isinstance(data[field], str) or not data[field].strip()):
            errors.append(f"frontmatter {field} must be a non-empty string")
    compatibility = data.get("compatibility")
    if isinstance(compatibility, str) and len(compatibility) > 500:
        errors.append("frontmatter compatibility must be <=500 characters")
    metadata = data.get("metadata")
    if metadata is not None:
        if not isinstance(metadata, dict):
            errors.append("frontmatter metadata must be a mapping")
        elif any(not isinstance(key, str) or not isinstance(value, str) for key, value in metadata.items()):
            errors.append("frontmatter metadata keys and values must be strings")

    if not body.strip():
        errors.append("SKILL.md body must not be empty")
    if re.search(r"(?i)\[TODO(?::|\])", body):
        errors.append("SKILL.md contains an unresolved TODO placeholder")
    if len(text.splitlines()) > 500:
        warnings.append("SKILL.md exceeds 500 lines; move branch-specific content into references")
    if len(body) > 20_000:
        warnings.append("SKILL.md body may exceed roughly 5,000 tokens")

    _validate_markdown_links(skill_dir, errors)

    present_extraneous = sorted(path.name for path in skill_dir.iterdir() if path.name in EXTRANEOUS_FILES)
    if present_extraneous:
        warnings.append(f"possible auxiliary skill docs: {', '.join(present_extraneous)}")

    if name:
        _validate_openai_yaml(skill_dir, name, errors, warnings)
        _validate_evals(skill_dir, name, errors, warnings)
        if check_unique_name:
            root = Path(skills_root).resolve() if skills_root else _default_skills_root(skill_dir)
            _validate_unique_name(skill_dir, name, root, errors)

    return {
        "ok": not errors,
        "skill": str(skill_dir),
        "errors": sorted(set(errors)),
        "warnings": sorted(set(warnings)),
    }


def validate_skills_root(skills_root: str | Path) -> dict[str, Any]:
    root = Path(skills_root).resolve()
    if not root.is_dir():
        return {
            "ok": False,
            "skills_root": str(root),
            "count": 0,
            "passed": 0,
            "failed": 0,
            "results": [],
            "errors": ["skills root is not a directory"],
        }

    skill_files = sorted(
        (path for path in root.rglob("SKILL.md") if not _inside_ignored_directory(path, root)),
        key=lambda path: str(path).casefold(),
    )
    results = [
        validate_skill(path.parent, root, check_unique_name=False)
        for path in skill_files
    ]
    names: dict[str, list[int]] = {}
    for index, path in enumerate(skill_files):
        try:
            data, _, _ = _frontmatter(_read_text(path))
        except (OSError, UnicodeError):
            continue
        name = data.get("name") if isinstance(data, dict) else None
        if isinstance(name, str) and name.strip():
            names.setdefault(name.strip(), []).append(index)

    for name, indexes in names.items():
        if len(indexes) < 2:
            continue
        locations = [str(skill_files[index].parent) for index in indexes]
        for index in indexes:
            others = [location for offset, location in zip(indexes, locations) if offset != index]
            results[index]["errors"].append(
                f"duplicate skill name {name!r}: {', '.join(others)}"
            )
            results[index]["errors"] = sorted(set(results[index]["errors"]))
            results[index]["ok"] = False

    passed = sum(result["ok"] for result in results)
    return {
        "ok": bool(results) and passed == len(results),
        "skills_root": str(root),
        "count": len(results),
        "passed": passed,
        "failed": len(results) - passed,
        "results": results,
        "errors": [] if results else ["no SKILL.md files found"],
    }


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("skill_path", nargs="?", help="Skill directory containing SKILL.md")
    parser.add_argument("--skills-root", help="Optional root scanned for duplicate skill names")
    parser.add_argument("--all", action="store_true", help="Validate every SKILL.md below the skills root")
    parser.add_argument("--json", action="store_true", help="Emit machine-readable JSON")
    args = parser.parse_args()

    if args.all:
        root = args.skills_root or args.skill_path
        if not root:
            parser.error("--all requires --skills-root or skill_path")
        result = validate_skills_root(root)
    else:
        if not args.skill_path:
            parser.error("skill_path is required unless --all is used")
        result = validate_skill(args.skill_path, args.skills_root)
    if args.json:
        print(json.dumps(result, indent=2))
    elif args.all:
        for skill in result["results"]:
            for warning in skill["warnings"]:
                print(f"WARN: {skill['skill']}: {warning}")
            for error in skill["errors"]:
                print(f"ERROR: {skill['skill']}: {error}")
        for error in result["errors"]:
            print(f"ERROR: {result['skills_root']}: {error}")
        print(
            ("PASS" if result["ok"] else "FAIL")
            + f": {result['passed']}/{result['count']} skills passed"
        )
    else:
        for warning in result["warnings"]:
            print(f"WARN: {warning}")
        for error in result["errors"]:
            print(f"ERROR: {error}")
        print(("PASS" if result["ok"] else "FAIL") + f": {result['skill']}")
    return 0 if result["ok"] else 1


if __name__ == "__main__":
    sys.exit(main())
