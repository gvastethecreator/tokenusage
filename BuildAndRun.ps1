<#
.SYNOPSIS
Builds and optionally runs a WinUI 3 / .NET project.

.DESCRIPTION
One command to build and run:  .\BuildAndRun.ps1 MyApp.csproj

- Checks Developer Mode is enabled (required for packaged WinUI apps)
- Auto-detects platform (x64/ARM64), defaults to Debug, auto-restores
- Packages TokenUsage through scripts/package.ps1; other projects use MSBuild or dotnet build
- After successful build, finds the output folder and runs with winapp run
- Pass -SkipRun to build without launching

.EXAMPLE
.\BuildAndRun.ps1 MyApp.csproj                    # Build + run
.\BuildAndRun.ps1 MyApp.csproj -SkipRun           # Build only
.\BuildAndRun.ps1 MyApp.csproj /p:Configuration=Release  # Override config
#>

param(
    [Parameter(Position = 0)]
    [string]$Project,
    [switch]$SkipRun,
    [switch]$Detach,
    [Parameter(ValueFromRemainingArguments)]
    [string[]]$ExtraArgs
)

$ErrorActionPreference = 'Stop'

# Accept --detach (CLI style) as an alias for -Detach (PS style)
if ($ExtraArgs -contains '--detach') {
    $Detach = $true
    $ExtraArgs = $ExtraArgs | Where-Object { $_ -ne '--detach' }
}

# Extra args are MSBuild-style flags like /p:Platform=x64
$extraArgs = $ExtraArgs

# -- 0. Check Developer Mode --
$devMode = $false
try {
    $regPath = "HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\AppModelUnlock"
    if (Test-Path $regPath) {
        $val = Get-ItemProperty $regPath -Name AllowDevelopmentWithoutDevLicense -ErrorAction SilentlyContinue
        if ($val.AllowDevelopmentWithoutDevLicense -eq 1) { $devMode = $true }
    }
} catch {}

if (-not $devMode) {
    Write-Host "ERROR: Developer Mode is not enabled." -ForegroundColor Red
    Write-Host "WinUI 3 packaged apps require Developer Mode to deploy and run." -ForegroundColor Red
    Write-Host "Enable it: Settings > System > For developers > Developer Mode" -ForegroundColor Yellow
    exit 1
}

# -- 1. Find the .csproj if not specified --
if (-not $Project) {
    $csprojFiles = Get-ChildItem -Path . -Filter "*.csproj" -Depth 0
    if ($csprojFiles.Count -eq 1) {
        $Project = $csprojFiles[0].Name
    } elseif ($csprojFiles.Count -gt 1) {
        Write-Error "Multiple .csproj files found. Specify which one: .\BuildAndRun.ps1 <name>.csproj"
        exit 1
    } else {
        Write-Error "No .csproj file found in current directory."
        exit 1
    }
}

# Keep the app's existing command line while the packaging script owns its layout.
$resolvedProject = (Resolve-Path $Project).Path
$packageScript = Join-Path $PSScriptRoot 'scripts\package.ps1'
$appProject = Join-Path $PSScriptRoot 'src\TokenUsage.App\TokenUsage.App.csproj'
$useTokenUsagePackage = [string]::Equals($resolvedProject, $appProject, [StringComparison]::OrdinalIgnoreCase)
if ($useTokenUsagePackage -and -not (Test-Path -LiteralPath $packageScript -PathType Leaf)) {
    throw "Missing packaging script: $packageScript"
}

# -- 2. Auto-detect platform --
$detectedPlatform = if ($env:PROCESSOR_ARCHITECTURE -eq "ARM64") { "ARM64" } else { "x64" }
$detectedConfig = "Debug"

$hasPlatform = $extraArgs | Where-Object { $_ -match "^[/|-]p:Platform=" }
$hasConfig = $extraArgs | Where-Object { $_ -match "^[/|-]p:Configuration=" }
$hasRestore = $extraArgs | Where-Object { $_ -match "^[/|-]restore$|^[/|-]t:restore$|^--restore$" }

# Extract actual values if overridden
if ($hasPlatform -and $hasPlatform -match "Platform=(\w+)") { $detectedPlatform = $Matches[1] }
if ($hasConfig -and $hasConfig -match "Configuration=(\w+)") { $detectedConfig = $Matches[1] }

$autoArgs = @()
if (-not $hasPlatform) { $autoArgs += "/p:Platform=$detectedPlatform" }
if (-not $hasConfig)   { $autoArgs += "/p:Configuration=$detectedConfig" }
if (-not $hasRestore)  { $autoArgs += "/restore" }

# -- 3. Find build tool --
$vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
$msbuild = $null

if (-not $useTokenUsagePackage -and (Test-Path $vswhere)) {
    $vsPath = & $vswhere -latest -products * -requires Microsoft.Component.MSBuild -property installationPath 2>$null
    if ($vsPath) {
        $candidate = Join-Path $vsPath "MSBuild\Current\Bin\MSBuild.exe"
        if (Test-Path $candidate) { $msbuild = $candidate }
    }
}

# -- 4. Build --
$defaultArgs = @("/nologo")
$hasVerbosity = $extraArgs | Where-Object { $_ -match "^[/|-]v(erbosity)?:" }
if (-not $hasVerbosity) { $defaultArgs += "/v:m" }

