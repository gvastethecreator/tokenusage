# Audits representative active projects for known NuGet vulnerabilities.
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$projects = @(
    (Join-Path $root 'src\TokenUsage.App\TokenUsage.App.csproj'),
    (Join-Path $root 'src\TokenUsage.Core\TokenUsage.Core.csproj'),
    (Join-Path $root 'tests\TokenUsage.Core.Tests\TokenUsage.Core.Tests.csproj')
)
foreach ($project in $projects) {
    Write-Host ("==> {0}" -f $project) -ForegroundColor Cyan
    dotnet list $project package --vulnerable --include-transitive
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}
Write-Host 'NuGet vulnerability audit completed.' -ForegroundColor Green
