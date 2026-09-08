'use strict';

const marker = '<!-- portal-test-report:v1 -->';
const suites = [
  ['Unit', 'unit-tests'],
  ['HttpIntegration', 'http-integration-tests'],
  ['Browser', 'browser-tests'],
];
const statuses = new Set(['passed', 'failed', 'invalid', 'not-run', 'skipped', 'cancelled']);
const suiteLimit = 8192;
const aggregateLimit = 32768;
const moduleNames = ['XtremeIdiots.Portal.Web', 'XtremeIdiots.Portal.Integrations.Forums'];
const measurementStatuses = new Set(['collected', 'not-collected', 'unavailable', 'invalid']);
const phaseStatuses = new Set(['completed', 'failed', 'not-run']);
const hash = value => typeof value === 'string' && /^[a-f0-9]{64}$/.test(value);
const number = (value, max = 31622400) => Number.isFinite(value) && value >= 0 && value <= max;
const integer = (value, max = 10000000) => Number.isSafeInteger(value) && number(value, max);
const reasons = {
  completed: 'Completed',
  'test-failures': 'Tests or test host failed',
  'run-failed': 'Build/setup/test command failed',
  'report-missing': 'No report; tests may not have started',
  'multiple-reports': 'Multiple reports; possibly stale results',
  'report-invalid': 'Invalid or unsafe report',
  'measurement-revision-mismatch': 'Measurement revision differs from the tested checkout',
  'zero-tests': 'No tests discovered',
  'all-skipped': 'All tests skipped',
  'not-started': 'Tests not started',
  cancelled: 'Cancelled',
  'job-failed': 'Job failed (including build/setup/upload/reporting)',
  'job-skipped': 'Job skipped unexpectedly',
  'draft-skipped': 'Intentionally disabled for draft validation',
};

function parseJson(text, limit = 32768) {
  if (typeof text !== 'string' || Buffer.byteLength(text, 'utf8') > limit) return null;
  try {
    return JSON.parse(text);
  } catch (error) {
    if (error instanceof SyntaxError) return null;
    throw error;
  }
}

function emptyReport(suite, status = 'invalid', reason = 'report-missing') {
  return { schema: 1, suite, status, reason, total: 0, executed: 0, passed: 0, failed: 0, skipped: 0, durationSeconds: 0, artifactId: null };
}

function validateMetric(value) {
  if (!value || !integer(value.covered) || !integer(value.valid) || value.covered > value.valid) return null;
  const percent = value.valid ? Math.floor((10000 * value.covered + value.valid / 2) / value.valid) / 100 : null;
  if (value.percent !== percent) return null;
  return { covered: value.covered, valid: value.valid, percent };
}

function validateCoverage(value, suite) {
  if (!value || !measurementStatuses.has(value.status) || typeof value.requested !== 'boolean' ||
      (suite === 'Browser' && (value.requested || value.status !== 'not-collected')) ||
      (!value.requested && value.status !== 'not-collected') ||
      (value.requested && value.status === 'not-collected')) return null;
  const result = { status: value.status, requested: value.requested };
  if (value.status !== 'collected') return result;
  const lines = validateMetric(value.lines);
  const branches = validateMetric(value.branches);
  const mappedBranches = validateMetric(value.mappedBranches);
  if (!lines || !branches || !mappedBranches || !Array.isArray(value.modules) || value.modules.length !== 2 ||
      branches.valid < mappedBranches.valid || branches.covered < mappedBranches.covered ||
      branches.covered - mappedBranches.covered > branches.valid - mappedBranches.valid) return null;
  const modules = moduleNames.map((name, index) => {
    const module = value.modules[index];
    const moduleLines = validateMetric(module?.lines);
    const moduleBranches = validateMetric(module?.branches);
    return module?.name === name && moduleLines && moduleBranches ? { name, lines: moduleLines, branches: moduleBranches } : null;
  });
  if (modules.some(module => !module)) return null;
  for (const [name, total] of [['lines', lines], ['branches', mappedBranches]]) {
    for (const key of ['covered', 'valid']) {
      if (modules.reduce((sum, module) => sum + module[name][key], 0) !== total[key]) return null;
    }
  }
  return { ...result, lines, branches, mappedBranches, modules };
}

