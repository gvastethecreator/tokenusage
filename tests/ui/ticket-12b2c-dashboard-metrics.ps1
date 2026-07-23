param(
    [Parameter(Mandatory)]
    [int]$AppPid,
    [Parameter(Mandatory)]
    [ValidateSet("Configure", "Verify")]
    [string]$Phase,
    [Parameter(Mandatory)]
    [string]$LayoutPath,
    [string]$ArtifactDirectory = "artifacts\ticket-12b2c"
)

$ErrorActionPreference = "Stop"
$passed = 0
$failed = 0
$results = @()
New-Item -ItemType Directory -Force -Path $ArtifactDirectory | Out-Null

function Test-Ui([string]$Name, [scriptblock]$Action) {
    try {
        & $Action
        $script:passed++
        $script:results += @{ phase = $Phase; name = $Name; status = "PASS" }
    }
    catch {
        $script:failed++
        $script:results += @{ phase = $Phase; name = $Name; status = "FAIL"; detail = "$_" }
    }
}

function Invoke-Element([string]$AutomationId) {
    winapp ui invoke $AutomationId -a $AppPid 2>$null | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "Could not invoke '$AutomationId'." }
}

function Wait-ForElement([string]$AutomationId, [int]$Timeout = 5000) {
    winapp ui wait-for $AutomationId -a $AppPid -t $Timeout 2>$null | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "Element '$AutomationId' did not appear." }
}

function Open-MetricOptions {
    Invoke-Element "FooterOptionsButton"
    Wait-ForElement "OptionsPersonalizationButton"
    Invoke-Element "OptionsPersonalizationButton"
    Wait-ForElement "DashboardLayoutExpander"
    winapp ui scroll-into-view "DashboardLayoutExpander" -a $AppPid 2>$null | Out-Null
    Invoke-Element "DashboardLayoutExpander"
    Expand-CodexMetricOptions
}

function Expand-CodexMetricOptions {
    Wait-ForElement "DashboardLayout.Provider.codex.Metrics"
    winapp ui scroll-into-view "DashboardLayout.Provider.codex.Metrics" -a $AppPid 2>$null | Out-Null
    Invoke-Element "DashboardLayout.Provider.codex.Metrics"
    Wait-ForElement "DashboardLayout.Provider.codex.Metric.usage.tokens.today"
}

function Get-CodexMetrics {
    if (-not (Test-Path -LiteralPath $LayoutPath)) {
        throw "Layout file was not created at '$LayoutPath'."
    }

    $document = Get-Content -LiteralPath $LayoutPath -Raw | ConvertFrom-Json
    $codex = $document.providers | Where-Object providerId -eq "codex"
    if ($null -eq $codex) { throw "Codex layout is absent." }
    return @($codex.metrics)
}

function Assert-PersistedMetrics {
    $metrics = Get-CodexMetrics
    $ids = @($metrics.metricId)
    $todayIndex = [Array]::IndexOf($ids, "usage.tokens.today")
    $weeklyIndex = [Array]::IndexOf($ids, "quota.weekly")
    if ($todayIndex -lt 0 -or $weeklyIndex -lt 0 -or $todayIndex -ge $weeklyIndex) {
        throw "Mixed metric order was not persisted: $($ids -join ', ')."
    }

    $today = $metrics | Where-Object metricId -eq "usage.tokens.today"
    if ($today.isOnDemand -ne $false -or $today.isHighlighted -ne $true) {
        throw "Today section/highlight was not persisted."
    }

    $yesterday = $metrics | Where-Object metricId -eq "usage.tokens.yesterday"
    if ($yesterday.isVisible -ne $false) {
        throw "Yesterday visibility was not persisted."
    }
}

function Assert-ControlName([string]$AutomationId, [string]$Pattern) {
    $property = winapp ui get-property $AutomationId -a $AppPid --property Name --json 2>$null |
        ConvertFrom-Json
    if ($LASTEXITCODE -ne 0 -or $property.properties.Name -notmatch $Pattern) {
        throw "Control '$AutomationId' has an invalid accessible name '$($property.properties.Name)'."
    }
}

Wait-ForElement "SampleProvider.Codex" 10000

