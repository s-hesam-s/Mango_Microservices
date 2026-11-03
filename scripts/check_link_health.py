"""Offline Markdown anchors and bounded, unauthenticated public URL monitoring."""

import argparse
from concurrent.futures import ThreadPoolExecutor, as_completed
import html
from html.parser import HTMLParser
import http.client
import ipaddress
import json
import os
from pathlib import Path
import re
import socket
import ssl
import subprocess
import time
import unicodedata
from urllib.parse import parse_qsl, unquote, urljoin, urlsplit, urlunsplit


class HtmlReferences(HTMLParser):
    def __init__(self):
        super().__init__()
        self.links = []
        self.ids = set()

    def handle_starttag(self, tag, attrs):
        attrs = dict(attrs)
        self.links.extend(attrs[k] for k in ('href', 'src') if attrs.get(k))
        self.links.extend(x.strip().split()[0] for x in attrs.get('srcset', '').split(',') if x.strip())
        self.ids.update(attrs[k] for k in ('id', 'name') if attrs.get(k) and (k == 'id' or tag == 'a'))


def visibleMarkdown(text):
    text = re.sub(r'<!--.*?-->|<pre\b[^>]*>.*?</pre>|<code\b[^>]*>.*?</code>', '', text, flags=re.S | re.I)
    result, fence = [], None
    for line in text.splitlines():
        marker = re.match(r'^ {0,3}(`{3,}|~{3,})(.*)$', line)
        if fence:
            if marker and marker[1][0] == fence[0] and len(marker[1]) >= len(fence) and not marker[2].strip():
                fence = None
            result.append('')
        elif marker:
            fence = marker[1]
            result.append('')
        else:
            result.append('' if line.startswith(('    ', '\t')) else line)
    return '\n'.join(result)


def linksIn(text):
    text = re.sub(r'(`+).*?\1', '', visibleMarkdown(text))
    parser = HtmlReferences()
    parser.feed(text)
    links = list(parser.links)
    destination = r'(<[^>\n]+>|(?:\\.|[^\s()\\]|\([^()\n]*\))+)'
    links.extend(m[1].strip('<>') for m in re.finditer(r'!?\[[^\]\n]*\]\(\s*' + destination + r'(?:\s+["\'][^\n]*?["\'])?\s*\)', text))
    definitions = {m[1].strip().casefold(): m[2].strip('<>') for m in re.finditer(r'^ {0,3}\[([^\]\n]+)\]:\s*' + destination, text, re.M)}
    for m in re.finditer(r'!?\[([^\]\n]+)\](?:\[([^\]\n]*)\])?', text):
        key = (m[2] or m[1]).strip().casefold()
        if key in definitions:
            links.append(definitions[key])
    links.extend(m[1] for m in re.finditer(r'<(https?://[^<>\s]+)>', text))
    return sorted({html.unescape(re.sub(r'\\([()])', r'\1', x)) for x in links})


def headingIds(text):
    text = visibleMarkdown(text)
    parser = HtmlReferences()
    parser.feed(text)
    ids, generated = set(parser.ids), set()
    lines = text.splitlines()
    headings = []
    for i, line in enumerate(lines):
        atx = re.match(r'^ {0,3}#{1,6}\s+(.+?)(?:\s+#+\s*)?$', line)
        if atx:
            headings.append(atx[1])
        elif i and re.match(r'^ {0,3}(?:=+|-+)\s*$', line) and lines[i - 1].strip() and not lines[i - 1].lstrip().startswith(('#', '<', '-', '|')):
            headings.append(lines[i - 1].strip())
    for title in headings:
        title = re.sub(r'!?\[([^\]]*)\]\([^)]*\)', r'\1', title)
        title = re.sub(r'<[^>]+>', '', title)
        title = re.sub(r'(`+)(.*?)\1', r'\2', title)
        title = re.sub(r'(\*{1,3}|~~)(.+?)\1', r'\2', title)
        title = re.sub(r'(?<!\w)(_{1,3})(.+?)\1(?!\w)', r'\2', title)
        title = html.unescape(title).strip().lower()
        slug = ''.join('-' if c == ' ' else c for c in title if c == ' ' or c in '-_' or (not c.isspace() and unicodedata.category(c)[0] not in 'PSC' and c not in '\ufe0f\ufe0e'))
        unique, number = slug, 0
        while unique in generated:
            number += 1
            unique = f'{slug}-{number}'
        generated.add(unique)
        ids.add(unique)
    return ids | {'user-content-' + x for x in parser.ids}


def trackedFiles(root):
    return set(filter(None, subprocess.check_output(['git', '--no-optional-locks', '-c', 'core.fsmonitor=false', 'ls-files', '-z'], cwd=root).decode('utf-8').split('\0')))


