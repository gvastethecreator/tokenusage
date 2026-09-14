#!/usr/bin/env python3
"""Build the documentation reader; never reads provider data or contacts a network."""
from __future__ import annotations
import argparse
import html
import json
import posixpath
import re
import sys
import unicodedata
from html.parser import HTMLParser
from pathlib import Path
from urllib.parse import unquote, urlsplit

ROOT = Path(__file__).resolve().parents[1]

class TextOnly(HTMLParser):
    def __init__(self) -> None:
        super().__init__(convert_charrefs=True)
        self.parts: list[str] = []
    def handle_data(self, data: str) -> None:
        self.parts.append(data)


def slug(text: str) -> str:
    text = unicodedata.normalize('NFKD', text)
    text = ''.join(c for c in text if not unicodedata.combining(c)).lower()
    text = re.sub(r'[^\w\s-]', '', text, flags=re.UNICODE)
    return re.sub(r'[-\s]+', '-', text).strip('-') or 'section'


def registry() -> list[dict[str, str]]:
    entries = json.loads((ROOT / 'tools/reader-documents.json').read_text(encoding='utf-8'))
    if not isinstance(entries, list) or not entries:
        raise ValueError('The reader registry must be a nonempty list')
    for field in ('id', 'path'):
        values = [d[field] for d in entries]
        if len(values) != len(set(values)):
            raise ValueError(f'Duplicate registry {field}')
    for d in entries:
        p = (ROOT / d['path']).resolve()
        if not p.is_relative_to(ROOT.resolve()) or not p.is_file():
            raise ValueError(f'Invalid document path: {d["path"]}')
    return entries


def render_document(entry: dict[str, str], entries: list[dict[str, str]]) -> dict:
    try:
        from markdown_it import MarkdownIt
    except ImportError as exc:
        raise RuntimeError('Optional markdown-it-py is missing; see tools/requirements-docs.txt') from exc
    markdown = (ROOT / entry['path']).read_text(encoding='utf-8')
    parser = MarkdownIt('commonmark', {'html': False, 'linkify': False}).enable('table')
    tokens = parser.parse(markdown)
    paths = {d['path']: d['id'] for d in entries}
    toc: list[dict] = []
    seen: dict[str, int] = {}
    for index, token in enumerate(tokens):
        if token.type == 'heading_open':
            inline = tokens[index + 1]
            title = ''.join(c.content for c in (inline.children or []) if c.type in {'text', 'code_inline'}) or inline.content
            base = slug(title)
            seen[base] = seen.get(base, 0) + 1
            unique = base + (f'-{seen[base]}' if seen[base] > 1 else '')
            anchor = entry['id'] + '--' + unique
            token.attrSet('id', anchor)
            if token.tag in ('h2', 'h3'):
                toc.append({'id': anchor, 'text': title, 'level': int(token.tag[1])})
        for child in token.children or []:
            if child.type != 'link_open':
                continue
            target = child.attrGet('href') or ''
            parts = urlsplit(target)
            if parts.scheme or parts.netloc:
                continue
            path = posixpath.normpath(posixpath.join(posixpath.dirname(entry['path']), unquote(parts.path))) if parts.path else entry['path']
            if path in paths:
                destination = paths[path]
                href = '#' + destination
                if parts.fragment:
                    href += '/' + destination + '--' + slug(unquote(parts.fragment))
                child.attrSet('href', href)
            elif parts.path:
                # Non-document assets are resolved relative to reader root, not Markdown directory.
                if path.startswith('../'):
                    child.attrSet('href', path)
                elif (ROOT / path).is_file():
                    child.attrSet('href', path + (('#' + parts.fragment) if parts.fragment else ''))
                elif entry['id'] == 'rfc-original':
                    child.attrSet('href', 'archive/initial-plan-original.zip')
                    child.attrSet('title', 'Compañero histórico conservado en el ZIP original')
    rendered = parser.renderer.render(tokens, parser.options, {})
    rendered = rendered.replace('<table>', '<div class="table-wrap" tabindex="0" role="region" aria-label="Tabla del documento"><table>').replace('</table>', '</table></div>')
    rendered = rendered.replace('<pre>', '<pre tabindex="0">')
    text = TextOnly(); text.feed(rendered)
    return {**entry, 'html': rendered, 'markdown': markdown, 'text': ' '.join(' '.join(text.parts).split()), 'toc': toc, 'words': len(markdown.split())}


def build() -> str:
    entries = registry()
    documents = [render_document(d, entries) for d in entries]
    payload = json.dumps(documents, ensure_ascii=False, separators=(',', ':'))
    # Keep all content inside the inert JSON element, even with adversarial examples.
    payload = payload.replace('<', '\\u003c').replace('>', '\\u003e').replace('&', '\\u0026').replace('\u2028', '\\u2028').replace('\u2029', '\\u2029')
    template = (ROOT / 'tools/reader.template.html').read_text(encoding='utf-8')
    if template.count('__DOCUMENTS_JSON__') != 1:
        raise ValueError('Expected exactly one documents placeholder')
    return template.replace('__DOCUMENTS_JSON__', payload).replace('__CURRENT_DOCUMENTS__', str(sum(d['id'] != 'rfc-original' for d in entries)))


def main() -> int:
    cli = argparse.ArgumentParser(description=__doc__)
    cli.add_argument('--check', action='store_true', help='Fail on generated-file drift without writing')
    args = cli.parse_args()
    try:
        result = build()
        target = ROOT / 'index.html'
        if args.check:
            if not target.exists() or target.read_text(encoding='utf-8') != result:
                print('Reader is out of date. Run tools/build_reader.py.', file=sys.stderr)
                return 1
            print('Reader matches all source documents; no files written.')
        else:
            with target.open('w', encoding='utf-8', newline='\n') as stream:
                stream.write(result)
            print(f'Built index.html from {len(registry())} registered documents (including archive).')
        return 0
    except (ValueError, OSError, RuntimeError) as exc:
        print(f'Documentation build failed: {exc}', file=sys.stderr)
        return 2

if __name__ == '__main__':
    raise SystemExit(main())