function validateMeasurement(value, suite, testStatus) {
  if (!value || suite === 'bootstrap' || !measurementStatuses.has(value.status) || typeof value.baselineEligible !== 'boolean') return null;
  const coverage = validateCoverage(value.coverage, suite);
  if (!coverage) return null;
  const result = { status: value.status, baselineEligible: false, coverage };
  if (!Object.hasOwn(value, 'scope')) return value.status !== 'collected' ? result : null;
  result.revisionStatus = Object.hasOwn(value, 'revisionStatus') ? value.revisionStatus : 'unverified';
  if (!['matched', 'mismatched', 'unverified'].includes(result.revisionStatus)) return null;
  if (!['full-suite', 'filtered'].includes(value.scope) || !['built', 'reused'].includes(value.buildMode) ||
      !['Debug', 'Release'].includes(value.configuration) || value.targetFramework !== 'net10.0' ||
      typeof value.sourceRevision !== 'string' || !/^[a-f0-9]{40}$/.test(value.sourceRevision) ||
      typeof value.workingTreeDirty !== 'boolean' || !hash(value.testAssemblySha256) || !hash(value.selectionSha256) ||
      !Array.isArray(value.modules) || value.modules.length !== 2 ||
      value.modules.some((module, index) => module?.name !== moduleNames[index] || !hash(module.sha256))) return null;
  const runtime = value.runtime;
  if (!runtime || typeof runtime.sdk !== 'string' || !/^[0-9]{1,2}\.[0-9]{1,2}\.[0-9]{1,4}$/.test(runtime.sdk) ||
      !['Windows', 'Linux', 'macOS'].includes(runtime.os) || !['X64', 'X86', 'Arm64', 'Arm'].includes(runtime.architecture) ||
      (coverage.requested ? typeof runtime.collectorVersion !== 'string' ||
        !/^[0-9]{1,3}\.[0-9]{1,3}\.[0-9]{1,4}(?:-[A-Za-z0-9.-]{1,64})?$/.test(runtime.collectorVersion) || !hash(value.profileSha256) :
        runtime.collectorVersion !== null || value.profileSha256 !== null)) return null;
  const phases = {};
  for (const name of ['discovery', 'execution']) {
    const phase = value[name];
    if (!phase || !phaseStatuses.has(phase.status) ||
        (phase.durationSeconds !== null && !number(phase.durationSeconds)) ||
        (phase.status === 'completed' && phase.durationSeconds === null)) return null;
    if (name === 'discovery') {
      if ((phase.count !== null && !integer(phase.count, 1000000)) ||
          (phase.status === 'completed' && phase.count === null)) return null;
      phases[name] = { status: phase.status, count: phase.count, durationSeconds: phase.durationSeconds };
    } else {
      if ((phase.exitCode !== null && (!Number.isSafeInteger(phase.exitCode) || Math.abs(phase.exitCode) > 2147483648)) ||
          (phase.status === 'completed' && phase.exitCode !== 0) ||
          (phase.status === 'failed' && phase.exitCode === 0)) return null;
      phases[name] = { status: phase.status, exitCode: phase.exitCode, durationSeconds: phase.durationSeconds };
    }
  }
  const timestamp = text => typeof text === 'string' && text.length <= 40 &&
    /^\d{4}-\d\d-\d\dT[\d:.]+(?:Z|\+00:00)$/.test(text) && Number.isFinite(Date.parse(text));
  if (!timestamp(value.startedAtUtc) || (value.completedAtUtc !== null &&
      (!timestamp(value.completedAtUtc) || !number((Date.parse(value.completedAtUtc) - Date.parse(value.startedAtUtc)) / 1000)))) return null;
  if (value.status === 'collected' && (phases.discovery.status !== 'completed' ||
      phases.execution.status !== 'completed' || value.completedAtUtc === null)) return null;
  for (const key of ['scope', 'buildMode', 'configuration', 'targetFramework', 'sourceRevision', 'workingTreeDirty',
    'testAssemblySha256', 'selectionSha256', 'profileSha256', 'startedAtUtc', 'completedAtUtc']) result[key] = value[key];
  result.modules = value.modules.map(module => ({ name: module.name, sha256: module.sha256 }));
  result.runtime = { sdk: runtime.sdk, os: runtime.os, architecture: runtime.architecture, collectorVersion: runtime.collectorVersion };
  Object.assign(result, phases);
  result.baselineEligible = value.baselineEligible && value.status === 'collected' && testStatus === 'passed' &&
    value.scope === 'full-suite' && value.buildMode === 'built' && !value.workingTreeDirty &&
    (suite === 'Browser' || coverage.status === 'collected');
  return result;
}

