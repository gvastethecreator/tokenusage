# Lists direct package updates for every active project in the solution.
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

$json = dotnet list $solution package --outdated --format json | Out-String
if ($LASTEXITCODE -ne 0) {
    throw "NuGet dependency check failed with exit code $LASTEXITCODE."
}
if ([string]::IsNullOrWhiteSpace($json)) {
    throw 'NuGet dependency check returned no result.'
}

$result = $json | ConvertFrom-Json
$pending = @()
foreach ($project in $result.projects) {
    foreach ($framework in $project.frameworks) {
        foreach ($package in $framework.topLevelPackages) {
            $projectPath = [System.IO.Path]::GetFullPath($project.path)
            $pending += [pscustomobject]@{
                Project = $projectPath.Substring($root.Length + 1)
                Package = $package.id
                Current = $package.resolvedVersion
                Latest = $package.latestVersion
            }
        }
    }
}
if ($pending.Count -gt 0) {
    $pending | Format-Table -AutoSize
    exit 1
}

Write-Host 'No direct package updates reported for the full solution.' -ForegroundColor Green
