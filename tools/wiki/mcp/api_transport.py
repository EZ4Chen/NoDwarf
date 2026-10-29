#!/usr/bin/env python3
"""Anonymous, bounded GET transport for the two configured public Wiki APIs."""
import json
import sys
import urllib.error
import urllib.parse
import urllib.request

API_URLS = {
    "https://www.dwarffortresswiki.org/api.php",
    "https://dwarffortresswiki.org/api.php",
    "https://dfzh.huijiwiki.com/api.php",
}


class WikiRedirects(urllib.request.HTTPRedirectHandler):
    def redirect_request(self, req, fp, code, msg, headers, newurl):
        parsed = urllib.parse.urlsplit(newurl)
        base = urllib.parse.urlunsplit((parsed.scheme, parsed.netloc, parsed.path, "", ""))
        if base not in API_URLS:
            raise ValueError("Redirect outside configured Wiki APIs")
        return super().redirect_request(req, fp, code, msg, headers, newurl)


def main():
    payload = json.load(sys.stdin)
    url, params = payload["url"], payload["params"]
    if url not in API_URLS or params.get("action") not in {"query", "parse", "compare", "paraminfo"}:
        raise ValueError("Only public read-only Wiki API requests are supported")
    if any(key in params for key in ("token", "password", "lgpassword")):
        raise ValueError("This transport does not support authentication")
    request = urllib.request.Request(url + "?" + urllib.parse.urlencode(params), headers={
        "User-Agent": "NoDwarfWikiResearch/0.1 (read-only MediaWiki MCP)",
    })
    opener = urllib.request.build_opener(WikiRedirects())
    try:
        with opener.open(request, timeout=25) as response:
            body = response.read(4 * 1024 * 1024 + 1)
            if len(body) > 4 * 1024 * 1024:
                raise ValueError("Wiki response exceeds 4 MiB")
            result = {"status": response.status, "data": json.loads(body)}
    except urllib.error.HTTPError as error:
        result = {"status": error.code, "error": "Wiki API rejected request",
                  "retryAfter": error.headers.get("Retry-After")}
    print(json.dumps(result, ensure_ascii=False))


if __name__ == "__main__":
    main()
