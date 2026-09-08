'use strict';

// Run with: node --test .github/scripts/test-reporting.test.js
const { test, after } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const { spawnSync } = require('node:child_process');
const reporting = require('./test-reporting');

const root = path.resolve(__dirname, '../..');
const fixtures = path.join(root, `.test-reporting-validation-${process.pid}`);
fs.mkdirSync(fixtures);
after(() => fs.rmSync(fixtures, { recursive: true, force: true }));
const headSha = 'a'.repeat(40);
const mergeSha = 'b'.repeat(40);
const context = {
  repo: { owner: 'example', repo: 'portal' },
  sha: mergeSha,
  runId: 123,
  actor: 'contributor',
  eventName: 'pull_request',
  payload: { pull_request: { number: 7, head: { sha: headSha, repo: { full_name: 'example/portal' } }, user: { login: 'contributor' } } },
};
process.env.GITHUB_RUN_ATTEMPT = '2';
process.env.GITHUB_SERVER_URL = 'https://github.com';

function passed(suite) {
  return { schema: 1, suite, status: 'passed', reason: 'completed', total: 2, executed: 1, passed: 1, failed: 0, skipped: 1, durationSeconds: 1.25, artifactId: '987' };
}

function allPassed() {
  return {
    schema: 1, repo: 'example/portal', sha: mergeSha, headSha, runId: '123', attempt: '2', serverUrl: 'https://github.com',
    suites: ['Unit', 'HttpIntegration', 'Browser'].map(passed), bootstrap: passed('bootstrap'),
  };
}

function fakeCore() {
  const state = { outputs: {}, summary: '', warnings: [], messages: [] };
  return {
    state,
    setOutput: (name, value) => { state.outputs[name] = value; },
    warning: value => state.warnings.push(value),
    info: value => state.messages.push(value),
    summary: { addRaw(value) { state.summary += value; return this; }, async write() {} },
  };
}

function fakeGithub({ comments = [], currentHead = headSha, secondHead = currentHead, error = null } = {}) {
  const writes = [];
  let gets = 0;
  return {
    writes,
    async paginate() { return comments; },
    rest: {
      pulls: { async get() { gets++; return { data: { state: 'open', head: { sha: gets === 1 ? currentHead : secondHead } } }; } },
      issues: {
        listComments() {},
        async createComment(value) { if (error) throw error; writes.push({ operation: 'create', ...value }); },
        async updateComment(value) { if (error) throw error; writes.push({ operation: 'update', ...value }); },
      },
    },
  };
}

test('aggregate validates counts, reports skipped drafts, and never adds smoke to Browser', async () => {
  const core = fakeCore();
  process.env.SUITE_JOBS = JSON.stringify({
    'unit-tests': { result: 'success', outputs: { report: JSON.stringify(passed('Unit')) } },
    'http-integration-tests': { result: 'success', outputs: { report: JSON.stringify(passed('HttpIntegration')) } },
    'browser-tests': { result: 'skipped', outputs: { bootstrap: JSON.stringify(passed('bootstrap')) } },
  });
  process.env.RUN_BROWSER = 'false';
  const result = await reporting.aggregate({ core, context });
  assert.equal(result.suites[2].status, 'skipped');
  assert.equal(result.suites[2].total, 0);
  assert.equal(result.bootstrap.total, 2);
  assert.match(core.state.summary, /Draft validation passed/);
  assert.ok(Buffer.byteLength(core.state.outputs.report) < 32768);
});

test('missing, malformed, zero, all-skipped, setup failures, cancellation, and unexpected skips cannot pass', () => {
  for (const report of ['', '{bad}', JSON.stringify({ ...passed('Unit'), total: 0, executed: 0, passed: 0, skipped: 0 }),
    JSON.stringify({ ...passed('Unit'), executed: 0, passed: 0, skipped: 2 })]) {
    assert.equal(reporting.normalizeSuite('Unit', { result: 'success', outputs: { report } }, true).status, 'invalid');
  }
  const outputs = { report: JSON.stringify(passed('Unit')) };
  assert.equal(reporting.normalizeSuite('Unit', { result: 'failure', outputs }, true).status, 'failed');
  assert.equal(reporting.normalizeSuite('Unit', { result: 'cancelled', outputs }, true).status, 'cancelled');
  assert.equal(reporting.normalizeSuite('Unit', { result: 'skipped', outputs }, true).status, 'not-run');
  assert.equal(reporting.normalizeSuite('Browser', { result: 'skipped' }, true).status, 'not-run');
});

test('structured report validation rejects injected values and impossible counts', () => {
  for (const changes of [{ suite: '[injected](evil)' }, { reason: '__proto__' }, { status: '**passed**' },
    { durationSeconds: Infinity }, { total: -1 }, { passed: 100 }, { schema: 2 }]) {
    assert.equal(reporting.validateReport({ ...passed('Unit'), ...changes }, 'Unit'), null);
  }
  assert.equal(reporting.validateReport({ ...passed('Unit'), artifactId: '123)\n@everyone' }, 'Unit').artifactId, null);
});

test('unexpected JavaScript parser faults propagate instead of becoming invalid input', () => {
  const original = JSON.parse;
  JSON.parse = () => { throw new TypeError('unexpected parser fault'); };
  try {
    assert.throws(() => reporting.normalizeSuite('Unit', { result: 'success', outputs: { report: '{}' } }, true), /unexpected parser fault/);
  } finally {
    JSON.parse = original;
  }
});

test('publisher is opt-in and skips forks and Dependabot without writes', async () => {
  const github = fakeGithub();
  const core = fakeCore();
  await reporting.publish({ github, core, context });
  const fork = structuredClone(context);
  fork.payload.pull_request.head.repo.full_name = 'fork/portal';
  await reporting.publish({ github, core, context: fork, publishComment: true });
  await reporting.publish({ github, core, context: { ...context, actor: 'dependabot[bot]' }, publishComment: true });
  assert.equal(github.writes.length, 0);
});

