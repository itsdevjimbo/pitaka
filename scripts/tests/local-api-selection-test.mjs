import assert from 'node:assert/strict';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { spawnSync } from 'node:child_process';
import test from 'node:test';

const repositoryRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../..');
const commandPath = path.join(repositoryRoot, 'scripts', 'select-local-api-image.mjs');

const sourceSha = 'aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa';
const oldSha = '1111111111111111111111111111111111111111';
const indexDigest = `sha256:${'1'.repeat(64)}`;
const amd64Digest = `sha256:${'a'.repeat(64)}`;
const arm64Digest = `sha256:${'b'.repeat(64)}`;

test('selects the verified current-main index and preserves the rest of the deploy record', (t) => {
  const fixture = makeFixture(t);
  const initialRecord = fixture.initialRecord;

  const result = runSelection(fixture);

  assert.equal(result.status, 0, result.stderr);
  const selected = readRemoteRecord(fixture);
  assert.deepEqual(selected.api, {
    repository: 'jimbodev0530/pitaka-api',
    sourceSha,
    sourceTag: `sha-${sourceSha}`,
    image: `jimbodev0530/pitaka-api@${indexDigest}`,
    digest: indexDigest,
    platform: 'linux/arm64',
  });
  assert.deepEqual({ ...selected, api: initialRecord.api }, initialRecord);
  assert.equal(result.stdout.includes('token'), false);

  const repeated = runSelection(fixture);
  assert.equal(repeated.status, 0, repeated.stderr);
  assert.equal(JSON.parse(fs.readFileSync(fixture.statePath, 'utf8')).outcome, 'already current');
  assert.equal(writeCount(fixture), 1);
});

test('rejects evidence bound to another publisher attempt without writing', (t) => {
  const fixture = makeFixture(t, { evidenceAttempt: 2 });

  const result = runSelection(fixture);

  assert.notEqual(result.status, 0);
  assert.match(`${result.stderr}\n${result.stdout}`, /evidence|attempt/i);
  assert.deepEqual(readRemoteRecord(fixture), fixture.initialRecord);
  assert.equal(fs.readFileSync(fixture.putLog, 'utf8'), '');
});

test('returns already current without creating a Contents API commit', (t) => {
  const fixture = makeFixture(t, { apiSha: sourceSha, apiDigest: indexDigest });

  const result = runSelection(fixture);

  assert.equal(result.status, 0, result.stderr);
  assert.equal(JSON.parse(fs.readFileSync(fixture.statePath, 'utf8')).outcome, 'already current');
  assert.equal(fs.readFileSync(fixture.putLog, 'utf8'), '');
});

test('rejects a moving alias with a different platform child digest', (t) => {
  const fixture = makeFixture(t, { aliasArmDigest: `sha256:${'c'.repeat(64)}` });

  const result = runSelection(fixture);

  assert.notEqual(result.status, 0);
  assert.match(result.stderr, /moving API main alias/i);
  assert.equal(fs.readFileSync(fixture.putLog, 'utf8'), '');
});

test('retries a stale file SHA and preserves a concurrent non-API edit', (t) => {
  const fixture = makeFixture(t, { writeMode: 'concurrent-non-api' });

  const result = runSelection(fixture);

  assert.equal(result.status, 0, result.stderr);
  const selected = readRemoteRecord(fixture);
  assert.equal(selected.configuration.compatibilityNotes, 'Keep this field when updating only API. Concurrent web edit.');
  assert.equal(selected.api.sourceSha, sourceSha);
  assert.equal(writeCount(fixture), 2);
});

test('reads back and reports an update when the write response is uncertain', (t) => {
  const fixture = makeFixture(t, { writeMode: 'commit-then-503' });

  const result = runSelection(fixture);

  assert.equal(result.status, 0, result.stderr);
  assert.equal(JSON.parse(fs.readFileSync(fixture.statePath, 'utf8')).outcome, 'applied');
  assert.equal(readRemoteRecord(fixture).api.sourceSha, sourceSha);
  assert.equal(writeCount(fixture), 1);
});

test('re-evaluates a newer live main after its own accepted write', (t) => {
  const descendantSha = 'dddddddddddddddddddddddddddddddddddddddd';
  const descendantDigest = `sha256:${'6'.repeat(64)}`;
  const fixture = makeFixture(t, {
    writeMode: 'commit-then-main-advance',
    advanceMainAfterWriteSha: descendantSha,
    proofs: [
      { sha: sourceSha, digest: indexDigest, amd64Digest, arm64Digest, runId: '200', attempt: 1 },
      { sha: descendantSha, digest: descendantDigest, amd64Digest: `sha256:${'c'.repeat(64)}`, arm64Digest: `sha256:${'d'.repeat(64)}`, runId: '210', attempt: 1 },
    ],
  });

  const result = runSelection(fixture);

  assert.equal(result.status, 0, result.stderr);
  assert.equal(readRemoteRecord(fixture).api.sourceSha, descendantSha);
  assert.equal(JSON.parse(fs.readFileSync(fixture.statePath, 'utf8')).outcome, 'applied');
  assert.equal(writeCount(fixture), 2);
});

