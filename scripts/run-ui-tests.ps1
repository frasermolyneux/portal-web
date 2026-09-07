#requires -Version 7.2

[CmdletBinding()]
param(
    [switch]$SkipBuild
)

$ErrorActionPreference = 'Stop'

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$testProject = Join-Path $repositoryRoot 'src/XtremeIdiots.Portal.Web.IntegrationTests/XtremeIdiots.Portal.Web.IntegrationTests.csproj'
$resultsDirectory = Join-Path $repositoryRoot 'src/TestResults'

Push-Location $repositoryRoot
try {
    $setupPhase = if ($SkipBuild) { 'Browser' } else { 'All' }
    & (Join-Path $PSScriptRoot 'setup-test-environment.ps1') -Phase $setupPhase

    dotnet test $testProject `
        --configuration Release `
        --no-build `
        --logger 'trx;LogFileName=integration-tests.trx' `
        --results-directory $resultsDirectory
    if ($LASTEXITCODE -ne 0) {
        throw "Integration tests failed with exit code $LASTEXITCODE."
    }
}
finally {
    Pop-Location
}
