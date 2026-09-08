#requires -Version 7.2

$measurementModules = @('XtremeIdiots.Portal.Web', 'XtremeIdiots.Portal.Integrations.Forums')

function Assert-Measurement {
    param([bool]$Condition)
    if (-not $Condition) { throw [IO.InvalidDataException]::new('Invalid measurement contract.') }
}

function Test-MeasurementNumber {
    param($Value, [double]$Maximum = 31622400, [switch]$Integer)
    $numeric = $Value -is [long] -or $Value -is [int] -or $Value -is [double] -or $Value -is [decimal]
    $numeric -and [double]::IsFinite([double]$Value) -and $Value -ge 0 -and $Value -le $Maximum -and
        (-not $Integer -or [math]::Truncate($Value) -eq $Value)
}

function ConvertFrom-MeasurementElement {
    param([System.Text.Json.JsonElement]$Element)
    switch ($Element.ValueKind.ToString()) {
        Object {
            $value = @{}
            foreach ($property in $Element.EnumerateObject()) {
                Assert-Measurement (-not $value.ContainsKey($property.Name))
                $value[$property.Name] = ConvertFrom-MeasurementElement $property.Value
            }
            return $value
        }
        Array { return ,@($Element.EnumerateArray() | ForEach-Object { ConvertFrom-MeasurementElement $_ }) }
        String { return $Element.GetString() }
        Number { return $Element.GetDouble() }
        True { return $true }
        False { return $false }
        Null { return $null }
        default { throw [IO.InvalidDataException]::new('Invalid JSON value.') }
    }
}

function Get-MeasurementFiles {
    param([string]$Directory)
    $root = [IO.DirectoryInfo]::new([IO.Path]::GetFullPath($Directory))
    if (-not $root.Exists) { return }
    $ancestor = $root
    while ($ancestor) {
        Assert-Measurement (-not ($ancestor.Attributes -band [IO.FileAttributes]::ReparsePoint))
        $ancestor = $ancestor.Parent
    }
    $pending = [Collections.Generic.Stack[IO.DirectoryInfo]]::new()
    $pending.Push($root)
    $count = 0
    while ($pending.Count) {
        $directoryInfo = $pending.Pop()
        foreach ($entry in $directoryInfo.EnumerateFileSystemInfos()) {
            Assert-Measurement ((++$count) -le 8192 -and -not ($entry.Attributes -band [IO.FileAttributes]::ReparsePoint))
            if ($entry -is [IO.DirectoryInfo]) { $pending.Push($entry) }
            elseif ($entry.Name -in @('measurement.json', 'coverage.cobertura.xml')) { $entry }
        }
    }
}

function Get-CoverageCounter {
    param([System.Xml.XmlElement]$Element, [string]$Name)
    $text = $Element.GetAttribute($Name)
    Assert-Measurement ($text -cmatch '^(0|[1-9][0-9]{0,8})$')
    $number = [long]$text
    Assert-Measurement ($number -le 10000000)
    $number
}

function New-CoverageMetric {
    param([long]$Covered, [long]$Valid)
    Assert-Measurement ($Covered -ge 0 -and $Covered -le $Valid -and $Valid -le 10000000)
    @{
        covered = $Covered
        valid = $Valid
        percent = $(if ($Valid) { [math]::Round([decimal]100 * $Covered / $Valid, 2, [MidpointRounding]::AwayFromZero) } else { $null })
    }
}

function Assert-CoverageRate {
    param([System.Xml.XmlElement]$Element, [string]$Name, $Metric)
    $text = $Element.GetAttribute($Name)
    $rate = 0.0
    Assert-Measurement ($text -cmatch '^(0|1)(\.[0-9]{1,20})?$' -and
        [double]::TryParse($text, [Globalization.NumberStyles]::AllowDecimalPoint, [cultureinfo]::InvariantCulture, [ref]$rate))
    Assert-Measurement ($rate -le 1)
    if ($null -eq $Metric) { return }
    $expected = if ($Metric.valid) { $Metric.covered / [double]$Metric.valid } else { 0 }
    # Coverlet rounds Cobertura rates to four decimal places.
    Assert-Measurement ($rate -le 1 -and [math]::Abs($rate - $expected) -le 0.00011)
}