def readTracked(root, name):
    path = root / name
    return path.read_text(encoding='utf-8-sig') if path.is_file() else subprocess.check_output(['git', '--no-optional-locks', 'show', 'HEAD:' + name], cwd=root).decode('utf-8-sig')


def documents(root, catalog):
    return {name: readTracked(root, name) for name in sorted(catalog) if name.lower().endswith('.md') and not {'vendor', 'wwwroot', 'node_modules'} & set(name.lower().split('/'))}


def checkAnchors(root, catalog=None):
    root = Path(root).resolve()
    catalog = trackedFiles(root) if catalog is None else set(catalog)
    docs = documents(root, catalog)
    findings, skipped, count = [], [], 0
    cache = {}
    for source, text in docs.items():
        for target in linksIn(text):
            parsed = urlsplit(target)
            if parsed.scheme or parsed.netloc or not parsed.fragment:
                continue
            count += 1
            fragment = unquote(parsed.fragment)
            path = unquote(parsed.path)
            absolute = root / path.lstrip('/') if path.startswith('/') else (root / source).parent / path if path else root / source
            try:
                destination = absolute.resolve().relative_to(root).as_posix()
            except ValueError:
                findings.append(f'{source}: anchor destination leaves repository')
                continue
            if destination not in catalog:
                destination = next((destination.rstrip('/') + '/' + n for n in ('README.md', 'readme.md') if destination.rstrip('/') + '/' + n in catalog), destination)
            if destination not in catalog:
                findings.append(f'{source}: anchor destination is not tracked: {destination}')
                continue
            if fragment == 'top':
                continue
            if not destination.lower().endswith(('.md', '.html', '.htm')):
                skipped.append(f'{source}: non-document fragment requires manual review')
                continue
            if destination not in cache:
                content = docs.get(destination) or readTracked(root, destination)
                if destination.lower().endswith('.md'):
                    cache[destination] = headingIds(content)
                else:
                    parser = HtmlReferences(); parser.feed(content); cache[destination] = parser.ids
            if fragment not in cache[destination]:
                findings.append(f'{source}: missing anchor {destination}#{fragment}')
    return {'documents': len(docs), 'anchors': count, 'findings': findings, 'manualReview': skipped}


class UnsafeDestination(ValueError):
    pass


def publicAddresses(host, port, resolver=socket.getaddrinfo):
    addresses = resolver(host, port, type=socket.SOCK_STREAM)
    ips = list(dict.fromkeys(item[4][0] for item in addresses))
    if not ips or any(not ipaddress.ip_address(ip).is_global for ip in ips):
        raise UnsafeDestination('non-public address')
    return ips


def validateUrl(url):
    if any(ord(c) < 32 or ord(c) == 127 for c in url):
        raise UnsafeDestination('control character')
    try:
        p = urlsplit(url)
        if p.scheme not in ('http', 'https') or not p.hostname or p.username is not None or p.password is not None:
            raise UnsafeDestination('unsupported URL or embedded credentials')
        if p.port not in (None, 80 if p.scheme == 'http' else 443):
            raise UnsafeDestination('non-standard port')
        if any(re.search(r'(?i)token|secret|password|signature|api[_-]?key|credential|authorization', key) for key, _ in parse_qsl(p.query)):
            raise UnsafeDestination('credential-like query')
        if p.hostname.lower().rstrip('.') in ('localhost', 'metadata.google.internal') or p.hostname.lower().rstrip('.').endswith(('.localhost', '.local', '.internal', '.invalid', '.example', '.test')):
            raise UnsafeDestination('local or example host')
    except ValueError as error:
        raise UnsafeDestination('invalid or unsupported URL') from error
    return p


def safeDisplay(url):
    try:
        p = urlsplit(url)
        return re.sub(r'[\x00-\x20\x7f]', '', urlunsplit((p.scheme, p.hostname or '', p.path, '', '')))[:300]
    except ValueError:
        return '(invalid URL)'


def requestPublic(url, method, timeout=6):
    p = validateUrl(url)
    port = p.port or (443 if p.scheme == 'https' else 80)
    # Connect to the validated numeric IP, retaining hostname verification and SNI.
    # There is no second DNS lookup between validation and socket creation.
    ips = publicAddresses(p.hostname, port)
    sock = socket.create_connection((ips[0], port), timeout=timeout)
    connection = None
    try:
        if p.scheme == 'https':
            sock = ssl.create_default_context().wrap_socket(sock, server_hostname=p.hostname)
        connection = http.client.HTTPConnection(p.hostname, port, timeout=timeout)
        connection.sock = sock
        path = urlunsplit(('', '', p.path or '/', p.query, ''))
        headers = {'User-Agent': 'PortfolioLinkMonitor/1.0', 'Accept': '*/*'}
        if method == 'GET':
            headers['Range'] = 'bytes=0-4095'
        connection.request(method, path, headers=headers)
        response = connection.getresponse()
        return response.status, response.getheader('Location')
    finally:
        if connection:
            connection.close()
        else:
            sock.close()