function verifyMeasurementRevision(report, sha) {
  const measurement = report.measurement;
  if (!measurement?.sourceRevision) return;
  measurement.revisionStatus = measurement.sourceRevision === sha ? 'matched' : 'mismatched';
  if (measurement.revisionStatus === 'mismatched') {
    measurement.status = 'invalid';
    measurement.baselineEligible = false;
    if (report.status === 'passed') {
      report.status = 'invalid';
      report.reason = 'measurement-revision-mismatch';
    }
  }
}

function validateReport(value, suite) {
  if (!value || value.schema !== 1 || value.suite !== suite || !statuses.has(value.status) ||
      !Object.hasOwn(reasons, value.reason)) return null;
  for (const key of ['total', 'executed', 'passed', 'failed', 'skipped']) {
    if (!Number.isSafeInteger(value[key]) || value[key] < 0 || value[key] > 1000000) return null;
  }
  if (value.executed + value.skipped !== value.total || value.passed + value.failed !== value.executed ||
      !Number.isFinite(value.durationSeconds) || value.durationSeconds < 0 || value.durationSeconds > 366 * 86400) return null;
  if (value.status === 'passed' &&
      (value.total === 0 || value.executed === 0 || value.failed !== 0 || value.reason !== 'completed')) return null;
  const testStatus = Object.hasOwn(value, 'testStatus') ? value.testStatus : value.status;
  if (!statuses.has(testStatus) || (testStatus === 'passed' && (value.executed === 0 || value.failed !== 0))) return null;
  if (value.status === 'passed' && testStatus !== 'passed') return null;
  const report = emptyReport(suite);
  for (const key of Object.keys(report)) {
    if (key !== 'artifactId') report[key] = value[key];
  }
  report.artifactId = typeof value.artifactId === 'string' && /^[1-9][0-9]{0,19}$/.test(value.artifactId) ? value.artifactId : null;
  report.testStatus = testStatus;
  if (Object.hasOwn(value, 'measurement')) {
    report.measurement = validateMeasurement(value.measurement, suite, value.status);
    if (!report.measurement || (report.measurement.discovery?.count === 0 && report.executed > 0)) return null;
  }
  return report;
}

function normalizeSuite(suite, job, browserEnabled) {
  const reportText = job?.outputs?.report;
  let report = validateReport(parseJson(reportText, suiteLimit), suite) ||
    emptyReport(suite, 'invalid', reportText ? 'report-invalid' : 'report-missing');
  if (suite === 'Browser' && !browserEnabled && job?.result === 'skipped') {
    report = emptyReport(suite, 'skipped', 'draft-skipped');
  } else if (job?.result === 'cancelled') {
    report.status = 'cancelled';
    report.reason = 'cancelled';
  } else if (job?.result === 'skipped') {
    report.status = 'not-run';
    report.reason = 'job-skipped';
  } else if (job?.result !== 'success') {
    report.status = 'failed';
    report.reason = 'job-failed';
  }
  if (report.measurement && report.status !== 'passed') report.measurement.baselineEligible = false;
  return report;
}

function identity(context) {
  const repo = `${context.repo.owner}/${context.repo.repo}`;
  const sha = context.sha;
  const headSha = context.payload.pull_request?.head?.sha || sha;
  const runId = String(context.runId);
  const attempt = String(process.env.GITHUB_RUN_ATTEMPT || '1');
  const serverUrl = process.env.GITHUB_SERVER_URL || 'https://github.com';
  if (!/^[a-zA-Z0-9_.-]+\/[a-zA-Z0-9_.-]+$/.test(repo) ||
      !/^[a-f0-9]{40,64}$/.test(sha) || !/^[a-f0-9]{40,64}$/.test(headSha) ||
      !/^[1-9][0-9]{0,19}$/.test(runId) || !/^[1-9][0-9]{0,5}$/.test(attempt) ||
      !/^https:\/\/[a-zA-Z0-9.-]+(?::[0-9]+)?$/.test(serverUrl)) throw new Error('Invalid workflow identity.');
  return { repo, sha, headSha, runId, attempt, serverUrl };
}