function Read-MeasurementCoverage {
    param([IO.FileInfo]$File)
    Assert-Measurement ($File.Length -gt 0 -and $File.Length -le 32MB)
    $settings = [Xml.XmlReaderSettings]::new()
    $settings.DtdProcessing = [Xml.DtdProcessing]::Prohibit
    $settings.XmlResolver = $null
    $settings.MaxCharactersInDocument = 32MB
    $settings.MaxCharactersFromEntities = 1024
    $reader = [Xml.XmlReader]::Create($File.FullName, $settings)
    try {
        $document = [Xml.XmlDocument]::new()
        $document.XmlResolver = $null
        $document.Load($reader)
    }
    finally { $reader.Dispose() }
    $root = $document.DocumentElement
    Assert-Measurement ($root.Name -ceq 'coverage')
    $packages = @($root.SelectNodes('./packages/package'))
    Assert-Measurement ($packages.Count -eq $measurementModules.Count)
    $modules = @()
    foreach ($name in $measurementModules) {
        $package = @($packages | Where-Object { $_.GetAttribute('name') -ceq $name })
        Assert-Measurement ($package.Count -eq 1)
        $linesValid = 0L; $linesCovered = 0L; $branchesValid = 0L; $branchesCovered = 0L
        foreach ($class in $package[0].SelectNodes('./classes/class')) {
            $seen = [Collections.Generic.HashSet[long]]::new()
            # Method lines repeat class lines in Cobertura: count only class-level lines.
            foreach ($line in $class.SelectNodes('./lines/line')) {
                $number = Get-CoverageCounter $line 'number'
                Assert-Measurement ($number -gt 0 -and $seen.Add($number))
                $hits = Get-CoverageCounter $line 'hits'
                $linesValid++
                if ($hits -gt 0) { $linesCovered++ }
                $branch = $line.GetAttribute('branch')
                Assert-Measurement ($branch -cin @('', 'false', 'true', 'False', 'True'))
                if ($branch -ieq 'true') {
                    $condition = $line.GetAttribute('condition-coverage')
                    Assert-Measurement ($condition -cmatch '^([0-9]{1,3}(?:\.[0-9]+)?)% \(([0-9]{1,8})/([1-9][0-9]{0,7})\)$')
                    $percent = [double]::Parse($Matches[1], [cultureinfo]::InvariantCulture)
                    $covered = [long]$Matches[2]; $valid = [long]$Matches[3]
                    Assert-Measurement ($covered -le $valid -and $valid -le 10000000 -and
                        [math]::Abs($percent - (100.0 * $covered / $valid)) -le 1.00001)
                    $branchesValid += $valid; $branchesCovered += $covered
                }
                else { Assert-Measurement (-not $line.HasAttribute('condition-coverage')) }
            }
        }
        $module = @{ name = $name; lines = (New-CoverageMetric $linesCovered $linesValid); branches = (New-CoverageMetric $branchesCovered $branchesValid) }
        Assert-CoverageRate $package[0] 'line-rate' $module.lines
        $modules += $module
    }
    $lines = New-CoverageMetric (($modules | ForEach-Object { $_.lines.covered } | Measure-Object -Sum).Sum) (($modules | ForEach-Object { $_.lines.valid } | Measure-Object -Sum).Sum)
    $mappedBranches = New-CoverageMetric (($modules | ForEach-Object { $_.branches.covered } | Measure-Object -Sum).Sum) (($modules | ForEach-Object { $_.branches.valid } | Measure-Object -Sum).Sum)
    Assert-Measurement ((Get-CoverageCounter $root 'lines-covered') -eq $lines.covered -and
        (Get-CoverageCounter $root 'lines-valid') -eq $lines.valid)
    # Coverlet's root includes branches without sequence-point lines. Those branches
    # have no per-module counters in Cobertura; never reverse-engineer counts from rates.
    $branches = New-CoverageMetric (Get-CoverageCounter $root 'branches-covered') (Get-CoverageCounter $root 'branches-valid')
    Assert-Measurement ($branches.valid -ge $mappedBranches.valid -and $branches.covered -ge $mappedBranches.covered -and
        ($branches.covered - $mappedBranches.covered) -le ($branches.valid - $mappedBranches.valid))
    foreach ($module in $modules) {
        $package = @($packages | Where-Object { $_.GetAttribute('name') -ceq $module.name })[0]
        $metric = if ($branches.valid -eq $mappedBranches.valid) { $module.branches } else { $null }
        Assert-CoverageRate $package 'branch-rate' $metric
    }
    Assert-CoverageRate $root 'line-rate' $lines
    Assert-CoverageRate $root 'branch-rate' $branches
    @{ status = 'collected'; requested = $true; lines = $lines; branches = $branches; mappedBranches = $mappedBranches; modules = $modules }
}

