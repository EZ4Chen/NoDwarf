const fs = require('node:fs');
const crypto = require('node:crypto');
const { execFileSync } = require('node:child_process');

module.exports = async ({ github, context, core }) => {
  const tag = context.ref.replace(/^refs\/tags\//, '');
  if (!/^v\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?$/.test(tag)) throw new Error('Invalid release tag');
  const commit = execFileSync('git', ['rev-parse', 'HEAD'], { encoding: 'utf8' }).trim();
  const packages = ['windows-x64', 'macos-arm64'].map(platform => {
    const manifest = JSON.parse(fs.readFileSync(`dist/${platform}.json`, 'utf8'));
    const name = `NoDwarf-${tag}-${platform}.zip`;
    if (manifest.commit !== commit || manifest.version !== tag || manifest.platform !== platform || manifest.file !== name) {
      throw new Error(`Package provenance mismatch: ${platform}`);
    }
    const data = fs.readFileSync(`dist/${name}`);
    const hash = crypto.createHash('sha256').update(data).digest('hex');
    if (hash !== manifest.sha256) throw new Error(`Package checksum mismatch: ${platform}`);
    return { name, data, hash };
  });
  const checksums = packages.map(p => `${p.hash}  ${p.name}\n`).join('');
  packages.push({ name: 'SHA256SUMS.txt', data: Buffer.from(checksums) });
  const repo = context.repo;
  // Do not release an old build if someone moved the tag during the workflow.
  const current = await github.rest.repos.getCommit({ ...repo, ref: tag });
  if (current.data.sha !== commit) throw new Error('Tag moved after this build started');
  let release;
  try {
    release = (await github.rest.repos.getReleaseByTag({ ...repo, tag })).data;
  } catch (error) {
    if (error.status !== 404) throw error;
  }
  if (release && !release.draft) {
    core.info(`Release ${tag} is already published; keeping existing assets unchanged.`);
    return;
  }
  if (release && release.target_commitish !== commit) throw new Error('Draft release belongs to a different commit');
  if (!release) {
    release = (await github.rest.repos.createRelease({
      ...repo, tag_name: tag, target_commitish: commit, name: `NoDwarf ${tag}`,
      draft: true, prerelease: tag.includes('-'), generate_release_notes: true,
      body: '免费开发版本。Windows x64 / macOS Apple Silicon。下载对应 ZIP 并完整解压，无需安装 Godot 或 .NET。\n\nmacOS 版本仅作 ad-hoc 签名，尚未 Apple 公证；Windows 版本尚未代码签名。操作方式和第三方素材署名见包内 README.txt。\n\n源码提交：`' + commit + '`。附 SHA256SUMS.txt 用于校验下载文件。'
    })).data;
  }
  const existing = await github.paginate(github.rest.repos.listReleaseAssets, { ...repo, release_id: release.id });
  for (const asset of packages) {
    // An interrupted upload can be retried while the release remains a draft.
    const old = existing.find(a => a.name === asset.name);
    if (old) await github.rest.repos.deleteReleaseAsset({ ...repo, asset_id: old.id });
    await github.rest.repos.uploadReleaseAsset({ ...repo, release_id: release.id,
      name: asset.name, data: asset.data });
  }
  await github.rest.repos.updateRelease({ ...repo, release_id: release.id, draft: false });
  core.info(`Published ${release.html_url}`);
};
