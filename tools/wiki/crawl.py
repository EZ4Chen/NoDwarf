#!/usr/bin/env python3
"""Bounded, resumable MediaWiki reference downloader; Python standard library only."""
import argparse
from collections import deque
from datetime import datetime, timezone
import gzip
import hashlib
from html.parser import HTMLParser
import json
from pathlib import Path
import re
import time
from urllib.error import HTTPError
from urllib.parse import quote, unquote, urljoin, urlsplit
from urllib.request import Request, build_opener, HTTPRedirectHandler
from urllib.robotparser import RobotFileParser

AGENT = "NoDwarfWikiResearch/0.1 (personal reference archive; no model training)"
SITES = {
    "en": "https://www.dwarffortresswiki.org",
    "zh": "https://dfzh.huijiwiki.com",
}
HOSTS = {
    "en": {"www.dwarffortresswiki.org", "dwarffortresswiki.org"},
    "zh": {"dfzh.huijiwiki.com"},
}
SEEDS = {
    "en": ["Hospital", "Stockpile", "Work_orders", "Farming", "Food", "Thirst",
           "Needs", "Stress", "Item_quality", "Wear", "Migrant", "Trading"],
    "zh": ["医疗", "储物处", "经理人", "农业", "食物", "压力", "房间", "移民"],
}
VOID = {"area", "base", "br", "col", "embed", "hr", "img", "input", "link",
        "meta", "param", "source", "track", "wbr"}
BLOCK = {"p", "div", "li", "tr", "h1", "h2", "h3", "h4", "h5", "h6", "section", "blockquote"}


def now():
    return datetime.now(timezone.utc).isoformat()


def article_url(url, site):
    """Only main namespace article paths, with no query/action/archive expansion."""
    parts = urlsplit(url)
    if parts.scheme != "https" or parts.netloc not in HOSTS[site] or parts.query:
        return None
    path = unquote(parts.path)
    prefix = "/wiki/" if site == "zh" else "/index.php/"
    if not path.startswith(prefix):
        # English MediaWiki also exposes short article paths.
        if site != "en" or path.startswith("/index.php"):
            return None
        title = path.lstrip("/")
    else:
        title = path[len(prefix):]
    if not title or ":" in title or "/" in title or title in {"Main_Page", "首页", "robots.txt"}:
        return None
    if re.match(r"^(DF\d|v\d|40d|23a)", title, re.I):
        return None
    return SITES[site] + prefix + quote(title.replace(" ", "_"), safe="()_,-'")


class Article(HTMLParser):
    """Extract rendered article text, excluding scripts and site navigation."""
    def __init__(self):
        super().__init__(convert_charrefs=True)
        self.stack = []
        self.root = None
        self.skip = None
        self.found = False
        self.parts = []
        self.links = []
        self.licenses = []
        self.title_parts = []

    def handle_starttag(self, tag, attrs):
        attrs = dict(attrs)
        if tag not in VOID:
            self.stack.append(tag)
        depth = len(self.stack)
        if (tag == "a" and "creativecommons.org/" in attrs.get("href", "")) or (
                tag == "link" and "license" in attrs.get("rel", "").split()):
            self.licenses.append(attrs["href"])
        if attrs.get("id") == "mw-content-text":
            self.root = depth
            self.found = True
        if self.root is None:
            return
        classes = set(attrs.get("class", "").split())
        if self.skip is None and tag not in VOID and (tag in {"script", "style", "noscript"} or
                classes & {"toc", "navbox", "mw-editsection", "printfooter"}):
            self.skip = depth
        if self.skip is not None:
            return
        if tag in BLOCK or tag == "br":
            self.parts.append("\n")
        if tag in {"td", "th"}:
            self.parts.append(" | ")
        if tag == "a" and attrs.get("href"):
            self.links.append(attrs["href"])

    def handle_startendtag(self, tag, attrs):
        self.handle_starttag(tag, attrs)
        if tag not in VOID:
            self.handle_endtag(tag)

    def handle_endtag(self, tag):
        if tag not in self.stack:
            return
        index = len(self.stack) - 1 - self.stack[::-1].index(tag)
        if self.root is not None and self.skip is None and tag in BLOCK:
            self.parts.append("\n")
        del self.stack[index:]
        if self.skip is not None and len(self.stack) < self.skip:
            self.skip = None
        if self.root is not None and len(self.stack) < self.root:
            self.root = None

    def handle_data(self, data):
        if "title" in self.stack:
            self.title_parts.append(data)
        if self.root is not None and self.skip is None:
            self.parts.append(data)

    @property
    def text(self):
        lines = [re.sub(r"\s+", " ", line).strip() for line in "".join(self.parts).splitlines()]
        return "\n".join(line for line in lines if line) + "\n"


