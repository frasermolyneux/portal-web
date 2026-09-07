'use strict';

const marker = '<!-- portal-test-report:v1 -->';
const suites = [
  ['Unit', 'unit-tests'],
  ['HttpIntegration', 'http-integration-tests'],
  ['Browser', 'browser-tests'],
];
const statuses = new Set(['passed', 'failed', 'invalid', 'not-run', 'skipped', 'cancelled']);
const reasons = {
  completed: 'Completed',
  'test-failures': 'Tests or test host failed',
  'run-failed': 'Build/setup/test command failed',
  'report-missing': 'No report; tests may not have started',
  'multiple-reports': 'Multiple reports; possibly stale results',
  'report-invalid': 'Invalid or unsafe report',
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
  const report = emptyReport(suite);
  for (const key of Object.keys(report)) {
    if (key !== 'artifactId') report[key] = value[key];
  }
  report.artifactId = typeof value.artifactId === 'string' && /^[1-9][0-9]{0,19}$/.test(value.artifactId) ? value.artifactId : null;
  return report;
}

function normalizeSuite(suite, job, browserEnabled) {
  const reportText = job?.outputs?.report;
  let report = validateReport(parseJson(reportText, 2048), suite) ||
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
    '| Suite | Status | Passed | Failed | Skipped | Total | Duration | Results |',
    '| --- | --- | ---: | ---: | ---: | ---: | ---: | --- |',
  ];
  for (const suite of report.suites) {
    const artifact = suite.artifactId ? `[TRX + diagnostics](${url}/artifacts/${suite.artifactId})` : `[Run logs](${url})`;
    lines.push(`| ${suite.suite} | ${suite.status} - ${reasons[suite.reason]} | ${suite.passed} | ${suite.failed} | ${suite.skipped} | ${suite.total} | ${suite.durationSeconds.toFixed(3)} s | ${artifact} |`);
  }
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
    bootstrap: validateReport(parseJson(jobs['browser-tests']?.outputs?.bootstrap, 2048), 'bootstrap'),
  };
  const output = JSON.stringify(report);
  if (Buffer.byteLength(output) > 8192) throw new Error('Aggregate report exceeds its output limit.');
  core.setOutput('report', output);
  await core.summary.addRaw(render(report)).write();
  if (report.suites.some(suite => suite.status !== 'passed' && suite.reason !== 'draft-skipped')) {
    core.warning('Test reporting found failed, missing, invalid, or unexpectedly skipped suite results; the test suite gate will reject them.');
  }
  return report;
}

function validateAggregate(text, current) {
  const value = parseJson(text, 8192);
  if (!value || value.schema !== 1 || ['repo', 'sha', 'headSha', 'runId', 'serverUrl'].some(key => value[key] !== current[key]) ||
      typeof value.attempt !== 'string' || !/^[1-9][0-9]{0,5}$/.test(value.attempt) ||
      Number(value.attempt) > Number(current.attempt) ||
      !Array.isArray(value.suites) || value.suites.length !== 3) return null;
  const checked = suites.map(([suite], index) => validateReport(value.suites[index], suite));
  if (checked.some(suite => !suite)) return null;
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
