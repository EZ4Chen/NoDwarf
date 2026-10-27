const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const os = require('node:os');
const path = require('node:path');
const crypto = require('node:crypto');
const { execFileSync } = require('node:child_process');
const publish = require('./publish.cjs');

// All GitHub writes are mocked; fixtures exercise the same publication entry point.
async function fixture(options, check) {
  const before = process.cwd();
  const temp = fs.mkdtempSync(path.join(os.tmpdir(), 'nodwarf-release-'));
  try {
    process.chdir(temp);
    execFileSync('git', ['init', '-q']);
    execFileSync('git', ['-c', 'user.name=Test', '-c', 'user.email=test@example.invalid',
      'commit', '--allow-empty', '-qm', 'fixture']);
    const commit = execFileSync('git', ['rev-parse', 'HEAD'], { encoding: 'utf8' }).trim();
    const tag = 'v0.1.0-alpha.1';
    fs.mkdirSync('dist');
    for (const platform of ['windows-x64', 'macos-arm64']) {
      const file = `NoDwarf-${tag}-${platform}.zip`;
      const data = Buffer.from(platform);
      fs.writeFileSync(`dist/${file}`, data);
      fs.writeFileSync(`dist/${platform}.json`, JSON.stringify({ platform, file, commit,
        version: tag, sha256: crypto.createHash('sha256').update(data).digest('hex') }));
    }
    const calls = [];
    const repos = {
      getCommit: async () => ({ data: { sha: options.moved ? 'different' : commit } }),
      getReleaseByTag: async () => {
        if (options.existing !== 'published') throw Object.assign(new Error('missing'), { status: 404 });
        return { data: { id: 1, draft: options.existing === 'draft', target_commitish: commit } };
      },
      createRelease: async args => { calls.push(['create', args]); return { data: { id: 1 } }; },
      listReleaseAssets: () => {},
      listReleases: () => {},
      deleteReleaseAsset: async args => { calls.push(['delete', args]); },
      uploadReleaseAsset: async args => {
        calls.push(['upload', args]);
        if (options.uploadFailure) throw new Error('upload failed');
      },
      updateRelease: async args => { calls.push(['publish', args]); }
    };
    const invoke = () => publish({
      github: { rest: { repos }, paginate: async method => method === repos.listReleases
        ? (options.existing === 'draft' ? [{ id: 1, draft: true, tag_name: tag, target_commitish: commit }] : [])
        : options.assets || [] },
      context: { ref: `refs/tags/${tag}`, repo: { owner: 'test', repo: 'test' } },
      core: { info: () => {} }
    });
    await check({ invoke, calls, tag });
  } finally {
    process.chdir(before);
    fs.rmSync(temp, { recursive: true, force: true });
  }
}

test('both checked packages and checksum file upload before publishing a prerelease', async () => {
  await fixture({}, async ({ invoke, calls }) => {
    await invoke();
    assert.deepEqual(calls.map(c => c[0]), ['create', 'upload', 'upload', 'upload', 'publish']);
    assert.equal(calls[0][1].draft, true);
    assert.equal(calls[0][1].prerelease, true);
    assert.equal(calls[3][1].name, 'SHA256SUMS.txt');
    assert.equal(calls[1][1].headers['content-type'], 'application/zip');
    assert.equal(calls[3][1].headers['content-type'], 'text/plain');
    for (const [, args] of calls.filter(c => c[0] === 'upload')) {
      assert.equal(args.headers['content-length'], args.data.length);
    }
    assert.equal(calls[4][1].draft, false);
  });
});
test('corrupt archive never creates a release', async () => {
  await fixture({}, async ({ invoke, calls, tag }) => {
    fs.appendFileSync(`dist/NoDwarf-${tag}-windows-x64.zip`, 'corrupt');
    await assert.rejects(invoke, /checksum mismatch/);
    assert.equal(calls.length, 0);
  });
});
test('missing platform never creates a release', async () => {
  await fixture({}, async ({ invoke, calls }) => {
    fs.unlinkSync('dist/macos-arm64.json');
    await assert.rejects(invoke, /ENOENT/);
    assert.equal(calls.length, 0);
  });
});
test('moved tag never publishes an outdated build', async () => {
  await fixture({ moved: true }, async ({ invoke, calls }) => {
    await assert.rejects(invoke, /Tag moved/);
    assert.equal(calls.length, 0);
  });
});
test('upload failure leaves release in draft', async () => {
  await fixture({ uploadFailure: true }, async ({ invoke, calls }) => {
    await assert.rejects(invoke, /upload failed/);
    assert.ok(!calls.some(c => c[0] === 'publish'));
  });
});
test('published releases are not overwritten', async () => {
  await fixture({ existing: 'published' }, async ({ invoke, calls }) => {
    await invoke();
    assert.equal(calls.length, 0);
  });
});
test('an interrupted draft can replace its incomplete asset and finish', async () => {
  await fixture({ existing: 'draft', assets: [{ id: 7, name: 'SHA256SUMS.txt' }] }, async ({ invoke, calls }) => {
    await invoke();
    assert.deepEqual(calls.map(c => c[0]), ['upload', 'upload', 'delete', 'upload', 'publish']);
  });
});
