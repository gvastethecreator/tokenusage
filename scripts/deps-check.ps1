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

# The packaging references are shared with the app; NuGet cannot list a .wapproj.
[xml] $solutionXml = Get-Content -LiteralPath $solution -Raw
$projects = @($solutionXml.SelectNodes('//Project') | Where-Object { $_.Path.EndsWith('.csproj') })
if ($projects.Count -eq 0) { throw 'No managed projects found in the solution.' }
$references = @{}
foreach ($project in $projects) {
    [xml] $projectXml = Get-Content -LiteralPath (Join-Path $root $project.Path) -Raw
    foreach ($reference in $projectXml.SelectNodes('//PackageReference')) {
        $references["$($reference.Include)@$($reference.Version)"] = $true
    }
}
foreach ($project in $solutionXml.SelectNodes('//Project') | Where-Object { $_.Path.EndsWith('.wapproj') }) {
    [xml] $packageXml = Get-Content -LiteralPath (Join-Path $root $project.Path) -Raw
    foreach ($reference in $packageXml.SelectNodes('//*[local-name()="PackageReference"]')) {
        $key = "$($reference.Include)@$($reference.Version)"
        if (-not $references.ContainsKey($key)) {
            throw "Packaging dependency $key is not covered by a managed project update check."
        }
    }
}
$results = foreach ($project in $projects) {
    $path = Join-Path $root $project.Path
    $json = dotnet list $path package --outdated --format json | Out-String
    if ($LASTEXITCODE -ne 0) {
        throw "NuGet dependency check failed for $($project.Path) with exit code $LASTEXITCODE."
    }
    $result = $json | ConvertFrom-Json
    if (-not $result.projects) { throw "NuGet returned no project result for $path." }
    $result.projects
}
$pending = @()
foreach ($project in $results) {
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
    $pending | Format-Table -AutoSize | Out-String -Width 240 | Write-Host
    exit 1
}

Write-Host "No direct package updates reported for $($projects.Count) managed projects." -ForegroundColor Green
