import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';
import { isDeepStrictEqual } from 'node:util';
import { spawnSync } from 'node:child_process';

const SOURCE_REPOSITORY = 'itsdevjimbo/pitaka';
const DEPLOY_REPOSITORY = 'itsdevjimbo/pitaka-deploy';
const API_IMAGE_REPOSITORY = 'jimbodev0530/pitaka-api';
const BUILD_WORKFLOW_PATH = '.github/workflows/build.yml';
const TEST_WORKFLOW_PATH = '.github/workflows/tests.yml';
const SELECTION_WORKFLOW_PATH = '.github/workflows/select-local-api-image.yml';
const BUILD_WORKFLOW_NAME = 'Build and Deploy';
const TEST_WORKFLOW_NAME = 'Code Quality and Tests';
const PUBLISH_JOB_NAME = 'Publish API image';
const PROMOTE_JOB_NAME = 'Advance the moving API image tag';
const UPLOAD_STEP_NAME = 'Upload API smoke evidence';
const SELECTION_JOB_NAME = 'Select verified current-main API image';
const EVIDENCE_MAX_BYTES = 16_384;
const API_FIELDS = ['repository', 'sourceSha', 'sourceTag', 'image', 'digest', 'platform'];
const REQUIRED_PLATFORMS = ['linux/amd64', 'linux/arm64'];
const FULL_SHA = /^[a-f0-9]{40}$/;
const SHA256 = /^sha256:[a-f0-9]{64}$/;
const MAX_WRITE_ATTEMPTS = 3;

class SelectionError extends Error {
  constructor(message, { recovery, retryable = false, status } = {}) {
    super(message);
    this.name = 'SelectionError';
    this.recovery = recovery;
    this.retryable = retryable;
    this.status = status;
  }
}

export async function runSelectionPhase(phase) {
  const statePath = getStatePath();
  const state = readState(statePath) ?? emptyState();
  try {
    const sourceRepository = process.env.PITAKA_SOURCE_REPOSITORY ?? SOURCE_REPOSITORY;
    if (sourceRepository !== SOURCE_REPOSITORY || process.env.GITHUB_REPOSITORY !== SOURCE_REPOSITORY) {
      throw new SelectionError(`The handoff must run in ${SOURCE_REPOSITORY}.`);
    }

    const trigger = await resolveTrigger();
    state.trigger = trigger;
    state.outcome = 'verifying';
    saveState(statePath, state);

    const workflowInfo = await loadWorkflowInfo(sourceRepository);
    const triggerRun = await verifyTriggerRun(sourceRepository, workflowInfo, trigger);
    state.trigger = {
      ...trigger,
      headSha: triggerRun.head_sha,
      workflow: BUILD_WORKFLOW_NAME,
    };

    if (phase === 'verify') {
      const candidate = await resolveCandidate(sourceRepository, workflowInfo, true);
      state.publisher = candidate.publisher;
      state.sourceSha = candidate.identity.sourceRevision;
      state.indexDigest = candidate.identity.indexDigest;
      state.outcome = 'eligible';
      state.recovery = 'The handoff is verified and waiting for deploy-repository access.';
      saveState(statePath, state);
      process.stdout.write(`Verified current-main API image ${state.sourceSha}@${state.indexDigest}.\n`);
      return;
    }

    if (!process.env.DEPLOY_APP_TOKEN) {
      throw new SelectionError('The deploy-scoped GitHub App token is unavailable.', {
        recovery: 'Check the shared deploy App installation and the repository Actions App ID/private-key settings.',
      });
    }
    const deployCheckout = process.env.PITAKA_DEPLOY_CHECKOUT;
    if (!deployCheckout || !fs.existsSync(path.join(deployCheckout, '.git'))) {
      throw new SelectionError('The pitaka-deploy checkout is unavailable.', {
        recovery: 'Check the deploy-scoped App Contents permission and retry this handoff.',
      });
    }

    const appIdentity = await resolveAppIdentity(sourceRepository);
    const outcome = await selectAndUpdate(sourceRepository, workflowInfo, trigger, state, deployCheckout, appIdentity);
    Object.assign(state, outcome);
    saveState(statePath, state);
    process.stdout.write(`API image selection: ${state.outcome}.\n`);
  } catch (error) {
    state.outcome = 'failed';
    state.error = publicError(error);
    state.recovery = error.recovery ?? 'Review the handoff job details, correct the failed provenance or deploy-record condition, then dispatch reconciliation with a successful Build and Deploy run ID and attempt.';
    saveState(statePath, state);
    throw error;
  }
}

export function writeFinalSummary() {
  const statePath = getStatePath();
  const state = readState(statePath) ?? emptyState();
  const phaseOutcomes = [
    process.env.PITAKA_VERIFY_OUTCOME,
    process.env.PITAKA_APP_TOKEN_OUTCOME,
    process.env.PITAKA_DEPLOY_CHECKOUT_OUTCOME,
    process.env.PITAKA_APPLY_OUTCOME,
  ];
  const failedStep = phaseOutcomes.some((value) => value === 'failure' || value === 'cancelled');
  const incomplete = !['applied', 'already current', 'validly superseded', 'failed'].includes(state.outcome);
  if (incomplete && (failedStep || phaseOutcomes.some((value) => value === 'skipped'))) {
    state.outcome = 'failed';
    state.error = state.error ?? 'The handoff stopped after candidate verification and before a final deploy selection was read back.';
    state.recovery = state.recovery ?? 'Review the failed App or deploy checkout step, then rerun reconciliation with the same successful promoter run ID and attempt.';
  }

  const summaryPath = process.env.GITHUB_STEP_SUMMARY;
  if (summaryPath) {
    fs.appendFileSync(summaryPath, formatSummary(state));
  }
  if (state.outcome === 'failed') {
    const message = state.error ?? 'The API image selection handoff did not reach a verified final state.';
    const annotation = message.replace(/%/g, '%25').replace(/\r/g, '%0D').replace(/\n/g, '%0A');
    process.stdout.write(`::error title=API image selection handoff::${annotation}\n`);
  }
}

