#!/usr/bin/env python3
"""Validate the DOCUMENTATION pack and synthetic mathematical design oracles.

This tool never connects to providers, reads user databases, compiles C#, or
claims to validate TokenUsage runtime behavior. Python 3.10+ is required.
JSON Schema checks use the optional `jsonschema` package. When that package is
present, `rfc3339-validator` is required so `date-time` formats are actually
checked. Missing format support is a failure, not a silent pass.
"""
from __future__ import annotations

import copy
import json
import math
import random
import re
import sys
from datetime import datetime, timezone
from decimal import Decimal
from fractions import Fraction
from pathlib import Path
from typing import Any, Callable
from urllib.parse import unquote, urlsplit

ROOT = Path(__file__).resolve().parents[1]
RESULTS: list[dict[str, Any]] = []
COMMIT = "df50c367b083e5624bf09213eddc7013d8299aa4"


def check(name: str, category: str, action: Callable[[], Any]) -> None:
    try:
        value = action()
        if value is False:
            raise AssertionError("Predicate returned False")
        RESULTS.append({"name": name, "category": category, "status": "passed"})
    except Exception as exc:
        RESULTS.append({"name": name, "category": category, "status": "failed",
                        "detail": f"{type(exc).__name__}: {exc}"})


def load(path: str) -> Any:
    return json.loads((ROOT / path).read_text(encoding="utf-8"))


def markdown_links() -> bool:
    missing: list[str] = []
    # Archived input is preserved verbatim, including references to its old companions.
    for p in [ROOT / "README.md", *sorted((ROOT / "docs").rglob("*.md"))]:
        text = re.sub(r"```.*?```", "", p.read_text(encoding="utf-8"), flags=re.S)
        for match in re.finditer(r"\[[^\]]*\]\(([^\s)]+)(?:\s+[^)]*)?\)", text):
            target = match.group(1)
            if target.startswith(("https:", "http:", "mailto:", "#")):
                continue
            destination = (p.parent / unquote(urlsplit(target).path)).resolve()
            if not destination.is_file():
                missing.append(f"{p.relative_to(ROOT)} -> {target}")
    if missing:
        raise AssertionError("; ".join(missing))
    return True


def traceability() -> bool:
    phase_text = "\n".join(p.read_text(encoding="utf-8") for p in (ROOT / "docs/phases").glob("*.md"))
    definitions = set(re.findall(r"\| (REQ-P\d-\d{2}) \|", phase_text))
    tests = set(re.findall(r"\| (TEST-P\d-\d{2}) \|", phase_text))
    rows = load("validation/traceability.json")["requirements"]
    assert {x["requirement"] for x in rows} == definitions
    assert len(rows) == len(definitions) == 36
    for row in rows:
        assert row["status"] == "planned_not_executed_in_product"
        assert (ROOT / row["document"]).is_file()
        assert row["planned_tests"] and set(row["planned_tests"]) <= tests
        assert row["phase_tasks"] and all(x in phase_text for x in row["phase_tasks"])
        assert row["gate"] in phase_text
    return True


def semantic_selection(value: dict[str, Any]) -> bool:
    interval = value["range"]
    start = datetime.fromisoformat(interval["fromInclusiveUtc"].replace("Z", "+00:00"))
    end = datetime.fromisoformat(interval["toExclusiveUtc"].replace("Z", "+00:00"))
    if end <= start:
        raise ValueError("Range must be nonempty and half-open")
    return True


def signed_count(value: str) -> int:
    number = int(value)
    if not 0 <= number <= 2**63 - 1:
        raise ValueError("Count exceeds the supported nonnegative Int64 range")
    return number


def rejects(action: Callable[[], Any]) -> bool:
    try:
        action()
    except (ValueError, AssertionError):
        return True
    return False


def common_cohort(rows: list[dict[str, Any]]) -> tuple[Decimal, Decimal, Decimal] | None:
    # Fixture rows are already keyed by logical identity + revision. This is an
    # oracle for eligibility, NOT a replacement for the C# source reconciler.
    eligible = [row for row in rows if row["a"] is not None and row["b"] is not None]
    if not eligible:
        return None
    a = sum((Decimal(row["a"]) for row in eligible), Decimal(0))
    b = sum((Decimal(row["b"]) for row in eligible), Decimal(0))
    return a, b, b - a


