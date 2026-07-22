#Requires -Version 5.1

param(
    [Parameter()]
    [ValidateSet('x64', 'ARM64')]
    [string] $Platform = 'x64',

    [Parameter()]
    [ValidateSet('Debug', 'Release')]
    [string] $Configuration = 'Debug'
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$solution = Join-Path $repoRoot 'WOpenUsage.slnx'
$architectureTests = Join-Path $repoRoot 'tests\WOpenUsage.Architecture.Tests\WOpenUsage.Architecture.Tests.csproj'

function Invoke-DotNetStep {
    param(
        [Parameter(Mandatory)]
        [string] $Name,

        [Parameter(Mandatory)]
        [string[]] $Arguments
    )

    Write-Host "==> $Name" -ForegroundColor Cyan
    & dotnet @Arguments

    if ($LASTEXITCODE -ne 0) {
        throw "$Name failed with exit code $LASTEXITCODE."
    }
}

if (-not (Test-Path -LiteralPath $solution)) {
    throw "Missing solution: $solution"
}

if (-not (Test-Path -LiteralPath $architectureTests)) {
    throw "Missing architecture tests: $architectureTests"
}

Push-Location $repoRoot
try {
    Invoke-DotNetStep 'Architecture tests' @(
        'test',
        $architectureTests,
        '--configuration', $Configuration,
        '-p:Platform=x64',
        '--verbosity', 'minimal'
    )

    Invoke-DotNetStep 'Restore' @(
        'restore',
        $solution,
        "-p:Platform=$Platform"
    )

    Invoke-DotNetStep 'Solution build' @(
        'build',
        $solution,
        '--configuration', $Configuration,
        "-p:Platform=$Platform",
        '--no-restore',
        '--verbosity', 'minimal'
    )
}
finally {
    Pop-Location
}

Write-Host "check.ps1 OK (Platform=$Platform, Configuration=$Configuration)" -ForegroundColor Green