test('publisher-only retries retain earlier-attempt results from the same run and head', async () => {
  const prior = { ...allPassed(), attempt: '1' };
  process.env.TEST_REPORT = JSON.stringify(prior);
  process.env.TEST_WORKFLOW_RESULT = 'success';
  const github = fakeGithub();
  await reporting.publish({ github, core: fakeCore(), context, publishComment: true });
  assert.match(github.writes[0].body, /All three test suites passed/);
  assert.match(github.writes[0].body, /runs\/123\/artifacts\/987/);
  assert.match(github.writes[0].body, /results are from attempt 1; this comment was refreshed in attempt 2/);
  assert.match(github.writes[0].body, /"attempt":"2"/);
  assert.equal(reporting.validateAggregate(JSON.stringify({ ...prior, attempt: '3' }), allPassed()), null);
});

test('publisher creates once, updates only its own marker, and retains identity/artifact links', async () => {
  process.env.TEST_REPORT = JSON.stringify(allPassed());
  process.env.TEST_WORKFLOW_RESULT = 'success';
  const core = fakeCore();
  const first = fakeGithub();
  await reporting.publish({ github: first, core, context, publishComment: true });
  assert.equal(first.writes[0].operation, 'create');
  assert.match(first.writes[0].body, /All three test suites passed/);
  assert.match(first.writes[0].body, /runs\/123\/artifacts\/987/);
  assert.match(first.writes[0].body, /attempt 2/);
  const comments = [
    { id: 1, body: first.writes[0].body, user: { login: 'other-bot[bot]', type: 'Bot' } },
    { id: 2, body: first.writes[0].body, user: { login: 'github-actions[bot]', type: 'Bot' } },
  ];
  const second = fakeGithub({ comments });
  await reporting.publish({ github: second, core, context, publishComment: true });
  assert.equal(second.writes.length, 1);
  assert.equal(second.writes[0].operation, 'update');
  assert.equal(second.writes[0].comment_id, 2);
});

test('publisher does not overwrite newer heads, runs, attempts, or a head changing during publication', async () => {
  process.env.TEST_REPORT = JSON.stringify(allPassed());
  for (const options of [
    { currentHead: mergeSha },
    { secondHead: mergeSha },
    ...[{ runId: '124', attempt: '1' }, { runId: '123', attempt: '3' }].map(metadata => ({
      comments: [{ id: 3, user: { login: 'github-actions[bot]', type: 'Bot' },
        body: `${reporting.marker}\n<!-- portal-test-report-run:${JSON.stringify(metadata)} -->\nprevious` }],
    })),
  ]) {
    const github = fakeGithub(options);
    await reporting.publish({ github, core: fakeCore(), context, publishComment: true });
    assert.equal(github.writes.length, 0);
  }
});

test('publisher fails closed for absent or stale output and never masks publish failures', async () => {
  for (const text of ['', '{broken}', JSON.stringify({ ...allPassed(), headSha: mergeSha })]) {
    process.env.TEST_REPORT = text;
    process.env.TEST_WORKFLOW_RESULT = 'success';
    const github = fakeGithub();
    await reporting.publish({ github, core: fakeCore(), context, publishComment: true });
    assert.match(github.writes[0].body, /Test validation did not pass/);
    assert.doesNotMatch(github.writes[0].body, /All three test suites passed/);
  }
  process.env.TEST_REPORT = JSON.stringify(allPassed());
  process.env.TEST_WORKFLOW_RESULT = 'failure';
  const github = fakeGithub();
  await reporting.publish({ github, core: fakeCore(), context, publishComment: true });
  assert.match(github.writes[0].body, /workflow did not succeed \(failure\)/);
  assert.doesNotMatch(github.writes[0].body, /All three test suites passed/);
  await assert.rejects(reporting.publish({
    github: fakeGithub({ error: new Error('403 write denied') }), core: fakeCore(), context, publishComment: true,
  }), /403 write denied/);
});

function xmlEscape(text) {
  return text.replaceAll('&', '&amp;').replaceAll('<', '&lt;').replaceAll('>', '&gt;').replaceAll('"', '&quot;');
}

function trx({ total = 2, passedCount = 1, failed = 0, skipped = 1, message = '', stack = '', testName = 'a passing test' } = {}) {
  const results = [
    ...Array.from({ length: passedCount }, () => '<UnitTestResult testName="passed" outcome="Passed" />'),
    ...Array.from({ length: skipped }, () => '<UnitTestResult testName="skipped" outcome="NotExecuted" />'),
    ...Array.from({ length: failed }, () => `<UnitTestResult testName="${xmlEscape(testName)}" outcome="Failed"><Output><ErrorInfo><Message>${xmlEscape(message)}</Message><StackTrace>${xmlEscape(stack)}</StackTrace></ErrorInfo></Output></UnitTestResult>`),
  ].join('');
  return `<TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010"><Times start="2026-09-07T10:00:00Z" finish="2026-09-07T10:00:01.250Z" /><Results>${results}</Results><ResultSummary outcome="${failed ? 'Failed' : 'Completed'}"><Counters total="${total}" executed="${total - skipped}" passed="${passedCount}" failed="${failed}" /></ResultSummary></TestRun>`;
}