def linear_split(q0: list[int], q1: list[int], r0: list[int], r1: list[int]) -> tuple[Fraction, Fraction, Fraction]:
    if not q0 or not (len(q0) == len(q1) == len(r0) == len(r1)):
        raise ValueError("Compatible cells required")
    if any(v < 0 for vs in (q0, q1, r0, r1) for v in vs):
        raise ValueError("Nonnegative quantities and rates required")
    Q0, Q1 = sum(q0), sum(q1)
    if Q0 == 0 or Q1 == 0:
        raise ValueError("This design method requires positive volume in both periods")
    s0 = [Fraction(v, Q0) for v in q0]
    s1 = [Fraction(v, Q1) for v in q1]
    V = (Q1 - Q0) * sum(s * r for s, r in zip(s0, r0))
    M = Q1 * sum((b - a) * r for a, b, r in zip(s0, s1, r0))
    P = Q1 * sum(s * (b - a) for s, a, b in zip(s1, r0, r1))
    return V, M, P


def date_time_format_checker():
    """Return a FormatChecker that rejects invalid RFC 3339 calendar dates.

    jsonschema only registers `date-time` when rfc3339-validator is installed.
    That package may accept a syntactically valid but impossible calendar date.
    The declared documentation environment must still reject those dates.
    """
    try:
        from jsonschema import FormatChecker
        from jsonschema.exceptions import FormatError
    except ImportError as exc:
        raise RuntimeError("jsonschema is not installed; date-time checks cannot run.") from exc
    try:
        from rfc3339_validator import validate_rfc3339
    except ImportError as exc:
        raise RuntimeError(
            "Required rfc3339-validator is not installed. "
            "JSON Schema date-time checks cannot reject invalid dates. "
            "Install the documentation environment from tools/requirements-docs.txt."
        ) from exc
    checker = FormatChecker()
    if "date-time" not in checker.checkers:
        raise RuntimeError(
            "jsonschema FormatChecker has no date-time checker. "
            "rfc3339-validator is required; see tools/requirements-docs.txt."
        )

    def is_date_time(instance: object) -> bool:
        if not isinstance(instance, str):
            return True
        if not validate_rfc3339(instance):
            return False
        try:
            datetime.fromisoformat(instance.replace("Z", "+00:00"))
        except ValueError:
            return False
        return True

    checker.checkers["date-time"] = (is_date_time, ())
    try:
        checker.check("2026-02-30T00:00:00Z", "date-time")
    except FormatError:
        pass
    else:
        raise RuntimeError(
            "The date-time checker accepted 2026-02-30T00:00:00Z; "
            "invalid dates cannot be rejected in this environment."
        )
    checker.check("2026-09-12T00:00:00Z", "date-time")
    return checker


def generated_linear_cases() -> bool:
    rng = random.Random(20260912)
    for _ in range(200):
        cells = rng.randint(1, 8)
        q0, q1 = [[rng.randint(1, 10000) for _ in range(cells)] for _ in range(2)]
        r0, r1 = [[rng.randint(0, 1000) for _ in range(cells)] for _ in range(2)]
        V, M, P = linear_split(q0, q1, r0, r1)
        expected = sum(q*r for q, r in zip(q1, r1)) - sum(q*r for q, r in zip(q0, r0))
        assert V + M + P == expected
    return True