test('restarts before writing when live main advances during candidate verification', (t) => {
  const descendantSha = 'eeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeee';
  const descendantDigest = `sha256:${'8'.repeat(64)}`;
  const fixture = makeFixture(t, {
    writeMode: 'advance-main-before-recheck',
    advanceMainAfterWriteSha: descendantSha,
    proofs: [
      { sha: sourceSha, digest: indexDigest, amd64Digest, arm64Digest, runId: '200', attempt: 1 },
      { sha: descendantSha, digest: descendantDigest, amd64Digest: `sha256:${'c'.repeat(64)}`, arm64Digest: `sha256:${'d'.repeat(64)}`, runId: '210', attempt: 1 },
    ],
  });

  const result = runSelection(fixture);

  assert.equal(result.status, 0, result.stderr);
  assert.equal(readRemoteRecord(fixture).api.sourceSha, descendantSha);
  assert.equal(JSON.parse(fs.readFileSync(fixture.statePath, 'utf8')).outcome, 'applied');
  assert.equal(writeCount(fixture), 1);
});

test('stops after three transient writes and emits a failed handoff state', (t) => {
  const fixture = makeFixture(t, { writeMode: 'always-503' });

  const result = runSelection(fixture);

  assert.notEqual(result.status, 0);
  assert.match(result.stderr, /3 conditional writes/i);
  assert.equal(writeCount(fixture), 3);
  assert.equal(JSON.parse(fs.readFileSync(fixture.statePath, 'utf8')).writeAttempts, 3);
  assert.deepEqual(readRemoteRecord(fixture), fixture.initialRecord);
});

test('does not retry an App write rejected by branch rules', (t) => {
  const fixture = makeFixture(t, { writeMode: 'protected-branch' });

  const result = runSelection(fixture);

  assert.notEqual(result.status, 0);
  assert.match(result.stderr, /branch rules rejected/i);
  assert.equal(writeCount(fixture), 1);
  assert.deepEqual(readRemoteRecord(fixture), fixture.initialRecord);
});

test('stops after an App authorization rejection', (t) => {
  const fixture = makeFixture(t, { writeMode: 'app-unauthorized' });

  const result = runSelection(fixture);

  assert.notEqual(result.status, 0);
  assert.match(result.stderr, /deploy-scoped App was not authorized/i);
  assert.equal(writeCount(fixture), 1);
  assert.deepEqual(readRemoteRecord(fixture), fixture.initialRecord);
});

test('preserves and flags an API edit that races the conditional write', (t) => {
  const fixture = makeFixture(t, { writeMode: 'concurrent-api' });

  const result = runSelection(fixture);

  assert.notEqual(result.status, 0);
  assert.match(result.stderr, /API-changing commit/i);
  assert.equal(writeCount(fixture), 1);
  const current = readRemoteRecord(fixture);
  assert.equal(current.api.sourceSha, '2222222222222222222222222222222222222222');
  assert.equal(current.api.digest, `sha256:${'f'.repeat(64)}`);
});

test('fails for an unrecognized API-changing deploy edit', (t) => {
  const fixture = makeFixture(t, { manualApiEdit: true });

  const result = runSelection(fixture);

  assert.notEqual(result.status, 0);
  assert.match(result.stderr, /API-changing commit/i);
  assert.equal(fs.readFileSync(fixture.putLog, 'utf8'), '');
});

test('fails when the same source SHA is recorded with a different digest', (t) => {
  const fixture = makeFixture(t, { apiSha: sourceSha, apiDigest: `sha256:${'d'.repeat(64)}` });

  const result = runSelection(fixture);

  assert.notEqual(result.status, 0);
  assert.match(result.stderr, /same digest|different digest/i);
  assert.equal(fs.readFileSync(fixture.putLog, 'utf8'), '');
});

test('rejects an expired publisher artifact and a fixed tag that contradicts the smoke evidence', async (t) => {
  for (const options of [
    { expiredEvidence: true },
    { evidenceDigest: `sha256:${'f'.repeat(64)}` },
  ]) {
    const fixture = makeFixture(t, options);
    const result = runSelection(fixture);
    assert.notEqual(result.status, 0);
    assert.equal(fs.readFileSync(fixture.putLog, 'utf8'), '');
  }
});

test('an older successful promoter can select the newer live-main source proof', (t) => {
  const fixture = makeFixture(t, { triggerSha: oldSha });

  const result = runSelection(fixture);

  assert.equal(result.status, 0, result.stderr);
  assert.equal(readRemoteRecord(fixture).api.sourceSha, sourceSha);
});

test('keeps a verified selected descendant when an older candidate runs late', (t) => {
  const descendantSha = 'bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb';
  const descendantDigest = `sha256:${'4'.repeat(64)}`;
  const fixture = makeFixture(t, {
    apiSha: descendantSha,
    apiDigest: descendantDigest,
    compareStatus: 'behind',
    proofs: [
      { sha: sourceSha, digest: indexDigest, amd64Digest, arm64Digest, runId: '200', attempt: 1 },
      { sha: descendantSha, digest: descendantDigest, amd64Digest: `sha256:${'c'.repeat(64)}`, arm64Digest: `sha256:${'d'.repeat(64)}`, runId: '210', attempt: 1 },
    ],
  });

  const result = runSelection(fixture);

  assert.equal(result.status, 0, result.stderr);
  assert.equal(JSON.parse(fs.readFileSync(fixture.statePath, 'utf8')).outcome, 'validly superseded');
  assert.equal(readRemoteRecord(fixture).api.sourceSha, descendantSha);
  assert.equal(fs.readFileSync(fixture.putLog, 'utf8'), '');
});

