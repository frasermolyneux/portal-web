[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$testProject = Join-Path $repositoryRoot 'src/XtremeIdiots.Portal.Web.IntegrationTests/XtremeIdiots.Portal.Web.IntegrationTests.csproj'
$playwrightScript = Join-Path $repositoryRoot 'src/XtremeIdiots.Portal.Web.IntegrationTests/bin/Release/net9.0/playwright.ps1'
$resultsDirectory = Join-Path $repositoryRoot 'src/TestResults'

Push-Location $repositoryRoot
try {
    dotnet build $testProject --configuration Release
    if ($LASTEXITCODE -ne 0) {
        throw "Integration test build failed with exit code $LASTEXITCODE."
    }

    $installArguments = @($playwrightScript, 'install')
    if ($IsLinux) {
        $installArguments += '--with-deps'
    }
    $installArguments += 'chromium'

    & pwsh @installArguments
    if ($LASTEXITCODE -ne 0) {
        throw "Playwright browser installation failed with exit code $LASTEXITCODE."
    }

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
