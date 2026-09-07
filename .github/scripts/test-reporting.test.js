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
  assert.ok(core.state.outputs.report.length < 8192);
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
function runPowerShell(xml, { outcome = 'success', duplicate = false, commandOverride = '', expectReport = true } = {}) {
  const directory = path.join(fixtures, String(++fixtureNumber));
  fs.mkdirSync(directory);
  if (xml !== null) fs.writeFileSync(path.join(directory, 'Unit.trx'), xml);
  if (duplicate) fs.writeFileSync(path.join(directory, 'old.trx'), xml);
  const output = path.join(directory, 'outputs.txt');
  const summary = path.join(directory, 'summary.md');
  const script = path.join(root, 'scripts', 'report-test-results.ps1');
  const args = commandOverride ? ['-NoProfile', '-Command',
    `${commandOverride}; & $env:REPORT_SCRIPT -Suite Unit -ResultsDirectory $env:REPORT_TEST_DIRECTORY -RunOutcome $env:REPORT_TEST_OUTCOME -ArtifactId 987`] :
    ['-NoProfile', '-File', script, '-Suite', 'Unit', '-ResultsDirectory', directory, '-RunOutcome', outcome, '-ArtifactId', '987'];
  const processResult = spawnSync('pwsh', args, {
    encoding: 'utf8', cwd: root, timeout: 30000,
    env: { ...process.env, GITHUB_ACTIONS: 'true', GITHUB_OUTPUT: output, GITHUB_STEP_SUMMARY: summary,
      GITHUB_REPOSITORY: 'example/portal', GITHUB_RUN_ID: '123',
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