let fixtureNumber = 0;
function runPowerShell(xml, { outcome = 'success', duplicate = false, commandOverride = '', expectReport = true,
  suite = 'Unit', requireMeasurements = false, measurement, coverage, duplicateMeasurement = false,
  duplicateCoverage = false, githubSha = mergeSha, prepare = () => {} } = {}) {
  const directory = path.join(fixtures, String(++fixtureNumber));
  fs.mkdirSync(directory);
  if (xml !== null) fs.writeFileSync(path.join(directory, 'Unit.trx'), xml);
  if (duplicate) fs.writeFileSync(path.join(directory, 'old.trx'), xml);
  if (measurement !== undefined) {
    const text = typeof measurement === 'string' ? measurement : JSON.stringify(measurement);
    fs.writeFileSync(path.join(directory, 'measurement.json'), text);
    fs.writeFileSync(path.join(directory, 'discovery.txt'), 'The following Tests are available:\n    example\n');
    if (duplicateMeasurement) {
      fs.mkdirSync(path.join(directory, 'stale'));
      fs.writeFileSync(path.join(directory, 'stale', 'measurement.json'), text);
    }
  }
  if (coverage !== undefined) {
    fs.mkdirSync(path.join(directory, 'collector'));
    fs.writeFileSync(path.join(directory, 'collector', 'coverage.cobertura.xml'), coverage);
    if (duplicateCoverage) fs.writeFileSync(path.join(directory, 'coverage.cobertura.xml'), coverage);
  }
  prepare(directory);
  const output = path.join(directory, 'outputs.txt');
  const summary = path.join(directory, 'summary.md');
  const script = path.join(root, 'scripts', 'report-test-results.ps1');
  const args = commandOverride ? ['-NoProfile', '-Command',
    `${commandOverride}; & $env:REPORT_SCRIPT -Suite Unit -ResultsDirectory $env:REPORT_TEST_DIRECTORY -RunOutcome $env:REPORT_TEST_OUTCOME -ArtifactId 987`] :
    ['-NoProfile', '-File', script, '-Suite', suite, '-ResultsDirectory', directory, '-RunOutcome', outcome, '-ArtifactId', '987',
      ...(requireMeasurements ? ['-RequireMeasurements'] : [])];
  const processResult = spawnSync('pwsh', args, {
    encoding: 'utf8', cwd: root, timeout: 30000,
    env: { ...process.env, GITHUB_ACTIONS: 'true', GITHUB_OUTPUT: output, GITHUB_STEP_SUMMARY: summary,
      GITHUB_REPOSITORY: 'example/portal', GITHUB_RUN_ID: '123',
      GITHUB_SHA: githubSha,
      REPORT_SCRIPT: script, REPORT_TEST_DIRECTORY: directory, REPORT_TEST_OUTCOME: outcome },
  });
  if (expectReport) assert.ok(fs.existsSync(output), processResult.stderr || processResult.error?.message);
  return {
    exitCode: processResult.status,
    report: fs.existsSync(output) ? JSON.parse(fs.readFileSync(output, 'utf8').trim().slice('report='.length)) : null,
    summary: fs.existsSync(summary) ? fs.readFileSync(summary, 'utf8') : '',
    stdout: processResult.stdout,
    stderr: processResult.stderr,
  };
}

test('PowerShell reads valid namespaced TRX and bounded machine-only output', () => {
  const result = runPowerShell(trx());
  assert.equal(result.exitCode, 0);
  assert.equal(result.report.status, 'passed');
  assert.equal(result.report.skipped, 1);
  assert.equal(result.report.durationSeconds, 1.25);
  assert.match(result.summary, /runs\/123\/artifacts\/987/);
  assert.ok(JSON.stringify(result.report).length < 2048);
});

const modules = ['XtremeIdiots.Portal.Web', 'XtremeIdiots.Portal.Integrations.Forums'];
function measurement(suite = 'Unit') {
  const requested = suite !== 'Browser';
  return {
    schema: 1, suite, selection: `Category=${suite}`, configuration: 'Release', targetFramework: 'net10.0',
    scope: 'full-suite', buildMode: 'built', sourceRevision: mergeSha, workingTreeDirty: false,
    testAssemblySha256: 'c'.repeat(64), modules: modules.map(name => ({ name, sha256: 'd'.repeat(64) })),
    runtime: { sdk: '10.0.400', os: 'Windows', architecture: 'X64', collectorVersion: requested ? '10.0.1' : null },
    coverage: { requested, profileSha256: requested ? 'e'.repeat(64) : null },
    discovery: { status: 'completed', count: 1, durationSeconds: 0.25 },
    execution: { status: 'completed', exitCode: 0, durationSeconds: 2.5 },
    startedAtUtc: '2026-09-07T10:00:00Z', completedAtUtc: '2026-09-07T10:00:03Z',
  };
}

function cobertura({ zeroBranches = false } = {}) {
  const branch = zeroBranches ? 'branch="false"' : 'branch="true" condition-coverage="50% (1/2)"';
  const rate = zeroBranches ? '0' : '0.5';
  return `<coverage lines-covered="3" lines-valid="4" branches-covered="${zeroBranches ? 0 : 1}" branches-valid="${zeroBranches ? 0 : 2}" line-rate="0.75" branch-rate="${rate}">
    <packages><package name="${modules[0]}" line-rate="0.6666" branch-rate="${rate}"><classes><class name="Example" filename="Example.cs"><methods>
    <method name="Example"><lines><line number="1" hits="1" branch="false" /></lines></method></methods>
    <lines><line number="1" hits="1" ${branch} /><line number="2" hits="1" branch="false" /><line number="3" hits="0" branch="false" /></lines></class></classes></package>
    <package name="${modules[1]}" line-rate="1" branch-rate="0"><classes><class name="Other" filename="Other.cs"><lines>
    <line number="10" hits="1" branch="false" /></lines></class></classes></package></packages></coverage>`;
}