function runUrl(report) {
  return `${report.serverUrl}/${report.repo}/actions/runs/${report.runId}`;
}

function formatMetric(metric) {
  return metric.valid ? `${metric.percent.toFixed(2)}% (${metric.covered}/${metric.valid})` : 'N/A (0/0)';
}

function renderMeasurements(reports) {
  const lines = [
    '', '### Current-run measurements', '',
    '| Suite | Coverage | Lines (covered/valid) | Collector branches (covered/valid) |',
    '| --- | --- | ---: | ---: |',
  ];
  for (const report of reports) {
    const measurement = report.measurement;
    const coverage = measurement?.coverage;
    if (!coverage || coverage.status !== 'collected') {
      const label = report.suite === 'Browser' ? 'not-collected (deliberately not instrumented)' : coverage?.status || 'not-collected';
      lines.push(`| ${report.suite} | ${label} | N/A | N/A |`);
      continue;
    }
    lines.push(`| ${report.suite} | ${coverage.status} | ${formatMetric(coverage.lines)} | ${formatMetric(coverage.branches)} |`);
  }
  for (const report of reports) {
    if (report.measurement?.coverage.status === 'collected') {
      const coverage = report.measurement.coverage;
      lines.push('', `<details><summary>${report.suite} module coverage detail</summary>`, '',
        '| .NET module | Lines (covered/valid) | Source-mapped branches (covered/valid) |',
        '| --- | ---: | ---: |');
      for (const module of [...coverage.modules, { name: `${report.suite} source-mapped total`, lines: coverage.lines, branches: coverage.mappedBranches }]) {
        lines.push(`| ${module.name} | ${formatMetric(module.lines)} | ${formatMetric(module.branches)} |`);
      }
      lines.push('', `${report.suite} collector all-branch total: ${formatMetric(coverage.branches)}. Includes branches without source-line details; module branch counts above cover source-mapped conditions only.`,
        '', '</details>');
    }
  }
  lines.push('', '| Suite | Measurement / baseline scope | Discovery cases | Discovery process | Test process |',
    '| --- | --- | ---: | --- | --- |');
  const duration = value => value === null ? 'N/A' : `${value.toFixed(3)} s`;
  for (const report of reports) {
    const m = report.measurement;
    if (!m?.scope) {
      lines.push(`| ${report.suite} | ${m?.status || 'not-collected'}; no comparable baseline | N/A | N/A | N/A |`);
      continue;
    }
    const label = m.baselineEligible ? 'full-suite baseline candidate' : 'not a clean comparable full-suite baseline';
    lines.push(`| ${report.suite} | ${m.status}; ${label}; ${m.scope}, ${m.buildMode}, dirty=${m.workingTreeDirty} | ${m.discovery.count ?? 'N/A'} | ${m.discovery.status}: ${duration(m.discovery.durationSeconds)} | ${m.execution.status}: ${duration(m.execution.durationSeconds)} |`);
  }
  for (const report of reports) {
    const m = report.measurement;
    if (!m?.scope) continue;
    lines.push('', `<details><summary>${report.suite} measurement provenance</summary>`, '',
      `${m.configuration} / ${m.targetFramework}; SDK ${m.runtime.sdk}; ${m.runtime.os} ${m.runtime.architecture}; revision \`${m.sourceRevision}\`.`,
      `Workflow revision verification: ${m.revisionStatus || 'unverified'} (unverified means recorded revision only; no workflow identity supplied).`,
      `Test assembly SHA-256: \`${m.testAssemblySha256}\`; selection SHA-256: \`${m.selectionSha256}\`.`,
      ...m.modules.map(module => `${module.name} pre-instrumentation SHA-256: \`${module.sha256}\`.`));
    if (m.profileSha256) lines.push(`Coverlet ${m.runtime.collectorVersion}; profile SHA-256: \`${m.profileSha256}\`.`);
    lines.push('', '</details>');
  }
  lines.push('', 'Discovery cases and executed TRX results are separate counts; dynamic theories may expand. TRX window is not summed test wall-clock time. Discovery/test-process timings exclude build/setup.',
    '.NET IL coverage only (compiled Razor IL may be included): excludes GeneratedCodeAttribute, ExcludeFromCodeCoverageAttribute and **/obj/**; includes auto-properties and async code, excludes test assemblies. No JavaScript, browser-behavior or Razor-rendering coverage claim. Unit and HTTP coverage overlap and are never merged or averaged.',
    'Single-run measurements provide no historical comparison or flake rate. No coverage percentage gate.');
  return lines;
}

