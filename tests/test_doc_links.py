import importlib.util
from pathlib import Path
import tempfile
import unittest

spec = importlib.util.spec_from_file_location('doc_check', Path(__file__).resolve().parents[1] / 'scripts/check_doc_links.py')
check = importlib.util.module_from_spec(spec)
spec.loader.exec_module(check)


class DocumentLinkTests(unittest.TestCase):
    def test_moved_historical_document_requires_parent_path(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            (root / 'docs/history').mkdir(parents=True)
            (root / 'docs/current.md').write_text('# Current', encoding='utf-8')
            old = root / 'docs/history/old.md'
            files = ['docs/history/old.md', 'docs/current.md']
            old.write_text('[Current](current.md)', encoding='utf-8')
            self.assertEqual(len(check.check(root, files)['findings']), 1)
            old.write_text('[Current](../current.md#chapter)', encoding='utf-8')
            self.assertEqual(check.check(root, files)['findings'], [])

    def test_destinations_cannot_leave_repository(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            (root / 'README.md').write_text('[Outside](../outside.md)', encoding='utf-8')
            self.assertIn('leaves repository', check.check(root, ['README.md'])['findings'][0])

    def test_missing_srcset_variant_is_detected(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            (root / 'README.md').write_text('<picture><source srcset="mobile.webp 1x, retina.webp 2x"/></picture>', encoding='utf-8')
            result = check.check(root, ['README.md', 'mobile.webp'])
            self.assertEqual(len(result['findings']), 1)
            self.assertIn('retina.webp', result['findings'][0])

    def test_external_urls_and_code_examples_do_not_fetch_or_fail(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            (root / 'README.md').write_text('[Site](https://example.invalid/)\n```md\n[Example](missing.md)\n```', encoding='utf-8')
            self.assertEqual(check.check(root, ['README.md'])['findings'], [])

    def test_untracked_destination_cannot_hide_broken_published_link(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            (root / 'README.md').write_text('[Draft](draft.md)', encoding='utf-8')
            (root / 'draft.md').write_text('Local only', encoding='utf-8')
            self.assertEqual(len(check.check(root, ['README.md'])['findings']), 1)


if __name__ == '__main__':
    unittest.main()
