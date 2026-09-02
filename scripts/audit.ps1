# Audits every active project for known NuGet vulnerabilities.
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$solution = Join-Path $root 'TokenUsage.slnx'
$vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"

if (-not (Test-Path -LiteralPath $solution -PathType Leaf)) {
    throw "Missing solution: $solution"
}

if (-not (Test-Path -LiteralPath $vswhere -PathType Leaf)) {
    throw 'Visual Studio MSBuild discovery tool is missing.'
}

$visualStudio = & $vswhere -latest -products * -requires Microsoft.Component.MSBuild -property installationPath
$env:WapProjPath = Join-Path $visualStudio 'MSBuild\Microsoft\DesktopBridge'
if (-not (Test-Path -LiteralPath (Join-Path $env:WapProjPath 'Microsoft.DesktopBridge.props') -PathType Leaf)) {
    throw 'Visual Studio Desktop Bridge build tools are missing.'
}

dotnet list $solution package --vulnerable --include-transitive
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

Write-Host 'NuGet vulnerability audit completed for the full solution.' -ForegroundColor Green