function measuredFixture() {
  const m = measurement();
  delete m.schema;
  delete m.suite;
  delete m.selection;
  m.status = 'collected';
  m.baselineEligible = true;
  m.selectionSha256 = 'f'.repeat(64);
  m.profileSha256 = m.coverage.profileSha256;
  m.coverage = {
    status: 'collected', requested: true,
    lines: { covered: 3, valid: 4, percent: 75 },
    branches: { covered: 1, valid: 2, percent: 50 },
    mappedBranches: { covered: 1, valid: 2, percent: 50 },
    modules: [
      { name: modules[0], lines: { covered: 2, valid: 3, percent: 66.67 }, branches: { covered: 1, valid: 2, percent: 50 } },
      { name: modules[1], lines: { covered: 1, valid: 1, percent: 100 }, branches: { covered: 0, valid: 0, percent: null } },
    ],
  };
  return { ...passed('Unit'), measurement: m };
}

test('headline coverage uses collector branch totals, with source-mapped module detail kept separate', () => {
  const measured = measuredFixture();
  measured.measurement.coverage.branches = { covered: 1, valid: 4, percent: 25 };
  const body = reporting.render({ ...allPassed(), suites: [measured, ...allPassed().suites.slice(1)] });
  assert.match(body, /\| Unit \| collected \| 75\.00% \(3\/4\) \| 25\.00% \(1\/4\) \|/);
  assert.match(body, /<details><summary>Unit module coverage detail<\/summary>/);
  assert.match(body, /Source-mapped branches/);
});

test('collector version is validated provenance, not a duplicated dependency pin', () => {
  const metadata = measurement();
  metadata.runtime.collectorVersion = '10.0.2';
  const result = runPowerShell(trx(), { requireMeasurements: true, measurement: metadata, coverage: cobertura() });
  assert.equal(result.exitCode, 0, result.stderr);
  assert.equal(result.report.measurement.runtime.collectorVersion, '10.0.2');
  assert.ok(reporting.validateReport(result.report, 'Unit'));
});

test('PowerShell derives per-module and suite coverage counts, percentages and distinct process timings', () => {
  const result = runPowerShell(trx(), { requireMeasurements: true, measurement: measurement(), coverage: cobertura() });
  assert.equal(result.exitCode, 0, result.stderr);
  const m = result.report.measurement;
  assert.equal(m.status, 'collected');
  assert.equal(m.baselineEligible, true);
  assert.equal(m.revisionStatus, 'matched');
  assert.equal(m.discovery.count, 1, 'dynamic discovery counts need not equal TRX total 2');
  assert.equal(result.report.total, 2);
  assert.equal(result.report.durationSeconds, 1.25);
  assert.equal(m.execution.durationSeconds, 2.5);
  assert.deepEqual(m.coverage.lines, { covered: 3, valid: 4, percent: 75 });
  assert.deepEqual(m.coverage.branches, { covered: 1, valid: 2, percent: 50 });
  assert.deepEqual(m.coverage.mappedBranches, { covered: 1, valid: 2, percent: 50 });
  assert.equal(m.coverage.modules[0].lines.percent, 66.67);
  assert.equal(m.coverage.modules[1].branches.percent, null);
  assert.ok(reporting.validateReport(result.report, 'Unit'));
  assert.ok(Buffer.byteLength(JSON.stringify(result.report)) < 8192);
  assert.match(result.summary, /TRX window/);
  assert.match(result.summary, /75.00% \(3\/4\)/);
  assert.match(result.summary, /N\/A \(0\/0\)/);
  assert.match(result.summary, /No coverage percentage gate/);
  assert.match(result.summary, /compiled Razor IL may be included/);
  assert.match(result.summary, /pre-instrumentation SHA-256/);
  assert.doesNotMatch(JSON.stringify(result.report), /Example\.cs|following Tests|Category=Unit/);
});

test('PowerShell uses N/A rather than zero percent for an empty branch denominator', () => {
  const result = runPowerShell(trx(), { requireMeasurements: true, measurement: measurement(), coverage: cobertura({ zeroBranches: true }) });
  assert.equal(result.exitCode, 0, result.stderr);
  assert.deepEqual(result.report.measurement.coverage.branches, { covered: 0, valid: 0, percent: null });
  assert.ok(reporting.validateReport(result.report, 'Unit'));
});

test('collected zero coverage is valid and imposes no arbitrary percentage target', () => {
  const coverage = cobertura().replaceAll('hits="1"', 'hits="0"').replaceAll(/line-rate="[^"]+"/g, 'line-rate="0"')
    .replaceAll(/branch-rate="[^"]+"/g, 'branch-rate="0"').replace('lines-covered="3"', 'lines-covered="0"')
    .replace('branches-covered="1"', 'branches-covered="0"').replace('50% (1/2)', '0% (0/2)');
  const result = runPowerShell(trx(), { requireMeasurements: true, measurement: measurement(), coverage });
  assert.equal(result.exitCode, 0, result.stderr);
  assert.equal(result.report.measurement.coverage.lines.percent, 0);
  assert.equal(result.report.measurement.coverage.branches.percent, 0);
});

test('real Coverlet shape preserves unmapped collector branches without inventing module totals', () => {
  const coverage = cobertura().replaceAll('branch="true"', 'branch="True"').replaceAll('branch="false"', 'branch="False"')
    .replace('branch-rate="0"', 'branch-rate="0.015600000000000001"')
    .replace('branches-covered="1"', 'branches-covered="2"').replace('branches-valid="2"', 'branches-valid="4"');
  const result = runPowerShell(trx(), { requireMeasurements: true, measurement: measurement(), coverage });
  assert.equal(result.exitCode, 0, result.stderr);
  assert.deepEqual(result.report.measurement.coverage.branches, { covered: 2, valid: 4, percent: 50 });
  assert.deepEqual(result.report.measurement.coverage.mappedBranches, { covered: 1, valid: 2, percent: 50 });
  assert.match(result.summary, /collector all-branch total: 50.00% \(2\/4\)/);
  assert.match(result.summary, /source-mapped conditions only/);
  assert.ok(reporting.validateReport(result.report, 'Unit'));
});

