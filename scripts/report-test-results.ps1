#requires -Version 7.2

[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateSet('Unit', 'HttpIntegration', 'Browser', 'bootstrap')]
    [string]$Suite,

    [string]$ResultsDirectory,

    [ValidateSet('success', 'failure', 'cancelled', 'skipped', 'unknown')]
    [string]$RunOutcome = 'unknown',

    [string]$ArtifactId,

    [ValidatePattern('^[a-zA-Z][a-zA-Z0-9_-]*$')]
    [string]$OutputName = 'report'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repositoryRoot = Split-Path -Parent $PSScriptRoot
if (-not $ResultsDirectory) {
    $ResultsDirectory = Join-Path $repositoryRoot "src\TestResults\$Suite"
}

function Limit-ReportText {
    param([string]$Text, [int]$Length = 2000)

    if ($Text.Length -gt $Length) { $Text = $Text.Substring(0, $Length) + ' [truncated]' }
    [regex]::Replace($Text, '[\p{Cc}\p{Cf}-[\r\n\t]]', '')
}

function ConvertTo-ReportHtml {
    param([string]$Text, [int]$Length = 2000)

    $text = [System.Net.WebUtility]::HtmlEncode((Limit-ReportText $Text $Length))
    # Encode Markdown and mention syntax as well as HTML, including inside summary elements.
    [regex]::Replace($text, '[@\\`*_{}\[\]()#+.!|~\-]', { param($match) "&#$([int][char]$match.Value);" })
}

function ConvertTo-AnnotationText {
    param([string]$Text, [switch]$Property)

    $text = (Limit-ReportText $Text 1500).Replace('%', '%25').Replace("`r", '%0D').Replace("`n", '%0A')
    if ($Property) { $text = $text.Replace(':', '%3A').Replace(',', '%2C') }
    $text
}

