#requires -Version 7.2

[CmdletBinding()]
param(
    [ValidateSet('Unit', 'HttpIntegration', 'Browser', 'Integration', 'All')]
    [string]$Suite = 'Unit',

    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',

    [string]$Filter,

    [switch]$NoBuild
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
. (Join-Path $PSScriptRoot 'test-support.ps1')

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$suites = switch ($Suite) {
    'All' { @('Unit', 'HttpIntegration', 'Browser') }
    'Integration' { @('HttpIntegration', 'Browser') }
    default { @($Suite) }
}
$unitProject = Join-Path $repositoryRoot 'src\XtremeIdiots.Portal.Web.Tests\XtremeIdiots.Portal.Web.Tests.csproj'
$integrationProject = Join-Path $repositoryRoot 'src\XtremeIdiots.Portal.Web.IntegrationTests\XtremeIdiots.Portal.Web.IntegrationTests.csproj'
$builtProjects = [System.Collections.Generic.HashSet[string]]::new()

Push-Location $repositoryRoot
try {
    & (Join-Path $PSScriptRoot 'setup-test-environment.ps1') -Phase Prerequisites -Configuration $Configuration

    foreach ($selectedSuite in $suites) {
        $project = if ($selectedSuite -eq 'Unit') { $unitProject } else { $integrationProject }
        if (-not $NoBuild -and $builtProjects.Add($project)) {
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
        Write-Host "Running $selectedSuite ($Configuration): $selection"
        Invoke-TestCommand 'dotnet' @(
            'test', $project, '--configuration', $Configuration, '--no-build',
            '--filter', $selection,
            '--logger', "trx;LogFileName=$selectedSuite.trx",
            '--results-directory', $resultsDirectory
        ) "$selectedSuite tests failed; inspect $resultsDirectory"

        $summary = Read-TestRunSummary -ResultsFile (Join-Path $resultsDirectory "$selectedSuite.trx") -Selection $selection
        Write-Host "$selectedSuite results: total=$($summary.Total), executed=$($summary.Executed), passed=$($summary.Passed), skipped=$($summary.Skipped)."
    }
}
finally {
    Pop-Location
}