test('measurement requirements preserve test status/counts and do not break legacy, bootstrap or not-started runs', () => {
  const missing = runPowerShell(trx(), { requireMeasurements: true });
  assert.notEqual(missing.exitCode, 0);
  assert.equal(missing.report.status, 'passed');
  assert.equal(missing.report.total, 2);
  assert.equal(missing.report.measurement.status, 'unavailable');
  assert.match(missing.summary, /Required measurement reporting failed/);
  const missingCoverage = runPowerShell(trx(), { requireMeasurements: true, measurement: measurement() });
  assert.notEqual(missingCoverage.exitCode, 0);
  assert.equal(missingCoverage.report.measurement.status, 'collected');
  assert.equal(missingCoverage.report.measurement.coverage.status, 'unavailable');
  assert.equal(missingCoverage.report.status, 'passed');
  assert.doesNotMatch(missingCoverage.summary, /0.00%/);
  const timingsOnly = measurement();
  timingsOnly.coverage = { requested: false, profileSha256: null };
  timingsOnly.runtime.collectorVersion = null;
  const requiredCoverage = runPowerShell(trx(), { requireMeasurements: true, measurement: timingsOnly });
  assert.notEqual(requiredCoverage.exitCode, 0, 'normal Unit CI runs must request coverage');
  assert.equal(requiredCoverage.report.status, 'passed');
  assert.equal(requiredCoverage.report.measurement.coverage.status, 'not-collected');
  assert.equal(runPowerShell(trx(), { measurement: timingsOnly }).exitCode, 0, 'local timing-only runs are valid');
  assert.equal(runPowerShell(trx(), { suite: 'bootstrap', requireMeasurements: true }).exitCode, 0);
  assert.equal(runPowerShell(null, { outcome: 'skipped', requireMeasurements: true }).exitCode, 0);
  assert.equal(runPowerShell(trx()).exitCode, 0);
  assert.ok(reporting.validateReport(passed('Unit'), 'Unit'), 'schema-1 reports without measurements remain supported');
});

test('PowerShell rejects another revision in CI without erasing tests, while local provenance remains explicit', () => {
  const stale = runPowerShell(trx(), {
    requireMeasurements: true, measurement: { ...measurement(), sourceRevision: headSha }, coverage: cobertura(),
  });
  assert.notEqual(stale.exitCode, 0);
  assert.equal(stale.report.status, 'passed');
  assert.equal(stale.report.total, 2);
  assert.equal(stale.report.measurement.status, 'invalid');
  assert.equal(stale.report.measurement.revisionStatus, 'mismatched');
  assert.equal(stale.report.measurement.baselineEligible, false);
  assert.match(stale.summary, /Workflow revision verification: mismatched/);
  const local = runPowerShell(trx(), {
    requireMeasurements: true, githubSha: '',
    measurement: { ...measurement(), sourceRevision: headSha, workingTreeDirty: true, buildMode: 'reused' },
    coverage: cobertura(),
  });
  assert.equal(local.exitCode, 0, local.stderr);
  assert.equal(local.report.measurement.status, 'collected');
  assert.equal(local.report.measurement.sourceRevision, headSha);
  assert.equal(local.report.measurement.revisionStatus, 'unverified');
  assert.match(local.summary, /reused, dirty=true/);
  assert.match(local.summary, /recorded revision only/);
});

test('Browser collects timings without instrumenting coverage', () => {
  const result = runPowerShell(trx(), { suite: 'Browser', requireMeasurements: true, measurement: measurement('Browser') });
  assert.equal(result.exitCode, 0, result.stderr);
  assert.equal(result.report.measurement.status, 'collected');
  assert.equal(result.report.measurement.coverage.status, 'not-collected');
  assert.match(result.summary, /deliberately not instrumented/);
  assert.ok(reporting.validateReport(result.report, 'Browser'));
  const invalid = measurement('Browser');
  invalid.coverage = measurement().coverage;
  invalid.runtime.collectorVersion = '10.0.1';
  assert.notEqual(runPowerShell(trx(), { suite: 'Browser', requireMeasurements: true, measurement: invalid, coverage: cobertura() }).exitCode, 0);
});

test('PowerShell rejects malformed, ambiguous, inconsistent, oversized and unsafe coverage without zeroing tests', () => {
  const xml = cobertura();
  const cases = [
    { coverage: '<broken' },
    { coverage: '<!DOCTYPE coverage [<!ENTITY attacker SYSTEM "file:///not-permitted">]><coverage>&attacker;</coverage>' },
    { coverage: ' '.repeat(32 * 1024 * 1024 + 1) },
    { coverage: xml, duplicateCoverage: true,
      prepare: directory => fs.appendFileSync(path.join(directory, 'coverage.cobertura.xml'), '\n') },
    { coverage: xml.replace('lines-covered="3"', 'lines-covered="4"') },
    { coverage: xml.replace('branches-valid="2"', 'branches-valid="1"') },
    { coverage: xml.replace('line-rate="0.75"', 'line-rate="0.9"') },
    { coverage: xml.replace('hits="1" branch="true"', 'hits="-1" branch="true"') },
    { coverage: xml.replace('50% (1/2)', '150% (3/2)') },
    { coverage: xml.replace(modules[1], 'Unexpected.Module') },
    { coverage: xml.replace(modules[1], modules[0]) },
  ];
  for (const options of cases) {
    const result = runPowerShell(trx(), { requireMeasurements: true, measurement: measurement(), ...options });
    assert.notEqual(result.exitCode, 0, result.stderr);
    assert.equal(result.report.status, 'passed');
    assert.equal(result.report.total, 2);
    assert.equal(result.report.measurement.coverage.status, 'invalid');
    assert.equal(result.report.measurement.baselineEligible, false);
    assert.ok(reporting.validateReport(result.report, 'Unit'));
    assert.doesNotMatch(result.summary + result.stderr, /attacker|not-permitted|Unexpected.Module/);
  }
});

