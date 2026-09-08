'use strict';

const { test } = require('node:test');
const assert = require('node:assert/strict');
const path = require('node:path');
const { spawnSync } = require('node:child_process');

const root = path.resolve(__dirname, '../..');
const helper = path.join(root, 'scripts', 'test-measurement.ps1');

function discovery(lines) {
  return spawnSync('pwsh', ['-NoProfile', '-Command',
    "$ErrorActionPreference='Stop'; . $env:MEASUREMENT_HELPER; Get-TestDiscoveryCount -Lines ($env:DISCOVERY_LINES | ConvertFrom-Json)"],
  { cwd: root, env: { ...process.env, MEASUREMENT_HELPER: helper, DISCOVERY_LINES: JSON.stringify(lines) }, encoding: 'utf8' });
}

test('discovery counts only listed cases after the VSTest header', () => {
  const result = discovery([
    '    unrelated preamble', 'The following Tests are available:',
    '    Namespace.Test(value: "one")', '[xUnit] diagnostic', '    Namespace.Test(value: "two")',
    '        continuation, not another case', '',
  ]);
  assert.equal(result.status, 0, result.stderr);
  assert.equal(result.stdout.trim(), '2');
});

test('empty test selection is represented as zero discovered cases', () => {
  const result = discovery(['The following Tests are available:', 'No test matches the given testcase filter.']);
  assert.equal(result.status, 0, result.stderr);
  assert.equal(result.stdout.trim(), '0');
});

test('unknown or duplicate discovery headers fail instead of inventing a count', () => {
  for (const lines of [['    TestWithoutHeader'], ['The following Tests are available:', 'The following Tests are available:']]) {
    const result = discovery(lines);
    assert.notEqual(result.status, 0);
    assert.match(result.stderr, /exactly one expected English test-list header/);
  }
});

test('coverage profile excludes tests and external modules without dropping async state machines', () => {
  const result = spawnSync('pwsh', ['-NoProfile', '-Command',
    '[xml]$p=Get-Content scripts/coverage.runsettings -Raw; $p.RunSettings.DataCollectionRunSettings.DataCollectors.DataCollector.Configuration | Select-Object Include,IncludeTestAssembly,SkipAutoProps,Format,ExcludeByFile,ExcludeByAttribute | ConvertTo-Json -Compress'],
  { cwd: root, encoding: 'utf8' });
  assert.equal(result.status, 0, result.stderr);
  const profile = JSON.parse(result.stdout);
  assert.equal(profile.Include, '[XtremeIdiots.Portal.Web]*,[XtremeIdiots.Portal.Integrations.Forums]*');
  assert.equal(profile.IncludeTestAssembly, 'false');
  assert.equal(profile.SkipAutoProps, 'false');
  assert.equal(profile.Format, 'cobertura');
  assert.equal(profile.ExcludeByFile, '**/obj/**');
  assert.equal(profile.ExcludeByAttribute.includes('CompilerGenerated'), false);
});

test('browser coverage is explicitly unsupported rather than silently reported as zero', () => {
  const result = spawnSync('pwsh', ['-NoProfile', '-File', path.join(root, 'scripts', 'run-tests.ps1'),
    '-Suite', 'Browser', '-Coverage'], { cwd: root, encoding: 'utf8' });
  assert.notEqual(result.status, 0);
  assert.match(result.stderr, /Browser coverage is not collected/);
});

test('coverage percentages use decimal midpoint rounding instead of binary-floating truncation', () => {
  const result = spawnSync('pwsh', ['-NoProfile', '-Command',
    '. ./scripts/measurement-report.ps1; New-CoverageMetric -Covered 201 -Valid 20000 | ConvertTo-Json -Compress'],
  { cwd: root, encoding: 'utf8' });
  assert.equal(result.status, 0, result.stderr);
  assert.deepEqual(JSON.parse(result.stdout), { covered: 201, valid: 20000, percent: 1.01 });
});