async function selectAndUpdate(sourceRepository, workflowInfo, trigger, state, deployCheckout, appIdentity) {
  let writeAttempts = 0;
  let staleSnapshots = 0;
  let pendingWrite = null;
  const triggerInfo = state.trigger;

  while (true) {
    const candidate = await resolveCandidate(sourceRepository, workflowInfo, true);
    state.publisher = candidate.publisher;
    state.sourceSha = candidate.identity.sourceRevision;
    state.indexDigest = candidate.identity.indexDigest;

    const snapshot = await readDeploySnapshot(deployCheckout);
    try {
      state.previousApi ??= snapshot.record.api;
      await validateApiChangeHistory(sourceRepository, workflowInfo, snapshot, appIdentity, triggerInfo);

      const evaluation = await evaluateSelection(sourceRepository, workflowInfo, snapshot.record, candidate, state);
      state.finalApi = evaluation.record.api;
      if (evaluation.outcome) {
        const ownWrite = pendingWrite && isDeepStrictEqual(pendingWrite, evaluation.record.api);
        state.outcome = ownWrite ? 'applied' : evaluation.outcome;
        state.writeAttempts = writeAttempts;
        state.recovery = outcomeRecovery(state.outcome);
        return state;
      }

      if (writeAttempts >= MAX_WRITE_ATTEMPTS) {
        throw new SelectionError(`The deploy selection did not converge after ${MAX_WRITE_ATTEMPTS} conditional writes.`, {
          recovery: 'Inspect the latest versions/local.json and main history, then dispatch reconciliation with a successful promoter run ID and attempt.',
        });
      }

      const nextRecord = { ...snapshot.record, api: evaluation.api };
      await validateCompleteRecord(snapshot.validatorPath, nextRecord, snapshot.tempDirectory);
      const content = `${JSON.stringify(nextRecord, null, 2)}\n`;
      const message = makeCommitMessage(trigger, candidate.publisher, candidate.identity);

      const latestCandidate = await resolveCandidate(sourceRepository, workflowInfo, true);
      if (
        !sameImageIdentity(latestCandidate.identity, candidate.identity) ||
        !isDeepStrictEqual(latestCandidate.publisher, candidate.publisher)
      ) {
        staleSnapshots += 1;
        if (staleSnapshots > MAX_WRITE_ATTEMPTS) {
          throw new SelectionError('Live main or its verified publisher proof kept changing before the conditional write.', {
            recovery: 'Wait for main publication to settle, then dispatch reconciliation with a successful Build and Deploy run ID and attempt.',
          });
        }
        continue;
      }

      const latestSnapshot = await readDeploySnapshot(deployCheckout);
      let deployStateUnchanged;
      try {
        deployStateUnchanged = latestSnapshot.mainCommit === snapshot.mainCommit;
      } finally {
        fs.rmSync(latestSnapshot.tempDirectory, { recursive: true, force: true });
      }
      if (!deployStateUnchanged) {
        staleSnapshots += 1;
        if (staleSnapshots > MAX_WRITE_ATTEMPTS) {
          throw new SelectionError('The deploy record kept changing before the conditional write.', {
            recovery: 'Wait for pitaka-deploy/main edits to settle, then dispatch reconciliation with a successful Build and Deploy run ID and attempt.',
          });
        }
        continue;
      }
      staleSnapshots = 0;

      writeAttempts += 1;
      state.writeAttempts = writeAttempts;
      state.finalApi = evaluation.api;
      state.outcome = 'writing';
      saveState(getStatePath(), state);

      try {
        await writeDeployRecord(snapshot.blobSha, content, message, appIdentity);
        pendingWrite = evaluation.api;
      } catch (error) {
        if (!error.retryable) {
          throw error;
        }
        pendingWrite = error.status === undefined || error.status >= 500 ? evaluation.api : null;
        await delay(Math.min(250 * writeAttempts, 1_000));
      }

      // The next pass re-reads candidate identities and the latest deploy record.
      // This read-back resolves accepted writes and uncertain responses before a retry.
    } finally {
      fs.rmSync(snapshot.tempDirectory, { recursive: true, force: true });
    }
  }
}

async function evaluateSelection(sourceRepository, workflowInfo, record, candidate, state) {
  const selected = record.api;
  if (!isExactApiObject(selected)) {
    throw new SelectionError('The current deploy record API selection must contain exactly the six supported fields.', {
      recovery: 'Review the API section of versions/local.json and restore the canonical six-field schema before retrying.',
    });
  }
  const candidateApi = makeApiSelection(candidate.identity, selected.platform);

  if (selected.sourceSha === candidateApi.sourceSha) {
    if (selected.digest !== candidateApi.digest) {
      throw new SelectionError(`The deploy record selects ${selected.sourceSha} with a different digest than the verified fixed tag.`, {
        recovery: 'Treat this same-SHA digest mismatch as an image-selection incident and review the fixed tag and deploy history before retrying.',
      });
    }
    if (!isDeepStrictEqual(selected, candidateApi)) {
      throw new SelectionError('The deploy record has a same-SHA API selection that differs from the exact verified selection.', {
        recovery: 'Review all six API selection fields in versions/local.json before retrying.',
      });
    }
    return { record, outcome: 'already current' };
  }

  const relation = await compareSourceHistory(sourceRepository, selected.sourceSha, candidateApi.sourceSha);
  if (relation === 'selected-is-ancestor') {
    return { record, api: candidateApi };
  }
  if (relation === 'candidate-is-ancestor') {
    const superseding = await verifyImageProof(sourceRepository, workflowInfo, selected.sourceSha, selected.digest, false);
    state.supersedingPublisher = superseding.publisher;
    return { record, outcome: 'validly superseded' };
  }
  throw new SelectionError(`The selected API source ${selected.sourceSha} is unrelated to current main ${candidateApi.sourceSha}, or its history cannot be compared.`, {
    recovery: 'Leave the deploy record unchanged and review the source history rewrite or unrelated selection before retrying.',
  });
}

async function resolveCandidate(sourceRepository, workflowInfo, requireAlias) {
  const branch = await ghApi(`repos/${sourceRepository}/git/ref/heads/main`);
  const liveSha = branch?.object?.sha;
  requireFullSha(liveSha, 'live API main SHA');
  const proof = await verifyImageProof(sourceRepository, workflowInfo, liveSha, undefined, requireAlias);
  return { ...proof, liveSha };
}