def median(values: list[int]) -> Fraction | None:
    if len(values) < 5:  # Proposed product display threshold; not a statistical guarantee.
        return None
    v = sorted(values)
    n = len(v)
    return Fraction(v[n//2]) if n % 2 else Fraction(v[n//2-1] + v[n//2], 2)


def p95(values: list[int]) -> int | None:
    if len(values) < 100:  # Same caveat; exact nearest-rank on eligible observations.
        return None
    v = sorted(values)
    return v[math.ceil(Fraction(95, 100) * len(v)) - 1]


def run() -> int:
    RESULTS.clear()
    docs = [ROOT / "README.md", *sorted((ROOT / "docs").rglob("*.md"))]
    expected = {entry["path"] for entry in load("tools/reader-documents.json") if entry["id"] != "rfc-original"}
    check("Current Markdown inventory matches reader registry", "structure", lambda: {p.relative_to(ROOT).as_posix() for p in docs} == expected)
    check("All current Markdown links resolve locally", "structure", markdown_links)
    check("Balanced fenced code blocks", "structure", lambda: all(p.read_text(encoding="utf-8").count("```") % 2 == 0 for p in docs))
    check("Baseline commit recorded in README", "structure", lambda: COMMIT in (ROOT / "README.md").read_text(encoding="utf-8"))
    check("36 requirements mapped to existing planned tests and tasks", "traceability", traceability)
    check("20 distinct metric definitions", "traceability", lambda: len(set(re.findall(r"M-\d{2}", (ROOT / "docs/architecture/03-METRIC-SEMANTICS.md").read_text(encoding="utf-8")))) == 20)
    source_data = load("docs/references/sources.json")
    entries = source_data.get("sources", source_data.get("entries", []))
    check("Nonempty unique source registry", "sources", lambda: len(entries) > 0 and len(entries) == len({x["id"] for x in entries}))
    check("TokenUsage source links pinned to baseline", "sources", lambda: all(COMMIT in x["url"] for x in entries if x["id"].startswith("TU-")))

    fixture_pairs = [("selection", "report-selection"), ("metric", "metric"), ("metadata", "record-metadata")]
    try:
        from jsonschema import Draft202012Validator
    except ImportError:
        RESULTS.append({"name": "JSON Schema validation", "category": "schema", "status": "not_run",
                        "detail": "Optional jsonschema package absent; no schema validation is claimed."})
    else:
        format_checker = None

        def load_date_time_checker() -> bool:
            nonlocal format_checker
            format_checker = date_time_format_checker()
            return True

        check("JSON Schema date-time format checking", "schema", load_date_time_checker)
        validators = {}
        examples = {}
        for fixture, schema_name in fixture_pairs:
            schema = load(f"schemas/{schema_name}.draft.v2.schema.json")
            check(f"Valid schema: {schema_name}", "schema", lambda s=schema: Draft202012Validator.check_schema(s))
            validators[fixture] = Draft202012Validator(schema, format_checker=format_checker)
            examples[fixture] = load(f"fixtures/{fixture}.example.json")
            check(f"Example satisfies draft schema: {fixture}", "schema", lambda f=fixture: validators[f].validate(examples[f]))
            invalid = copy.deepcopy(examples[fixture]); invalid["prompt"] = "SYNTHETIC_CONTENT_CANARY"
            check(f"Unknown content field rejected: {fixture}", "schema", lambda f=fixture, v=invalid: not validators[f].is_valid(v))
        invalid_cases = []
        v = copy.deepcopy(examples["selection"]); v["priceMode"] = "reference"; invalid_cases.append(("Reference date required", "selection", v))
        if format_checker is not None:
            v = copy.deepcopy(examples["selection"]); v["range"]["fromInclusiveUtc"] = "2026-02-30T00:00:00Z"; invalid_cases.append(("Invalid calendar date rejected", "selection", v))
        v = copy.deepcopy(examples["selection"]); v["filters"]["tier"] = {"mode": "unknown", "values": ["standard"]}; invalid_cases.append(("Unknown filter cannot also name a value", "selection", v))
        v = copy.deepcopy(examples["selection"]); v["filters"]["effort"] = {"mode": "known-values", "values": []}; invalid_cases.append(("Known filter requires values", "selection", v))
        v = copy.deepcopy(examples["metric"]); v["state"] = "unavailable"; v["reasons"] = ["missing-evidence"]; invalid_cases.append(("Unavailable does not carry numeric zero or subtotal", "metric", v))
        v = copy.deepcopy(examples["metric"]); v["value"] = None; invalid_cases.append(("Available metric requires value", "metric", v))
        v = copy.deepcopy(examples["metric"]); v["eligibleTokens"] = 9007199254740993; invalid_cases.append(("Exact large counts encoded as strings", "metric", v))
        v = copy.deepcopy(examples["metadata"]); v["sessionKey"] = "unapproved"; invalid_cases.append(("P2 sample does not smuggle in P3 attribution", "metadata", v))
        for name, fixture, value in invalid_cases:
            check(name, "schema", lambda f=fixture, v=value: not validators[f].is_valid(v))

    q = load("fixtures/selection.example.json")
    check("Valid half-open range", "semantic_oracle", lambda: semantic_selection(q))
    bad = copy.deepcopy(q); bad["range"]["toExclusiveUtc"] = bad["range"]["fromInclusiveUtc"]
    check("Empty range rejected by semantic layer", "semantic_oracle", lambda: rejects(lambda: semantic_selection(bad)))
    check("Large token integer remains exact", "semantic_oracle", lambda: signed_count(load("fixtures/metric.example.json")["value"]) == 9007199254740993)
    check("Int64 overflow rejected by semantic layer", "semantic_oracle", lambda: rejects(lambda: signed_count(str(2**63))))

    f = load("fixtures/design-oracles.json")
    n = f["normalization"]
    canonical = [n["inputTotal"] - n["cacheRead"], n["outputTotal"] - n["reasoning"], n["reasoning"], n["cacheRead"], 0]
    check("Disjoint component normalization conserves 1250 tokens", "arithmetic_oracle", lambda: canonical == n["expectedComponents"] and sum(canonical) == n["expectedTotal"])
    ratio = f["weightedRatio"]
    check("Ratio of totals, not mean of percentages", "arithmetic_oracle", lambda: Fraction(sum(ratio["numerators"]), sum(ratio["denominators"])) == Fraction(*map(int, ratio["expectedFraction"])))
    check("Fixed cohort uses common eligible records only", "arithmetic_oracle", lambda: common_cohort(f["fixedCohort"]) == (Decimal("0.1"), Decimal("0.2"), Decimal("0.1")))
    check("Disjoint identities with equal token sums are not comparable", "arithmetic_oracle", lambda: common_cohort(f["disjointCohort"]) is None)
    check("Empty cohort is unavailable, not zero", "arithmetic_oracle", lambda: common_cohort([]) is None)
    check("Valid zero-cost cohort is available", "arithmetic_oracle", lambda: common_cohort([{"a":"0", "b":"0"}]) == (Decimal(0),)*3)
    check("Cohort identity includes revision", "semantic_oracle", lambda: not ({("e1", 1)} & {("e1", 2)}))
    for case in f["linearScenarios"]:
        check(f"Linear V/M/P example: {case['name']}", "arithmetic_oracle", lambda c=case: linear_split(c["q0"], c["q1"], c["r0"], c["r1"]) == tuple(map(Fraction, c["expected"])))
    check("200 seeded linear conservation cases", "arithmetic_oracle", generated_linear_cases)
    check("Zero-volume linear decomposition rejected", "arithmetic_oracle", lambda: rejects(lambda: linear_split([0], [1], [1], [1])))
    a, b, qa, qb = map(Fraction, (13, 37, 23, 71))
    check("Previous aggregate formula has zero mix algebraically", "arithmetic_oracle", lambda: b-a-a*(qb/qa-1)-(b/qb-a/qa)*qb == 0)
    check("Median odd sample", "arithmetic_oracle", lambda: median([1,9,2,8,4]) == 4)
    check("Median even sample retains fraction", "arithmetic_oracle", lambda: median([1,2,3,4,5,6]) == Fraction(7,2))
    check("Median sample-size gate", "arithmetic_oracle", lambda: median([1,2,3,4]) is None)
    check("P95 nearest rank", "arithmetic_oracle", lambda: p95(list(range(1,101))) == 95)
    check("P95 sample-size gate", "arithmetic_oracle", lambda: p95(list(range(99))) is None)
    check("Adjacent half-open windows do not double count", "semantic_oracle", lambda: sum(lo <= 10 < hi for lo, hi in [(0,10),(10,20)]) == 1)
    metadata = load("fixtures/metadata.example.json")
    check("Timestamped snapshot is not a request-final", "semantic_oracle", lambda: metadata["timePrecision"] == "timestamp" and metadata["recordKind"] != "request-final")

    failed = [x for x in RESULTS if x["status"] == "failed"]
    skipped = [x for x in RESULTS if x["status"] == "not_run"]
    output = {"scope":"documentation, draft schemas and synthetic design oracles ONLY",
              "createdAtUtc":datetime.now(timezone.utc).isoformat(),
              "status":"failed" if failed else "partial" if skipped else "passed",
              "baselineCommit":COMMIT,
              "summary":{"passed":sum(x["status"] == "passed" for x in RESULTS), "failed":len(failed), "not_run":len(skipped)},
              "currentDocuments":len(docs),
              "wordCountApprox":sum(len(p.read_text(encoding="utf-8").split()) for p in docs),
              "checks":RESULTS,
              "notValidated":["C# compilation and tests", "WinUI packaged app", "real providers and PC usage", "Windows/ARM64 runtime", "product performance", "actual schema migrations", "secure physical erasure", "concurrent consent revocation"]}
    target = ROOT / "validation/pack-validation.json"
    target.parent.mkdir(parents=True, exist_ok=True)
    target.write_text(json.dumps(output,ensure_ascii=False,indent=2)+"\n", encoding="utf-8")
    print(json.dumps(output["summary"],ensure_ascii=False))
    for row in failed:
        print(f"FAILED: {row['name']}: {row['detail']}")
    print("Scope: documentation and synthetic design only. Product tests were NOT run.")
    return 1 if failed else 2 if skipped else 0


if __name__ == "__main__":
    if not __debug__:
        raise SystemExit("Run without Python optimization; validation assertions must remain enabled.")
    sys.exit(run())
