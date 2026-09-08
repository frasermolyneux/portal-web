#requires -Version 7.2

[CmdletBinding()]
param(
    [ValidateSet('Unit', 'HttpIntegration', 'Browser', 'Integration', 'All')]
    [string]$Suite = 'Unit',

    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',

    [string]$Filter,

    [switch]$NoBuild,

    [switch]$Measure,

    [switch]$Coverage
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
. (Join-Path $PSScriptRoot 'test-support.ps1')
. (Join-Path $PSScriptRoot 'test-measurement.ps1')

$Suite = @{ Unit = 'Unit'; HttpIntegration = 'HttpIntegration'; Browser = 'Browser'; Integration = 'Integration'; All = 'All' }[$Suite]
$Configuration = @{ Debug = 'Debug'; Release = 'Release' }[$Configuration]
if ($Coverage -and $Suite -eq 'Browser') {
    throw 'Browser coverage is not collected. Use -Measure for browser execution measurements, or -Coverage with Unit/HttpIntegration/Integration/All.'
}
$Measure = $Measure -or $Coverage

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$suites = switch ($Suite) {
    'All' { @('Unit', 'HttpIntegration', 'Browser') }
    'Integration' { @('HttpIntegration', 'Browser') }
    default { @($Suite) }
}
$unitProject = Join-Path $repositoryRoot 'src\XtremeIdiots.Portal.Web.Tests\XtremeIdiots.Portal.Web.Tests.csproj'
$integrationProject = Join-Path $repositoryRoot 'src\XtremeIdiots.Portal.Web.IntegrationTests\XtremeIdiots.Portal.Web.IntegrationTests.csproj'
$builtProjects = [System.Collections.Generic.HashSet[string]]::new()
$originalDiagnosticsDirectory = $env:PORTAL_TEST_DIAGNOSTICS_DIRECTORY
$originalUiLanguage = $env:DOTNET_CLI_UI_LANGUAGE

Push-Location $repositoryRoot
try {
    & (Join-Path $PSScriptRoot 'setup-test-environment.ps1') -Phase Prerequisites -Configuration $Configuration

    foreach ($selectedSuite in $suites) {
        $project = if ($selectedSuite -eq 'Unit') { $unitProject } else { $integrationProject }
        $buildReused = $NoBuild -or -not $builtProjects.Add($project)
        if (-not $buildReused) {
            Invoke-TestCommand 'dotnet' @('build', $project, '--configuration', $Configuration) "$selectedSuite test project build failed"
        }

        $targetFramework = ([xml](Get-Content $project -Raw)).Project.PropertyGroup.TargetFramework
        $assemblyName = [IO.Path]::GetFileNameWithoutExtension($project)
        $assemblyPath = Join-Path (Split-Path -Parent $project) "bin\$Configuration\$targetFramework\$assemblyName.dll"
        if (-not (Test-Path $assemblyPath)) {
            throw "$Configuration test output is missing: $assemblyPath. Rerun without -NoBuild."
        }

        if ($selectedSuite -eq 'Browser') {
            & (Join-Path $PSScriptRoot 'setup-test-environment.ps1') -Phase Browser -Configuration $Configuration
        }

        $selection = "Category=$selectedSuite"
        if (-not [string]::IsNullOrWhiteSpace($Filter)) {
            $selection = "($selection)&($Filter)"
        }

        $resultsDirectory = Join-Path $repositoryRoot "src\TestResults\$selectedSuite\$([guid]::NewGuid().ToString('N'))"
        $env:PORTAL_TEST_DIAGNOSTICS_DIRECTORY = Join-Path $resultsDirectory 'diagnostics'
        Write-Host "Running $selectedSuite ($Configuration): $selection"
        $testArguments = @(
            'test', $project, '--configuration', $Configuration, '--no-build',
            '--filter', $selection,
            '--logger', "trx;LogFileName=$selectedSuite.trx",
            '--results-directory', $resultsDirectory
        )
        if ($selectedSuite -eq 'Browser') {
            $testArguments += Get-BrowserTestArguments -RepositoryRoot $repositoryRoot
        }
        $collectCoverage = $Coverage -and $selectedSuite -ne 'Browser'
        if ($collectCoverage) {
            $testArguments += @('--settings', (Join-Path $repositoryRoot 'scripts\coverage.runsettings'), '--collect', 'XPlat Code Coverage')
        }

        $measurement = $null
        if ($Measure) {
            $measurement = New-TestMeasurement -RepositoryRoot $repositoryRoot -Project $project -AssemblyPath $assemblyPath `
                -Suite $selectedSuite -Selection $selection -Configuration $Configuration -TargetFramework $targetFramework `
                -Filtered (-not [string]::IsNullOrWhiteSpace($Filter)) -BuildReused $buildReused -CollectCoverage $collectCoverage
            Write-TestMeasurement -Directory $resultsDirectory -Measurement $measurement
        }
        try {
            if ($Measure) {
                $env:DOTNET_CLI_UI_LANGUAGE = 'en'
                $discoveryTimer = [Diagnostics.Stopwatch]::StartNew()
                $measurement.discovery.status = 'failed'
                try {
                    $discovery = @(& dotnet test $project --configuration $Configuration --no-build --list-tests --filter $selection 2>&1 |
                        ForEach-Object { $_.ToString() })
                    $discoveryExit = $LASTEXITCODE
                    [IO.File]::WriteAllLines((Join-Path $resultsDirectory 'discovery.txt'), [string[]]$discovery)
                    if ($discoveryExit -ne 0) {
                        throw "Test discovery failed (exit code $discoveryExit). Inspect $resultsDirectory."
                    }
                    $measurement.discovery.count = Get-TestDiscoveryCount -Lines $discovery
                    $measurement.discovery.status = 'completed'
                    if ($measurement.discovery.count -eq 0) {
                        throw "No tests discovered for '$selection'. Inspect $resultsDirectory."
                    }
                    Write-Host "Discovered $($measurement.discovery.count) VSTest cases for $selectedSuite."
                }
                finally {
                    $discoveryTimer.Stop()
                    $measurement.discovery.durationSeconds = [math]::Round($discoveryTimer.Elapsed.TotalSeconds, 3)
                }
            }

            $testTimer = [Diagnostics.Stopwatch]::StartNew()
            try {
                if ($measurement) { $measurement.execution.status = 'failed' }
                & dotnet @testArguments
                $testExit = $LASTEXITCODE
                if ($measurement) {
                    $measurement.execution.exitCode = $testExit
                    $measurement.execution.status = $(if ($testExit -eq 0) { 'completed' } else { 'failed' })
                }
                if ($testExit -ne 0) {
                    throw "$selectedSuite tests failed (exit code $testExit); inspect $resultsDirectory."
                }
            }
            finally {
                $testTimer.Stop()
                if ($measurement) { $measurement.execution.durationSeconds = [math]::Round($testTimer.Elapsed.TotalSeconds, 3) }
            }

            $summary = Read-TestRunSummary -ResultsFile (Join-Path $resultsDirectory "$selectedSuite.trx") -Selection $selection
            Write-Host "$selectedSuite results: total=$($summary.Total), executed=$($summary.Executed), passed=$($summary.Passed), skipped=$($summary.Skipped)."
            if ($collectCoverage) {
                $coverageFiles = @(Get-ChildItem -LiteralPath $resultsDirectory -Filter 'coverage.cobertura.xml' -Recurse -File)
                # VSTest may copy a collector attachment into its TRX attachment directory.
                $coverageHashes = @($coverageFiles | Get-FileHash -Algorithm SHA256 | Select-Object -ExpandProperty Hash -Unique)
                if ($coverageHashes.Count -ne 1) {
                    throw "Coverage was requested but exactly one distinct Cobertura report was not produced. Inspect $resultsDirectory."
                }
            }
        }
        finally {
            if ($measurement) {
                $measurement.completedAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
                Write-TestMeasurement -Directory $resultsDirectory -Measurement $measurement
            }
            $env:DOTNET_CLI_UI_LANGUAGE = $originalUiLanguage
        }
    }
}
finally {
    $env:PORTAL_TEST_DIAGNOSTICS_DIRECTORY = $originalDiagnosticsDirectory
    $env:DOTNET_CLI_UI_LANGUAGE = $originalUiLanguage
    Pop-Location
}