async function verifyImageProof(sourceRepository, workflowInfo, sha, expectedDigest, requireAlias) {
  requireFullSha(sha, 'selected API source SHA');
  const fixedTag = `sha-${sha}`;
  const fixedIdentity = inspectImage(`${API_IMAGE_REPOSITORY}:${fixedTag}`, sha);
  const pinnedIdentity = inspectImage(`${API_IMAGE_REPOSITORY}@${fixedIdentity.indexDigest}`, sha);
  if (!sameImageIdentity(fixedIdentity, pinnedIdentity)) {
    throw new SelectionError(`The fixed API tag ${fixedTag} and its digest-pinned index identify different platform images.`, {
      recovery: 'Require a new successful publisher attempt for this exact fixed tag, then retry the handoff.',
    });
  }
  if (expectedDigest && fixedIdentity.indexDigest !== expectedDigest) {
    throw new SelectionError(`The fixed API tag ${fixedTag} no longer has the digest recorded in the deploy selection.`, {
      recovery: 'Review the fixed-tag movement as an image-publication incident; do not replace the selected digest automatically.',
    });
  }

  const publisher = await findPublisherEvidence(sourceRepository, workflowInfo, sha, fixedIdentity);
  if (expectedDigest && publisher.identity.indexDigest !== expectedDigest) {
    throw new SelectionError(`Smoke evidence for ${sha} does not prove the digest selected by the deploy record.`, {
      recovery: 'Review the same-SHA digest mismatch and publish fresh digest-bound smoke evidence before retrying.',
    });
  }

  if (requireAlias) {
    const movingIdentity = inspectImage(`${API_IMAGE_REPOSITORY}:main`, sha);
    if (!samePlatforms(movingIdentity, fixedIdentity)) {
      throw new SelectionError('The moving API main alias does not contain the verified fixed tag platform images.', {
        recovery: 'Wait for a successful Build and Deploy promoter for the live main SHA, then rerun reconciliation.',
      });
    }
  }
  return { identity: fixedIdentity, publisher: publisher.publisher };
}

async function findPublisherEvidence(sourceRepository, workflowInfo, sha, fixedIdentity) {
  const runsResponse = await ghApi(
    `repos/${sourceRepository}/actions/workflows/build.yml/runs?branch=main&event=workflow_run&head_sha=${sha}&per_page=100`,
  );
  const runs = boundedList(runsResponse, 'publisher workflow runs');
  const candidates = runs
    .filter((run) => isExpectedBuildRun(run, workflowInfo.build, sha))
    .sort((left, right) => String(right.created_at).localeCompare(String(left.created_at)));
  const proven = [];
  const artifactLists = new Map();

  for (const listedRun of candidates) {
    const latestAttempt = positiveInteger(listedRun.run_attempt, 'publisher run attempt');
    if (latestAttempt > 20) {
      throw new SelectionError(`Publisher run ${listedRun.id} has an unexpected attempt count.`);
    }
    for (let attemptNumber = 1; attemptNumber <= latestAttempt; attemptNumber += 1) {
      const run = await getRunAttempt(sourceRepository, listedRun.id, attemptNumber);
      validateBuildRun(run, workflowInfo.build, sha, attemptNumber, false);
      await requireSuccessfulSourceChecks(sourceRepository, workflowInfo, sha, run.created_at);
      const jobs = await getRunJobs(sourceRepository, run.id, attemptNumber);
      const publisherJob = findUnique(jobs, PUBLISH_JOB_NAME);
      if (publisherJob.conclusion !== 'success') continue;
      const uploadStep = findUnique(publisherJob.steps ?? [], UPLOAD_STEP_NAME);
      if (uploadStep.conclusion !== 'success') {
        throw new SelectionError(`Publisher run ${run.id} attempt ${attemptNumber} succeeded without a successful evidence upload step.`);
      }

      let artifacts = artifactLists.get(String(run.id));
      if (!artifacts) {
        const response = await ghApi(`repos/${sourceRepository}/actions/runs/${run.id}/artifacts?per_page=100`);
        artifacts = boundedList(response, `artifacts for publisher run ${run.id}`);
        artifactLists.set(String(run.id), artifacts);
      }
      const artifactName = `api-smoke-evidence-${run.id}-${attemptNumber}`;
      const matchingArtifacts = artifacts.filter((artifact) => artifact.name === artifactName);
      if (matchingArtifacts.length === 0) continue;
      if (matchingArtifacts.length !== 1) {
        throw new SelectionError(`Publisher run ${run.id} attempt ${attemptNumber} has duplicate smoke evidence artifacts.`);
      }
      const artifact = matchingArtifacts[0];
      if (artifact.expired || !artifact.expires_at || Date.parse(artifact.expires_at) <= Date.now()) continue;
      validateArtifactMetadata(artifact, run, attemptNumber, sha);
      const evidence = downloadEvidence(sourceRepository, run.id, attemptNumber, artifactName);
      validateEvidence(evidence, sourceRepository, sha, run.id, attemptNumber);
      const identity = evidenceIdentity(evidence);
      if (identity.indexDigest !== fixedIdentity.indexDigest || !samePlatforms(identity, fixedIdentity)) {
        throw new SelectionError(`Smoke evidence from publisher run ${run.id} attempt ${attemptNumber} contradicts the current fixed tag identity.`, {
          recovery: 'Review the fixed-tag movement or evidence mismatch and require a fresh successful publisher attempt.',
        });
      }
      proven.push({
        runId: String(run.id),
        attempt: attemptNumber,
        jobCompletedAt: publisherJob.completed_at ?? run.completed_at ?? run.created_at,
        identity,
      });
    }
  }

  if (proven.length === 0) {
    throw new SelectionError(`No unexpired successful publisher evidence proves the fixed API image for ${sha}.`, {
      recovery: 'Run a fresh successful Build and Deploy publisher attempt for this exact main SHA, then rerun the handoff.',
    });
  }
  const canonicalIdentity = proven[0].identity;
  if (proven.some((proof) => !sameImageIdentity(proof.identity, canonicalIdentity))) {
    throw new SelectionError(`Successful publisher attempts for fixed tag ${`sha-${sha}`} contain conflicting index or platform digests.`, {
      recovery: 'Stop automatic selection and review every successful publisher artifact for this fixed SHA tag.',
    });
  }
  proven.sort((left, right) => String(right.jobCompletedAt).localeCompare(String(left.jobCompletedAt)));
  const selected = proven[0];
  return {
    publisher: { runId: selected.runId, attempt: selected.attempt },
    identity: canonicalIdentity,
  };
}

async function loadWorkflowInfo(sourceRepository) {
  const build = await ghApi(`repos/${sourceRepository}/actions/workflows/build.yml`);
  const tests = await ghApi(`repos/${sourceRepository}/actions/workflows/tests.yml`);
  const selection = await ghApi(`repos/${sourceRepository}/actions/workflows/select-local-api-image.yml`);
  assertWorkflow(build, BUILD_WORKFLOW_NAME, BUILD_WORKFLOW_PATH);
  assertWorkflow(tests, TEST_WORKFLOW_NAME, TEST_WORKFLOW_PATH);
  assertWorkflow(selection, 'Select local API image', SELECTION_WORKFLOW_PATH);
  return { build, tests, selection };
}

