#!/usr/bin/env python3
"""Read-only live checks of the installed MCP server over JSON-RPC stdio."""
import json
import os
import argparse
from pathlib import Path
import queue
import shutil
import subprocess
import threading
import time

ROOT = Path(__file__).resolve().parent


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--registered", action="store_true",
                        help="Use the actual Codex mediawiki entry")
    args = parser.parse_args()
    node = shutil.which("node")
    if not node:
        raise RuntimeError("Node.js >=22.12 is required")
    env = dict(os.environ, CONFIG=str(ROOT / "config.json"),
               MCP_TRANSPORT="stdio", MCP_LOG_LEVEL="error")
    server = ROOT / "start.mjs"
    command = [node, str(server)]
    enabled_tools = None
    if args.registered:
        registered = json.loads(subprocess.check_output(
            ["codex", "mcp", "get", "mediawiki", "--json"], text=True))
        transport = registered["transport"]
        assert transport["type"] == "stdio"
        command = [transport["command"], *transport.get("args", [])]
        env.update(transport.get("env") or {})
        enabled_tools = set(registered["enabled_tools"])
    with subprocess.Popen(command, env=env, cwd=ROOT,
                          stdin=subprocess.PIPE, stdout=subprocess.PIPE,
                          text=True, encoding="utf-8") as process:
        incoming = queue.Queue()

        def read_stdout():
            for line in process.stdout:
                try:
                    incoming.put(json.loads(line))
                except ValueError:
                    incoming.put({"protocol_error": line[:200]})
            incoming.put({"protocol_error": "Server stdout closed"})

        threading.Thread(target=read_stdout, daemon=True).start()
        request_id = 0

        def send(message):
            process.stdin.write(json.dumps(message, ensure_ascii=False) + "\n")
            process.stdin.flush()

        def call(method, params=None):
            nonlocal request_id
            request_id += 1
            send({"jsonrpc": "2.0", "id": request_id, "method": method,
                  "params": params or {}})
            deadline = time.monotonic() + 120
            while True:
                response = incoming.get(timeout=max(0.01, deadline - time.monotonic()))
                if "protocol_error" in response:
                    raise RuntimeError(response["protocol_error"])
                if response.get("id") != request_id:
                    continue
                if "error" in response:
                    raise RuntimeError(response["error"])
                result = response["result"]
                if result.get("isError"):
                    raise RuntimeError(result)
                return result

        def tool(name, arguments):
            result = call("tools/call", {"name": name, "arguments": arguments})
            data = result.get("structuredContent")
            if data is None:
                raise RuntimeError(f"{name}: missing structuredContent: {result}")
            return data

        try:
            init = call("initialize", {
                "protocolVersion": "2025-03-26", "capabilities": {},
                "clientInfo": {"name": "nodwarf-wiki-smoke", "version": "1.0"}})
            send({"jsonrpc": "2.0", "method": "notifications/initialized"})
            print(json.dumps({"server": init["serverInfo"]}), flush=True)
            definitions = call("tools/list")["tools"]
            unsafe = [t["name"] for t in definitions
                      if not t.get("annotations", {}).get("readOnlyHint")]
            # Upstream also exposes a local credential logout tool. Codex's
            # enabled_tools allowlist excludes it; it cannot change wiki pages.
            if set(unsafe) - {"oauth-logout"}:
                raise RuntimeError(f"Unexpected non-read-only tools: {unsafe}")
            names = {t["name"] for t in definitions}
            if enabled_tools is not None:
                assert enabled_tools <= names, "Configured tools missing from server"
                assert not enabled_tools & set(unsafe), "Codex allows non-read-only tools"
            assert {"search-page", "get-page", "list-wikis"} <= names
            assert not {"add-wiki", "remove-wiki", "update-page", "delete-page"} & names
            print(json.dumps({"read_only_tools": len(names) - len(unsafe),
                              "excluded_local_auth_tools": unsafe}), flush=True)
            wikis = tool("list-wikis", {})
            assert {w["key"] for w in wikis["wikis"]} == {"df-en", "df-zh"}
            assert all(w["readOnly"] and w["reachable"] for w in wikis["wikis"]), wikis
            print(json.dumps({"wikis": wikis}, ensure_ascii=False), flush=True)
            for wiki, query, title in [("df-en", "hospital", "Hospital"),
                                       ("df-zh", "医疗", "医疗")]:
                search = tool("search-page", {"wiki": wiki, "query": query,
                                               "limit": 3, "namespaces": [0]})
                print(json.dumps({"wiki": wiki, "search": search}, ensure_ascii=False), flush=True)
                assert search.get("results"), f"Empty search for {wiki}"
                time.sleep(3)
                page = tool("get-page", {"wiki": wiki, "title": title,
                                         "content": "source", "metadata": True})
                source = page.get("source", "")
                assert len(source) > 200, f"Missing body for {wiki}: {page}"
                assert page.get("latestRevisionId"), f"Missing revision for {wiki}"
                print(json.dumps({"wiki": wiki, "page": {k: v for k, v in page.items()
                                                         if k != "source"},
                                  "source_chars": len(source), "excerpt": source[:250]},
                                 ensure_ascii=False), flush=True)
            print("PASS: both wikis searched and read via MCP; wiki write tools disabled.")
        finally:
            process.terminate()
            try:
                process.wait(timeout=5)
            except subprocess.TimeoutExpired:
                process.kill()
                process.wait()


if __name__ == "__main__":
    main()
