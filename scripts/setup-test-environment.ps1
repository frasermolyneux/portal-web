#requires -Version 7.2

[CmdletBinding()]
param(
    [ValidateSet('All', 'Prerequisites', 'Dependencies', 'Browser')]
    [string]$Phase = 'All',

    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

. (Join-Path $PSScriptRoot 'test-support.ps1')

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$solution = Join-Path $repositoryRoot 'src\XtremeIdiots.Portal.Web.slnx'
$webProjectDirectory = Join-Path $repositoryRoot 'src\XtremeIdiots.Portal.Web'
$testProjectDirectory = Join-Path $repositoryRoot 'src\XtremeIdiots.Portal.Web.IntegrationTests'
$testProject = Join-Path $testProjectDirectory 'XtremeIdiots.Portal.Web.IntegrationTests.csproj'
$requiredSdk = (Get-Content (Join-Path $repositoryRoot 'global.json') -Raw | ConvertFrom-Json).sdk.version
$requiredNodeMajor = [int](Get-Content (Join-Path $repositoryRoot '.node-version') -Raw).Trim()
$originalDiagnosticsDirectory = $env:PORTAL_TEST_DIAGNOSTICS_DIRECTORY

Push-Location $repositoryRoot
try {
    foreach ($command in @('git', 'dotnet', 'node', 'npm', 'pwsh')) {
        if (-not (Get-Command $command -ErrorAction SilentlyContinue)) {
            throw "Required command '$command' was not found. Install Git, .NET SDK $requiredSdk, Node.js $requiredNodeMajor.x (with npm >=10), and PowerShell >=7.2, then reopen your terminal. See docs/ui-testing.md."
        }
    }

    $sdkVersion = Invoke-TestCommand 'dotnet' @('--version') "Install the .NET SDK required by global.json ($requiredSdk); SDK resolution failed"
    $nodeVersion = Invoke-TestCommand 'node' @('--version') 'Could not determine the Node.js version'
    if (([version]$nodeVersion.TrimStart('v')).Major -ne $requiredNodeMajor) {
        throw "Node.js $requiredNodeMajor.x is required by .node-version; found $nodeVersion. Switch Node.js versions and rerun setup."
    }

    $npmVersion = Invoke-TestCommand 'npm' @('--version') 'Could not determine the npm version'
    if (([version]$npmVersion).Major -lt 10) {
        throw "npm >=10 is required for the committed lockfile; found $npmVersion. Install the npm bundled with Node.js $requiredNodeMajor.x."
    }

    Write-Host "Test prerequisites: .NET $sdkVersion; Node.js $nodeVersion; npm $npmVersion; PowerShell $($PSVersionTable.PSVersion)."
    Initialize-TestRepositoryHistory -RepositoryRoot $repositoryRoot

    if ($Phase -in @('All', 'Dependencies')) {
        Push-Location $webProjectDirectory
        try {
            Invoke-TestCommand 'npm' @('ci', '--include=dev', '--no-audit', '--no-fund') 'Locked npm installation failed; check package.json/package-lock.json consistency and registry access'
        }
        finally {
            Pop-Location
        }
    }

    if ($Phase -eq 'All') {
        Invoke-TestCommand 'dotnet' @('build', $solution, '--configuration', $Configuration) "$Configuration solution build failed; fix restore/build errors before installing browsers"
    }

    if ($Phase -in @('All', 'Browser')) {
        $targetFramework = ([xml](Get-Content $testProject -Raw)).Project.PropertyGroup.TargetFramework
        $outputDirectory = Join-Path $testProjectDirectory "bin\$Configuration\$targetFramework"
        $playwrightScript = Join-Path $outputDirectory 'playwright.ps1'
        $testAssembly = Join-Path $outputDirectory 'XtremeIdiots.Portal.Web.IntegrationTests.dll'
        if (-not (Test-Path $playwrightScript) -or -not (Test-Path $testAssembly)) {
            throw "$Configuration integration-test outputs are missing. Run 'pwsh -NoProfile -File scripts/setup-test-environment.ps1 -Configuration $Configuration' without -Phase Browser to build them."
        }

        $installArguments = @('-NoProfile', '-File', $playwrightScript, 'install', 'chromium')
        if ($IsLinux) {
            $installArguments += '--with-deps'
        }

        Invoke-TestCommand 'pwsh' $installArguments 'Chromium installation failed; on Linux allow sudo for system dependencies, and check access to the Playwright download hosts'

        # A fresh result path prevents a stale TRX from making an empty test selection look successful.
        $resultsDirectory = Join-Path $repositoryRoot "src\TestResults\bootstrap\$([guid]::NewGuid().ToString('N'))"
        $env:PORTAL_TEST_DIAGNOSTICS_DIRECTORY = Join-Path $resultsDirectory 'diagnostics'
        $smokeTest = 'XtremeIdiots.Portal.Web.IntegrationTests.Playwright.LoginPageIntegrationTests.LoginPage_RendersInChromium'
        $smokeArguments = @(
            'test', $testProject, '--configuration', $Configuration, '--no-build',
            '--filter', "Category=Browser&FullyQualifiedName=$smokeTest",
            '--logger', 'trx;LogFileName=bootstrap.trx',
            '--results-directory', $resultsDirectory
        )
        $smokeArguments += Get-BrowserTestArguments -RepositoryRoot $repositoryRoot
        Invoke-TestCommand 'dotnet' $smokeArguments "Chromium smoke test failed; inspect $resultsDirectory and the test output"

        $resultsFile = Join-Path $resultsDirectory 'bootstrap.trx'
        Read-TestRunSummary -ResultsFile $resultsFile -Selection 'Chromium bootstrap smoke' -ExpectedTotal 1 | Out-Null

        Write-Host "Chromium smoke test passed. Results: $resultsFile"
    }

    Write-Host "Test environment setup completed ($Phase)."
}
finally {
    $env:PORTAL_TEST_DIAGNOSTICS_DIRECTORY = $originalDiagnosticsDirectory
    Pop-Location
}
