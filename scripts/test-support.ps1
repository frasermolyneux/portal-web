#requires -Version 7.2

function Invoke-TestCommand {
    param(
        [string]$Command,
        [string[]]$Arguments,
        [string]$FailureMessage
    )

    & $Command @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "$FailureMessage (exit code $LASTEXITCODE)."
    }
}

function Read-TestRunSummary {
    param(
        [string]$ResultsFile,
        [string]$Selection,
        [int]$ExpectedTotal = -1
    )

    if (-not (Test-Path $ResultsFile)) {
        throw "No test report was produced for '$Selection'. Check discovery and the runner output. Expected: $ResultsFile"
    }

    $results = [xml](Get-Content $ResultsFile -Raw)
    $counters = $results.SelectSingleNode("/*[local-name()='TestRun']/*[local-name()='ResultSummary']/*[local-name()='Counters']")
    if ($null -eq $counters) {
        throw "The test report for '$Selection' has no counters: $ResultsFile"
    }

    $counts = @{}
    foreach ($name in @('total', 'executed', 'passed', 'failed')) {
        $value = 0
        if (-not [int]::TryParse($counters.GetAttribute($name), [ref]$value) -or $value -lt 0) {
            throw "The test report for '$Selection' has an invalid '$name' counter: $ResultsFile"
        }
        $counts[$name] = $value
    }

    if ($counts.total -eq 0 -or $counts.executed -eq 0 -or $counts.passed -eq 0) {
        throw "No tests executed successfully for '$Selection'. Zero matches and all-skipped selections are failures. Check the filter and categories. Results: $ResultsFile"
    }
    if ($counts.failed -gt 0 -or ($ExpectedTotal -ge 0 -and $counts.passed -ne $ExpectedTotal) -or
        ($ExpectedTotal -ge 0 -and $counts.total -ne $ExpectedTotal)) {
        throw "Unexpected test results for '$Selection' (total=$($counts.total), passed=$($counts.passed), failed=$($counts.failed)). Results: $ResultsFile"
    }

    [pscustomobject]@{
        Total = $counts.total
        Executed = $counts.executed
        Passed = $counts.passed
        Failed = $counts.failed
        Skipped = $counts.total - $counts.executed
    }
}
