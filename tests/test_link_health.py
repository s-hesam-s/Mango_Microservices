import importlib.util
from pathlib import Path
import socket
import tempfile
import unittest
import uuid
import shutil
from contextlib import contextmanager
from unittest.mock import patch, Mock

spec = importlib.util.spec_from_file_location('link_health', Path(__file__).resolve().parents[1] / 'scripts/check_link_health.py')
health = importlib.util.module_from_spec(spec)
spec.loader.exec_module(health)


@contextmanager
def fixtureDirectory():
    # Inherit the test runner's directory ACL on Windows; fixtures contain only
    # public sample Markdown, never credentials or application data.
    root = Path(tempfile.gettempdir()) / ('link-health-' + uuid.uuid4().hex)
    root.mkdir()
    try:
        yield root
    finally:
        shutil.rmtree(root)


class AnchorTests(unittest.TestCase):
    def checkFixture(self, text, other=None):
        with fixtureDirectory() as temp:
            root = Path(temp)
            (root / 'README.md').write_text(text, encoding='utf-8')
            catalog = ['README.md']
            for name, contents in (other or {}).items():
                path = root / name; path.parent.mkdir(parents=True, exist_ok=True)
                path.write_text(contents, encoding='utf-8'); catalog.append(name)
            return health.checkAnchors(root, catalog)

    def test_renamed_heading_breaks_existing_fragment(self):
        self.assertIn('missing anchor', self.checkFixture('# New title\n[Read](#old-title)')['findings'][0])

    def test_duplicate_slug_collisions_are_numbered_in_order(self):
        text = '# Notes\n# Notes\n# Notes-1\n# Notes\n' + '\n'.join(f'[Go](#{x})' for x in ('notes', 'notes-1', 'notes-1-1', 'notes-2'))
        self.assertEqual(self.checkFixture(text)['findings'], [])

    def test_github_formatting_punctuation_spaces_and_unicode(self):
        ids = health.headingIds("# This'll be a _Helpful_ Section!\n# Use `pre_commit` & **café**\n# Two  spaces\n# راهنمای نصب")
        self.assertTrue({'thisll-be-a-helpful-section', 'use-pre_commit--café', 'two--spaces', 'راهنمای-نصب'} <= ids)

    def test_encoded_unicode_and_cross_document_fragments(self):
        self.assertEqual(self.checkFixture('[Go](docs/guide.md#%D9%86%D8%B5%D8%A8)', {'docs/guide.md': '# نصب'})['findings'], [])

    def test_custom_id_and_name_remain_case_sensitive(self):
        text = '<a id="MyAnchor"></a>\n<a name="chapter"></a>\n[Go](#MyAnchor) [Go](#chapter)'
        self.assertEqual(self.checkFixture(text)['findings'], [])
        self.assertEqual(len(self.checkFixture(text + '\n[Wrong](#myanchor)')['findings']), 1)

    def test_custom_anchors_do_not_increment_heading_suffix(self):
        self.assertTrue({'notes', 'notes-1'} <= health.headingIds('<a id="notes"></a>\n# Notes\n# Notes'))

    def test_intraword_underscores_in_identifiers_are_preserved(self):
        self.assertIn('use-pre_commit_id', health.headingIds('# Use `pre_commit_id`'))

    def test_non_anchor_name_attributes_do_not_hide_missing_fragment(self):
        self.assertIn('missing anchor', self.checkFixture('<meta name="missing">\n[Go](#missing)')['findings'][0])

    def test_fenced_indented_and_inline_code_are_not_links(self):
        text = '```md\n[Ignore](#missing)\n# Fake\n```\n    [Ignore](#missing)\n`[Ignore](#missing)`\n# Real\n[Go](#real)'
        result = self.checkFixture(text)
        self.assertEqual(result['anchors'], 1)
        self.assertEqual(result['findings'], [])

    def test_reference_links_and_setext_headings(self):
        text = 'Install\n=======\n[Go][install]\n[install]: #install\n[install]'
        self.assertEqual(self.checkFixture(text)['findings'], [])
        self.assertIn('#install', health.linksIn(text))

    def test_html_fragments_and_directory_readme(self):
        text = '[HTML](page.html#section) [Directory](docs/#chapter)'
        self.assertEqual(self.checkFixture(text, {'page.html': '<div id="section"></div>', 'docs/README.md': '# Chapter'})['findings'], [])

    def test_untracked_or_outside_anchor_destinations_fail(self):
        self.assertIn('not tracked', self.checkFixture('[Go](draft.md#chapter)')['findings'][0])
        self.assertIn('leaves repository', self.checkFixture('[Go](../outside.md#chapter)')['findings'][0])

    def test_external_fragments_are_not_claimed_as_checked(self):
        result = self.checkFixture('[External](https://example.com/#missing)')
        self.assertEqual(result['anchors'], 0)
        self.assertEqual(result['findings'], [])

    def test_balanced_parentheses_html_entities_and_autolinks(self):
        links = health.linksIn('[Read](https://example.com/a_(b)) <https://example.org/help>\n<a href="https://example.net/?a=1&amp;b=2">Go</a>')
        self.assertIn('https://example.com/a_(b)', links)
        self.assertIn('https://example.net/?a=1&b=2', links)
        self.assertIn('https://example.org/help', links)