async function verifyTriggerRun(sourceRepository, workflowInfo, trigger) {
  const run = await getRunAttempt(sourceRepository, trigger.id, trigger.attempt);
  validateBuildRun(run, workflowInfo.build, run.head_sha, trigger.attempt, true);
  await requireSuccessfulSourceChecks(sourceRepository, workflowInfo, run.head_sha, run.created_at);
  const jobs = await getRunJobs(sourceRepository, run.id, trigger.attempt);
  const publish = findUnique(jobs, PUBLISH_JOB_NAME);
  const promote = findUnique(jobs, PROMOTE_JOB_NAME);
  if (publish.conclusion !== 'success' || promote.conclusion !== 'success') {
    throw new SelectionError('The triggering Build and Deploy attempt did not complete both publisher and promoter jobs successfully.', {
      recovery: 'Choose a completed successful Build and Deploy promoter run and attempt, or rerun publication and promotion for live main.',
    });
  }
  const uploadStep = findUnique(publish.steps ?? [], UPLOAD_STEP_NAME);
  if (uploadStep.conclusion !== 'success') {
    throw new SelectionError('The triggering publisher did not upload its required smoke evidence successfully.');
  }
  return run;
}

async function requireSuccessfulSourceChecks(sourceRepository, workflowInfo, sha, beforeTime) {
  const response = await ghApi(
    `repos/${sourceRepository}/actions/workflows/tests.yml/runs?branch=main&event=push&head_sha=${sha}&per_page=100`,
  );
  const runs = boundedList(response, `source checks for ${sha}`);
  const found = runs.some((run) =>
    run.workflow_id === workflowInfo.tests.id &&
    run.name === TEST_WORKFLOW_NAME &&
    run.event === 'push' &&
    run.conclusion === 'success' &&
    run.head_branch === 'main' &&
    run.head_sha === sha &&
    run.head_repository?.full_name === sourceRepository &&
    (!beforeTime || !run.created_at || Date.parse(run.created_at) <= Date.parse(beforeTime)),
  );
  if (!found) {
    throw new SelectionError(`No successful Code Quality and Tests push run to main proves source ${sha}.`, {
      recovery: 'Wait for the source SHA to pass Code Quality and Tests, then rerun the Build and Deploy promoter handoff.',
    });
  }
}

function validateBuildRun(run, workflow, expectedSha, attempt, requireRunSuccess) {
  if (
    run.workflow_id !== workflow.id ||
    run.name !== BUILD_WORKFLOW_NAME ||
    run.path !== BUILD_WORKFLOW_PATH ||
    run.run_attempt !== attempt ||
    run.event !== 'workflow_run' ||
    run.head_branch !== 'main' ||
    run.head_sha !== expectedSha ||
    run.head_repository?.full_name !== SOURCE_REPOSITORY ||
    run.repository?.full_name !== SOURCE_REPOSITORY ||
    (requireRunSuccess && run.conclusion !== 'success')
  ) {
    throw new SelectionError(`Build and Deploy run ${run.id} attempt ${attempt} does not match the expected successful main publisher identity.`, {
      recovery: 'Select a successful Build and Deploy run from this repository for a push originating on main.',
    });
  }
  requireFullSha(run.head_sha, 'Build and Deploy source SHA');
}

function isExpectedBuildRun(run, workflow, sha) {
  return run.workflow_id === workflow.id &&
    run.name === BUILD_WORKFLOW_NAME &&
    run.path === BUILD_WORKFLOW_PATH &&
    run.event === 'workflow_run' &&
    run.head_branch === 'main' &&
    run.head_sha === sha &&
    run.head_repository?.full_name === SOURCE_REPOSITORY &&
    run.repository?.full_name === SOURCE_REPOSITORY;
}

async function getRunAttempt(repository, runId, attempt) {
  return ghApi(`repos/${repository}/actions/runs/${runId}/attempts/${attempt}`);
}

async function getRunJobs(repository, runId, attempt) {
  const response = await ghApi(`repos/${repository}/actions/runs/${runId}/attempts/${attempt}/jobs?per_page=100`);
  return boundedList(response, `jobs for run ${runId} attempt ${attempt}`);
}

function validateArtifactMetadata(artifact, run, attempt, sha) {
  const workflowRun = artifact.workflow_run;
  if (
    !Number.isSafeInteger(artifact.id) ||
    artifact.expired !== false ||
    artifact.size_in_bytes <= 0 ||
    artifact.size_in_bytes > EVIDENCE_MAX_BYTES ||
    workflowRun?.id !== run.id ||
    workflowRun?.repository_id !== run.repository?.id ||
    workflowRun?.head_sha !== sha ||
    workflowRun?.head_branch !== 'main' ||
    workflowRun?.head_repository?.full_name !== SOURCE_REPOSITORY
  ) {
    throw new SelectionError(`Smoke evidence artifact ${artifact.name} is not bound to the verified publisher run and source SHA.`);
  }
  if (artifact.name !== `api-smoke-evidence-${run.id}-${attempt}`) {
    throw new SelectionError('The successful publisher evidence artifact name does not match its run and attempt.');
  }
}

function downloadEvidence(repository, runId, attempt, artifactName) {
  const directory = fs.mkdtempSync(path.join(os.tmpdir(), 'pitaka-api-evidence-'));
  try {
    const result = spawnSync('gh', [
      'run', 'download', String(runId), '--repo', repository, '--name', artifactName, '--dir', directory,
    ], { encoding: 'utf8', env: ghEnvironment(process.env.GITHUB_TOKEN), maxBuffer: 2 * 1024 * 1024 });
    if (result.status !== 0) {
      throw new SelectionError(`Could not download the unexpired publisher evidence for run ${runId} attempt ${attempt}.`, {
        recovery: 'Check that the named smoke artifact is still available, then create a fresh successful publisher attempt if it expired or was removed.',
      });
    }
    const files = listFilesSafely(directory);
    const expectedFilename = `api-smoke-evidence-${runId}-${attempt}.json`;
    if (files.length !== 1 || path.basename(files[0]) !== expectedFilename) {
      throw new SelectionError(`Publisher evidence artifact ${artifactName} must contain exactly ${expectedFilename}.`);
    }
    const stat = fs.statSync(files[0]);
    if (stat.size <= 0 || stat.size > EVIDENCE_MAX_BYTES) {
      throw new SelectionError(`Publisher evidence ${expectedFilename} exceeds the ${EVIDENCE_MAX_BYTES}-byte limit.`);
    }
    try {
      return JSON.parse(fs.readFileSync(files[0], 'utf8'));
    } catch {
      throw new SelectionError(`Publisher evidence ${expectedFilename} is not valid JSON.`);
    }
  } finally {
    fs.rmSync(directory, { recursive: true, force: true });
  }
}