function Read-TestMeasurement {
    param([string]$Directory, [string]$Suite, $Report, [string]$TrxPath, [bool]$Required)
    $result = @{ status = 'not-collected'; baselineEligible = $false; coverage = @{ status = 'not-collected'; requested = $false } }
    try {
        $files = @(Get-MeasurementFiles $Directory)
        $metadata = @($files | Where-Object Name -EQ 'measurement.json')
        $coverageFiles = @($files | Where-Object Name -EQ 'coverage.cobertura.xml')
        if (-not $metadata.Count) {
            if ($Required) {
                $result.status = 'unavailable'
                $result.coverage = @{ status = $(if ($Suite -eq 'Browser') { 'not-collected' } else { 'unavailable' }); requested = ($Suite -ne 'Browser') }
            }
            Assert-Measurement ($coverageFiles.Count -eq 0)
            return $result
        }
        $result.status = 'invalid'
        Assert-Measurement ($metadata.Count -eq 1 -and $metadata[0].Length -gt 0 -and $metadata[0].Length -le 32768)
        $options = [Text.Json.JsonDocumentOptions]::new()
        $options.MaxDepth = 12
        $json = [Text.Json.JsonDocument]::Parse([IO.File]::ReadAllText($metadata[0].FullName), $options)
        try { $data = ConvertFrom-MeasurementElement $json.RootElement }
        finally { $json.Dispose() }
        Assert-Measurement ($data -is [Collections.IDictionary])
        Assert-Measurement ($data.schema -is [double] -and $data.schema -eq 1 -and
            $data.suite -is [string] -and $data.suite -ceq $Suite -and $Suite -ne 'bootstrap')
        Assert-Measurement ($data.selection -is [string] -and $data.selection.Length -gt 0 -and $data.selection.Length -le 4096)
        Assert-Measurement ($data.configuration -is [string] -and $data.targetFramework -is [string] -and
            $data.scope -is [string] -and $data.buildMode -is [string] -and
            $data.configuration -cin @('Debug', 'Release') -and $data.targetFramework -ceq 'net10.0' -and
            $data.scope -cin @('full-suite', 'filtered') -and $data.buildMode -cin @('built', 'reused'))
        if ($data.scope -ceq 'full-suite') { Assert-Measurement ($data.selection -ceq "Category=$Suite") }
        Assert-Measurement ($data.sourceRevision -is [string] -and $data.sourceRevision -cmatch '^[a-f0-9]{40}$' -and
            $data.testAssemblySha256 -is [string] -and $data.testAssemblySha256 -cmatch '^[a-f0-9]{64}$' -and $data.workingTreeDirty -is [bool])
        Assert-Measurement ($data.modules -is [array] -and $data.modules.Count -eq 2)
        $modules = @()
        foreach ($name in $measurementModules) {
            $module = @($data.modules | Where-Object { $_ -is [Collections.IDictionary] -and $_.name -ceq $name })
            Assert-Measurement ($module.Count -eq 1 -and $module[0].sha256 -is [string] -and $module[0].sha256 -cmatch '^[a-f0-9]{64}$')
            $modules += @{ name = $name; sha256 = $module[0].sha256 }
        }
        Assert-Measurement ($data.runtime -is [Collections.IDictionary] -and $data.coverage -is [Collections.IDictionary])
        Assert-Measurement ($data.runtime.sdk -is [string] -and $data.runtime.sdk -cmatch '^[0-9]{1,2}\.[0-9]{1,2}\.[0-9]{1,4}$' -and
            $data.runtime.os -cin @('Windows', 'Linux', 'macOS') -and $data.runtime.architecture -cin @('X64', 'X86', 'Arm64', 'Arm'))
        Assert-Measurement ($data.coverage.requested -is [bool])
        if ($data.coverage.requested) {
            Assert-Measurement ($Suite -cin @('Unit', 'HttpIntegration') -and $data.runtime.collectorVersion -is [string] -and
                $data.runtime.collectorVersion -cmatch '^[0-9]{1,3}\.[0-9]{1,3}\.[0-9]{1,4}(?:-[A-Za-z0-9.-]{1,64})?$' -and
                $data.coverage.profileSha256 -is [string] -and $data.coverage.profileSha256 -cmatch '^[a-f0-9]{64}$')
        }
        else { Assert-Measurement ($null -eq $data.runtime.collectorVersion -and $null -eq $data.coverage.profileSha256 -and $coverageFiles.Count -eq 0) }
        foreach ($phase in @('discovery', 'execution')) {
            $value = $data[$phase]
            Assert-Measurement ($value -is [Collections.IDictionary] -and $value.status -cin @('completed', 'failed', 'not-run'))
            Assert-Measurement ($null -eq $value.durationSeconds -or (Test-MeasurementNumber $value.durationSeconds))
            if ($value.status -ceq 'completed') { Assert-Measurement (Test-MeasurementNumber $value.durationSeconds) }
            if ($phase -eq 'discovery') {
                Assert-Measurement ($null -eq $value.count -or (Test-MeasurementNumber $value.count 1000000 -Integer))
                if ($value.status -ceq 'completed') { Assert-Measurement (Test-MeasurementNumber $value.count 1000000 -Integer) }
            }
            else {
                Assert-Measurement ($null -eq $value.exitCode -or ($value.exitCode -is [double] -and
                    (Test-MeasurementNumber ([math]::Abs($value.exitCode)) 2147483648 -Integer)))
                if ($value.status -ceq 'completed') { Assert-Measurement ($null -ne $value.exitCode -and $value.exitCode -eq 0) }
                if ($value.status -ceq 'failed') { Assert-Measurement ($null -eq $value.exitCode -or $value.exitCode -ne 0) }
            }
        }
        Assert-Measurement (-not ($data.discovery.count -eq 0 -and $Report.executed -gt 0))
        $start = [DateTimeOffset]::MinValue; $finish = [DateTimeOffset]::MinValue
        Assert-Measurement ($data.startedAtUtc -is [string] -and $data.startedAtUtc -cmatch '^\d{4}-\d\d-\d\dT[\d:.]+(?:Z|\+00:00)$' -and
            [DateTimeOffset]::TryParse($data.startedAtUtc, [cultureinfo]::InvariantCulture, [Globalization.DateTimeStyles]::None, [ref]$start))
        if ($null -ne $data.completedAtUtc) {
            Assert-Measurement ($data.completedAtUtc -is [string] -and $data.completedAtUtc -cmatch '^\d{4}-\d\d-\d\dT[\d:.]+(?:Z|\+00:00)$' -and
                [DateTimeOffset]::TryParse($data.completedAtUtc, [cultureinfo]::InvariantCulture, [Globalization.DateTimeStyles]::None, [ref]$finish) -and
                $finish -ge $start -and ($finish - $start).TotalDays -le 366)
        }
        if ($TrxPath) { Assert-Measurement ([IO.Path]::GetDirectoryName($TrxPath) -eq $metadata[0].DirectoryName) }
        if ($data.discovery.status -ceq 'completed') {
            $discovery = [IO.FileInfo]::new((Join-Path $metadata[0].DirectoryName 'discovery.txt'))
            Assert-Measurement ($discovery.Exists -and $discovery.Length -le 32MB)
        }
        $result = @{
            status = $(if ($data.discovery.status -ceq 'completed' -and $data.execution.status -ceq 'completed' -and $null -ne $data.completedAtUtc) { 'collected' } else { 'unavailable' })
            baselineEligible = $false
            scope = $data.scope
            buildMode = $data.buildMode
            configuration = $data.configuration
            targetFramework = $data.targetFramework
            sourceRevision = $data.sourceRevision
            workingTreeDirty = $data.workingTreeDirty
            testAssemblySha256 = $data.testAssemblySha256
            selectionSha256 = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($data.selection))).ToLowerInvariant()
            modules = $modules
            runtime = @{ sdk = $data.runtime.sdk; os = $data.runtime.os; architecture = $data.runtime.architecture; collectorVersion = $data.runtime.collectorVersion }
            profileSha256 = $data.coverage.profileSha256
            discovery = @{ status = $data.discovery.status; count = $data.discovery.count; durationSeconds = $data.discovery.durationSeconds }
            execution = @{ status = $data.execution.status; exitCode = $data.execution.exitCode; durationSeconds = $data.execution.durationSeconds }
            startedAtUtc = $start.ToUniversalTime().ToString('o')
            completedAtUtc = $(if ($null -ne $data.completedAtUtc) { $finish.ToUniversalTime().ToString('o') } else { $null })
            coverage = @{ status = 'not-collected'; requested = $data.coverage.requested }
        }
        if ($data.coverage.requested) {
            $result.coverage.status = 'unavailable'
            if ($coverageFiles.Count) {
                $result.coverage.status = 'invalid'
                Assert-Measurement ($coverageFiles.Count -le 8)
                $hashes = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
                foreach ($coverageFile in $coverageFiles) {
                    Assert-Measurement ($coverageFile.Length -gt 0 -and $coverageFile.Length -le 32MB)
                    $relative = [IO.Path]::GetRelativePath($metadata[0].DirectoryName, $coverageFile.FullName)
                    Assert-Measurement (-not $relative.StartsWith('..') -and -not [IO.Path]::IsPathRooted($relative))
                    $stream = $coverageFile.OpenRead()
                    try { [void]$hashes.Add([Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($stream))) }
                    finally { $stream.Dispose() }
                    Assert-Measurement ($hashes.Count -eq 1)
                }
                # VSTest mirrors collector attachments in the TRX directory. Parse a
                # byte-identical report once, never merge different coverage payloads.
                $result.coverage = Read-MeasurementCoverage $coverageFiles[0]
            }
        }
        $result.baselineEligible = $result.status -eq 'collected' -and $Report.status -eq 'passed' -and
            $data.scope -eq 'full-suite' -and $data.buildMode -eq 'built' -and -not $data.workingTreeDirty -and
            ($Suite -eq 'Browser' -or $result.coverage.status -eq 'collected')
        return $result
    }
    catch [IO.InvalidDataException], [IO.IOException], [UnauthorizedAccessException], [Security.SecurityException],
        [Xml.XmlException], [Text.Json.JsonException], [System.Management.Automation.PropertyNotFoundException] {
        if ($result.coverage.status -ne 'invalid') { $result.status = 'invalid' }
        $result.baselineEligible = $false
        return $result
    }
}