test('reports a verified descendant that supersedes the write before read-back', (t) => {
  const descendantSha = 'cccccccccccccccccccccccccccccccccccccccc';
  const descendantDigest = `sha256:${'5'.repeat(64)}`;
  const fixture = makeFixture(t, {
    writeMode: 'commit-then-supersede',
    compareStatuses: { [`${descendantSha}...${sourceSha}`]: 'behind' },
    proofs: [
      { sha: sourceSha, digest: indexDigest, amd64Digest, arm64Digest, runId: '200', attempt: 1 },
      { sha: descendantSha, digest: descendantDigest, amd64Digest: `sha256:${'c'.repeat(64)}`, arm64Digest: `sha256:${'d'.repeat(64)}`, runId: '210', attempt: 1 },
    ],
  });

  const result = runSelection(fixture);

  assert.equal(result.status, 0, result.stderr);
  assert.equal(JSON.parse(fs.readFileSync(fixture.statePath, 'utf8')).outcome, 'validly superseded');
  assert.equal(readRemoteRecord(fixture).api.sourceSha, descendantSha);
  assert.equal(writeCount(fixture), 1);
});

test('fails closed when the selected source is unrelated to current main', (t) => {
  const fixture = makeFixture(t, { compareStatus: 'diverged' });

  const result = runSelection(fixture);

  assert.notEqual(result.status, 0);
  assert.match(result.stderr, /unrelated|ancestor/i);
  assert.equal(fs.readFileSync(fixture.putLog, 'utf8'), '');
});

test('ignores a completed publisher when its promoter did not succeed', (t) => {
  const fixture = makeFixture(t, { promoterFailure: true });

  const result = runSelection(fixture);

  assert.notEqual(result.status, 0);
  assert.match(result.stderr, /publisher and promoter jobs successfully/i);
  assert.equal(fs.readFileSync(fixture.putLog, 'utf8'), '');
});

test('writes a complete failure summary and error annotation', (t) => {
  const fixture = makeFixture(t, { evidenceAttempt: 2 });
  const summaryPath = path.join(fixture.directory, 'summary.md');
  fixture.env.GITHUB_STEP_SUMMARY = summaryPath;
  fixture.env.PITAKA_APPLY_OUTCOME = 'failure';

  const result = runSelection(fixture);
  const report = spawnSync('node', [commandPath, 'report'], {
    cwd: repositoryRoot,
    env: fixture.env,
    encoding: 'utf8',
  });

  assert.notEqual(result.status, 0);
  assert.equal(report.status, 0, report.stderr);
  assert.match(report.stdout, /::error title=API image selection handoff::/);
  assert.match(fs.readFileSync(summaryPath, 'utf8'), /Outcome: failed/);
  assert.match(fs.readFileSync(summaryPath, 'utf8'), /Recovery:/);
});