function validateEvidence(evidence, repository, sha, runId, attempt) {
  assertExactKeys(evidence, ['image', 'run', 'schemaVersion', 'source'], 'evidence');
  if (evidence.schemaVersion !== 1) throw new SelectionError('Publisher evidence has an unsupported schema version.');
  assertExactKeys(evidence.source, ['repository', 'sha'], 'evidence source');
  assertExactKeys(evidence.image, ['fixedTag', 'indexDigest', 'platforms', 'repository'], 'evidence image');
  assertExactKeys(evidence.run, ['attempt', 'id'], 'evidence run');
  if (
    evidence.source.repository !== repository ||
    evidence.source.sha !== sha ||
    evidence.image.repository !== API_IMAGE_REPOSITORY ||
    evidence.image.fixedTag !== `sha-${sha}` ||
    !SHA256.test(evidence.image.indexDigest) ||
    evidence.run.id !== String(runId) ||
    evidence.run.attempt !== attempt
  ) {
    throw new SelectionError(`Publisher evidence does not match repository, source SHA, fixed tag, digest, run ${runId}, and attempt ${attempt}.`);
  }
  assertExactKeys(evidence.image.platforms, REQUIRED_PLATFORMS, 'evidence platforms');
  for (const platform of REQUIRED_PLATFORMS) {
    const value = evidence.image.platforms[platform];
    assertExactKeys(value, ['apiReadiness', 'childDigest', 'migration'], `${platform} evidence`);
    if (!SHA256.test(value.childDigest) || value.migration !== 'success' || value.apiReadiness !== 'success') {
      throw new SelectionError(`Publisher evidence does not prove successful migration and API readiness for ${platform}.`);
    }
  }
}

function evidenceIdentity(evidence) {
  return {
    sourceRevision: evidence.source.sha,
    indexDigest: evidence.image.indexDigest,
    platforms: Object.fromEntries(REQUIRED_PLATFORMS.map((platform) => [platform, evidence.image.platforms[platform].childDigest])),
  };
}

function inspectImage(reference, expectedSha) {
  const inspector = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..', 'inspect-published-image.sh');
  const result = spawnSync(inspector, [reference, expectedSha], {
    encoding: 'utf8',
    env: process.env,
    maxBuffer: 2 * 1024 * 1024,
  });
  if (result.status !== 0) {
    throw new SelectionError(`Registry inspection rejected ${reference} for source ${expectedSha}.`, {
      recovery: 'Check the fixed or moving API tag identity and registry availability, then rerun the publisher and handoff if necessary.',
    });
  }
  try {
    const identity = JSON.parse(result.stdout);
    if (identity.sourceRevision !== expectedSha || !SHA256.test(identity.indexDigest)) {
      throw new Error('invalid identity');
    }
    return identity;
  } catch {
    throw new SelectionError(`Registry inspection returned invalid image identity for ${reference}.`);
  }
}

async function compareSourceHistory(repository, baseSha, headSha) {
  if (baseSha === headSha) return 'identical';
  const comparison = await ghApi(`repos/${repository}/compare/${baseSha}...${headSha}`);
  if (comparison.status === 'ahead') return 'selected-is-ancestor';
  if (comparison.status === 'behind') return 'candidate-is-ancestor';
  throw new SelectionError(`GitHub could not prove an ancestor relationship between ${baseSha} and ${headSha}.`, {
    recovery: 'Review the current main history and selected deploy source SHA before changing versions/local.json.',
  });
}

async function readDeploySnapshot(checkout) {
  runGit(checkout, ['fetch', '--no-tags', 'origin', '+refs/heads/main:refs/remotes/origin/main']);
  const mainCommit = runGit(checkout, ['rev-parse', 'refs/remotes/origin/main']);
  const recordText = runGit(checkout, ['show', 'refs/remotes/origin/main:versions/local.json']);
  let record;
  try {
    record = JSON.parse(recordText);
  } catch {
    throw new SelectionError('The complete current versions/local.json record is not valid JSON.');
  }
  const validatorText = runGit(checkout, ['show', 'refs/remotes/origin/main:pitaka_deploy/versions.mjs']);
  const tempDirectory = fs.mkdtempSync(path.join(os.tmpdir(), 'pitaka-deploy-validator-'));
  const validatorPath = path.join(tempDirectory, `versions-${mainCommit}.mjs`);
  const recordPath = path.join(tempDirectory, `local-${mainCommit}.json`);
  fs.writeFileSync(validatorPath, validatorText);
  fs.writeFileSync(recordPath, recordText);
  await validateCompleteRecord(validatorPath, record, tempDirectory, recordPath);
  const blobSha = runGit(checkout, ['rev-parse', `refs/remotes/origin/main:versions/local.json`]);
  return { checkout, mainCommit, record, recordText, blobSha, validatorPath, tempDirectory };
}

async function validateCompleteRecord(validatorPath, record, directory, existingRecordPath) {
  if (!isExactApiObject(record.api)) {
    throw new SelectionError('The current or resulting API section must contain exactly six supported fields.');
  }
  const recordPath = existingRecordPath ?? path.join(directory, `candidate-${Date.now()}-${Math.random().toString(16).slice(2)}.json`);
  if (!existingRecordPath) fs.writeFileSync(recordPath, `${JSON.stringify(record, null, 2)}\n`);
  const runnerPath = path.join(directory, 'validate-record.mjs');
  fs.writeFileSync(runnerPath,
    `import fs from 'node:fs';\n` +
    `import { pathToFileURL } from 'node:url';\n` +
    `const validator = await import(pathToFileURL(process.argv[2]).href);\n` +
    `validator.validateVersion(JSON.parse(fs.readFileSync(process.argv[3], 'utf8')));\n`,
  );
  const safeEnvironment = { ...process.env };
  for (const key of ['GH_TOKEN', 'GITHUB_TOKEN', 'DEPLOY_APP_TOKEN', 'ACTIONS_RUNTIME_TOKEN']) delete safeEnvironment[key];
  const result = spawnSync(process.execPath, [runnerPath, validatorPath, recordPath], {
    encoding: 'utf8', env: safeEnvironment, maxBuffer: 2 * 1024 * 1024,
  });
  if (result.status !== 0) {
    throw new SelectionError(`The canonical pitaka-deploy version validator rejected the complete record: ${sanitize(result.stderr || result.stdout)}`, {
      recovery: 'Correct the complete deploy record with the pitaka-deploy canonical validator before retrying.',
    });
  }
}