function Format-MeasurementMetric {
    param($Metric)
    if (-not $Metric.valid) { return 'N/A (0/0)' }
    "$($Metric.percent.ToString('F2', [cultureinfo]::InvariantCulture))% ($($Metric.covered)/$($Metric.valid))"
}

function Add-MeasurementSummary {
    param([Text.StringBuilder]$Summary, $Measurement, [string]$Suite)
    [void]$Summary.AppendLine()
    [void]$Summary.AppendLine("### Measurements: $($Measurement.status)")
    if ($Measurement.ContainsKey('scope')) {
        $label = if ($Measurement.baselineEligible) { 'Full-suite baseline candidate' } else { 'Not a clean comparable full-suite baseline' }
        [void]$Summary.AppendLine("$label; $($Measurement.scope), $($Measurement.buildMode), dirty=$($Measurement.workingTreeDirty.ToString().ToLowerInvariant()).")
        $discoveryTime = if ($null -eq $Measurement.discovery.durationSeconds) { 'N/A' } else { "$($Measurement.discovery.durationSeconds.ToString('F3', [cultureinfo]::InvariantCulture)) s" }
        $executionTime = if ($null -eq $Measurement.execution.durationSeconds) { 'N/A' } else { "$($Measurement.execution.durationSeconds.ToString('F3', [cultureinfo]::InvariantCulture)) s" }
        $count = if ($null -eq $Measurement.discovery.count) { 'N/A' } else { $Measurement.discovery.count }
        [void]$Summary.AppendLine("Discovery: $($Measurement.discovery.status), $count cases, $discoveryTime. Test process: $($Measurement.execution.status), $executionTime.")
        [void]$Summary.AppendLine("Provenance: $($Measurement.configuration) / $($Measurement.targetFramework); SDK $($Measurement.runtime.sdk); $($Measurement.runtime.os) $($Measurement.runtime.architecture); revision ``$($Measurement.sourceRevision)``.")
        [void]$Summary.AppendLine("Workflow revision verification: $($Measurement.revisionStatus) (unverified means recorded revision only; no workflow identity supplied).")
        [void]$Summary.AppendLine("Test assembly SHA-256: ``$($Measurement.testAssemblySha256)``; selection SHA-256: ``$($Measurement.selectionSha256)``.")
        foreach ($module in $Measurement.modules) { [void]$Summary.AppendLine("$($module.name) pre-instrumentation SHA-256: ``$($module.sha256)``.") }
        if ($Measurement.profileSha256) { [void]$Summary.AppendLine("Coverlet $($Measurement.runtime.collectorVersion); profile SHA-256: ``$($Measurement.profileSha256)``.") }
    }
    [void]$Summary.AppendLine()
    if ($Suite -eq 'Browser') { [void]$Summary.AppendLine('Browser coverage: not collected (deliberately not instrumented).') }
    elseif ($Measurement.coverage.status -ne 'collected') { [void]$Summary.AppendLine(".NET coverage: $($Measurement.coverage.status); no percentage available.") }
    else {
        [void]$Summary.AppendLine('| Suite | Lines (covered/valid) | Collector branches (covered/valid) |')
        [void]$Summary.AppendLine('| --- | ---: | ---: |')
        [void]$Summary.AppendLine("| $Suite | $(Format-MeasurementMetric $Measurement.coverage.lines) | $(Format-MeasurementMetric $Measurement.coverage.branches) |")
        [void]$Summary.AppendLine()
        [void]$Summary.AppendLine('<details><summary>Module coverage detail</summary>')
        [void]$Summary.AppendLine()
        [void]$Summary.AppendLine('| .NET module | Lines (covered/valid) | Source-mapped branches (covered/valid) |')
        [void]$Summary.AppendLine('| --- | ---: | ---: |')
        foreach ($module in $Measurement.coverage.modules) {
            [void]$Summary.AppendLine("| $($module.name) | $(Format-MeasurementMetric $module.lines) | $(Format-MeasurementMetric $module.branches) |")
        }
        [void]$Summary.AppendLine("| $Suite source-mapped total | $(Format-MeasurementMetric $Measurement.coverage.lines) | $(Format-MeasurementMetric $Measurement.coverage.mappedBranches) |")
        [void]$Summary.AppendLine()
        [void]$Summary.AppendLine("$Suite collector all-branch total: $(Format-MeasurementMetric $Measurement.coverage.branches). Includes branches without source-line details; module branch counts above cover source-mapped conditions only.")
        [void]$Summary.AppendLine()
        [void]$Summary.AppendLine('</details>')
    }
    [void]$Summary.AppendLine()
    [void]$Summary.AppendLine('Discovery cases and executed TRX results are separate counts; dynamic theories may expand. TRX window is not summed test wall-clock time. Discovery/test-process timings exclude build/setup.')
    [void]$Summary.AppendLine('.NET IL coverage only (compiled Razor IL may be included): excludes GeneratedCodeAttribute, ExcludeFromCodeCoverageAttribute and **/obj/**; includes auto-properties and async code, excludes test assemblies. No JavaScript, browser-behavior or Razor-rendering coverage claim. Unit and HTTP coverage overlap and are never merged or averaged.')
    [void]$Summary.AppendLine('Single-run measurements provide no historical comparison or flake rate. No coverage percentage gate.')
}
