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
$solution = Join-Path $repoRoot 'TokenUsage.slnx'
$packageProject = Join-Path $repoRoot 'src\TokenUsage.Package\TokenUsage.Package.wapproj'
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

function Resolve-MSBuild {
    $vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
    if (-not (Test-Path -LiteralPath $vswhere)) {
        throw 'Visual Studio MSBuild discovery tool is missing.'
    }

    $visualStudio = & $vswhere -latest -products * -requires Microsoft.Component.MSBuild -property installationPath
    $candidate = Join-Path $visualStudio 'MSBuild\Current\Bin\MSBuild.exe'
    if (-not (Test-Path -LiteralPath $candidate)) {
        throw 'Visual Studio MSBuild is required to build the packaging project.'
    }

    return $candidate
}

function Invoke-MSBuildStep {
    param(
        [Parameter(Mandatory)]
        [string] $Name,

        [Parameter(Mandatory)]
        [string[]] $Arguments
    )

    Write-Host "==> $Name" -ForegroundColor Cyan
    & $script:MSBuild @Arguments

    if ($LASTEXITCODE -ne 0) {
        throw "$Name failed with exit code $LASTEXITCODE."
    }
}

if (-not (Test-Path -LiteralPath $solution)) {
    throw "Missing solution: $solution"
}

if (-not (Test-Path -LiteralPath $packageProject)) {
    throw "Missing packaging project: $packageProject"
}

$script:MSBuild = Resolve-MSBuild

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
        '--configuration', $Configuration,
        '-p:Platform=x64',
        '--verbosity', 'minimal'
    )

    Invoke-MSBuildStep 'Solution and package build' @(
        $solution,
        '/restore',
        "/p:Configuration=$Configuration",
        "/p:Platform=$Platform",
        '/p:GenerateAppxPackageOnBuild=true',
        '/verbosity:minimal',
        '/nologo'
    )
}
finally {
    Pop-Location
}

Write-Host "check.ps1 OK (Platform=$Platform, Configuration=$Configuration)" -ForegroundColor Green
