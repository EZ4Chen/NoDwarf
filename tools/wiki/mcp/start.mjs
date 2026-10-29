// Keep upstream MCP tools intact; use the verified Python HTTPS transport on
// this host, where native Node requests receive a Chinese Wiki challenge and
// synthetic DNS addresses confuse upstream's public-address probe.
import { execFile } from 'node:child_process';
import { fileURLToPath } from 'node:url';
import { Mwn } from 'mwn';
import { serveStdio } from '@modelcontextprotocol/server/stdio';
import { guardStdout } from './node_modules/@professional-wiki/mediawiki-mcp-server/dist/runtime/stdoutGuard.js';
import { loadConfigFromFile } from './node_modules/@professional-wiki/mediawiki-mcp-server/dist/config/loadConfig.js';
import { createAppState } from './node_modules/@professional-wiki/mediawiki-mcp-server/dist/wikis/state.js';
import { createSharedContext } from './node_modules/@professional-wiki/mediawiki-mcp-server/dist/runtime/createContext.js';
import { createServer } from './node_modules/@professional-wiki/mediawiki-mcp-server/dist/server.js';
import { logger } from './node_modules/@professional-wiki/mediawiki-mcp-server/dist/runtime/logger.js';
import { readAuthPolicy } from './node_modules/@professional-wiki/mediawiki-mcp-server/dist/runtime/authShape.js';
import { StdioSignIn } from './node_modules/@professional-wiki/mediawiki-mcp-server/dist/auth/stdioSignIn.js';
import { readMetrics } from './node_modules/@professional-wiki/mediawiki-mcp-server/dist/runtime/metrics.js';
import { registerShutdownHandlers } from './node_modules/@professional-wiki/mediawiki-mcp-server/dist/runtime/shutdown.js';

guardStdout();
process.env.CONFIG ??= fileURLToPath(new URL('./config.json', import.meta.url));
const python = process.env.WIKI_PYTHON ?? 'python3';
const script = fileURLToPath(new URL('./api_transport.py', import.meta.url));
const queues = new Map();
const nextStart = new Map();

function request(url, params, options = {}) {
  const host = new URL(url).hostname.replace(/^www\./, '');
  const run = async () => {
    await new Promise(resolve => setTimeout(resolve, Math.max(0, (nextStart.get(host) ?? 0) - Date.now())));
    options.signal?.throwIfAborted();
    nextStart.set(host, Date.now() + 3000);
    return new Promise((resolve, reject) => {
      const child = execFile(python, [script], {
        timeout: 30000, maxBuffer: 5 * 1024 * 1024, signal: options.signal,
      }, (error, stdout) => {
        if (error) return reject(error);
        try {
          const response = JSON.parse(stdout);
          if (response.status !== 200) {
            const failure = new Error(`Wiki API returned HTTP ${response.status}`);
            failure.response = { status: response.status, headers: { 'retry-after': response.retryAfter } };
            return reject(failure);
          }
          resolve(response.data);
        } catch (error) { reject(error); }
      });
      child.stdin.on('error', reject);
      child.stdin.end(JSON.stringify({ url, params }));
    });
  };
  const pending = (queues.get(host) ?? Promise.resolve()).then(run);
  queues.set(host, pending.catch(() => {}));
  return pending;
}

const config = loadConfigFromFile();
if (config.allowWikiManagement !== false || Object.values(config.wikis).some(w => !w.readOnly || w.private || w.token || w.username || w.password)) {
  throw new Error('This launcher requires fixed, anonymous, read-only Wikis');
}
const state = createAppState(config);
// The upstream 5-second startup probe is too short on this connection. Keep
// a finite 30-second bound; Codex's configured startup budget is 60 seconds.
state.wikiProbe.request = (url, params) => request(url, params, {
  signal: AbortSignal.timeout(30000),
});
state.mwnProvider.createMwn = options => {
  const bot = new Mwn({ ...options, maxRetries: 0 });
  bot.setRequestOptions({ adapter: async cfg => ({
    data: await request(cfg.url, cfg.params ?? Object.fromEntries(new URLSearchParams(cfg.data)), { signal: cfg.signal }),
    status: 200, statusText: 'OK', headers: {}, config: cfg,
  }) });
  return bot;
};
const auth = readAuthPolicy(process.env, 'stdio', null, new StdioSignIn());
const ctx = createSharedContext({ logger, state, auth, metrics: readMetrics(process.env, 'stdio') });
const handle = serveStdio(() => createServer(ctx), {
  onerror: error => logger.error(`stdio serving error: ${error.message}`),
});
registerShutdownHandlers({ transport: 'stdio', graceMs: 0, stdioTransport: handle });