function render(report, workflowResult) {
  const url = runUrl(report);
  const allPassed = report.suites.every(suite => suite.status === 'passed');
  const draftPassed = report.suites.every(suite => suite.status === 'passed' ||
    (suite.suite === 'Browser' && suite.status === 'skipped' && suite.reason === 'draft-skipped'));
  let outcome = allPassed ? 'All three test suites passed.' : draftPassed ?
    'Draft validation passed; Browser tests were intentionally not run.' : 'Test validation did not pass.';
  if (workflowResult && workflowResult !== 'success') {
    outcome = `The test/publish workflow did not succeed (${workflowResult}). Suite results below do not override that outcome.`;
  }
  const lines = [
    '## Portal test results',
    '',
    outcome,
    '',
    `Head commit: [\`${report.headSha.slice(0, 12)}\`](${report.serverUrl}/${report.repo}/commit/${report.headSha})`,
    ` - Tested commit: \`${report.sha.slice(0, 12)}\``,
    ` - [Run ${report.runId}, attempt ${report.attempt}](${url})`,
    '',
    '| Suite | Status | Passed | Failed | Skipped | Total | TRX window | Results |',
    '| --- | --- | ---: | ---: | ---: | ---: | ---: | --- |',
  ];
  for (const suite of report.suites) {
    const artifact = suite.artifactId ? `[TRX + diagnostics](${url}/artifacts/${suite.artifactId})` : `[Run logs](${url})`;
    const testStatus = suite.testStatus && suite.testStatus !== suite.status ? ` (TRX: ${suite.testStatus})` : '';
    lines.push(`| ${suite.suite} | ${suite.status} - ${reasons[suite.reason]}${testStatus} | ${suite.passed} | ${suite.failed} | ${suite.skipped} | ${suite.total} | ${suite.durationSeconds.toFixed(3)} s | ${artifact} |`);
  }
  lines.push(...renderMeasurements(report.suites));
  lines.push('', '### Bootstrap context (not an additional Browser test)', '');
  if (report.bootstrap) {
    const smoke = report.bootstrap;
    const artifact = smoke.artifactId ? ` - [Artifact](${url}/artifacts/${smoke.artifactId})` : '';
    lines.push(`Environment smoke: **${smoke.status}** - ${reasons[smoke.reason]} (${smoke.passed}/${smoke.total} passed)${artifact}.`);
  } else {
    lines.push('Environment smoke: **not run / report unavailable**. It is never included in the three-suite totals.');
  }
  lines.push('', 'Existing test jobs and the required **build-and-test** gate remain authoritative. Failure details and source annotations are in the suite summaries; artifacts expire after 7 days.');
  return lines.join('\n');
}

async function aggregate({ core, context }) {
  const jobs = parseJson(process.env.SUITE_JOBS) || {};
  const report = {
    schema: 1,
    ...identity(context),
    suites: suites.map(([suite, job]) => normalizeSuite(suite, jobs[job], process.env.RUN_BROWSER === 'true')),
    bootstrap: validateReport(parseJson(jobs['browser-tests']?.outputs?.bootstrap, suiteLimit), 'bootstrap'),
  };
  report.suites.forEach(suite => verifyMeasurementRevision(suite, report.sha));
  const output = JSON.stringify(report);
  if (Buffer.byteLength(output) > aggregateLimit) throw new Error('Aggregate report exceeds its output limit.');
  core.setOutput('report', output);
  await core.summary.addRaw(render(report)).write();
  if (report.suites.some(suite => suite.status !== 'passed' && suite.reason !== 'draft-skipped')) {
    core.warning('Test reporting found failed, missing, invalid, or unexpectedly skipped suite results; the test suite gate will reject them.');
  }
  return report;
}

