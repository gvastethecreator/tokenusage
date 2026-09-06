"""Reduced counterexamples for TokenUsage at 27d36e2 (not C# product tests).

Only synthetic values and an in-memory SQLite database are used. Success means
that the documented limitation is reproduced; it does NOT mean it is fixed.
Run: python docs/research/usage-comparison/reproduce_counterexamples.py
"""
from __future__ import annotations

import sqlite3
import unittest
from decimal import Decimal

BASE = "27d36e2b95aeb8ef4111f02233143ca6e2e87aaf"


def fixture_database() -> sqlite3.Connection:
    db = sqlite3.connect(":memory:")
    db.executescript("""
        CREATE TABLE usage_event (
            event_key TEXT PRIMARY KEY, agent_id TEXT NOT NULL,
            civil_date TEXT NOT NULL, parser_version TEXT NOT NULL,
            input_tokens INTEGER NOT NULL
        );
        CREATE TABLE daily_usage_rollup (
            agent_id TEXT NOT NULL, civil_date TEXT NOT NULL,
            input_tokens INTEGER NOT NULL,
            PRIMARY KEY (agent_id, civil_date)
        );
    """)
    return db


# Reduced projection of RebuildAgentRollupsCoreAsync. The production method also
# groups timezone/model and sums other token/cost columns. With one fixed model
# and timezone, those columns cannot change the missing-source-row behavior.
def rebuild_scope(db: sqlite3.Connection, start: str, end: str) -> None:
    params = {"agentId": "codex", "from": start, "to": end}
    db.execute("""
        DELETE FROM daily_usage_rollup
        WHERE agent_id = $agentId AND civil_date BETWEEN $from AND $to
    """, params)
    db.execute("""
        INSERT INTO daily_usage_rollup (agent_id, civil_date, input_tokens)
        SELECT agent_id, civil_date, SUM(input_tokens)
        FROM usage_event
        WHERE agent_id = $agentId AND civil_date BETWEEN $from AND $to
        GROUP BY agent_id, civil_date
    """, params)


class Counterexamples(unittest.TestCase):
    def test_daily_timestamp_does_not_preserve_reset_slices(self) -> None:
        events = [(9, 600), (13, 400)]
        before = sum(tokens for hour, tokens in events if hour < 11)
        after = sum(tokens for hour, tokens in events if hour >= 11)
        synthetic = [(12, sum(tokens for _, tokens in events))]
        synthetic_before = sum(tokens for hour, tokens in synthetic if hour < 11)
        self.assertEqual((before, after), (600, 400))
        self.assertEqual(synthetic_before, 0)

    def test_refresh_timestamp_moves_unchanged_daily_total(self) -> None:
        def select_at_refresh(hour: int) -> int:
            return 1000 if 6 <= hour < 11 else 0
        self.assertEqual(select_at_refresh(10), 1000)
        self.assertEqual(select_at_refresh(12), 0)

    def test_final_meter_is_not_gross_consumption(self) -> None:
        values = [0, 40, 20, 60]
        gross = sum(max(0, right - left) for left, right in zip(values, values[1:]))
        self.assertEqual(gross, 80)
        self.assertEqual(values[-1], 60)
        # This sequence assumes fully known consumption/replenishment and no noise.

    def test_zero_row_supersession_rebuild_erases_retained_summary(self) -> None:
        db = fixture_database()
        try:
            # Ordinary retention has already removed the fine event, but kept its rollup.
            db.execute("INSERT INTO daily_usage_rollup VALUES ('codex', '2025-01-01', 300)")
            candidates = db.execute("SELECT COUNT(*) FROM usage_event").fetchone()[0]
            self.assertEqual(candidates, 0)
            self.assertEqual(db.execute("SELECT SUM(input_tokens) FROM daily_usage_rollup").fetchone()[0], 300)
            rebuild_scope(db, "0001-01-01", "2026-08-02")
            self.assertEqual(db.execute("SELECT COUNT(*) FROM daily_usage_rollup").fetchone()[0], 0)
        finally:
            db.close()

    def test_parser_mismatch_does_not_prove_replacement(self) -> None:
        db = fixture_database()
        try:
            db.execute("INSERT INTO usage_event VALUES ('synthetic-key', 'codex', '2026-07-01', 'codex-jsonl/7', 700)")
            selected = db.execute("""
                SELECT event_key FROM usage_event
                WHERE civil_date < $cutoff AND agent_id = $agentId
                  AND parser_version NOT IN (SELECT value FROM json_each($versions))
            """, {"cutoff": "2026-08-02", "agentId": "codex", "versions": '["codex-jsonl/8"]'}).fetchall()
            self.assertEqual(selected, [("synthetic-key",)])
            self.assertEqual(db.execute("SELECT COUNT(*) FROM usage_event WHERE parser_version = 'codex-jsonl/8'").fetchone()[0], 0)
        finally:
            db.close()

    def test_equal_values_do_not_establish_pool_identity(self) -> None:
        default = {"id": "default-pool", "window": (20, 300, 1000)}
        separate = {"id": "model-pool", "window": (20, 300, 1000)}
        self.assertNotEqual(default["id"], separate["id"])
        self.assertEqual(default["window"], separate["window"])

    def test_capped_credit_details_are_not_inventory(self) -> None:
        official_count, available_detail_rows = 5, 2
        self.assertEqual(min(official_count, available_detail_rows), 2)
        self.assertNotEqual(min(official_count, available_detail_rows), official_count)

    def test_intensity_and_inverse_have_different_percentage_changes(self) -> None:
        tokens = Decimal(1_000_000)
        baseline, current = Decimal(20), Decimal(30)
        intensity_change = 100 * (current / baseline - 1)
        inverse_change = 100 * ((tokens / current) / (tokens / baseline) - 1)
        self.assertEqual(intensity_change, Decimal(50))
        self.assertEqual(inverse_change.quantize(Decimal('0.1')), Decimal('-33.3'))


if __name__ == "__main__":
    print(f"Reduced counterexamples; base={BASE}. NOT C# integration validation.", flush=True)
    unittest.main(verbosity=2)