function makeFixture(t, options = {}) {
  const directory = fs.mkdtempSync(path.join(os.tmpdir(), 'pitaka-api-selection-test-'));
  t.after(() => fs.rmSync(directory, { recursive: true, force: true }));

  const bin = path.join(directory, 'bin');
  const origin = path.join(directory, 'deploy.git');
  const deployCheckout = path.join(directory, 'deploy-checkout');
  const evidencePath = path.join(directory, 'evidence.json');
  const putLog = path.join(directory, 'puts.log');
  const fixedManifestPath = path.join(directory, 'fixed.json');
  const pinnedManifestPath = path.join(directory, 'pinned.json');
  const aliasManifestPath = path.join(directory, 'alias.json');
  const statePath = path.join(directory, 'state.json');
  const mainShaPath = path.join(directory, 'main-sha.txt');
  const mainRefCountPath = path.join(directory, 'main-ref-count.txt');
  fs.mkdirSync(bin, { recursive: true });

  const initialDigest = options.apiDigest ?? (options.apiSha === sourceSha ? indexDigest : `sha256:${'2'.repeat(64)}`);
  const initialRecord = makeDeployRecord(options.apiSha ?? oldSha, initialDigest);
  const proofDefinitions = options.proofs ?? [{
    sha: sourceSha,
    digest: options.fixedDigest ?? indexDigest,
    amd64Digest,
    arm64Digest,
    evidenceArm64Digest: options.evidenceArm64Digest ?? arm64Digest,
    evidenceIndexDigest: options.evidenceDigest,
    runId: '200',
    attempt: 1,
    evidenceAttempt: options.evidenceAttempt ?? 1,
    expired: Boolean(options.expiredEvidence),
  }];
  const proofs = [];
  const fixedManifests = {};
  const pinnedManifests = {};
  for (const [index, definition] of proofDefinitions.entries()) {
    const proof = { ...definition, evidencePath: path.join(directory, `evidence-${definition.runId}.json`) };
    const evidence = makeEvidence({
      attempt: proof.evidenceAttempt ?? proof.attempt,
      sha: proof.sha,
      digest: proof.evidenceIndexDigest ?? proof.digest,
      runId: proof.runId,
      amd64Digest: proof.evidenceAmd64Digest ?? proof.amd64Digest,
      arm64Digest: proof.evidenceArm64Digest ?? proof.arm64Digest,
    });
    fs.writeFileSync(proof.evidencePath, JSON.stringify(evidence));
    const manifestPath = path.join(directory, `manifest-${index}.json`);
    fs.writeFileSync(manifestPath, JSON.stringify(makeManifest(proof.sha, proof.digest, proof.amd64Digest, proof.arm64Digest)));
    fixedManifests[proof.sha] = manifestPath;
    pinnedManifests[proof.digest] = manifestPath;
    proofs.push(proof);
  }
  fs.copyFileSync(proofs[0].evidencePath, evidencePath);
  const aliasManifest = makeManifest(sourceSha, `sha256:${'3'.repeat(64)}`, amd64Digest, options.aliasArmDigest ?? arm64Digest);
  fs.writeFileSync(fixedManifestPath, JSON.stringify(makeManifest(sourceSha, options.fixedDigest ?? indexDigest, amd64Digest, arm64Digest)));
  fs.writeFileSync(pinnedManifestPath, JSON.stringify(makeManifest(sourceSha, options.fixedDigest ?? indexDigest, amd64Digest, arm64Digest)));
  fs.writeFileSync(aliasManifestPath, JSON.stringify(aliasManifest));
  fs.writeFileSync(putLog, '');
  fs.writeFileSync(mainShaPath, sourceSha);
  fs.writeFileSync(mainRefCountPath, '0');

  git(directory, ['init', '--bare', '--initial-branch=main', origin]);
  const seed = path.join(directory, 'seed');
  fs.mkdirSync(seed);
  git(seed, ['init', '--initial-branch=main']);
  git(seed, ['config', 'user.name', 'pitaka-deploy[bot]']);
  git(seed, ['config', 'user.email', '123+pitaka-deploy[bot]@users.noreply.github.com']);
  fs.mkdirSync(path.join(seed, 'versions'));
  fs.mkdirSync(path.join(seed, 'pitaka_deploy'));
  fs.writeFileSync(path.join(seed, 'versions', 'local.json'), `${JSON.stringify(initialRecord, null, 2)}\n`);
  fs.writeFileSync(
    path.join(seed, 'pitaka_deploy', 'versions.mjs'),
    `export function validateVersion(record) {\n` +
      `  if (record.schemaVersion !== 1 || !record.api || !record.web || !record.images) {\n` +
      `    throw new Error('invalid canonical version record');\n` +
      `  }\n` +
      `}\n`,
  );
  git(seed, ['add', '.']);
  git(seed, ['commit', '-m', 'Initialize the local version record']);
  git(seed, ['remote', 'add', 'origin', origin]);
  git(seed, ['push', '-u', 'origin', 'main']);
  git(directory, ['clone', origin, deployCheckout]);
  git(deployCheckout, ['config', 'user.name', 'pitaka-deploy[bot]']);
  git(deployCheckout, ['config', 'user.email', '123+pitaka-deploy[bot]@users.noreply.github.com']);
  if (options.manualApiEdit) {
    const manuallySelected = makeDeployRecord('2222222222222222222222222222222222222222', `sha256:${'e'.repeat(64)}`);
    manuallySelected.configuration = initialRecord.configuration;
    manuallySelected.web = initialRecord.web;
    manuallySelected.images = initialRecord.images;
    manuallySelected.anotherNonApiField = initialRecord.anotherNonApiField;
    fs.writeFileSync(path.join(deployCheckout, 'versions', 'local.json'), `${JSON.stringify(manuallySelected, null, 2)}\n`);
    git(deployCheckout, ['config', 'user.name', 'operator']);
    git(deployCheckout, ['config', 'user.email', 'operator@example.com']);
    git(deployCheckout, ['add', 'versions/local.json']);
    git(deployCheckout, ['commit', '-m', 'Pin API image manually']);
    git(deployCheckout, ['push', 'origin', 'main']);
  }

  writeExecutable(path.join(bin, 'docker'), dockerStub);
  writeExecutable(path.join(bin, 'gh'), ghStub);

  return {
    directory,
    bin,
    origin,
    deployCheckout,
    evidencePath,
    fixedManifestPath,
    pinnedManifestPath,
    aliasManifestPath,
    statePath,
    putLog,
    initialRecord,
    env: {
      ...process.env,
      PATH: `${bin}${path.delimiter}${process.env.PATH}`,
      GITHUB_REPOSITORY: 'itsdevjimbo/pitaka',
      GITHUB_TOKEN: 'source-token-test',
      DEPLOY_APP_TOKEN: 'deploy-token-test',
      DEPLOY_APP_SLUG: 'pitaka-deploy',
      PITAKA_TRIGGER_RUN_ID: '100',
      PITAKA_TRIGGER_RUN_ATTEMPT: '1',
      GITHUB_RUN_ID: '400',
      GITHUB_RUN_ATTEMPT: '1',
      PITAKA_DEPLOY_CHECKOUT: deployCheckout,
      PITAKA_HANDOFF_STATE: statePath,
      PITAKA_TEST_ORIGIN: origin,
    PITAKA_TEST_PUT_LOG: putLog,
    PITAKA_TEST_MAIN_SHA_FILE: mainShaPath,
    PITAKA_TEST_MAIN_REF_COUNT_FILE: mainRefCountPath,
      PITAKA_TEST_EVIDENCE: evidencePath,
      PITAKA_TEST_SOURCE_SHA: sourceSha,
      PITAKA_TEST_ADVANCE_MAIN_SHA: options.advanceMainAfterWriteSha ?? '',
      PITAKA_TEST_TRIGGER_SHA: options.triggerSha ?? sourceSha,
      PITAKA_TEST_OLD_SHA: oldSha,
      PITAKA_TEST_INDEX_DIGEST: indexDigest,
      PITAKA_TEST_AMD64_DIGEST: amd64Digest,
      PITAKA_TEST_ARM64_DIGEST: arm64Digest,
      PITAKA_TEST_FIXED_MANIFEST: fixedManifestPath,
      PITAKA_TEST_PINNED_MANIFEST: pinnedManifestPath,
      PITAKA_TEST_FIXED_MANIFESTS: JSON.stringify(fixedManifests),
      PITAKA_TEST_PINNED_MANIFESTS: JSON.stringify(pinnedManifests),
      PITAKA_TEST_PROOFS: JSON.stringify(proofs),
      PITAKA_TEST_ALIAS_MANIFEST: aliasManifestPath,
      PITAKA_TEST_REPO_ID: '99',
      PITAKA_TEST_EXPIRED: String(Boolean(options.expiredEvidence)),
      PITAKA_TEST_MISSING_EVIDENCE: String(Boolean(options.missingEvidence)),
      PITAKA_TEST_WRITE_MODE: options.writeMode ?? '',
      PITAKA_TEST_COMPARE_STATUS: options.compareStatus ?? 'ahead',
      PITAKA_TEST_COMPARE_STATUSES: JSON.stringify(options.compareStatuses ?? {}),
      PITAKA_TEST_PROMOTER_FAILURE: String(Boolean(options.promoterFailure)),
      PITAKA_TEST_MANUAL_API_EDIT: String(Boolean(options.manualApiEdit)),
      PITAKA_TEST_ARTIFACT_ID: '501',
      PITAKA_TEST_PUBLISHER_RUN_ID: '200',
      PITAKA_TEST_RUN_ATTEMPT: '1',
    },
  };
}

