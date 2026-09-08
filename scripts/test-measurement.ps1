#requires -Version 7.2

function Get-TestDiscoveryCount {
    param([string[]]$Lines)

    $header = 'The following Tests are available:'
    $headers = @($Lines | Where-Object { $_.Trim() -ceq $header })
    if ($headers.Count -ne 1) {
        throw [IO.InvalidDataException]::new('VSTest discovery output did not contain exactly one expected English test-list header. Inspect discovery.txt.')
    }

    $afterHeader = $false
    $count = 0
    foreach ($line in $Lines) {
        if ($line.Trim() -ceq $header) {
            $afterHeader = $true
        }
        elseif ($afterHeader -and $line -cmatch '^    \S') {
            $count++
        }
    }
    $count
}

function New-TestMeasurement {
    param(
        [string]$RepositoryRoot,
        [string]$Project,
        [string]$AssemblyPath,
        [string]$Suite,
        [string]$Selection,
        [string]$Configuration,
        [string]$TargetFramework,
        [bool]$Filtered,
        [bool]$BuildReused,
        [bool]$CollectCoverage
    )

    $profile = Join-Path $RepositoryRoot 'scripts\coverage.runsettings'
    [xml]$settings = Get-Content -LiteralPath $profile -Raw
    $includes = $settings.RunSettings.DataCollectionRunSettings.DataCollectors.DataCollector.Configuration.Include.Split(',')
    $directory = Split-Path -Parent $AssemblyPath
    $modules = @(
        foreach ($include in $includes) {
            if ($include -cnotmatch '^\[([A-Za-z0-9.]+)\]\*$') {
                throw "The coverage profile contains an unsupported module filter: $include"
            }
            $name = $Matches[1]
            [ordered]@{
                name = $name
                sha256 = (Get-FileHash -LiteralPath (Join-Path $directory "$name.dll") -Algorithm SHA256).Hash.ToLowerInvariant()
            }
        }
    )
    $source = Invoke-TestCommand 'git' @('-C', $RepositoryRoot, 'rev-parse', 'HEAD') 'Could not identify the source revision for measurements'
    $changes = @(Invoke-TestCommand 'git' @('-C', $RepositoryRoot, 'status', '--porcelain', '--untracked-files=normal') 'Could not identify working-tree changes')
    $sdk = Invoke-TestCommand 'dotnet' @('--version') 'Could not identify the measurement SDK'
    $collectorVersion = $null
    if ($CollectCoverage) {
        [xml]$projectXml = Get-Content -LiteralPath $Project -Raw
        $collectors = @($projectXml.SelectNodes("/Project/ItemGroup/PackageReference[@Include='coverlet.collector']"))
        if ($collectors.Count -ne 1) {
            throw "Coverage was requested but $Project does not declare exactly one coverlet.collector version."
        }
        $collectorVersion = $collectors[0].GetAttribute('Version')
    }

    [ordered]@{
        schema = 1
        suite = $Suite
        selection = $Selection
        configuration = $Configuration
        targetFramework = $TargetFramework
        scope = $(if ($Filtered) { 'filtered' } else { 'full-suite' })
        buildMode = $(if ($BuildReused) { 'reused' } else { 'built' })
        sourceRevision = $source
        workingTreeDirty = $changes.Count -gt 0
        testAssemblySha256 = (Get-FileHash -LiteralPath $AssemblyPath -Algorithm SHA256).Hash.ToLowerInvariant()
        modules = $modules
        runtime = [ordered]@{
            sdk = $sdk
            os = $(if ($IsWindows) { 'Windows' } elseif ($IsMacOS) { 'macOS' } else { 'Linux' })
            architecture = [System.Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture.ToString()
            collectorVersion = $collectorVersion
        }
        coverage = [ordered]@{
            requested = $CollectCoverage
            profileSha256 = $(if ($CollectCoverage) { (Get-FileHash -LiteralPath $profile -Algorithm SHA256).Hash.ToLowerInvariant() } else { $null })
        }
        discovery = [ordered]@{ status = 'not-run'; count = $null; durationSeconds = $null }
        execution = [ordered]@{ status = 'not-run'; exitCode = $null; durationSeconds = $null }
        startedAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
        completedAtUtc = $null
    }
}

function Write-TestMeasurement {
    param([string]$Directory, [System.Collections.IDictionary]$Measurement)

    [IO.Directory]::CreateDirectory($Directory) | Out-Null
    [IO.File]::WriteAllText((Join-Path $Directory 'measurement.json'), ($Measurement | ConvertTo-Json -Depth 8), [Text.UTF8Encoding]::new($false))
}
