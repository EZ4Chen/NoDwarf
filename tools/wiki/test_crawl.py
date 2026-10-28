import unittest
from contextlib import redirect_stdout
import io
import json
from pathlib import Path
from tempfile import TemporaryDirectory
from types import SimpleNamespace
from unittest.mock import patch

from crawl import Article, SafeRedirect, article_url, run
from urllib.error import HTTPError
from urllib.request import Request


class ReferenceCrawlerTests(unittest.TestCase):
    def test_article_keeps_cjk_tables_and_links_but_excludes_navigation(self):
        page = Article()
        page.feed('''<html><head><title>医疗 &amp; 护理</title>
          <link rel="license" href="/wiki/Copyrights"/></head><body>
          <nav>NOT ARTICLE</nav><div id="mw-content-text">
          <div class="mw-parser-output"><h2>照料</h2><p>给<a href="/wiki/食物">食物</a>。
          <br/>无需医生。</p><script>BAD SCRIPT</script><span class="mw-editsection">EDIT</span>
          <table><tr><th>物资</th><th>数量</th></tr><tr><td>绷带</td><td>2</td></tr></table>
          </div></div><footer>NOT ARTICLE<a href="https://creativecommons.org/licenses/by-sa/4.0/">许可</a></footer>
          </body></html>''')
        self.assertTrue(page.found)
        self.assertIn("给食物。", page.text)
        self.assertIn("无需医生。", page.text)
        self.assertIn("绷带 | 2", page.text)
        for text in ["NOT ARTICLE", "BAD SCRIPT", "EDIT"]:
            self.assertNotIn(text, page.text)
        self.assertEqual(page.links, ["/wiki/食物"])
        self.assertEqual(len(page.licenses), 2)

    def test_challenge_page_is_not_an_article(self):
        page = Article()
        page.feed("<html><title>Please wait</title><body>Verify your browser</body></html>")
        self.assertFalse(page.found)

    def test_link_scope_and_normalization(self):
        base = "https://www.dwarffortresswiki.org"
        self.assertEqual(article_url(base + "/Hospital#Supplies", "en"), base + "/index.php/Hospital")
        self.assertEqual(article_url(base + "/index.php/Work_orders", "en"), base + "/index.php/Work_orders")
        self.assertEqual(article_url("https://dwarffortresswiki.org/Hospital", "en"), base + "/index.php/Hospital")
        for path in ["/DF2014:Hospital", "/Special:Random", "/File:Bed.png", "/index.php?title=Hospital&action=edit", "/Talk%3AHospital"]:
            self.assertIsNone(article_url(base + path, "en"))
        self.assertIsNone(article_url("https://example.com/Hospital", "en"))

    def test_redirect_does_not_escape_policy(self):
        handler = SafeRedirect(lambda url: False)
        with self.assertRaises(ValueError):
            handler.redirect_request(Request("https://www.dwarffortresswiki.org/Hospital"),
                                     None, 302, "Found", {}, "https://example.com/")

    def test_resume_and_failed_refresh_preserve_download(self):
        def response(url, *args):
            if url.endswith("robots.txt"):
                return b"User-agent: *\nAllow: /\n", url, {}, "utf-8"
            return (b'<title>Hospital</title><div id="mw-content-text"><p>'
                    b'Patients need real food and supplies to recover.</p></div>',
                    url, {"Content-Type": "text/html"}, "utf-8")
        with TemporaryDirectory() as directory, redirect_stdout(io.StringIO()):
            args = SimpleNamespace(output=Path(directory), site=["en"], title=["Hospital"],
                                   delay=3, max_pages=1, depth=0, refresh=False)
            with patch("crawl.read_url", side_effect=response) as request:
                self.assertEqual(run(args), 0)
                self.assertEqual(request.call_count, 2)
                request.reset_mock()
                self.assertEqual(run(args), 0)
                self.assertEqual(request.call_count, 1)  # Only robots, no repeated article request.
            before = json.loads((args.output / "manifest.json").read_text())
            record = next(iter(before.values()))
            text_before = (args.output / record["text_file"]).read_text()
            args.refresh = True
            with patch("crawl.read_url", side_effect=[response("https://example.com/robots.txt"),
                       HTTPError("https://www.dwarffortresswiki.org/Hospital", 403, "Forbidden", {}, None)]):
                self.assertEqual(run(args), 1)
            after = next(iter(json.loads((args.output / "manifest.json").read_text()).values()))
            self.assertEqual(after["status"], "ok")
            self.assertIn("last_refresh_error", after)
            self.assertEqual((args.output / after["text_file"]).read_text(), text_before)


if __name__ == "__main__":
    unittest.main()