function makeDeployRecord(apiSha, digest) {
  return {
    schemaVersion: 1,
    environment: 'local',
    configuration: {
      revision: 'pitaka-local-compose-v1',
      origin: 'http://localhost:8080',
      secretReferences: ['secrets/local.env'],
      compatibilityNotes: 'Keep this field when updating only API.',
    },
    web: { sourceSha: oldSha, selected: 'web-unchanged' },
    api: {
      repository: 'jimbodev0530/pitaka-api',
      sourceSha: apiSha,
      sourceTag: `sha-${apiSha}`,
      image: `jimbodev0530/pitaka-api@${digest}`,
      digest,
      platform: 'linux/arm64',
    },
    images: { nginx: {}, mysql: {}, seaweedfs: {}, smtp4dev: {}, awscli: {} },
    anotherNonApiField: { keep: true },
  };
}

function makeEvidence({ attempt = 1, sha = sourceSha, digest = indexDigest, runId = '200', amd64Digest: amd = amd64Digest, arm64Digest: arm = arm64Digest } = {}) {
  return {
    schemaVersion: 1,
    source: { repository: 'itsdevjimbo/pitaka', sha },
    image: {
      repository: 'jimbodev0530/pitaka-api',
      fixedTag: `sha-${sha}`,
      indexDigest: digest,
      platforms: {
        'linux/amd64': { childDigest: amd, migration: 'success', apiReadiness: 'success' },
        'linux/arm64': { childDigest: arm, migration: 'success', apiReadiness: 'success' },
      },
    },
    run: { id: String(runId), attempt },
  };
}

function makeManifest(sha, digest, amd64, arm64) {
  return {
    schemaVersion: 2,
    digest,
    annotations: { 'org.opencontainers.image.revision': sha },
    manifests: [
      { digest: amd64, platform: { os: 'linux', architecture: 'amd64' } },
      { digest: arm64, platform: { os: 'linux', architecture: 'arm64' } },
    ],
  };
}

function runSelection(fixture) {
  return spawnSync('node', [commandPath, 'apply'], {
    cwd: repositoryRoot,
    env: fixture.env,
    encoding: 'utf8',
    timeout: 30_000,
  });
}

function readRemoteRecord(fixture) {
  const output = git(fixture.deployCheckout, ['fetch', 'origin', 'main']);
  void output;
  return JSON.parse(git(fixture.deployCheckout, ['show', 'FETCH_HEAD:versions/local.json']));
}

function writeCount(fixture) {
  return fs.readFileSync(fixture.putLog, 'utf8').split('\n').filter(Boolean).length;
}

function git(cwd, args) {
  const result = spawnSync('git', args, { cwd, encoding: 'utf8' });
  assert.equal(result.status, 0, `git ${args.join(' ')} failed: ${result.stderr}`);
  return result.stdout.trim();
}

function writeExecutable(filename, contents) {
  fs.writeFileSync(filename, contents);
  fs.chmodSync(filename, 0o755);
}

const dockerStub = `#!/usr/bin/env node
const fs = require('node:fs');
const args = process.argv.slice(2);
if (args[0] !== 'buildx' || args[1] !== 'imagetools' || args[2] !== 'inspect') process.exit(2);
const reference = args.at(-1);
const fixed = JSON.parse(process.env.PITAKA_TEST_FIXED_MANIFESTS);
const pinned = JSON.parse(process.env.PITAKA_TEST_PINNED_MANIFESTS);
const file = reference.endsWith(':main') ? process.env.PITAKA_TEST_ALIAS_MANIFEST :
  reference.includes('@') ? (pinned[reference.split('@').at(-1)] ?? process.env.PITAKA_TEST_PINNED_MANIFEST) :
  (fixed[reference.split(':').at(-1).replace(/^sha-/, '')] ?? process.env.PITAKA_TEST_FIXED_MANIFEST);
process.stdout.write(fs.readFileSync(file, 'utf8'));
`;

