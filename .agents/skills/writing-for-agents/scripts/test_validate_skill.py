#!/usr/bin/env python3
"""Focused regression tests for validate_skill.py."""

from __future__ import annotations

import json
import os
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path

from validate_skill import validate_skill, validate_skills_root


class ValidateSkillTests(unittest.TestCase):
    def make_skill(
        self,
        root: Path,
        name: str = "demo-skill",
        frontmatter: str | None = None,
        body: str = "# Demo Skill\n\nUse `references/guide.md`.\n",
    ) -> Path:
        skill = root / name
        (skill / "references").mkdir(parents=True)
        (skill / "references" / "guide.md").write_text("# Guide\n", encoding="utf-8")
        yaml_text = frontmatter or (
            f"---\nname: {name}\ndescription: \"Use when validating demo skills.\"\n---\n\n"
        )
        (skill / "SKILL.md").write_text(yaml_text + body, encoding="utf-8")
        return skill

    def assert_error(self, result: dict, needle: str) -> None:
        self.assertFalse(result["ok"])
        self.assertTrue(any(needle in error for error in result["errors"]), result)

    def test_valid_skill_passes(self) -> None:
        with tempfile.TemporaryDirectory() as temp:
            skill = self.make_skill(Path(temp))
            agents = skill / "agents"
            agents.mkdir()
            (agents / "openai.yaml").write_text(
                "interface:\n"
                '  short_description: "Design and validate demo skills"\n'
                '  default_prompt: "Use $demo-skill to validate this skill."\n'
                "policy:\n"
                "  allow_implicit_invocation: true\n",
                encoding="utf-8",
            )
            evals = skill / "evals"
            evals.mkdir()
            (evals / "evals.json").write_text(
                json.dumps(
                    {
                        "skill_name": "demo-skill",
                        "evals": [
                            {
                                "id": "smoke",
                                "prompt": "Validate this demo skill.",
                                "expected_output": "A validation result.",
                                "files": [],
                                "assertions": ["The result reports pass or fail."],
                            }
                        ],
                    }
                ),
                encoding="utf-8",
            )
            result = validate_skill(skill)
            self.assertTrue(result["ok"], result)

    def test_empty_required_field_fails(self) -> None:
        with tempfile.TemporaryDirectory() as temp:
            skill = self.make_skill(
                Path(temp),
                frontmatter='---\nname: demo-skill\ndescription: ""\n---\n\n',
            )
            self.assert_error(validate_skill(skill), "description must be a non-empty string")

    def test_name_must_match_directory(self) -> None:
        with tempfile.TemporaryDirectory() as temp:
            skill = self.make_skill(
                Path(temp),
                frontmatter='---\nname: another-name\ndescription: "Use for demos."\n---\n\n',
            )
            self.assert_error(validate_skill(skill), "must match parent directory")

    def test_junction_uses_published_alias_for_name_check(self) -> None:
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            target = self.make_skill(
                root,
                name="canonical-folder",
                frontmatter=(
                    "---\n"
                    "name: published-alias\n"
                    'description: "Use for aliased skills."\n'
                    "---\n\n"
                ),
            )
            alias = root / "published-alias"
            if os.name == "nt":
                created = subprocess.run(
                    ["cmd", "/c", "mklink", "/J", str(alias), str(target)],
                    capture_output=True,
                    text=True,
                    check=False,
                )
                if created.returncode != 0:
                    self.skipTest(f"junction unavailable: {created.stderr or created.stdout}")
            else:
                alias.symlink_to(target, target_is_directory=True)

            result = validate_skill(alias)

            self.assertTrue(result["ok"], result)
            self.assertEqual(Path(result["skill"]).name, "published-alias")

    def test_broken_reference_fails(self) -> None:
        with tempfile.TemporaryDirectory() as temp:
            skill = self.make_skill(
                Path(temp),
                body="# Demo\n\nUse [the missing guide](references/missing.md).\n",
            )
            self.assert_error(validate_skill(skill), "markdown link target does not exist")

    def test_placeholder_links_in_fenced_examples_are_ignored(self) -> None:
        with tempfile.TemporaryDirectory() as temp:
            skill = self.make_skill(
                Path(temp),
                body=(
                    "# Demo\n\n"
                    "```md\n"
                    "Read [an example](references/not-a-real-file.md).\n"
                    "```\n"
                ),
            )
            self.assertTrue(validate_skill(skill)["ok"])

    def test_broken_link_inside_reference_fails(self) -> None:
        with tempfile.TemporaryDirectory() as temp:
            skill = self.make_skill(Path(temp))
            (skill / "references" / "guide.md").write_text(
                "# Guide\n\nRead [missing](missing.md).\n",
                encoding="utf-8",
            )
            self.assert_error(validate_skill(skill), "markdown link target does not exist")

    def test_node_modules_resources_and_nested_skills_are_ignored(self) -> None:
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            skill = self.make_skill(root)
            vendored = skill / "node_modules" / "vendored-package"
            vendored.mkdir(parents=True)
            (vendored / "README.md").write_text(
                "Read [a missing vendored file](missing.md).\n",
                encoding="utf-8",
            )
            (vendored / "SKILL.md").write_text(
                '---\nname: vendored-package\ndescription: "Vendored fixture."\n---\n\n# Vendored\n',
                encoding="utf-8",
            )

            self.assertTrue(validate_skill(skill)["ok"])
            result = validate_skills_root(root)
            self.assertTrue(result["ok"], result)
            self.assertEqual(result["count"], 1)

    def test_compatibility_and_string_metadata_pass(self) -> None:
        with tempfile.TemporaryDirectory() as temp:
            skill = self.make_skill(
                Path(temp),
                frontmatter=(
                    "---\n"
                    "name: demo-skill\n"
                    'description: "Use for demos."\n'
                    'compatibility: "Requires Python 3."\n'
                    "metadata:\n"
                    '  version: "1"\n'
                    "---\n\n"
                ),
            )
            self.assertTrue(validate_skill(skill)["ok"])

    def test_non_string_metadata_fails(self) -> None:
        with tempfile.TemporaryDirectory() as temp:
            skill = self.make_skill(
                Path(temp),
                frontmatter=(
                    "---\n"
                    "name: demo-skill\n"
                    'description: "Use for demos."\n'
                    "metadata:\n"
                    "  version: 1\n"
                    "---\n\n"
                ),
            )
            self.assert_error(validate_skill(skill), "metadata keys and values must be strings")

    def test_openai_policy_and_default_prompt_are_validated(self) -> None:
        with tempfile.TemporaryDirectory() as temp:
            skill = self.make_skill(Path(temp))
            agents = skill / "agents"
            agents.mkdir()
            (agents / "openai.yaml").write_text(
                "interface:\n"
                '  short_description: "Design and validate demo skills"\n'
                '  default_prompt: "Validate this skill."\n'
                "policy:\n"
                '  allow_implicit_invocation: "yes"\n',
                encoding="utf-8",
            )
            result = validate_skill(skill)
            self.assert_error(result, "default_prompt must mention $demo-skill")
            self.assert_error(result, "allow_implicit_invocation must be boolean")

    def test_unknown_openai_policy_key_fails(self) -> None:
        with tempfile.TemporaryDirectory() as temp:
            skill = self.make_skill(Path(temp))
            agents = skill / "agents"
            agents.mkdir()
            (agents / "openai.yaml").write_text(
                "policy:\n"
                "  allow_implicit_invocaton: false\n",
                encoding="utf-8",
            )
            self.assert_error(validate_skill(skill), "policy has unsupported keys")

    def test_non_utf8_openai_yaml_becomes_finding(self) -> None:
        with tempfile.TemporaryDirectory() as temp:
            skill = self.make_skill(Path(temp))
            agents = skill / "agents"
            agents.mkdir()
            (agents / "openai.yaml").write_bytes(b"interface:\n  display_name: \xff\n")
            self.assert_error(validate_skill(skill), "agents/openai.yaml is invalid YAML")

    def test_trigger_queries_require_balance_and_uniqueness(self) -> None:
        with tempfile.TemporaryDirectory() as temp:
            skill = self.make_skill(Path(temp))
            evals = skill / "evals"
            evals.mkdir()
            cases = [
                {"query": f"positive {index}", "should_trigger": True}
                for index in range(15)
            ]
            cases.append({"query": "positive 0", "should_trigger": False})
            (evals / "trigger_queries.json").write_text(
                json.dumps(cases),
                encoding="utf-8",
            )
            result = validate_skill(skill)
            self.assert_error(result, "duplicates another trigger case")
            self.assertTrue(
                any("fewer than 8 negative" in warning for warning in result["warnings"]),
                result,
            )

    def test_duplicate_name_fails(self) -> None:
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp) / "skills"
            first = self.make_skill(root / "group-a")
            second = self.make_skill(root / "group-b")
            self.assert_error(validate_skill(first, root), "duplicate skill name")
            self.assert_error(validate_skill(second, root), "duplicate skill name")

    def test_batch_validation_reports_all_skills_and_duplicates(self) -> None:
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp) / "skills"
            first = self.make_skill(root / "group-a")
            second = self.make_skill(root / "group-b")
            self.make_skill(root, name="unique-skill")

            result = validate_skills_root(root)

            self.assertEqual(result["count"], 3)
            self.assertEqual(result["passed"], 1)
            self.assertEqual(result["failed"], 2)
            self.assertFalse(result["ok"])
            duplicate_results = [
                item
                for item in result["results"]
                if any("duplicate skill name" in error for error in item["errors"])
            ]
            self.assertEqual(len(duplicate_results), 2)
            duplicate_paths = {item["skill"] for item in duplicate_results}
            self.assertIn(str(first.resolve()), duplicate_paths)
            self.assertIn(str(second.resolve()), duplicate_paths)

    def test_batch_validation_rejects_an_empty_root(self) -> None:
        with tempfile.TemporaryDirectory() as temp:
            result = validate_skills_root(Path(temp))
            self.assertFalse(result["ok"])
            self.assertEqual(result["count"], 0)
            self.assertIn("no SKILL.md files found", result["errors"])

    def test_cli_does_not_crash_on_unicode_paths_with_cp1252(self) -> None:
        with tempfile.TemporaryDirectory() as temp:
            skill = self.make_skill(Path(temp) / "技能")
            (skill / "README.md").write_text("# Extra\n", encoding="utf-8")
            env = {**os.environ, "PYTHONIOENCODING": "cp1252"}

            result = subprocess.run(
                [sys.executable, str(Path(__file__).with_name("validate_skill.py")), str(skill)],
                capture_output=True,
                text=True,
                encoding="cp1252",
                env=env,
                check=False,
            )

            self.assertEqual(result.returncode, 0, result.stderr or result.stdout)
            self.assertNotIn("UnicodeEncodeError", result.stderr)


if __name__ == "__main__":
    unittest.main()
