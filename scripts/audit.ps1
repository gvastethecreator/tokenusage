# Audits every active project for known NuGet vulnerabilities.
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
        throw "NuGet auditing does not support solution project: $($project.Path)"
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
Write-Host "NuGet audit passed for $($projects.Count) managed projects." -ForegroundColor Green