const ghStub = `#!/usr/bin/env node
const fs = require('node:fs');
const os = require('node:os');
const path = require('node:path');
const { spawnSync } = require('node:child_process');

const args = process.argv.slice(2);
const json = (value) => process.stdout.write(JSON.stringify(value));
const respond = (value) => { json(value); process.exit(0); };
const sourceSha = process.env.PITAKA_TEST_SOURCE_SHA;
const repo = 'itsdevjimbo/pitaka';
const api = args[0] === 'api';
const endpoint = api ? args.find((part) => part.startsWith('repos/') || part.startsWith('/users/')) : '';
const proofs = JSON.parse(process.env.PITAKA_TEST_PROOFS);
const proofForRun = (id) => proofs.find((proof) => String(proof.runId) === String(id));
const setAliasFor = (proof) => fs.writeFileSync(process.env.PITAKA_TEST_ALIAS_MANIFEST, JSON.stringify({
  schemaVersion: 2,
  digest: 'sha256:' + '7'.repeat(64),
  annotations: { 'org.opencontainers.image.revision': proof.sha },
  manifests: [
    { digest: proof.amd64Digest, platform: { os: 'linux', architecture: 'amd64' } },
    { digest: proof.arm64Digest, platform: { os: 'linux', architecture: 'arm64' } },
  ],
}));
const run = (id) => {
  const proof = proofForRun(id);
  const isTrigger = String(id) === process.env.PITAKA_TRIGGER_RUN_ID;
  const isSelection = String(id) === '350';
  return {
    id: Number(id), run_attempt: isSelection ? 1 : (proof?.attempt ?? 1), workflow_id: isSelection ? 44 : 42,
    name: isSelection ? 'Select local API image' : 'Build and Deploy',
    path: isSelection ? '.github/workflows/select-local-api-image.yml' : '.github/workflows/build.yml',
    event: isSelection ? 'workflow_run' : 'workflow_run',
    conclusion: isTrigger || isSelection ? 'success' : (process.env.PITAKA_TEST_PUBLISHER_CONCLUSION || 'success'),
    head_branch: 'main', head_sha: proof?.sha ?? (isTrigger ? process.env.PITAKA_TEST_TRIGGER_SHA : sourceSha),
    head_repository: { full_name: repo }, repository: { full_name: repo, id: 99 },
    created_at: '2026-09-30T00:00:00Z',
  };
};
const successJob = (name, steps = []) => ({ name, conclusion: 'success', completed_at: '2026-09-30T00:00:02Z', steps });
const publishJobs = [successJob('Publish API image', [successJob('Upload API smoke evidence')])];
const allJobs = [
  successJob('Publish API image', [successJob('Upload API smoke evidence')]),
  { ...successJob('Advance the moving API image tag'), conclusion: process.env.PITAKA_TEST_PROMOTER_FAILURE === 'true' ? 'failure' : 'success' },
];

if (!api && args[0] === 'run' && args[1] === 'download') {
  const runId = args[2];
  const proof = proofForRun(runId);
  if (!proof) process.exit(2);
  const outputIndex = args.indexOf('--dir');
  const nameIndex = args.indexOf('--name');
  const artifactName = args[nameIndex + 1];
  fs.mkdirSync(args[outputIndex + 1], { recursive: true });
  fs.copyFileSync(proof.evidencePath, path.join(args[outputIndex + 1], artifactName + '.json'));
  process.exit(0);
}

if (!api) process.exit(2);
if (endpoint.includes('/actions/workflows/build.yml/runs?')) {
  const sha = new URLSearchParams(endpoint.split('?')[1]).get('head_sha');
  const matches = proofs.filter((proof) => proof.sha === sha).map((proof) => run(proof.runId));
  respond({ total_count: matches.length, workflow_runs: matches });
}
if (endpoint.includes('/actions/workflows/tests.yml/runs?')) {
  const sha = new URLSearchParams(endpoint.split('?')[1]).get('head_sha');
  respond({ total_count: 1, workflow_runs: [{
    id: 300, workflow_id: 43, name: 'Code Quality and Tests', event: 'push', conclusion: 'success',
    head_branch: 'main', head_sha: sha, head_repository: { full_name: repo },
    created_at: '2026-09-29T23:59:00Z',
  }] });
}
if (endpoint.endsWith('/actions/workflows/build.yml')) {
  respond({ id: 42, name: 'Build and Deploy', path: '.github/workflows/build.yml' });
}
if (endpoint.endsWith('/actions/workflows/tests.yml')) {
  respond({ id: 43, name: 'Code Quality and Tests', path: '.github/workflows/tests.yml' });
}
if (endpoint.endsWith('/actions/workflows/select-local-api-image.yml')) {
  respond({ id: 44, name: 'Select local API image', path: '.github/workflows/select-local-api-image.yml' });
}
if (endpoint.includes('/git/ref/heads/main')) {
  let count = Number(fs.readFileSync(process.env.PITAKA_TEST_MAIN_REF_COUNT_FILE, 'utf8')) + 1;
  fs.writeFileSync(process.env.PITAKA_TEST_MAIN_REF_COUNT_FILE, String(count));
  if (process.env.PITAKA_TEST_WRITE_MODE === 'advance-main-before-recheck' && count === 2) {
    const nextProof = proofs.find((proof) => proof.sha === process.env.PITAKA_TEST_ADVANCE_MAIN_SHA);
    if (!nextProof) throw new Error('new live-main proof is missing');
    fs.writeFileSync(process.env.PITAKA_TEST_MAIN_SHA_FILE, nextProof.sha);
    setAliasFor(nextProof);
  }
  respond({ object: { sha: fs.readFileSync(process.env.PITAKA_TEST_MAIN_SHA_FILE, 'utf8').trim() } });
}
if (endpoint.includes('/compare/')) {
  const comparison = endpoint.split('/compare/')[1];
  const statuses = JSON.parse(process.env.PITAKA_TEST_COMPARE_STATUSES);
  respond({ status: statuses[comparison] || process.env.PITAKA_TEST_COMPARE_STATUS || 'ahead', ahead_by: 1, behind_by: 0 });
}
const jobsMatch = endpoint.match(/\\/actions\\/runs\\/([0-9]+)\\/attempts\\/([0-9]+)\\/jobs/);
if (jobsMatch) {
  const runId = jobsMatch[1];
  if (runId === process.env.PITAKA_TRIGGER_RUN_ID) respond({ total_count: 2, jobs: allJobs });
  if (runId === '350') respond({ total_count: 1, jobs: [successJob('Select verified current-main API image')] });
  const proof = proofForRun(runId);
  if (proof) respond({ total_count: 1, jobs: publishJobs });
}
const attemptMatch = endpoint.match(/\\/actions\\/runs\\/([0-9]+)\\/attempts\\/([0-9]+)$/);
if (attemptMatch) respond(run(attemptMatch[1]));
const artifactMatch = endpoint.match(/\\/actions\\/runs\\/([0-9]+)\\/artifacts/);
if (artifactMatch) {
  const proof = proofForRun(artifactMatch[1]);
  const artifacts = !proof || process.env.PITAKA_TEST_MISSING_EVIDENCE === 'true' ? [] : [{
    id: Number(proof.artifactId ?? 501),
    name: 'api-smoke-evidence-' + proof.runId + '-' + proof.attempt,
    expired: Boolean(proof.expired), size_in_bytes: 1024,
    expires_at: proof.expired ? '2000-10-14T00:00:00Z' : '2099-10-14T00:00:00Z',
    workflow_run: { id: Number(proof.runId), repository_id: 99, head_sha: proof.sha, head_branch: 'main', head_repository: { full_name: repo } },
  }];
  respond({ total_count: artifacts.length, artifacts });
}
if (endpoint.includes('/users/pitaka-deploy%5Bbot%5D')) respond({ id: 123 });
if (endpoint.includes('/users/pitaka-deploy[bot]')) respond({ id: 123 });

const method = args.findIndex((part) => part === '--method' || part === '-X');
if (method >= 0 && args[method + 1] === 'PUT' && endpoint.includes('/contents/versions/local.json')) {
  const parsedFields = {};
  for (let index = 0; index < args.length; index += 1) {
    if (args[index] === '--field' || args[index] === '-f') {
      const [key, ...value] = args[++index].split('=');
      const text = value.join('=');
      const nested = key.match(/^(author|committer)\\[(name|email)\\]$/);
      if (nested) {
        parsedFields[nested[1]] ??= {};
        parsedFields[nested[1]][nested[2]] = text;
      } else {
        parsedFields[key] = text;
      }
    }
  }
  const loggedFields = { ...parsedFields, content: '[redacted test payload]' };
  fs.appendFileSync(process.env.PITAKA_TEST_PUT_LOG, JSON.stringify(loggedFields) + '\\n');
  const writeMode = process.env.PITAKA_TEST_WRITE_MODE;
  const attemptNumber = fs.readFileSync(process.env.PITAKA_TEST_PUT_LOG, 'utf8').trim().split('\\n').length;
  if (writeMode === 'protected-branch') {
    process.stderr.write('gh: Protected branch update failed for refs/heads/main (HTTP 403)\\n');
    process.exit(1);
  }
  if (writeMode === 'always-503') {
    process.stderr.write('gh: server unavailable (HTTP 503)\\n');
    process.exit(1);
  }
  if (writeMode === 'app-unauthorized') {
    process.stderr.write('gh: Resource not accessible by integration (HTTP 403)\\n');
    process.exit(1);
  }
  const temp = fs.mkdtempSync(path.join(os.tmpdir(), 'pitaka-fake-gh-'));
  const cloned = path.join(temp, 'repo');
  const clone = spawnSync('git', ['clone', '-q', process.env.PITAKA_TEST_ORIGIN, cloned], { encoding: 'utf8' });
  if (clone.status !== 0) throw new Error(clone.stderr);
  if (writeMode === 'concurrent-non-api' && attemptNumber === 1) {
    const recordPath = path.join(cloned, 'versions/local.json');
    const concurrent = JSON.parse(fs.readFileSync(recordPath, 'utf8'));
    concurrent.configuration.compatibilityNotes += ' Concurrent web edit.';
    fs.writeFileSync(recordPath, JSON.stringify(concurrent, null, 2) + '\\n');
    const update = spawnSync('git', ['add', 'versions/local.json'], { cwd: cloned, encoding: 'utf8' });
    if (update.status !== 0) throw new Error(update.stderr);
    const commit = spawnSync('git', ['-c', 'user.name=operator', '-c', 'user.email=operator@example.com', 'commit', '-m', 'Update web compatibility note'], { cwd: cloned, encoding: 'utf8' });
    if (commit.status !== 0) throw new Error(commit.stderr);
    const push = spawnSync('git', ['push', 'origin', 'main'], { cwd: cloned, encoding: 'utf8' });
    if (push.status !== 0) throw new Error(push.stderr);
    fs.rmSync(temp, { recursive: true, force: true });
    process.stderr.write('gh: file changed since it was read (HTTP 409)\\n');
    process.exit(1);
  }
  if (writeMode === 'concurrent-api' && attemptNumber === 1) {
    const recordPath = path.join(cloned, 'versions/local.json');
    const concurrent = JSON.parse(fs.readFileSync(recordPath, 'utf8'));
    concurrent.api.sourceSha = '2222222222222222222222222222222222222222';
    concurrent.api.sourceTag = 'sha-' + concurrent.api.sourceSha;
    concurrent.api.digest = 'sha256:' + 'f'.repeat(64);
    concurrent.api.image = 'jimbodev0530/pitaka-api@' + concurrent.api.digest;
    fs.writeFileSync(recordPath, JSON.stringify(concurrent, null, 2) + '\\n');
    const update = spawnSync('git', ['add', 'versions/local.json'], { cwd: cloned, encoding: 'utf8' });
    if (update.status !== 0) throw new Error(update.stderr);
    const commit = spawnSync('git', ['-c', 'user.name=operator', '-c', 'user.email=operator@example.com', 'commit', '-m', 'Pin another API image'], { cwd: cloned, encoding: 'utf8' });
    if (commit.status !== 0) throw new Error(commit.stderr);
    const push = spawnSync('git', ['push', 'origin', 'main'], { cwd: cloned, encoding: 'utf8' });
    if (push.status !== 0) throw new Error(push.stderr);
  }
  const currentBlob = spawnSync('git', ['rev-parse', 'main:versions/local.json'], { cwd: cloned, encoding: 'utf8' }).stdout.trim();
  if (currentBlob !== parsedFields.sha) {
    process.stderr.write('gh: file changed since it was read (HTTP 409)\\n');
    process.exit(1);
  }
  fs.writeFileSync(path.join(cloned, 'versions/local.json'), Buffer.from(parsedFields.content, 'base64'));
  const commit = spawnSync('git', [
    '-c', 'user.name=' + parsedFields.author.name,
    '-c', 'user.email=' + parsedFields.author.email,
    '-c', 'committer.name=' + parsedFields.committer.name,
    '-c', 'committer.email=' + parsedFields.committer.email,
    'commit', '-am', parsedFields.message,
  ], { cwd: cloned, encoding: 'utf8' });
  if (commit.status !== 0) throw new Error(commit.stderr);
  const push = spawnSync('git', ['push', 'origin', 'main'], { cwd: cloned, encoding: 'utf8' });
  if (push.status !== 0) throw new Error(push.stderr);
  if (writeMode === 'commit-then-supersede') {
    const supersedingProof = proofs.find((proof) => proof.sha !== sourceSha);
    if (!supersedingProof) throw new Error('superseding proof is missing');
    const next = JSON.parse(fs.readFileSync(path.join(cloned, 'versions/local.json'), 'utf8'));
    next.api = {
      repository: 'jimbodev0530/pitaka-api',
      sourceSha: supersedingProof.sha,
      sourceTag: 'sha-' + supersedingProof.sha,
      image: 'jimbodev0530/pitaka-api@' + supersedingProof.digest,
      digest: supersedingProof.digest,
      platform: next.api.platform,
    };
    fs.writeFileSync(path.join(cloned, 'versions/local.json'), JSON.stringify(next, null, 2) + '\\n');
    const update = spawnSync('git', ['add', 'versions/local.json'], { cwd: cloned, encoding: 'utf8' });
    if (update.status !== 0) throw new Error(update.stderr);
    const message = [
      'Select verified API image for local deployment',
      '',
      'Handoff: Select local API image run 350, attempt 1',
      'Trigger: Build and Deploy run 120, attempt 1',
      'Publisher: Build and Deploy run ' + supersedingProof.runId + ', attempt ' + supersedingProof.attempt,
      'API source SHA: ' + supersedingProof.sha,
      'API index digest: ' + supersedingProof.digest,
    ].join('\\n');
    const followup = spawnSync('git', [
      '-c', 'user.name=pitaka-deploy[bot]',
      '-c', 'user.email=123+pitaka-deploy[bot]@users.noreply.github.com',
      'commit', '-m', message,
    ], { cwd: cloned, encoding: 'utf8' });
    if (followup.status !== 0) throw new Error(followup.stderr);
    const followupPush = spawnSync('git', ['push', 'origin', 'main'], { cwd: cloned, encoding: 'utf8' });
    if (followupPush.status !== 0) throw new Error(followupPush.stderr);
  }
  if (writeMode === 'commit-then-main-advance' && attemptNumber === 1) {
    const nextProof = proofs.find((proof) => proof.sha === process.env.PITAKA_TEST_ADVANCE_MAIN_SHA);
    if (!nextProof) throw new Error('new live-main proof is missing');
    fs.writeFileSync(process.env.PITAKA_TEST_MAIN_SHA_FILE, nextProof.sha);
    setAliasFor(nextProof);
  }
  fs.rmSync(temp, { recursive: true, force: true });
  if (writeMode === 'commit-then-503') {
    process.stderr.write('gh: response lost after commit (HTTP 503)\\n');
    process.exit(1);
  }
  respond({ commit: { sha: 'test-commit' } });
}

process.stderr.write('unexpected gh request: ' + args.join(' ') + '\\n');
process.exit(2);
`;
