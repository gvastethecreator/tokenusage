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

# The NuGet list command cannot inspect a .wapproj. Audit each managed project
# and ensure packaging adds no dependency outside that audited set.
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
            throw "Packaging dependency $key is not covered by a managed project audit."
        }
    }
}
$failed = $false
foreach ($project in $projects) {
    $path = Join-Path $root $project.Path
    $json = dotnet list $path package --vulnerable --include-transitive --format json | Out-String
    if ($LASTEXITCODE -ne 0) {
        Write-Warning "NuGet audit failed for $($project.Path)."
        $failed = $true
        continue
    }
    $result = $json | ConvertFrom-Json
    if (-not $result.projects) { throw "NuGet returned no project result for $path." }
    $vulnerable = @($result.projects | ForEach-Object {
        foreach ($framework in $_.frameworks) {
            @($framework.topLevelPackages) + @($framework.transitivePackages) |
                Where-Object { $_.vulnerabilities }
        }
    })
    if ($vulnerable.Count -gt 0) {
        $failed = $true
        Write-Warning "Vulnerable packages in $($project.Path):"
        $vulnerable | Select-Object id, resolvedVersion, vulnerabilities | ConvertTo-Json -Depth 6
    } else {
        Write-Host "$($project.Path): no known vulnerable packages."
    }
}
if ($failed) { exit 1 }
Write-Host "NuGet audit passed for $($projects.Count) managed projects; packaging references are covered by those projects." -ForegroundColor Green
