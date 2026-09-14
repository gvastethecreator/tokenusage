#!/usr/bin/env python3
"""Validate this documentation integration. Does not execute product checks."""
from __future__ import annotations
import hashlib
import json
import re
import sys
import zipfile
from datetime import datetime, timezone
from pathlib import Path
from typing import Callable
import build_reader

ROOT = Path(__file__).resolve().parents[1]


def main() -> int:
    results: list[dict] = []
    def check(name: str, action: Callable[[], object]) -> None:
        try:
            if action() is False:
                raise AssertionError('Predicate returned false')
            results.append({'name': name, 'status': 'passed'})
        except Exception as exc:
            results.append({'name': name, 'status': 'failed', 'detail': f'{type(exc).__name__}: {exc}'})
    load = lambda p: json.loads((ROOT / p).read_text(encoding='utf-8'))
    def check_archive(record: dict) -> bool:
        p = ROOT / record['path']
        data = p.read_bytes()
        assert len(data) == record['bytes'] and hashlib.sha256(data).hexdigest() == record['sha256']
        with zipfile.ZipFile(p) as archive:
            assert archive.testzip() is None
            assert set(archive.namelist()) == {e['path'] for e in record['entries']}
            for item in record['entries']:
                value = archive.read(item['path'])
                assert len(value) == item['bytes']
                assert hashlib.sha256(value).hexdigest() == item['sha256']
        return True
    for a in load('validation/source-archives.json')['archives']:
        check('Complete original archive: ' + a['path'], lambda item=a: check_archive(item))
    def preserved_phases() -> bool:
        with zipfile.ZipFile(ROOT / 'archive/engineering-v2-original.zip') as archive:
            for p in (ROOT / 'docs/phases').glob('*.md'):
                assert p.read_bytes() == archive.read('TokenUsage-Reports-Engineering/' + p.relative_to(ROOT).as_posix())
        return True
    check('All five phase documents preserved byte-for-byte', preserved_phases)
    check('Generated reader matches source', lambda: build_reader.build() == (ROOT / 'index.html').read_text(encoding='utf-8'))
    check('Reader generation is deterministic', lambda: build_reader.build() == build_reader.build())
    check('Registry contains all current specifications', lambda: {d['path'] for d in build_reader.registry() if d['id'] != 'rfc-original'} == {'README.md', *[p.relative_to(ROOT).as_posix() for p in (ROOT / 'docs').rglob('*.md')]})
    def check_additional_traceability() -> bool:
        data = load('validation/integration-traceability.json')
        spec = (ROOT / 'docs/operations/12-REVIEW-AND-ACCEPTANCE.md').read_text(encoding='utf-8')
        assert len(data['requirements']) == 12
        assert {r['requirement'] for r in data['requirements']} == set(re.findall(r'REQ-INT-\d{2}', spec))
        for row in data['requirements']:
            assert row['planned_test'] in spec
            assert row['status'] == 'planned_not_executed_in_product'
        native = (ROOT / 'docs/operations/13-NATIVE-SQLITE-GATE.md').read_text(encoding='utf-8')
        assert len(data['native_sqlite_tests']) == 6
        for row in data['native_sqlite_tests']:
            assert row['test'] in native and row['status'] == 'planned_not_executed_in_product'
        return True
    check('Additional requirements and SQLite tests remain planned', check_additional_traceability)
    def check_payload() -> bool:
        value = (ROOT / 'index.html').read_text(encoding='utf-8')
        match = re.search(r'<script[^>]*id="documents"[^>]*>(.*?)</script>', value, re.S)
        assert match is not None
        docs = json.loads(match.group(1))
        assert len(docs) == len(build_reader.registry())
        for doc in docs:
            assert doc['markdown'] == (ROOT / doc['path']).read_text(encoding='utf-8')
            assert '<script' not in doc['html'].lower()
            for h in doc['toc']:
                assert f'id="{h["id"]}"' in doc['html']
        return True
    check('Embedded Markdown and section anchors match source', check_payload)
    # Negative renderer test on the parser configuration, without reading user content.
    def escaped_html() -> bool:
        from markdown_it import MarkdownIt
        output = MarkdownIt('commonmark', {'html': False}).render('<script>alert("SYNTHETIC")</script>')
        return '<script>' not in output and '&lt;script&gt;' in output
    check('Raw script examples are escaped by Markdown renderer', escaped_html)
    check('No active browser runner dependency', lambda: 'playwright' not in (ROOT / 'tools/requirements-docs.txt').read_text(encoding='utf-8').lower())
    check('No sandbox or local execution paths in active specs', lambda: all('sandbox:/mnt/data/' not in p.read_text(encoding='utf-8') and '/mnt/data/' not in p.read_text(encoding='utf-8') for p in (ROOT / 'docs').rglob('*.md')))
    check('Historical validations preserved separately', lambda: all((ROOT / 'validation/historical-v2' / name).exists() for name in ['pack-validation.json', 'reader-validation.json', 'manifest.json', 'reader-desktop.png', 'reader-mobile.png']))
    check('Prototype is explicitly synthetic', lambda: any(word in (ROOT / 'prototypes/model-explorer-lab.html').read_text(encoding='utf-8').lower() for word in ['sintétic','fictici','synthetic','sample']))
    # Manifest checking is separate from product provenance; a hash is not a signature.
    def integrity() -> bool:
        data = load('manifest.json')
        declared = {row['path'] for row in data['files']}
        excluded = {'manifest.json', 'validation/integration-validation.json', 'validation/pack-validation.json', 'validation/reader-validation.json'}
        actual = {p.relative_to(ROOT).as_posix() for p in ROOT.rglob('*') if p.is_file() and '__pycache__' not in p.parts and '.venv-docs' not in p.parts and p.relative_to(ROOT).as_posix() not in excluded}
        assert actual == declared
        for row in data['files']:
            payload = (ROOT / row['path']).read_bytes()
            assert len(payload) == row['bytes'] and hashlib.sha256(payload).hexdigest() == row['sha256'], row['path']
        return True
    check('Current file manifest covers exact bytes', integrity)
    summary = {'passed': sum(x['status'] == 'passed' for x in results), 'failed': sum(x['status'] == 'failed' for x in results)}
    report = {'scope': 'local documentation integration only', 'createdAtUtc': datetime.now(timezone.utc).isoformat(), 'summary': summary, 'checks': results, 'remotePublication': 'local_complete_pr62_partial_pending_authorization', 'notValidated': ['updating issue #61 or PR #62', 'C# tests or build', 'WinUI runtime', 'native SQLite binaries in TokenUsage', 'providers', 'product CI', 'product migrations and concurrency']}
    (ROOT / 'validation/integration-validation.json').write_text(json.dumps(report, ensure_ascii=False, indent=2)+'\n', encoding='utf-8')
    print(json.dumps(summary))
    for row in results:
        if row['status'] != 'passed': print(row)
    return int(summary['failed'] != 0)

if __name__ == '__main__':
    if not __debug__:
        raise SystemExit('Run without Python optimization; validation assertions must remain enabled.')
    raise SystemExit(main())