async function validateApiChangeHistory(sourceRepository, workflowInfo, snapshot, appIdentity, triggerInfo) {
  const commits = runGit(snapshot.checkout, ['log', '--format=%H', 'refs/remotes/origin/main', '--', 'versions/local.json'])
    .split('\n').filter(Boolean);
  if (commits.length > 5000) {
    throw new SelectionError('The deploy record history is too large to audit safely in one handoff.');
  }
  for (const commit of commits) {
    const current = parseHistoricalRecord(snapshot.checkout, `${commit}:versions/local.json`);
    const parents = runGit(snapshot.checkout, ['show', '-s', '--format=%P', commit]).split(' ').filter(Boolean);
    let previous;
    if (parents.length > 0) {
      previous = tryHistoricalRecord(snapshot.checkout, `${parents[0]}:versions/local.json`);
    }
    if (previous && isDeepStrictEqual(current.api, previous.api)) continue;
    if (parents.length === 0) return; // Initial deploy record is the seed selection.
    if (!previous || !previous.api) {
      throw new SelectionError(`Deploy commit ${commit.slice(0, 12)} introduced an API selection outside the recognized handoff.`, {
        recovery: 'Review the API-changing deploy commit; an initial seed is allowed only in the root commit.',
      });
    }
    await validateAutomaticApiCommit(sourceRepository, workflowInfo, snapshot.checkout, commit, current, appIdentity, triggerInfo);
    return;
  }
  throw new SelectionError('Could not find the deploy commit that established the current API selection.');
}

async function validateAutomaticApiCommit(sourceRepository, workflowInfo, checkout, commit, record, appIdentity, triggerInfo) {
  const metadata = runGit(checkout, ['show', '-s', '--format=%an%x00%ae%x00%cn%x00%ce%x00%B', commit]);
  const [authorName, authorEmail, committerName, committerEmail, ...messageParts] = metadata.split('\0');
  const message = messageParts.join('\0');
  if (
    authorName !== `${appIdentity.slug}[bot]` ||
    committerName !== `${appIdentity.slug}[bot]` ||
    authorEmail !== appIdentity.email ||
    committerEmail !== appIdentity.email
  ) {
    throw new SelectionError(`Deploy API-changing commit ${commit.slice(0, 12)} is not attributed to the shared deploy App bot.`, {
      recovery: 'Review the API-changing commit and use a recognized deploy App workflow to establish the selection.',
    });
  }
  const parsed = parseCommitDetails(message);
  if (!parsed || parsed.sourceSha !== record.api.sourceSha || parsed.digest !== record.api.digest) {
    throw new SelectionError(`Deploy API-changing commit ${commit.slice(0, 12)} has no matching handoff provenance.`, {
      recovery: 'Review the API-changing commit message and restore a selection written by the verified handoff.',
    });
  }
  const ownRunId = process.env.GITHUB_RUN_ID;
  const ownAttempt = Number(process.env.GITHUB_RUN_ATTEMPT ?? '1');
  if (parsed.handoffRunId === ownRunId && parsed.handoffAttempt <= ownAttempt) {
    if (parsed.triggerRunId !== triggerInfo.id || parsed.triggerAttempt !== triggerInfo.attempt) {
      throw new SelectionError('The deploy read-back commit metadata does not match this handoff run.');
    }
    const proof = await verifyImageProof(sourceRepository, workflowInfo, record.api.sourceSha, record.api.digest, false);
    if (parsed.publisherRunId !== proof.publisher.runId || parsed.publisherAttempt !== proof.publisher.attempt) {
      throw new SelectionError('The deploy read-back commit publisher metadata does not match its verified image evidence.');
    }
    return;
  }
  await verifyPastHandoffRun(sourceRepository, workflowInfo, parsed.handoffRunId, parsed.handoffAttempt);
}

function parseCommitDetails(message) {
  const value = (label, expression) => {
    const match = message.match(expression);
    return match?.[1];
  };
  const handoffRunId = value('handoff', /^Handoff: Select local API image run ([0-9]+), attempt ([1-9][0-9]*)$/m);
  const handoffAttempt = message.match(/^Handoff: Select local API image run [0-9]+, attempt ([1-9][0-9]*)$/m)?.[1];
  const triggerRunId = message.match(/^Trigger: Build and Deploy run ([0-9]+), attempt [1-9][0-9]*$/m)?.[1];
  const triggerAttempt = message.match(/^Trigger: Build and Deploy run [0-9]+, attempt ([1-9][0-9]*)$/m)?.[1];
  const publisherRunId = message.match(/^Publisher: Build and Deploy run ([0-9]+), attempt [1-9][0-9]*$/m)?.[1];
  const publisherAttempt = message.match(/^Publisher: Build and Deploy run [0-9]+, attempt ([1-9][0-9]*)$/m)?.[1];
  const sourceSha = value('source', /^API source SHA: ([a-f0-9]{40})$/m);
  const digest = value('digest', /^API index digest: (sha256:[a-f0-9]{64})$/m);
  if (!handoffRunId || !triggerRunId || !publisherRunId || !sourceSha || !digest) return null;
  return {
    handoffRunId,
    handoffAttempt: Number(handoffAttempt),
    triggerRunId,
    triggerAttempt: Number(triggerAttempt),
    publisherRunId,
    publisherAttempt: Number(publisherAttempt),
    sourceSha,
    digest,
  };
}

async function verifyPastHandoffRun(repository, workflowInfo, runId, attempt) {
  const run = await getRunAttempt(repository, runId, attempt);
  if (
    run.workflow_id !== workflowInfo.selection.id ||
    run.name !== 'Select local API image' ||
    run.path !== SELECTION_WORKFLOW_PATH ||
    run.run_attempt !== attempt ||
    run.conclusion !== 'success' ||
    run.head_branch !== 'main' ||
    run.head_repository?.full_name !== repository
  ) {
    throw new SelectionError(`Prior API-changing deploy commit refers to an unverified handoff run ${runId} attempt ${attempt}.`);
  }
  const jobs = await getRunJobs(repository, runId, attempt);
  if (findUnique(jobs, SELECTION_JOB_NAME).conclusion !== 'success') {
    throw new SelectionError(`Prior API-changing deploy commit's handoff run ${runId} did not finish successfully.`);
  }
}

function parseHistoricalRecord(checkout, revision) {
  const raw = runGit(checkout, ['show', revision]);
  return JSON.parse(raw);
}