function validateAggregate(text, current) {
  const value = parseJson(text, aggregateLimit);
  if (!value || value.schema !== 1 || ['repo', 'sha', 'headSha', 'runId', 'serverUrl'].some(key => value[key] !== current[key]) ||
      typeof value.attempt !== 'string' || !/^[1-9][0-9]{0,5}$/.test(value.attempt) ||
      Number(value.attempt) > Number(current.attempt) ||
      !Array.isArray(value.suites) || value.suites.length !== 3) return null;
  const checked = suites.map(([suite], index) => validateReport(value.suites[index], suite));
  if (checked.some(suite => !suite)) return null;
  checked.forEach(suite => verifyMeasurementRevision(suite, current.sha));
  return { schema: 1, ...current, attempt: value.attempt, suites: checked, bootstrap: validateReport(value.bootstrap, 'bootstrap') };
}

async function publish({ github, core, context, publishComment = false }) {
  if (!publishComment) return;
  const pr = context.payload.pull_request;
  const current = identity(context);
  if (context.eventName !== 'pull_request' || !pr || pr.head.repo?.full_name !== current.repo ||
      context.actor === 'dependabot[bot]' || pr.user?.login === 'dependabot[bot]') {
    core.info('PR comment skipped: this event does not have an eligible same-repository write context.');
    return;
  }
  let report = validateAggregate(process.env.TEST_REPORT, current);
  if (!report) {
    core.warning('Aggregate test report is missing or invalid; publishing an explicit non-success result.');
    report = { schema: 1, ...current, suites: suites.map(([suite]) => emptyReport(suite)), bootstrap: null };
  }
  const workflowResult = ['success', 'failure', 'cancelled', 'skipped'].includes(process.env.TEST_WORKFLOW_RESULT) ?
    process.env.TEST_WORKFLOW_RESULT : 'unknown';
  const parameters = { ...context.repo, pull_number: pr.number };
  const latest = await github.rest.pulls.get(parameters);
  if (latest.data.state !== 'open' || latest.data.head.sha !== current.headSha) {
    core.info('PR comment skipped: the PR is closed or its head has changed since this run.');
    return;
  }
  const comments = await github.paginate(github.rest.issues.listComments, { ...context.repo, issue_number: pr.number, per_page: 100 });
  const owned = comments.filter(comment => comment.user?.login === 'github-actions[bot]' && comment.user?.type === 'Bot' &&
    typeof comment.body === 'string' && comment.body.startsWith(`${marker}\n`));
  for (const comment of owned) {
    const metadata = /<!-- portal-test-report-run:([^\n]+) -->/.exec(comment.body);
    const prior = metadata && parseJson(metadata[1], 512);
    if (prior && /^[1-9][0-9]{0,19}$/.test(String(prior.runId)) && /^[1-9][0-9]{0,5}$/.test(String(prior.attempt)) &&
        (BigInt(prior.runId) > BigInt(current.runId) ||
         (prior.runId === current.runId && Number(prior.attempt) > Number(current.attempt)))) {
      core.info('PR comment skipped: a newer run or attempt has already reported.');
      return;
    }
  }
  const metadata = JSON.stringify({ runId: current.runId, attempt: current.attempt, headSha: current.headSha });
  const retryNote = report.attempt === current.attempt ? '' :
    `\n\nTest results are from attempt ${report.attempt}; this comment was refreshed in attempt ${current.attempt}.`;
  const body = `${marker}\n<!-- portal-test-report-run:${metadata} -->\n${render(report, workflowResult)}${retryNote}`;
  const beforeWrite = await github.rest.pulls.get(parameters);
  if (beforeWrite.data.state !== 'open' || beforeWrite.data.head.sha !== current.headSha) {
    core.info('PR comment skipped: the head changed while preparing the report.');
    return;
  }
  // API failures deliberately fail this isolated publisher job, never alter the test conclusions.
  if (owned.length) {
    await github.rest.issues.updateComment({ ...context.repo, comment_id: owned[0].id, body });
  } else {
    await github.rest.issues.createComment({ ...context.repo, issue_number: pr.number, body });
  }
}

module.exports = { aggregate, publish, validateReport, validateAggregate, normalizeSuite, render, marker };
