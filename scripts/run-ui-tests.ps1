#requires -Version 7.2

[CmdletBinding()]
param(
    [switch]$SkipBuild,

    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',

    [string]$Filter
)

$ErrorActionPreference = 'Stop'
& (Join-Path $PSScriptRoot 'run-tests.ps1') -Suite Integration -Configuration $Configuration -Filter $Filter -NoBuild:$SkipBuild