def probe(url, request=requestPublic):
    current, seen, method = url, set(), 'HEAD'
    for _ in range(8):
        try:
            validateUrl(current)
            key = (current, method)
            if key in seen:
                return {'state': 'review', 'reason': 'redirect loop'}
            seen.add(key)
            status, location = request(current, method)
            if status in (301, 302, 303, 307, 308):
                if not location:
                    return {'state': 'review', 'reason': 'redirect without destination'}
                destination = urljoin(current, location)
                validateUrl(destination)
                if urlsplit(current).scheme == 'https' and urlsplit(destination).scheme != 'https':
                    return {'state': 'review', 'reason': 'HTTPS downgrade blocked'}
                current = destination
                continue
            if method == 'HEAD' and status in (404, 405, 410, 501):
                method = 'GET'
                continue
            if status in (404, 410):
                return {'state': 'broken', 'reason': f'confirmed HTTP {status}'}
            if 200 <= status < 300:
                return {'state': 'ok', 'reason': f'HTTP {status}'}
            return {'state': 'review', 'reason': f'HTTP {status}; access restriction or temporary response'}
        except UnsafeDestination:
            return {'state': 'skipped', 'reason': 'unsafe, private, credential-bearing or example URL blocked'}
        except (OSError, ValueError, http.client.HTTPException):
            return {'state': 'review', 'reason': 'network, DNS or TLS error; retry or manual review required'}
    return {'state': 'review', 'reason': 'redirect limit exceeded'}


def externalInventory(root, catalog=None):
    root = Path(root).resolve()
    catalog = trackedFiles(root) if catalog is None else set(catalog)
    inventory = {}
    for source, text in documents(root, catalog).items():
        for target in linksIn(text):
            p = urlsplit(target)
            if p.scheme not in ('http', 'https'):
                continue
            url = urlunsplit((p.scheme, p.netloc, p.path, p.query, ''))
            inventory.setdefault(url, []).append(source)
    return inventory


def checkExternal(root, budget=240, limit=200):
    inventory = externalInventory(root)
    started = time.monotonic()
    results = []
    def inspect(url):
        if time.monotonic() - started >= budget:
            return {'state': 'review', 'reason': 'run time budget reached'}
        return probe(url)
    urls = list(inventory)
    with ThreadPoolExecutor(max_workers=3) as pool:
        pending = {pool.submit(inspect, url): url for url in urls[:limit]}
        for future in as_completed(pending):
            url = pending[future]
            results.append({'url': safeDisplay(url), 'documents': inventory[url], **future.result()})
    results.extend({'url': safeDisplay(url), 'documents': inventory[url], 'state': 'review', 'reason': 'URL count limit reached'} for url in urls[limit:])
    return sorted(results, key=lambda x: x['url'])


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--external', action='store_true', help='Network mode; scheduled/manual workflow only')
    parser.add_argument('--root', type=Path, default=Path(__file__).resolve().parents[1])
    args = parser.parse_args()
    if not args.external:
        result = checkAnchors(args.root)
        print(f"{result['documents']} documents; {result['anchors']} local fragments checked.")
        for finding in result['findings'] + result['manualReview']:
            print(finding)
        return bool(result['findings'])
    results = checkExternal(args.root)
    counts = {state: sum(x['state'] == state for x in results) for state in ('ok', 'broken', 'review', 'skipped')}
    print(json.dumps(counts, sort_keys=True))
    for item in results:
        if item['state'] != 'ok':
            print(f"{item['state']}: {item['url']} - {item['reason']}")
    summary = os.environ.get('GITHUB_STEP_SUMMARY')
    if summary:
        with open(summary, 'a', encoding='utf-8') as stream:
            stream.write('## Weekly external link health\n\n' + str(counts) + '\n\n')
            stream.write('Only confirmed GET 404/410 responses fail this scheduled check. Review and skipped results are not proof of a healthy destination. No credentials or cookies are sent. External fragments are not checked.\n\n')
            for item in results:
                if item['state'] != 'ok':
                    stream.write('- ' + html.escape(item['state'] + ': ' + item['url'] + ' - ' + item['reason']) + '\n')
    return bool(counts['broken'])


if __name__ == '__main__':
    raise SystemExit(main())