test('identical VSTest coverage attachment mirrors count once, with bounded copy enumeration', () => {
  const result = runPowerShell(trx(), {
    requireMeasurements: true, measurement: measurement(), coverage: cobertura(), duplicateCoverage: true,
  });
  assert.equal(result.exitCode, 0, result.stderr);
  assert.deepEqual(result.report.measurement.coverage.lines, { covered: 3, valid: 4, percent: 75 });
  assert.deepEqual(result.report.measurement.coverage.branches, { covered: 1, valid: 2, percent: 50 });
  assert.ok(reporting.validateReport(result.report, 'Unit'));
  const excessive = runPowerShell(trx(), {
    requireMeasurements: true, measurement: measurement(), coverage: cobertura(),
    prepare: directory => {
      for (let i = 0; i < 8; i++) {
        const copy = path.join(directory, `mirror-${i}`);
        fs.mkdirSync(copy);
        fs.writeFileSync(path.join(copy, 'coverage.cobertura.xml'), cobertura());
      }
    },
  });
  assert.notEqual(excessive.exitCode, 0);
  assert.equal(excessive.report.total, 2);
  assert.equal(excessive.report.measurement.coverage.status, 'invalid');
});

test('PowerShell rejects unsafe or invalid metadata and invocation paths, not legitimate discovery expansion', () => {
  const cases = [
    { measurement: '{broken' },
    { measurement: '{}' },
    { measurement: { ...measurement(), schema: '1' } },
    { measurement: { ...measurement(), targetFramework: ['net10.0', '<script>@everyone</script>'] } },
    { measurement: { ...measurement(), runtime: { ...measurement().runtime, collectorVersion: ['10.0.1', '<script>'] } } },
    { measurement: ' '.repeat(32769) },
    { measurement: JSON.stringify(measurement()).replace('"schema":1', '"schema":1,"schema":1') },
    { duplicateMeasurement: true },
    { measurement: { ...measurement(), configuration: '<script>@everyone</script>' } },
    { measurement: { ...measurement(), scope: 'full-suite', selection: 'FullyQualifiedName=filtered' } },
    { measurement: { ...measurement(), discovery: { status: 'completed', count: 0, durationSeconds: 1 } } },
    { measurement: { ...measurement(), discovery: { status: 'completed', count: -1, durationSeconds: 1 } } },
    { measurement: { ...measurement(), execution: { status: 'completed', exitCode: 1, durationSeconds: 1 } } },
    { measurement: { ...measurement(), execution: { status: 'completed', exitCode: 'unsafe', durationSeconds: 1 } } },
    { measurement: { ...measurement(), completedAtUtc: '2026-09-07T09:00:00Z' } },
    { prepare: directory => fs.renameSync(path.join(directory, 'measurement.json'), path.join(directory, 'collector', 'measurement.json')) },
    { prepare: directory => fs.rmSync(path.join(directory, 'discovery.txt')) },
    { prepare: directory => fs.symlinkSync(path.join(directory, 'collector'), path.join(directory, 'linked'), process.platform === 'win32' ? 'junction' : 'dir') },
  ];
  for (const options of cases) {
    const result = runPowerShell(trx(), { requireMeasurements: true, measurement: measurement(), coverage: cobertura(), ...options });
    assert.notEqual(result.exitCode, 0, result.stderr);
    assert.equal(result.report.status, 'passed');
    assert.equal(result.report.total, 2);
    assert.equal(result.report.measurement.status, 'invalid');
    assert.doesNotMatch(result.summary, /<script>|@everyone/);
  }
});

test('filtered, reused, dirty, failed, cancelled and all-skipped runs never claim a clean full baseline', () => {
  for (const options of [
    { measurement: { ...measurement(), scope: 'filtered', selection: 'Category=Unit&Name=<script>@everyone</script>' } },
    { measurement: { ...measurement(), buildMode: 'reused' } },
    { measurement: { ...measurement(), workingTreeDirty: true } },
    { outcome: 'failure' }, { outcome: 'cancelled' },
    { xml: trx({ total: 2, passedCount: 0, skipped: 2 }) },
    { measurement: { ...measurement(), discovery: { status: 'failed', count: null, durationSeconds: 0.1 },
      execution: { status: 'not-run', exitCode: null, durationSeconds: null } } },
  ]) {
    const result = runPowerShell(options.xml || trx(), { requireMeasurements: true, measurement: measurement(), coverage: cobertura(), ...options });
    assert.equal(result.report.measurement.baselineEligible, false, result.stderr);
    assert.match(result.summary, /Not a clean comparable full-suite baseline/);
    assert.doesNotMatch(result.summary, /<script>|@everyone/);
    assert.ok(reporting.validateReport(result.report, 'Unit'));
  }
});

