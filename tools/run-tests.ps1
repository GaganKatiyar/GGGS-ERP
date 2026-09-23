<#
.SYNOPSIS
    Builds and runs every test project in the repository.

.DESCRIPTION
    Test projects are executed directly rather than through `dotnet test`.

    xunit v3 runs on Microsoft.Testing.Platform, and on this SDK (10.0.401 with MTP 2.4.0)
    `dotnet test` completes with "Zero tests ran" while the very same assembly runs its full suite
    when invoked directly. Running the test executables avoids that gap and gives identical
    results locally and in CI. Revisit once the SDK and xunit agree again.

.PARAMETER Filter
    Optional substring matched against project names, e.g. -Filter Isolation.

.PARAMETER SkipIntegration
    Skips suites that need SQL Server (the tenant isolation suite).
#>
[CmdletBinding()]
param(
    [string] $Filter,
    [switch] $SkipIntegration
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$env:TESTINGPLATFORM_TELEMETRY_OPTOUT = '1'

$projects = Get-ChildItem -Path (Join-Path $repositoryRoot 'tests') -Recurse -Filter '*.csproj' |
    Where-Object { -not $Filter -or $_.BaseName -like "*$Filter*" } |
    Where-Object { -not ($SkipIntegration -and $_.BaseName -match 'Isolation|IntegrationTests') }

if (-not $projects) {
    Write-Warning 'No test projects matched.'
    exit 0
}

Write-Host "Building $($projects.Count) test project(s)..." -ForegroundColor Cyan
foreach ($project in $projects) {
    dotnet build $project.FullName --nologo -v quiet
    if ($LASTEXITCODE -ne 0) { throw "Build failed: $($project.BaseName)" }
}

$failed = @()
foreach ($project in $projects) {
    Write-Host "`n=== $($project.BaseName) ===" -ForegroundColor Cyan
    dotnet run --project $project.FullName --no-build
    if ($LASTEXITCODE -ne 0) { $failed += $project.BaseName }
}

if ($failed.Count -gt 0) {
    Write-Host "`nFailed: $($failed -join ', ')" -ForegroundColor Red
    exit 1
}

Write-Host "`nAll test projects passed." -ForegroundColor Green