if ($Phase -eq "Configure") {
    Test-Ui "Metric detail exposes compact localized controls" {
        Open-MetricOptions
        foreach ($id in @(
            "DashboardLayout.Provider.codex.Metric.usage.tokens.today.MoveUp",
            "DashboardLayout.Provider.codex.Metric.usage.tokens.today.Visibility",
            "DashboardLayout.Provider.codex.Metric.usage.tokens.today.Highlight",
            "DashboardLayout.Provider.codex.Metric.usage.tokens.today.Section")) {
            Wait-ForElement $id
            Assert-ControlName $id "Today|Hoy"
        }
        winapp ui screenshot -a $AppPid -o (Join-Path $ArtifactDirectory "01-metric-options.png") 2>$null | Out-Null
    }

    Test-Ui "Metric order section visibility and highlight save together" {
        Invoke-Element "DashboardLayout.Provider.codex.Metric.usage.tokens.today.Section"
        Wait-ForElement "DashboardLayout.Provider.codex.Metric.usage.tokens.today"
        Invoke-Element "DashboardLayout.Provider.codex.Metric.usage.tokens.today.MoveUp"
        Wait-ForElement "DashboardLayout.Provider.codex.Metric.usage.tokens.today"
        Invoke-Element "DashboardLayout.Provider.codex.Metric.usage.tokens.yesterday.Visibility"
        Wait-ForElement "DashboardLayout.Provider.codex.Metric.usage.tokens.today"
        Invoke-Element "DashboardLayout.Provider.codex.Metric.usage.tokens.today.Highlight"

        $deadline = [DateTime]::UtcNow.AddSeconds(5)
        do {
            Start-Sleep -Milliseconds 100
            try { Assert-PersistedMetrics; $saved = $true } catch { $saved = $false }
        } while (-not $saved -and [DateTime]::UtcNow -lt $deadline)
        Assert-PersistedMetrics
    }

    Test-Ui "Third highlighted metric explains the provider limit" {
        Invoke-Element "DashboardLayout.Provider.codex.Metric.quota.session.Highlight"
        Wait-ForElement "DashboardLayout.Provider.codex.Metric.usage.tokens.today"
        Invoke-Element "DashboardLayout.Provider.codex.Metric.quota.weekly.Highlight"
        Wait-ForElement "DashboardLayoutStatus"
        $status = winapp ui get-property "Message" -a $AppPid --property Name --json 2>$null | ConvertFrom-Json
        if ($status.properties.Name -notmatch "two metrics|dos métricas") {
            throw "Highlight limit status is '$($status.properties.Name)'."
        }

        Invoke-Element "DashboardLayout.Provider.codex.Metric.quota.session.Highlight"
    }

    Test-Ui "Dashboard applies primary hidden and highlighted metrics" {
        Invoke-Element "OptionsBackButton"
        Invoke-Element "OptionsBackButton"
        Wait-ForElement "CodexUsage.Today"
        Assert-ControlName "CodexUsage.Today" "Highlighted|Destacado"
        winapp ui screenshot -a $AppPid -o (Join-Path $ArtifactDirectory "02-metric-dashboard.png") 2>$null | Out-Null
    }
}
else {
    Test-Ui "Restart restores projected metric state" {
        Assert-PersistedMetrics
        Wait-ForElement "CodexUsage.Today"
        Assert-ControlName "CodexUsage.Today" "Highlighted|Destacado"
    }

    Test-Ui "Restarted controls reflect metric preferences" {
        Open-MetricOptions
        $section = winapp ui get-value "DashboardLayout.Provider.codex.Metric.usage.tokens.today.Section" -a $AppPid --json 2>$null | ConvertFrom-Json
        if ($section.text -ne "Off") { throw "Today section toggle is '$($section.text)'." }
        $highlight = winapp ui get-value "DashboardLayout.Provider.codex.Metric.usage.tokens.today.Highlight" -a $AppPid --json 2>$null | ConvertFrom-Json
        if ($highlight.text -ne "On") { throw "Today highlight toggle is '$($highlight.text)'." }
        $visibility = winapp ui get-value "DashboardLayout.Provider.codex.Metric.usage.tokens.yesterday.Visibility" -a $AppPid --json 2>$null | ConvertFrom-Json
        if ($visibility.text -ne "Off") { throw "Yesterday visibility toggle is '$($visibility.text)'." }
        winapp ui screenshot -a $AppPid -o (Join-Path $ArtifactDirectory "03-metric-restart.png") 2>$null | Out-Null
    }
}

$resultsPath = Join-Path $ArtifactDirectory "ui-results-$($Phase.ToLowerInvariant()).json"
$results | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $resultsPath -Encoding utf8
Write-Output "Ticket 12B2C $Phase UI: $passed passed, $failed failed"
if ($failed -gt 0) { exit 1 }
