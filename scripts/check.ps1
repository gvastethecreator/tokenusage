#Requires -Version 5.1

param(
    [Parameter()]
    [ValidateSet('x64', 'ARM64')]
    [string] $Platform = 'x64',

    [Parameter()]
    [ValidateSet('Debug', 'Release')]
    [string] $Configuration = 'Debug',

    [Parameter()]
    [string] $PackageCertificateKeyFile,

    [Parameter()]
    [string] $PackageCertificatePassword
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$packageScript = Join-Path $PSScriptRoot 'package.ps1'
$architectureTests = Join-Path $repoRoot 'tests\TokenUsage.Architecture.Tests\TokenUsage.Architecture.Tests.csproj'
$coreTests = Join-Path $repoRoot 'tests\TokenUsage.Core.Tests\TokenUsage.Core.Tests.csproj'
$cliTests = Join-Path $repoRoot 'tests\TokenUsage.Cli.Tests\TokenUsage.Cli.Tests.csproj'
$providerTests = Join-Path $repoRoot 'tests\TokenUsage.Providers.Tests\TokenUsage.Providers.Tests.csproj'
$platformWindowsTests = Join-Path $repoRoot 'tests\TokenUsage.Platform.Windows.Tests\TokenUsage.Platform.Windows.Tests.csproj'

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

if (-not (Test-Path -LiteralPath $packageScript)) {
    throw "Missing packaging script: $packageScript"
}

if ($PackageCertificatePassword -and -not $PackageCertificateKeyFile) {
    throw 'PackageCertificateKeyFile is required when PackageCertificatePassword is set.'
}

$packageParameters = @{
    Configuration = $Configuration
    Platform = $Platform
}

if ($PackageCertificateKeyFile) {
    $resolvedCertificate = (Resolve-Path -LiteralPath $PackageCertificateKeyFile).Path
    $packageParameters.PackageCertificateKeyFile = $resolvedCertificate
    if ($PackageCertificatePassword) {
        $packageParameters.PackageCertificatePassword = $PackageCertificatePassword
    }
}

foreach ($testProject in @($architectureTests, $coreTests, $cliTests, $providerTests, $platformWindowsTests)) {
    if (-not (Test-Path -LiteralPath $testProject)) {
        throw "Missing test project: $testProject"
    }
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

    Invoke-DotNetStep 'Core tests' @(
        'test',
        $coreTests,
        '--configuration', $Configuration,
        '-p:Platform=x64',
        '--verbosity', 'minimal'
    )

    Invoke-DotNetStep 'CLI tests' @(
        'test',
        $cliTests,
        '--configuration', $Configuration,
        '-p:Platform=x64',
        '--verbosity', 'minimal'
    )

    Invoke-DotNetStep 'Provider tests' @(
        'test',
        $providerTests,
        '--configuration', $Configuration,
        '-p:Platform=x64',
        '--verbosity', 'minimal'
    )

    Invoke-DotNetStep 'Platform Windows tests' @(
        'test',
        $platformWindowsTests,
        '--filter', 'Category!=OptIn',
        '--configuration', $Configuration,
        '-p:Platform=x64',
        '--verbosity', 'minimal'
    )

    Write-Host '==> Package and production project build' -ForegroundColor Cyan
    & $packageScript @packageParameters
}
finally {
    Pop-Location
}

Write-Host "check.ps1 OK (Platform=$Platform, Configuration=$Configuration)" -ForegroundColor Green
