# Lists direct package updates for active projects without touching archived
# probes under .scratch, .reference, or .snapshots.
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$projects = Get-ChildItem (Join-Path $root 'src'), (Join-Path $root 'tests') -Recurse -File -Filter '*.csproj'
$pending = @()
foreach ($project in $projects) {
    $json = dotnet list $project.FullName package --outdated --format json 2>$null | Out-String
    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($json)) { continue }
    $result = $json | ConvertFrom-Json
    foreach ($framework in $result.projects.frameworks) {
        foreach ($package in $framework.topLevelPackages) {
            $pending += [pscustomobject]@{
                Project = $project.FullName.Substring($root.Length + 1)
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
Write-Host 'No direct package updates reported for active projects.' -ForegroundColor Green