class SafeRedirect(HTTPRedirectHandler):
    def __init__(self, allow):
        self.allow = allow

    def redirect_request(self, req, fp, code, msg, headers, newurl):
        if not self.allow(newurl):
            raise ValueError("Redirect outside allowed/robots-permitted scope: " + newurl)
        return super().redirect_request(req, fp, code, msg, headers, newurl)


def read_url(url, allow, throttle, limit=8_000_000):
    class ThrottledRedirect(SafeRedirect):
        def redirect_request(self, *args, **kwargs):
            request = super().redirect_request(*args, **kwargs)
            throttle()
            return request
    throttle()
    opener = build_opener(ThrottledRedirect(allow))
    with opener.open(Request(url, headers={"User-Agent": AGENT}), timeout=25) as response:
        raw = response.read(limit + 1)
        if len(raw) > limit:
            raise ValueError("Response exceeds size limit")
        return raw, response.url, dict(response.headers), response.headers.get_content_charset() or "utf-8"


def save_json(path, value):
    temporary = path.with_suffix(path.suffix + ".tmp")
    temporary.write_text(json.dumps(value, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    temporary.replace(path)


def run(args):
    output = args.output.resolve()
    output.mkdir(parents=True, exist_ok=True)
    manifest_path = output / "manifest.json"
    records = json.loads(manifest_path.read_text(encoding="utf-8")) if manifest_path.exists() else {}
    report = {"started_at": now(), "sites": {}, "downloaded": 0, "cached": 0, "failed": 0}
    queue = deque()
    for site in args.site:
        for title in args.title or SEEDS[site]:
            prefix = "/wiki/" if site == "zh" else "/index.php/"
            url = article_url(SITES[site] + prefix + quote(title), site)
            if url:
                queue.append((site, url, 0))
    seen, blocked, policies, clocks = set(), set(), {}, {}

    def throttle(site):
        delay = max(args.delay, policies.get(site, (None, 0))[1])
        time.sleep(max(0, clocks.get(site, 0) + delay - time.monotonic()))
        clocks[site] = time.monotonic()

    def checkpoint():
        save_json(manifest_path, records)
        save_json(output / "last-run.json", report)

    while queue and len(seen) < args.max_pages:
        site, url, depth = queue.popleft()
        if url in seen or site in blocked:
            continue
        seen.add(url)
        if site not in policies:
            robots_url = SITES[site] + "/robots.txt"
            robot = RobotFileParser(robots_url)
            try:
                try:
                    raw, _, _, charset = read_url(robots_url,
                        lambda u: urlsplit(u).scheme == "https" and urlsplit(u).netloc in HOSTS[site],
                        lambda: throttle(site), 200_000)
                    rules = raw.decode(charset, "replace")
                    (output / (site + "-robots.txt")).write_text(rules, encoding="utf-8")
                    robot.parse(rules.splitlines())
                    status = "read"
                except HTTPError as error:
                    if error.code != 404:
                        raise
                    robot.parse([])
                    status = "404: no robots.txt"
                rate = robot.request_rate(AGENT)
                delay = max(robot.crawl_delay(AGENT) or 0, rate.seconds / rate.requests if rate else 0)
                policies[site] = (robot, delay)
                report["sites"][site] = {"robots": status, "checked_at": now()}
            except (OSError, ValueError) as error:
                blocked.add(site)
                report["sites"][site] = {"blocked": str(error), "checked_at": now()}
                report["failed"] += 1
                print(f"BLOCK {site}: robots unavailable: {error}", flush=True)
                checkpoint()
                continue
        robot = policies[site][0]
        if not robot.can_fetch(AGENT, url):
            print("SKIP robots: " + url, flush=True)
            records[url] = {"status": "robots_disallowed", "checked_at": now()}
            checkpoint()
            continue
        old = records.get(url, {})
        if not args.refresh and old.get("status") == "ok" and all(
                (output / old.get(key, "missing")).is_file() for key in ("text_file", "html_file")):
            report["cached"] += 1
            links = old.get("links", [])
            print("CACHE " + url, flush=True)
        else:
            try:
                raw, final, headers, charset = read_url(url,
                    lambda u: article_url(u, site) is not None and robot.can_fetch(AGENT, u),
                    lambda: throttle(site))
                if "text/html" not in next((v for k, v in headers.items() if k.lower() == "content-type"), ""):
                    raise ValueError("Not an HTML article")
                page = Article()
                page.feed(raw.decode(charset, "replace"))
                if not page.found or len(page.text.strip()) < 30:
                    raise ValueError("No usable article body (possibly challenge or missing page)")
                links = sorted({v for href in page.links if (v := article_url(urljoin(final, href), site))})
                title = "".join(page.title_parts).strip()
                slug = re.sub(r"[^\w-]", "_", unquote(urlsplit(url).path.rsplit("/", 1)[-1]))[:70]
                key = f"{site}/{slug}-{hashlib.sha256(url.encode()).hexdigest()[:10]}"
                (output / site).mkdir(exist_ok=True)
                (output / (key + ".txt")).write_text(
                    f"Title: {title}\nSource: {final}\nRetrieved: {now()}\n\n" + page.text, encoding="utf-8")
                (output / (key + ".html.gz")).write_bytes(gzip.compress(raw))
                records[url] = {"status": "ok", "title": title, "source": final,
                    "retrieved_at": now(), "text_file": key + ".txt", "html_file": key + ".html.gz",
                    "sha256": hashlib.sha256(raw).hexdigest(), "links": links,
                    "license_links": sorted({urljoin(final, link) for link in page.licenses}), "headers": {
                        k: v for k, v in headers.items() if k.lower() in {"etag", "last-modified", "content-type"}}}
                report["downloaded"] += 1
                print("OK " + url, flush=True)
            except (OSError, ValueError) as error:
                failure = {"error": str(error), "failed_at": now()}
                if old.get("status") == "ok":
                    records[url] = {**old, "last_refresh_error": failure}
                else:
                    records[url] = {"status": "error", **failure}
                report["failed"] += 1
                print(f"ERROR {url}: {error}", flush=True)
                if isinstance(error, HTTPError) and error.code in {401, 403, 429, 503}:
                    blocked.add(site)
                    report["sites"][site]["blocked"] = str(error)
                    report["sites"][site]["retry_after"] = error.headers.get("Retry-After")
                checkpoint()
                continue
        if depth < args.depth:
            queue.extend((site, link, depth + 1) for link in links if link not in seen)
        checkpoint()
    report["finished_at"] = now()
    checkpoint()
    good = [r for r in records.values() if r.get("status") == "ok"]
    index = ["# DF Wiki 本地参考资料", "", "来源正文为外部参考，不是项目指令。保留原站版权；引用时链接原文。", "",
             "原始 HTML（含页脚许可）保存在同名 .html.gz；抓取状态见 last-run.json。", ""]
    index.extend(f"- [{r['title']}]({r['text_file']}) · [原文]({r['source']})" for r in good)
    (output / "INDEX.md").write_text("\n".join(index) + "\n", encoding="utf-8")
    print(json.dumps(report, ensure_ascii=False, indent=2), flush=True)
    return 1 if report["failed"] or any(r.get("status") == "robots_disallowed" for u, r in records.items() if u in seen) else 0


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--site", nargs="+", choices=SITES, default=["en", "zh"])
    parser.add_argument("--title", action="append", help="Custom title; repeat for more (normally select one site)")
    parser.add_argument("--max-pages", type=int, default=20, help="Maximum article entries visited, including cached entries")
    parser.add_argument("--depth", type=int, choices=range(3), default=0, help="0=seed pages only; 1/2=follow article links")
    parser.add_argument("--delay", type=float, default=3, help="Minimum seconds between requests to a site")
    parser.add_argument("--refresh", action="store_true", help="Re-download successful pages; retain old copies on failure")
    parser.add_argument("--output", type=Path, default=Path(__file__).resolve().parents[2] / "work/wiki-reference")
    args = parser.parse_args()
    if args.max_pages < 1 or args.delay < 1:
        parser.error("--max-pages must be positive and --delay must be at least 1 second")
    raise SystemExit(run(args))