function Get-TestSource {
    param([string]$StackTrace)

    foreach ($match in [regex]::Matches((Limit-ReportText $StackTrace 12000), '(?m)^\s*at .+ in (?<path>.+):line (?<line>[1-9][0-9]{0,6})\s*$')) {
        try {
            $sourcePath = $match.Groups['path'].Value.Trim().Replace('\', '/')
            # Deterministic .NET builds map the repository root to /_/ in portable PDBs.
            if ($sourcePath.StartsWith('/_/src/', [StringComparison]::Ordinal)) {
                $sourcePath = Join-Path $repositoryRoot $sourcePath.Substring(3)
            }
            $path = [IO.Path]::GetFullPath($sourcePath)
            $relative = [IO.Path]::GetRelativePath($repositoryRoot, $path).Replace('\', '/')
            if ($relative -notmatch '^src/XtremeIdiots\.Portal\.Web\.(?:Tests|IntegrationTests)/.+\.(?:cs|feature)$' -or
                -not (Test-Path -LiteralPath $path -PathType Leaf)) { continue }
            $entry = Get-Item -LiteralPath $path
            $linked = $false
            while ($entry -and $entry.FullName -ne $repositoryRoot) {
                if ($entry.Attributes -band [IO.FileAttributes]::ReparsePoint) { $linked = $true; break }
                $entry = if ($entry -is [IO.FileInfo]) { $entry.Directory } else { $entry.Parent }
            }
            if (-not $linked) {
                return @{ File = $relative; Line = $match.Groups['line'].Value }
            }
        }
        catch [ArgumentException], [NotSupportedException], [IO.IOException], [UnauthorizedAccessException],
            [System.Security.SecurityException], [System.Management.Automation.ItemNotFoundException],
            [System.Management.Automation.DriveNotFoundException] {
            Write-Warning 'Source annotation unavailable: the recorded source location could not be resolved safely.'
        }
    }
}

$report = [ordered]@{
    schema = 1
    suite = $Suite
    status = 'invalid'
    reason = 'report-missing'
    total = 0
    executed = 0
    passed = 0
    failed = 0
    skipped = 0
    durationSeconds = 0
    artifactId = $(if ($ArtifactId -match '^[1-9][0-9]{0,19}$') { $ArtifactId } else { $null })
}
$failures = @()
$document = $null
if ($RunOutcome -eq 'skipped') {
    $report.status = 'not-run'
    $report.reason = 'not-started'
}
else {
    try {
        $files = @(if (Test-Path -LiteralPath $ResultsDirectory -PathType Container) {
            Get-ChildItem -LiteralPath $ResultsDirectory -Filter '*.trx' -File -Recurse | Select-Object -First 2
        })
        if ($files.Count -ne 1) {
            if ($files.Count -gt 1) { $report.reason = 'multiple-reports' }
            throw [IO.InvalidDataException]::new('Expected exactly one TRX for this invocation.')
        }
        $report.reason = 'report-invalid'
        if ($files[0].Length -gt 32MB -or $files[0].Length -eq 0 -or
            ($files[0].Attributes -band [IO.FileAttributes]::ReparsePoint)) {
            throw [IO.InvalidDataException]::new('TRX is empty, oversized, or a symbolic link.')
        }
        $settings = [System.Xml.XmlReaderSettings]::new()
        $settings.DtdProcessing = [System.Xml.DtdProcessing]::Prohibit
        $settings.XmlResolver = $null
        $settings.MaxCharactersInDocument = 32MB
        $settings.MaxCharactersFromEntities = 1024
        $reader = [System.Xml.XmlReader]::Create($files[0].FullName, $settings)
        try {
            $document = [System.Xml.XmlDocument]::new()
            $document.XmlResolver = $null
            $document.Load($reader)
        }
        finally { $reader.Dispose() }

        $summaries = $document.SelectNodes("/*[local-name()='TestRun']/*[local-name()='ResultSummary']")
        $counters = $document.SelectNodes("/*[local-name()='TestRun']/*[local-name()='ResultSummary']/*[local-name()='Counters']")
        if ($summaries.Count -ne 1 -or $counters.Count -ne 1) { throw [IO.InvalidDataException]::new('Missing or duplicate TRX summary.') }
        foreach ($name in @('total', 'executed', 'passed', 'failed')) {
            $number = 0
            if (-not [int]::TryParse($counters[0].GetAttribute($name), [ref]$number) -or $number -lt 0 -or $number -gt 1000000) {
                throw [IO.InvalidDataException]::new('Invalid TRX counters.')
            }
            $report[$name] = $number
        }
        $results = $document.SelectNodes("/*[local-name()='TestRun']/*[local-name()='Results']/*[local-name()='UnitTestResult']")
        $passed = @($results | Where-Object { $_.GetAttribute('outcome') -eq 'Passed' }).Count
        $skipped = @($results | Where-Object { $_.GetAttribute('outcome') -eq 'NotExecuted' }).Count
        $failures = @($results | Where-Object { $_.GetAttribute('outcome') -notin @('Passed', 'NotExecuted') })
        if ($results.Count -ne $report.total -or $passed -ne $report.passed -or
            $report.executed -ne ($report.total - $skipped) -or $report.failed -gt $failures.Count) {
            throw [IO.InvalidDataException]::new('TRX results do not match the counters.')
        }
        $report.failed = $failures.Count
        $report.skipped = $skipped
        $times = $document.SelectSingleNode("/*[local-name()='TestRun']/*[local-name()='Times']")
        $start = [DateTimeOffset]::MinValue
        $finish = [DateTimeOffset]::MinValue
        if ($null -eq $times -or
            -not [DateTimeOffset]::TryParse($times.GetAttribute('start'), [cultureinfo]::InvariantCulture, [System.Globalization.DateTimeStyles]::None, [ref]$start) -or
            -not [DateTimeOffset]::TryParse($times.GetAttribute('finish'), [cultureinfo]::InvariantCulture, [System.Globalization.DateTimeStyles]::None, [ref]$finish) -or
            $finish -lt $start -or ($finish - $start).TotalDays -gt 366) { throw [IO.InvalidDataException]::new('Invalid TRX duration.') }
        $report.durationSeconds = [math]::Round(($finish - $start).TotalSeconds, 3)
        $report.status = 'failed'
        $report.reason = 'test-failures'
        if ($report.total -eq 0) { $report.status = 'invalid'; $report.reason = 'zero-tests' }
        elseif ($report.executed -eq 0) { $report.status = 'invalid'; $report.reason = 'all-skipped' }
        elseif ($report.failed -eq 0 -and $summaries[0].GetAttribute('outcome') -in @('Completed', 'Passed')) {
            $report.status = 'passed'
            $report.reason = 'completed'
        }
        if ($RunOutcome -eq 'failure' -and $report.status -eq 'passed') {
            $report.status = 'failed'
            $report.reason = 'run-failed'
        }
    }
    catch [IO.InvalidDataException], [System.Xml.XmlException], [IO.IOException], [UnauthorizedAccessException],
        [System.Security.SecurityException], [System.Management.Automation.ItemNotFoundException],
        [System.Management.Automation.DriveNotFoundException] {
        # Parser messages may contain attacker-controlled XML; expose only fixed reason codes.
        $report.status = 'invalid'
        $failures = @()
        foreach ($name in @('total', 'executed', 'passed', 'failed', 'skipped', 'durationSeconds')) { $report[$name] = 0 }
    }
}
if ($RunOutcome -eq 'cancelled') { $report.status = 'cancelled'; $report.reason = 'cancelled' }

$reasons = @{
    completed = 'Tests completed.'
    'test-failures' = 'Tests or the test host failed.'
    'run-failed' = 'The test/build/setup command failed despite passing test results.'
    'report-missing' = 'No TRX was produced; tests may not have started.'
    'multiple-reports' = 'Multiple TRX files found; refusing to combine possibly stale invocations.'
    'report-invalid' = 'The TRX is malformed, inconsistent, unsafe, or exceeds the 32 MiB limit.'
    'zero-tests' = 'No tests were discovered.'
    'all-skipped' = 'Every discovered test was skipped.'
    'not-started' = 'The test action was not reached.'
    cancelled = 'The test command was cancelled.'
}
$title = if ($Suite -eq 'bootstrap') { 'Bootstrap smoke (environment verification only)' } else { "$Suite tests" }
$summary = [System.Text.StringBuilder]::new()
[void]$summary.AppendLine("## $title")
[void]$summary.AppendLine()
[void]$summary.AppendLine("**$($report.status)** - $($reasons[$report.reason])")
[void]$summary.AppendLine()
[void]$summary.AppendLine('| Total | Executed | Passed | Failed | Skipped | Duration |')
[void]$summary.AppendLine('| ---: | ---: | ---: | ---: | ---: | ---: |')
[void]$summary.AppendLine("| $($report.total) | $($report.executed) | $($report.passed) | $($report.failed) | $($report.skipped) | $($report.durationSeconds.ToString([cultureinfo]::InvariantCulture)) s |")

$runUrl = $null
if ($env:GITHUB_SERVER_URL -match '^https://[a-zA-Z0-9.-]+(?::[0-9]+)?$' -and
    $env:GITHUB_REPOSITORY -match '^[a-zA-Z0-9_.-]+/[a-zA-Z0-9_.-]+$' -and $env:GITHUB_RUN_ID -match '^[0-9]+$') {
    $runUrl = "$env:GITHUB_SERVER_URL/$env:GITHUB_REPOSITORY/actions/runs/$env:GITHUB_RUN_ID"
    [void]$summary.AppendLine()
    [void]$summary.AppendLine("[Workflow run]($runUrl)")
    if ($report.artifactId) { [void]$summary.AppendLine(" - [TRX and diagnostics artifact]($runUrl/artifacts/$($report.artifactId))") }
    else { [void]$summary.AppendLine(' - Artifact unavailable (see upload step).') }
}
if ($Suite -eq 'bootstrap') {
    [void]$summary.AppendLine()
    [void]$summary.AppendLine('Bootstrap is not included in the Browser suite counts.')
}
foreach ($failure in ($failures | Select-Object -First 8)) {
    $name = $failure.GetAttribute('testName')
    $message = $failure.SelectSingleNode("./*[local-name()='Output']/*[local-name()='ErrorInfo']/*[local-name()='Message']")
    $stack = $failure.SelectSingleNode("./*[local-name()='Output']/*[local-name()='ErrorInfo']/*[local-name()='StackTrace']")
    $messageText = if ($message) { $message.InnerText } else { 'No failure message was recorded.' }
    $stackText = if ($stack) { $stack.InnerText } else { '' }
    [void]$summary.AppendLine()
    [void]$summary.AppendLine("<details><summary>$(ConvertTo-ReportHtml $name 200)</summary>")
    [void]$summary.AppendLine("<pre>$(ConvertTo-ReportHtml "$messageText`n$stackText")</pre></details>")
    $source = Get-TestSource $stackText
    if ($source -and $env:GITHUB_ACTIONS -eq 'true') {
        $file = ConvertTo-AnnotationText $source.File -Property
        $annotationTitle = ConvertTo-AnnotationText "$Suite : $(Limit-ReportText $name 200)" -Property
        Write-Host "::error file=$file,line=$($source.Line),title=$annotationTitle::$(ConvertTo-AnnotationText $messageText)"
    }
}
if ($failures.Count -gt 8) { [void]$summary.AppendLine("Only the first 8 of $($failures.Count) failures are shown; see the artifact for complete results.") }
if ($env:GITHUB_STEP_SUMMARY) { [IO.File]::AppendAllText($env:GITHUB_STEP_SUMMARY, $summary.ToString(), [System.Text.UTF8Encoding]::new($false)) }
else { Write-Host $summary.ToString() }
$json = $report | ConvertTo-Json -Compress
if ($env:GITHUB_OUTPUT) { [IO.File]::AppendAllText($env:GITHUB_OUTPUT, "$OutputName=$json`n", [System.Text.UTF8Encoding]::new($false)) }
Write-Host "$Suite report: $json"
if ($RunOutcome -eq 'success' -and $report.status -ne 'passed') {
    throw "$Suite did not produce a valid passing test report ($($report.reason))."
}