class NetworkPolicyTests(unittest.TestCase):
    def answers(self, *answers):
        calls = []
        def request(url, method):
            calls.append((url, method))
            return answers[len(calls) - 1]
        return request, calls

    def test_head_405_falls_back_to_bounded_get(self):
        request, calls = self.answers((405, None), (200, None))
        self.assertEqual(health.probe('https://example.com/', request)['state'], 'ok')
        self.assertEqual([c[1] for c in calls], ['HEAD', 'GET'])

    def test_head_404_is_confirmed_with_get_before_failure(self):
        request, _ = self.answers((404, None), (200, None))
        self.assertEqual(health.probe('https://example.com/', request)['state'], 'ok')
        request, _ = self.answers((404, None), (404, None))
        self.assertEqual(health.probe('https://example.com/', request)['state'], 'broken')

    def test_gone_is_confirmed_with_get(self):
        request, _ = self.answers((410, None), (410, None))
        self.assertEqual(health.probe('https://example.com/', request)['state'], 'broken')

    def test_rate_limits_server_errors_and_auth_are_review_not_healthy(self):
        for code in (401, 403, 408, 429, 500, 502, 503):
            with self.subTest(code=code):
                request, _ = self.answers((code, None))
                self.assertEqual(health.probe('https://example.com/', request)['state'], 'review')

    def test_timeouts_and_tls_failures_are_review(self):
        request = Mock(side_effect=socket.timeout())
        self.assertEqual(health.probe('https://example.com/', request)['state'], 'review')

    def test_redirect_is_revalidated_and_downgrade_is_blocked(self):
        request, calls = self.answers((302, 'http://example.com/'))
        self.assertEqual(health.probe('https://example.com/', request)['state'], 'review')
        self.assertEqual(len(calls), 1)
        request, calls = self.answers((302, 'https://localhost/'))
        self.assertEqual(health.probe('https://example.com/', request)['state'], 'skipped')
        self.assertEqual(len(calls), 1)

    def test_relative_redirects_and_loops(self):
        request, calls = self.answers((301, '/new'), (200, None))
        self.assertEqual(health.probe('https://example.com/old', request)['state'], 'ok')
        self.assertEqual(calls[-1][0], 'https://example.com/new')
        request, _ = self.answers((302, '/'))
        self.assertEqual(health.probe('https://example.com/', request)['state'], 'review')

    def test_credentials_unusual_ports_and_control_characters_never_request(self):
        for url in ('https://user:sample@example.com/', 'https://example.com:8443/', 'https://example.com/?api_key=sample', 'https://example.com/?%74oken=sample', 'https://example.com/\nheader'):
            with self.subTest(url=health.safeDisplay(url)):
                request = Mock()
                self.assertEqual(health.probe(url, request)['state'], 'skipped')
                request.assert_not_called()

    def test_all_dns_answers_must_be_public_including_ipv6(self):
        for ip in ('127.0.0.1', '10.0.0.1', '169.254.169.254', '::1', '::ffff:127.0.0.1'):
            with self.subTest(ip=ip):
                resolver = Mock(return_value=[(0, 0, 0, '', ('93.184.216.34', 443)), (0, 0, 0, '', (ip, 443))])
                with self.assertRaises(health.UnsafeDestination):
                    health.publicAddresses('example.com', 443, resolver)

    def test_transport_connects_to_validated_ip_without_credentials(self):
        sock = Mock()
        response = Mock(status=200); response.getheader.return_value = None
        connection = Mock(); connection.getresponse.return_value = response
        with patch.object(health, 'publicAddresses', return_value=['93.184.216.34']), patch.object(health.socket, 'create_connection', return_value=sock) as create, patch.object(health.http.client, 'HTTPConnection', return_value=connection):
            self.assertEqual(health.requestPublic('http://example.com/help', 'GET'), (200, None))
            create.assert_called_once_with(('93.184.216.34', 80), timeout=6)
            headers = connection.request.call_args.kwargs['headers']
            self.assertEqual(headers['Range'], 'bytes=0-4095')
            self.assertFalse({'Authorization', 'Cookie'} & set(headers))
            connection.close.assert_called_once()

    def test_https_retains_default_verification_and_sni(self):
        connection = Mock(); connection.getresponse.return_value.status = 200
        context = Mock()
        with patch.object(health, 'publicAddresses', return_value=['93.184.216.34']), patch.object(health.socket, 'create_connection') as create, patch.object(health.ssl, 'create_default_context', return_value=context) as default, patch.object(health.http.client, 'HTTPConnection', return_value=connection):
            health.requestPublic('https://example.com/help', 'HEAD')
            default.assert_called_once_with()
            context.wrap_socket.assert_called_once_with(create.return_value, server_hostname='example.com')

    def test_reports_do_not_include_query_or_userinfo(self):
        value = health.safeDisplay('https://user:sample@example.com/path?token=sample#section')
        self.assertEqual(value, 'https://example.com/path')


if __name__ == '__main__':
    unittest.main()