function tryHistoricalRecord(checkout, revision) {
  const result = spawnSync('git', ['show', revision], { cwd: checkout, encoding: 'utf8' });
  if (result.status !== 0) return undefined;
  try { return JSON.parse(result.stdout); } catch { return undefined; }
}

async function resolveAppIdentity(sourceRepository) {
  const slug = process.env.DEPLOY_APP_SLUG;
  if (!slug || !/^[a-z0-9-]+$/.test(slug)) {
    throw new SelectionError('The deploy GitHub App slug output is missing or invalid.');
  }
  const account = await ghApi(`/users/${encodeURIComponent(`${slug}[bot]`)}`);
  if (!Number.isSafeInteger(account.id) || account.id <= 0) {
    throw new SelectionError('GitHub did not resolve the shared deploy App bot identity.');
  }
  return { slug, email: `${account.id}+${slug}[bot]@users.noreply.github.com` };
}

async function writeDeployRecord(blobSha, content, message, appIdentity) {
  const fields = [
    '--method', 'PUT',
    '--field', `message=${message}`,
    '--field', `content=${Buffer.from(content).toString('base64')}`,
    '--field', `sha=${blobSha}`,
    '--field', 'branch=main',
    '--field', `author[name]=${appIdentity.slug}[bot]`,
    '--field', `author[email]=${appIdentity.email}`,
    '--field', `committer[name]=${appIdentity.slug}[bot]`,
    '--field', `committer[email]=${appIdentity.email}`,
  ];
  await ghApi(`repos/${DEPLOY_REPOSITORY}/contents/versions/local.json`, { token: process.env.DEPLOY_APP_TOKEN, args: fields });
}

function makeCommitMessage(trigger, publisher, identity) {
  return [
    'Select verified API image for local deployment',
    '',
    `Handoff: Select local API image run ${process.env.GITHUB_RUN_ID}, attempt ${positiveInteger(process.env.GITHUB_RUN_ATTEMPT ?? '1', 'handoff attempt')}`,
    `Trigger: Build and Deploy run ${trigger.id}, attempt ${trigger.attempt}`,
    `Publisher: Build and Deploy run ${publisher.runId}, attempt ${publisher.attempt}`,
    `API source SHA: ${identity.sourceRevision}`,
    `API index digest: ${identity.indexDigest}`,
  ].join('\n');
}

async function resolveTrigger() {
  let runId = process.env.PITAKA_TRIGGER_RUN_ID;
  let attemptValue = process.env.PITAKA_TRIGGER_RUN_ATTEMPT;
  const eventName = process.env.GITHUB_EVENT_NAME;
  if (eventName === 'workflow_run') {
    const eventPath = process.env.GITHUB_EVENT_PATH;
    if (eventPath && fs.existsSync(eventPath)) {
      const payload = JSON.parse(fs.readFileSync(eventPath, 'utf8'));
      runId = String(payload.workflow_run?.id ?? '');
      attemptValue = String(payload.workflow_run?.run_attempt ?? '');
    }
  } else if (eventName === 'workflow_dispatch') {
    runId = process.env.INPUT_TRIGGER_RUN_ID ?? runId;
    attemptValue = process.env.INPUT_TRIGGER_RUN_ATTEMPT ?? attemptValue;
  }
  if (!/^\d+$/.test(runId ?? '') || !/^[1-9][0-9]*$/.test(attemptValue ?? '')) {
    throw new SelectionError('A Build and Deploy run ID and positive attempt are required.', {
      recovery: 'Dispatch reconciliation with a successful Build and Deploy run ID and attempt, or wait for a successful promoter event.',
    });
  }
  return { id: runId, attempt: Number(attemptValue), event: eventName };
}

async function ghApi(endpoint, { token = process.env.GITHUB_TOKEN, args = [] } = {}) {
  if (!token) throw new SelectionError('The source-repository read token is unavailable.');
  const result = spawnSync('gh', ['api', endpoint, ...args], {
    encoding: 'utf8', env: ghEnvironment(token), maxBuffer: 16 * 1024 * 1024,
  });
  if (result.status !== 0) {
    const stderr = sanitize(result.stderr || result.stdout || 'GitHub API request failed.');
    const status = Number(stderr.match(/HTTP\s+(\d{3})/i)?.[1]);
    if (args.includes('--method') && args.includes('PUT')) throw classifyWriteError(status || undefined, stderr);
    throw new SelectionError(`GitHub metadata request failed: ${stderr}`);
  }
  try { return JSON.parse(result.stdout); } catch {
    throw new SelectionError(`GitHub returned invalid JSON for ${endpoint}.`);
  }
}

function classifyWriteError(status, message) {
  const lower = message.toLowerCase();
  if (/protected branch|branch protection|ruleset|required status|not allowed to push|branch is protected/.test(lower)) {
    return new SelectionError('The deploy repository branch rules rejected the Contents API write.', {
      recovery: 'Review the pitaka-deploy main branch rules and App bypass permissions; retry after the write path is authorized.', status,
    });
  }
  if (status === 401 || status === 403) {
    if (status === 403 && /rate limit|secondary rate|abuse detection/.test(lower)) {
      return new SelectionError('The Contents API write was rate limited.', { retryable: true, status });
    }
    return new SelectionError('The deploy-scoped App was not authorized to update versions/local.json.', {
      recovery: 'Verify the App installation on pitaka-deploy and its Contents read/write permission.', status,
    });
  }
  if (status === 409 || status === 422) {
    if (/sha.*(does not match|mismatch|current)|file.*(changed|conflict)|stale/.test(lower)) {
      return new SelectionError('The deploy record changed before the conditional write.', { retryable: true, status });
    }
    return new SelectionError('The Contents API rejected the deploy update; the failure was not a recognized stale-blob conflict.', {
      recovery: 'Review the API response and pitaka-deploy branch rules before retrying.', status,
    });
  }
  if (status === 429 || status >= 500 || status === undefined) {
    return new SelectionError('The Contents API write returned a transient or uncertain response.', { retryable: true, status });
  }
  return new SelectionError(`The Contents API write failed${status ? ` with HTTP ${status}` : ''}.`, {
    recovery: 'Review deploy-repository permissions and the failed Contents API response.', status,
  });
}

function ghEnvironment(token) {
  return { ...process.env, GH_TOKEN: token };
}

function sameImageIdentity(left, right) {
  return left.sourceRevision === right.sourceRevision &&
    left.indexDigest === right.indexDigest &&
    samePlatforms(left, right);
}

function samePlatforms(left, right) {
  return REQUIRED_PLATFORMS.every((platform) => left.platforms?.[platform] === right.platforms?.[platform]);
}