test('measurement JSON validation rejects injected metadata, invalid counts and fabricated percentages', () => {
  const measuredReport = measuredFixture();
  for (const change of [
    m => { m.runtime.os = '<script>'; },
    m => { m.sourceRevision = '@everyone'; },
    m => { m.coverage.lines.percent = 99; },
    m => { m.coverage.lines.covered = 5; },
    m => { m.coverage.modules[0].name = 'Injected.Module'; },
    m => { m.coverage.modules[0].lines = { covered: 1, valid: 3, percent: 33.33 }; },
    m => { m.coverage.branches = { covered: 0, valid: 0, percent: 0 }; },
    m => { m.discovery.count = 0; },
    m => { m.execution.durationSeconds = -1; },
    m => { m.runtime.collectorVersion = '<script>'; },
  ]) {
    const report = structuredClone(measuredReport);
    change(report.measurement);
    assert.equal(reporting.validateReport(report, 'Unit'), null);
  }
  const withNoise = structuredClone(measuredReport);
  withNoise.measurement.untrusted = '<script>@everyone</script>';
  assert.doesNotMatch(JSON.stringify(reporting.validateReport(withNoise, 'Unit')), /untrusted|<script>|@everyone/);
  const cancelled = reporting.normalizeSuite('Unit', { result: 'cancelled', outputs: { report: JSON.stringify(measuredReport) } }, true);
  assert.equal(cancelled.measurement.baselineEligible, false);
  assert.equal(cancelled.testStatus, 'passed', 'job outcomes must not erase actual test status');
  const reportFailure = reporting.normalizeSuite('Unit', { result: 'failure', outputs: { report: JSON.stringify(measuredReport) } }, true);
  assert.match(reporting.render({ ...allPassed(), suites: [reportFailure, ...allPassed().suites.slice(1)] }), /TRX: passed/);
});

test('measured aggregates fit bounded outputs and retain separate coverage on publisher-only reruns', async () => {
  const measuredReport = measuredFixture();
  const report = allPassed();
  report.attempt = '1';
  report.suites[0] = structuredClone(measuredReport);
  report.suites[1] = { ...structuredClone(measuredReport), suite: 'HttpIntegration' };
  report.suites[1].measurement.workingTreeDirty = true;
  report.suites[1].measurement.baselineEligible = false;
  const core = fakeCore();
  process.env.SUITE_JOBS = JSON.stringify(Object.fromEntries(
    ['unit-tests', 'http-integration-tests', 'browser-tests'].map((job, index) =>
      [job, { result: 'success', outputs: { report: JSON.stringify(report.suites[index]) } }]),
  ));
  process.env.RUN_BROWSER = 'true';
  const aggregate = await reporting.aggregate({ core, context });
  assert.ok(Buffer.byteLength(core.state.outputs.report) < 32768);
  assert.equal(aggregate.suites[0].measurement.coverage.lines.percent, 75);
  assert.equal(aggregate.suites[0].measurement.revisionStatus, 'matched');
  assert.match(core.state.summary, /not a clean comparable full-suite baseline/);
  assert.match(core.state.summary, /compiled Razor IL may be included/);
  assert.match(core.state.summary, /pre-instrumentation SHA-256/);
  process.env.TEST_REPORT = JSON.stringify(report);
  process.env.TEST_WORKFLOW_RESULT = 'success';
  const github = fakeGithub();
  await reporting.publish({ github, core, context, publishComment: true });
  assert.match(github.writes[0].body, /Unit source-mapped total/);
  assert.match(github.writes[0].body, /HttpIntegration source-mapped total/);
  assert.match(github.writes[0].body, /no historical comparison or flake rate/);
  assert.match(github.writes[0].body, /results are from attempt 1/);
  assert.ok(github.writes[0].body.length < 32768);
  assert.equal(reporting.validateAggregate(' '.repeat(32769), allPassed()), null);
  assert.equal(reporting.normalizeSuite('Unit', { result: 'success', outputs: { report: ' '.repeat(8193) } }, true).status, 'invalid');
});

test('aggregation and publication recheck measurement revisions against the tested SHA, not claimed verification', async () => {
  const stale = measuredFixture();
  stale.measurement.sourceRevision = headSha;
  stale.measurement.revisionStatus = 'matched';
  process.env.SUITE_JOBS = JSON.stringify({
    'unit-tests': { result: 'success', outputs: { report: JSON.stringify(stale) } },
    'http-integration-tests': { result: 'success', outputs: { report: JSON.stringify(passed('HttpIntegration')) } },
    'browser-tests': { result: 'success', outputs: { report: JSON.stringify(passed('Browser')) } },
  });
  process.env.RUN_BROWSER = 'true';
  const core = fakeCore();
  const aggregate = await reporting.aggregate({ core, context });
  const unit = aggregate.suites[0];
  assert.equal(unit.status, 'invalid');
  assert.equal(unit.testStatus, 'passed');
  assert.equal(unit.total, 2);
  assert.equal(unit.measurement.revisionStatus, 'mismatched');
  assert.equal(unit.measurement.baselineEligible, false);
  assert.match(core.state.summary, /Measurement revision differs from the tested checkout/);
  assert.doesNotMatch(core.state.summary, /All three test suites passed/);
  const prior = allPassed();
  prior.attempt = '1';
  prior.suites[0] = stale;
  process.env.TEST_REPORT = JSON.stringify(prior);
  process.env.TEST_WORKFLOW_RESULT = 'success';
  const github = fakeGithub();
  await reporting.publish({ github, core: fakeCore(), context, publishComment: true });
  assert.match(github.writes[0].body, /Workflow revision verification: mismatched/);
  assert.match(github.writes[0].body, /TRX: passed/);
  assert.doesNotMatch(github.writes[0].body, /All three test suites passed|full-suite baseline candidate/);
  assert.match(github.writes[0].body, /results are from attempt 1/);
});

