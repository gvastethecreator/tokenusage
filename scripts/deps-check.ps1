# Lists direct package updates for every active project in the solution.
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$solution = Join-Path $root 'TokenUsage.slnx'

if (-not (Test-Path -LiteralPath $solution -PathType Leaf)) {
    throw "Missing solution: $solution"
}

# Packaging tools are PackageReferences in the app and are included here.
[xml] $solutionXml = Get-Content -LiteralPath $solution -Raw
$projects = @($solutionXml.SelectNodes('//Project'))
if ($projects.Count -eq 0) { throw 'No managed projects found in the solution.' }
foreach ($project in $projects) {
    if (-not $project.Path.EndsWith('.csproj', [StringComparison]::OrdinalIgnoreCase)) {
        throw "Dependency checking does not support solution project: $($project.Path)"
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
