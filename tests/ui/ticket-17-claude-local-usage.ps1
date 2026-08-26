param([Parameter(Mandatory)][int]$AppPid)

$ErrorActionPreference = 'Stop'
$checks = @(
    @{ Name = 'Claude card appears'; Id = 'UsageProductCard'; Value = $null },
    @{ Name = 'Claude local source appears'; Id = 'UsageProductCard.DataOrigin'; Value = 'Claude' },
    @{ Name = 'Total tokens appear'; Id = 'UsageProductCard.TotalTokens'; Value = $null },
    @{ Name = 'Reported cost appears'; Id = 'UsageProductCard.ReportedCost'; Value = $null },
    @{ Name = 'Estimated cost appears'; Id = 'UsageProductCard.EstimatedCost'; Value = $null },
    @{ Name = 'Unpriced tokens appear'; Id = 'UsageProductCard.UnpricedUsage'; Value = $null },
    @{ Name = 'Cost coverage appears'; Id = 'UsageProductCard.CostCoverage'; Value = $null }
)
$results = @()
winapp ui wait-for 'UsageProductCard' -a $AppPid -t 10000 2>$null | Out-Null
$usageDetails = winapp ui search 'UsageProductCard.DetailsToggle' -a $AppPid --json 2>$null |
    ConvertFrom-Json
if (($usageDetails.matches | Select-Object -First 1).toggleState -ne 'on') {
    winapp ui invoke 'UsageProductCard.DetailsToggle' -a $AppPid 2>$null | Out-Null
}
foreach ($check in $checks) {
    try {
        winapp ui wait-for $check.Id -a $AppPid -t 10000 2>$null | Out-Null
        if ($LASTEXITCODE -ne 0) { throw "Missing $($check.Id)" }
        $value = winapp ui get-value $check.Id -a $AppPid --json 2>$null | ConvertFrom-Json
        if ($check.Value -and $value.text -notmatch [regex]::Escape($check.Value)) {
            throw "Unexpected value: $($value.text)"
        }
        if (-not $check.Value -and $check.Id -ne 'UsageProductCard' -and
            $value.text -match 'No data') {
            throw "Missing metric: $($value.text)"
        }
        $results += @{ name = $check.Name; status = 'PASS' }
    }
    catch {
        $results += @{ name = $check.Name; status = 'FAIL'; detail = "$_" }
    }
}

$artifactDirectory = Join-Path $PSScriptRoot '..\..\artifacts\ticket-17'
New-Item -ItemType Directory -Force -Path $artifactDirectory | Out-Null
winapp ui screenshot -a $AppPid -o (Join-Path $artifactDirectory 'claude-local-usage.png') 2>$null | Out-Null
$results | ConvertTo-Json -Depth 4 | Set-Content (Join-Path $artifactDirectory 'ui-results.json')

$failed = @($results | Where-Object status -eq 'FAIL')
Write-Host "Passed: $($results.Count - $failed.Count) | Failed: $($failed.Count)"
if ($failed.Count -gt 0) {
    $failed | ForEach-Object { Write-Host "FAIL: $($_.name) - $($_.detail)" }
    exit 1
}