function isExactApiObject(value) {
  return value && typeof value === 'object' && !Array.isArray(value) &&
    isDeepStrictEqual(Object.keys(value).sort(), [...API_FIELDS].sort()) &&
    value.repository === API_IMAGE_REPOSITORY &&
    FULL_SHA.test(value.sourceSha ?? '') &&
    value.sourceTag === `sha-${value.sourceSha}` &&
    SHA256.test(value.digest ?? '') &&
    value.image === `${API_IMAGE_REPOSITORY}@${value.digest}` &&
    REQUIRED_PLATFORMS.includes(value.platform);
}

function makeApiSelection(identity, platform) {
  return {
    repository: API_IMAGE_REPOSITORY,
    sourceSha: identity.sourceRevision,
    sourceTag: `sha-${identity.sourceRevision}`,
    image: `${API_IMAGE_REPOSITORY}@${identity.indexDigest}`,
    digest: identity.indexDigest,
    platform,
  };
}

function assertWorkflow(workflow, name, workflowPath) {
  if (!Number.isSafeInteger(workflow?.id) || workflow.name !== name || workflow.path !== workflowPath) {
    throw new SelectionError(`Expected workflow ${name} at ${workflowPath} was not found in GitHub metadata.`);
  }
}

function assertExactKeys(value, keys, name) {
  if (!value || typeof value !== 'object' || Array.isArray(value) || !isDeepStrictEqual(Object.keys(value).sort(), [...keys].sort())) {
    throw new SelectionError(`Publisher ${name} does not match its strict schema.`);
  }
}

function boundedList(response, name) {
  const list = response?.jobs ?? response?.artifacts ?? response?.workflow_runs;
  if (!Array.isArray(list) || list.length > 100 || (response.total_count !== undefined && response.total_count > 100)) {
    throw new SelectionError(`GitHub returned an incomplete or unexpectedly large ${name} list.`);
  }
  return list;
}

function findUnique(values, name) {
  const matches = values.filter((value) => value.name === name);
  if (matches.length !== 1) throw new SelectionError(`Expected one ${name} identity, found ${matches.length}.`);
  return matches[0];
}

function listFilesSafely(root) {
  const files = [];
  function visit(directory, depth) {
    if (depth > 8) throw new SelectionError('Publisher evidence artifact has an unexpected directory depth.');
    for (const entry of fs.readdirSync(directory, { withFileTypes: true })) {
      const filename = path.join(directory, entry.name);
      if (entry.isSymbolicLink()) throw new SelectionError('Publisher evidence artifact contains a symbolic link.');
      if (entry.isDirectory()) visit(filename, depth + 1);
      else if (entry.isFile()) files.push(filename);
      else throw new SelectionError('Publisher evidence artifact contains an unsupported file type.');
    }
  }
  visit(root, 0);
  return files;
}

function positiveInteger(value, name) {
  const parsed = typeof value === 'number' ? value : Number(value);
  if (!Number.isSafeInteger(parsed) || parsed < 1) throw new SelectionError(`${name} must be a positive integer.`);
  return parsed;
}

function requireFullSha(value, name) {
  if (!FULL_SHA.test(value ?? '')) throw new SelectionError(`${name} must be a lowercase full Git SHA.`);
}

function emptyState() {
  return { trigger: null, publisher: null, sourceSha: null, indexDigest: null, previousApi: null, finalApi: null, writeAttempts: 0, outcome: 'not started' };
}

function getStatePath() {
  return process.env.PITAKA_HANDOFF_STATE ?? path.join(process.env.RUNNER_TEMP ?? os.tmpdir(), 'pitaka-api-selection-state.json');
}

function readState(filename) {
  try { return JSON.parse(fs.readFileSync(filename, 'utf8')); } catch { return undefined; }
}

function saveState(filename, state) {
  fs.mkdirSync(path.dirname(filename), { recursive: true });
  fs.writeFileSync(filename, `${JSON.stringify(state, null, 2)}\n`, { mode: 0o600 });
}

function formatSummary(state) {
  const selection = (value) => value ? `\n\n\`\`\`json\n${JSON.stringify(value, null, 2)}\n\`\`\`` : 'not established';
  const trigger = state.trigger ? `Build and Deploy run ${state.trigger.id}, attempt ${state.trigger.attempt}` : 'not verified';
  const publisher = state.publisher ? `Build and Deploy run ${state.publisher.runId}, attempt ${state.publisher.attempt}` : 'not verified';
  return [
    '## API image selection handoff',
    '',
    `- Trigger: ${trigger}`,
    `- Publisher proof: ${publisher}`,
    `- Selected source SHA: ${state.sourceSha ?? 'not verified'}`,
    `- Fixed index digest: ${state.indexDigest ?? 'not verified'}`,
    `- Previous API selection:${selection(state.previousApi)}`,
    `- Final API selection:${selection(state.finalApi)}`,
    `- Contents API write attempts: ${state.writeAttempts ?? 0}`,
    `- Outcome: ${state.outcome}`,
    `- Recovery: ${state.recovery ?? 'Review the handoff run and dispatch reconciliation with a successful promoter run ID and attempt if needed.'}`,
    '',
    'This changes the local desired API image selection. It does not apply the running local stack.',
    '',
  ].join('\n');
}

function outcomeRecovery(outcome) {
  if (outcome === 'applied') return 'The verified API digest is selected in pitaka-deploy/main; run the local deploy procedure separately when ready.';
  if (outcome === 'already current') return 'The deploy record already selects the verified API digest; no commit was needed.';
  if (outcome === 'validly superseded') return 'A verified descendant API selection is already current; keep it and review only if local desired state should change.';
  return 'Review the handoff before retrying.';
}

function publicError(error) {
  return sanitize(error instanceof Error ? error.message : String(error));
}

function sanitize(message) {
  return String(message)
    .replace(/\bgh[pousr]_[A-Za-z0-9_]{10,}\b/g, '[redacted token]')
    .replace(/\bgithub_pat_[A-Za-z0-9_]{10,}\b/g, '[redacted token]')
    .slice(0, 1000);
}

function delay(milliseconds) {
  return new Promise((resolve) => setTimeout(resolve, milliseconds));
}

function runGit(checkout, args) {
  const result = spawnSync('git', args, { cwd: checkout, encoding: 'utf8', maxBuffer: 16 * 1024 * 1024 });
  if (result.status !== 0) {
    throw new SelectionError(`Could not read pitaka-deploy main history: ${sanitize(result.stderr || result.stdout)}`, {
      recovery: 'Check the deploy-scoped App checkout and main branch before retrying.',
    });
  }
  return result.stdout.trimEnd();
}
