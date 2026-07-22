param([Parameter(Mandatory)][int]$AppPid)

$ErrorActionPreference = 'Stop'
$results = @()

function Add-Check([string]$Name, [scriptblock]$Action) {
    try {
        & $Action
        $script:results += @{ name = $Name; status = 'PASS' }
    }
    catch {
        $script:results += @{ name = $Name; status = 'FAIL'; detail = "$_" }
    }
}

function Require-Value([string]$Id, [string]$Pattern, [bool]$Reject = $false) {
    winapp ui wait-for $Id -a $AppPid -t 10000 2>$null | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "Missing $Id" }
    $value = winapp ui get-value $Id -a $AppPid --json 2>$null | ConvertFrom-Json
    $matches = $value.text -match $Pattern
    if (($Reject -and $matches) -or (-not $Reject -and -not $matches)) {
        throw "Unexpected value for $Id"
    }
}

Add-Check 'Today has real spend' {
    Require-Value 'UsageProductCard.Period.Today' 'Inf\.|Rep\.'
    Require-Value 'UsageProductCard.Period.Today' 'Sin datos|No data' $true
}
Add-Check 'Yesterday has an empty state' {
    Require-Value 'UsageProductCard.Period.Yesterday' 'Sin datos|No data'
}
Add-Check 'Seven days has coverage' {
    Require-Value 'UsageProductCard.Period.7Days' '%'
}
Add-Check 'Current month has tokens' {
    Require-Value 'UsageProductCard.Period.Month' 'tokens'
}
Add-Check 'Thirty-day cost types stay separate' {
    Require-Value 'UsageProductCard.ReportedCost' 'Sin datos|No data' $true
    Require-Value 'UsageProductCard.EstimatedCost' 'Sin datos|No data' $true
}
Add-Check 'Cost per million appears' {
    Require-Value 'UsageProductCard.CostPerMillion' '1 M|1M'
}
Add-Check 'Breakdown expands through UI Automation' {
    winapp ui invoke 'UsageProductCard.Breakdown30Days' -a $AppPid 2>$null | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Could not expand breakdown' }
    winapp ui wait-for 'UsageProductCard.AgentRing30Days' -a $AppPid -t 10000 2>$null | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Missing agent ring' }
}
Add-Check 'Claude model appears' {
    winapp ui wait-for 'UsageProductCard.Model.claude.claude-sonnet-4-6' -a $AppPid -t 10000 2>$null | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Missing Claude model' }
}
Add-Check 'OpenCode model appears' {
    winapp ui scroll 'UsageProductCard.ModelBreakdown' -a $AppPid --to bottom 2>$null | Out-Null
    winapp ui wait-for 'UsageProductCard.Model.opencode.gpt-5' -a $AppPid -t 10000 2>$null | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Missing OpenCode model' }
}

$artifactDirectory = Join-Path $PSScriptRoot '..\..\artifacts\ticket-20'
New-Item -ItemType Directory -Force -Path $artifactDirectory | Out-Null
winapp ui scroll 'UsageProductCard.ModelBreakdown' -a $AppPid --to top 2>$null | Out-Null
winapp ui scroll-into-view 'UsageProductCard.Breakdown30Days' -a $AppPid 2>$null | Out-Null
winapp ui screenshot -a $AppPid -o (Join-Path $artifactDirectory 'real-spend-expanded.png') 2>$null | Out-Null
$results | ConvertTo-Json -Depth 4 | Set-Content (Join-Path $artifactDirectory 'ui-results.json')

$failed = @($results | Where-Object status -eq 'FAIL')
Write-Host "Passed: $($results.Count - $failed.Count) | Failed: $($failed.Count)"
if ($failed.Count -gt 0) {
    $failed | ForEach-Object { Write-Host "FAIL: $($_.name) - $($_.detail)" }
    exit 1
}