Write-Host ""
if ($useTokenUsagePackage) {
    $packageParameters = @{
        Platform = $detectedPlatform
        Configuration = $detectedConfig
    }
    foreach ($argument in $extraArgs) {
        if ($argument -match '^[-/]p:([^=]+)=(.*)$') {
            $propertyName = $Matches[1]
            $propertyValue = $Matches[2]
            switch ($propertyName) {
                'Platform' { $packageParameters.Platform = $propertyValue }
                'Configuration' { $packageParameters.Configuration = $propertyValue }
                'EnableUiTestFixtures' {
                    $enabled = $false
                    if (-not [bool]::TryParse($propertyValue, [ref] $enabled)) {
                        throw 'EnableUiTestFixtures must be true or false.'
                    }
                    $packageParameters.EnableUiTestFixtures = $enabled
                }
                'PackageCertificateKeyFile' {
                    $packageParameters.PackageCertificateKeyFile = (Resolve-Path -LiteralPath $propertyValue).Path
                }
                'PackageCertificatePassword' { $packageParameters.PackageCertificatePassword = $propertyValue }
                default { throw "Unsupported TokenUsage packaging property: $propertyName. Use scripts/package.ps1 for packaging options." }
            }
        }
        elseif ($argument -notmatch '^[-/](restore|t:restore|nologo|v(erbosity)?:.+)$|^--restore$') {
            throw 'Unsupported TokenUsage packaging argument. Use scripts/package.ps1 for packaging options.'
        }
    }

    Write-Host "--> Packaging TokenUsage (Platform: $detectedPlatform, Config: $detectedConfig)" -ForegroundColor Cyan
    & $packageScript @packageParameters
    $buildExit = 0
} elseif ($msbuild) {
    Write-Host "--> Building with MSBuild (Platform: $detectedPlatform, Config: $detectedConfig)" -ForegroundColor Cyan
    Write-Host "--> MSBuild: $msbuild" -ForegroundColor DarkGray
    $allArgs = $defaultArgs + $autoArgs + @($Project) + $extraArgs
    & $msbuild $allArgs
    $buildExit = $LASTEXITCODE
} else {
    Write-Host "--> Building with dotnet build (Platform: $detectedPlatform, Config: $detectedConfig)" -ForegroundColor Yellow
    $dotnetArgs = @($Project)
    foreach ($a in ($autoArgs + $extraArgs)) {
        if ($a -match "^[/|-]restore$|^[/|-]t:restore$") {
            # dotnet build restores by default
        } elseif ($a -match "^[/|-]p:(.+)$") {
            $dotnetArgs += "-p:$($Matches[1])"
        } elseif ($a -notmatch "\.(csproj|sln)$") {
            $dotnetArgs += $a
        }
    }
    & dotnet build @dotnetArgs
    $buildExit = $LASTEXITCODE
}

if ($buildExit -ne 0) {
    Write-Host ""
    Write-Host "BUILD FAILED (exit code $buildExit)" -ForegroundColor Red
    exit $buildExit
}

Write-Host ""
Write-Host "BUILD SUCCEEDED" -ForegroundColor Green

# -- 5. Run with winapp --
if ($SkipRun) {
    Write-Host "--> Skipping run (-SkipRun)" -ForegroundColor DarkGray
    exit 0
}

# Find the build output directory
$rid = $detectedPlatform.ToLower()
$projectDir = Split-Path (Resolve-Path $Project) -Parent
if (-not $projectDir) { $projectDir = "." }
if ($useTokenUsagePackage) { $projectDir = Join-Path $PSScriptRoot 'src\TokenUsage.Package' }

# The packaging script emits a ready-to-register layout directly under
# bin\<Platform>\<Configuration>. SDK-style app projects retain their TFM path.
$binDir = Join-Path $projectDir "bin\$detectedPlatform\$detectedConfig"
if (-not (Test-Path $binDir)) {
    Write-Host "WARNING: Build output not found at $binDir -- skipping run" -ForegroundColor Yellow
    exit 0
}

if ($useTokenUsagePackage) {
    $outputDir = $binDir
} else {
    $tfmDirs = Get-ChildItem $binDir -Directory | Where-Object { $_.Name -match "^net\d" }
    if (-not $tfmDirs) {
        Write-Host "WARNING: No TFM folder found in $binDir -- skipping run" -ForegroundColor Yellow
        exit 0
    }

    $tfmDir = $tfmDirs | Sort-Object Name -Descending | Select-Object -First 1
    $outputDir = Join-Path $tfmDir.FullName "win-$rid"
    if (-not (Test-Path $outputDir)) {
        $outputDir = $tfmDir.FullName
    }
}

# Check winapp is available
$winapp = Get-Command winapp -ErrorAction SilentlyContinue
if (-not $winapp) {
    Write-Host "WARNING: winapp CLI not found in PATH -- skipping run" -ForegroundColor Yellow
    Write-Host "Build output at: $outputDir"
    exit 0
}

Write-Host ""
if ($Detach) {
    Write-Host "--> Launching app in background..." -ForegroundColor Cyan
    & winapp run $outputDir --detach --json
} else {
    Write-Host "--> Launching app: winapp run $outputDir --debug-output" -ForegroundColor Cyan
    Write-Host "    The script will stay running while the app is open." -ForegroundColor DarkGray
    Write-Host "    Debug output and exceptions will appear below." -ForegroundColor DarkGray
    Write-Host ""
    & winapp run $outputDir --debug-output
}
