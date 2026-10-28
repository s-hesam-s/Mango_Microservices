"""Check tracked Markdown file destinations without fetching external services."""

from html.parser import HTMLParser
from pathlib import Path
import re
import subprocess
import sys
from urllib.parse import unquote, urlsplit


class HtmlLinks(HTMLParser):
    def __init__(self):
        super().__init__()
        self.targets = []

    def handle_starttag(self, tag, attrs):
        attributes = dict(attrs)
        self.targets.extend(attributes[key] for key in ('src', 'href') if attributes.get(key))
        self.targets.extend(part.strip().split()[0] for part in attributes.get('srcset', '').split(',') if part.strip())


def check(root, tracked=None):
    root = root.resolve()
    if tracked is None:
        tracked = subprocess.check_output(['git', '--no-optional-locks', 'ls-files', '-z'], cwd=root).decode('utf-8').split('\0')
    catalog = set(filter(None, tracked))
    findings = []
    documents = references = 0
    for name in sorted(catalog):
        if not name.endswith('.md') or any(part in name.lower().split('/') for part in ('vendor', 'wwwroot', 'node_modules')):
            continue
        documents += 1
        document = root / name
        text = (document.read_text(encoding='utf-8-sig') if document.is_file() else
                subprocess.check_output(['git', '--no-optional-locks', 'show', 'HEAD:' + name], cwd=root).decode('utf-8-sig'))
        text = re.sub(r'```.*?```|~~~.*?~~~', '', text, flags=re.S)
        parser = HtmlLinks()
        parser.feed(text)
        targets = parser.targets + [m[1].strip('<>') for m in re.finditer(r'!?\[[^\]\n]*\]\(\s*(<[^>]+>|[^\s)]+)(?:\s+["\'][^\n]*?["\'])?\s*\)', text)]
        for target in targets:
            parsed = urlsplit(target)
            if parsed.scheme in ('https', 'http', 'mailto') or parsed.netloc:
                continue
            if parsed.scheme:
                findings.append(f'{name}: unsupported destination scheme')
                continue
            if not parsed.path:
                continue
            references += 1
            value = unquote(parsed.path)
            absolute = (root / value.lstrip('/')) if value.startswith('/') else (root / name).parent / value
            try:
                relative = absolute.resolve().relative_to(root).as_posix()
            except ValueError:
                findings.append(f'{name}: destination leaves repository: {target}')
                continue
            if relative not in catalog and not any(file.startswith(relative.rstrip('/') + '/') for file in catalog):
                findings.append(f'{name}: missing tracked destination: {target}')
    return {'documents': documents, 'references': references, 'findings': findings}


if __name__ == '__main__':
    result = check(Path(__file__).resolve().parents[1])
    print(f"{result['documents']} documents; {result['references']} local file references checked.")
    for finding in result['findings']:
        print(finding, file=sys.stderr)
    raise SystemExit(bool(result['findings']))