test('PowerShell rejects missing, malformed, inconsistent, duplicate, empty and all-skipped reports', () => {
  const cases = [
    [null, 'report-missing'],
    ['<broken', 'report-invalid'],
    [trx({ total: 5 }), 'report-invalid'],
    [trx(), 'multiple-reports', true],
    [trx({ total: 0, passedCount: 0, skipped: 0 }), 'zero-tests'],
    [trx({ total: 2, passedCount: 0, skipped: 2 }), 'all-skipped'],
  ];
  for (const [xml, reason, duplicate] of cases) {
    const result = runPowerShell(xml, { duplicate });
    assert.notEqual(result.exitCode, 0, reason);
    assert.equal(result.report.status, 'invalid', reason);
    assert.equal(result.report.reason, reason);
  }
});

test('PowerShell disables DTD/entity expansion, bounds file size and hides parser payloads', () => {
  const dtd = runPowerShell('<!DOCTYPE TestRun [<!ENTITY attacker SYSTEM "file:///not-permitted">]><TestRun>&attacker;</TestRun>');
  assert.equal(dtd.report.status, 'invalid');
  assert.doesNotMatch(dtd.summary, /attacker|not-permitted/);
  const oversized = runPowerShell(' '.repeat(32 * 1024 * 1024 + 1));
  assert.equal(oversized.report.status, 'invalid');
  assert.ok(oversized.summary.length < 2000);
});

test('PowerShell preserves setup failures, not-started and cancelled outcomes', () => {
  const failure = runPowerShell(trx(), { outcome: 'failure' });
  assert.equal(failure.report.status, 'failed');
  assert.equal(failure.report.reason, 'run-failed');
  assert.equal(failure.exitCode, 0, 'reporting must not replace the already-failed command');
  assert.equal(runPowerShell(null, { outcome: 'skipped' }).report.status, 'not-run');
  assert.equal(runPowerShell(trx(), { outcome: 'cancelled' }).report.status, 'cancelled');
});

test('PowerShell bounds and escapes failures and emits reliable source annotations safely', () => {
  const source = path.join(root, 'src', 'XtremeIdiots.Portal.Web.Tests', 'Controllers', 'HomeControllerTests.cs');
  const result = runPowerShell(trx({
    total: 10, passedCount: 0, skipped: 0, failed: 10,
    testName: 'unsafe,title: @everyone <script> **bold** %\n::warning::injected',
    message: `</pre><script>evil</script> @everyone\n::error::forged % ${'x'.repeat(10000)}`,
    stack: `   at Test.Example() in ${source}:line 20\n`,
  }), { outcome: 'failure' });
  assert.equal(result.report.status, 'failed');
  assert.equal(result.report.failed, 10);
  assert.ok(result.summary.length < 40000);
  assert.doesNotMatch(result.summary, /<script>|@everyone|\*\*bold\*\*/);
  assert.match(result.summary, /Only the first 8 of 10/);
  assert.equal((result.stdout.match(/^::error file=/gm) || []).length, 8);
  assert.match(result.stdout, /file=src\/XtremeIdiots.Portal.Web.Tests\/Controllers\/HomeControllerTests.cs,line=20/);
  assert.match(result.stdout, /unsafe%2Ctitle%3A/);
  assert.doesNotMatch(result.stdout, /^::warning::injected|^::error::forged/gm);
});

test('PowerShell leaves unmappable/external source locations in the summary, not annotations', () => {
  const result = runPowerShell(trx({
    total: 1, passedCount: 0, skipped: 0, failed: 1, message: 'failure',
    stack: '   at External.Test() in /external/not-checked-out.cs:line 20',
  }), { outcome: 'failure' });
  assert.doesNotMatch(result.stdout, /^::error file=/m);
});

test('PowerShell resolves deterministic SourceLink paths only to existing repository test sources', () => {
  const source = '/_/src/XtremeIdiots.Portal.Web.Tests/Controllers/HomeControllerTests.cs';
  const mapped = runPowerShell(trx({
    total: 1, passedCount: 0, skipped: 0, failed: 1, message: 'failure',
    stack: `   at Test.Example() in ${source}:line 20`,
  }), { outcome: 'failure' });
  assert.match(mapped.stdout, /^::error file=src\/XtremeIdiots.Portal.Web.Tests\/Controllers\/HomeControllerTests.cs,line=20,/m);
  const rejected = runPowerShell(trx({
    total: 1, passedCount: 0, skipped: 0, failed: 1, message: 'failure',
    stack: `   at Test.Example() in ${source}:line 0\n` +
      '   at Test.Example() in /_/src/XtremeIdiots.Portal.Web.Tests/Missing.cs:line 20\n' +
      '   at Test.Example() in /_/src/../../external.cs:line 20',
  }), { outcome: 'failure' });
  assert.doesNotMatch(rejected.stdout, /^::error file=/m);
});

test('PowerShell reports expected source access failures safely and propagates implementation faults', () => {
  const sourceXml = trx({
    total: 1, passedCount: 0, skipped: 0, failed: 1, message: 'failure',
    stack: '   at Test.Example() in /_/src/XtremeIdiots.Portal.Web.Tests/Controllers/HomeControllerTests.cs:line 20',
  });
  const expected = runPowerShell(sourceXml, {
    outcome: 'failure',
    commandOverride: "function Get-Item { throw [IO.IOException]::new('untrusted source exception details') }",
  });
  assert.equal(expected.report.status, 'failed');
  assert.match(expected.stdout, /Source annotation unavailable: the recorded source location could not be resolved safely/);
  assert.doesNotMatch(expected.stdout + expected.stderr, /untrusted source exception details/);
  for (const command of ['Get-ChildItem', 'Get-Item']) {
    const unexpected = runPowerShell(sourceXml, {
      outcome: 'failure', expectReport: false,
      commandOverride: `function ${command} { throw [InvalidOperationException]::new('unexpected reporting fault') }`,
    });
    assert.notEqual(unexpected.exitCode, 0);
    assert.equal(unexpected.report, null);
    assert.match(unexpected.stderr, /unexpected reporting fault/);
  }
});
