param(
    [Parameter(Mandatory)]
    [int]$AppPid,
    [Parameter(Mandatory)]
    [ValidateSet("Configure", "Verify")]
    [string]$Phase,
    [Parameter(Mandatory)]
    [string]$LayoutPath,
    [string]$ArtifactDirectory = "artifacts\ticket-12b1"
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
    if ($LASTEXITCODE -ne 0) {
        throw "Could not invoke '$AutomationId'."
    }
}

function Wait-ForElement([string]$AutomationId, [int]$Timeout = 5000) {
    winapp ui wait-for $AutomationId -a $AppPid -t $Timeout 2>$null | Out-Null
    if ($LASTEXITCODE -ne 0) {
        throw "Element '$AutomationId' did not appear."
    }
}

function Open-LayoutOptions {
    Invoke-Element "FooterOptionsButton"
    Wait-ForElement "DashboardLayoutExpander"
    winapp ui scroll-into-view "DashboardLayoutExpander" -a $AppPid 2>$null | Out-Null
    Invoke-Element "DashboardLayoutExpander"
    Wait-ForElement "DashboardLayout.Provider.codex"
}

function Assert-PersistedLayout {
    if (-not (Test-Path -LiteralPath $LayoutPath)) {
        throw "Layout file was not created at '$LayoutPath'."
    }

    $document = Get-Content -LiteralPath $LayoutPath -Raw | ConvertFrom-Json
    if ($document.schemaVersion -ne 1) {
        throw "Unexpected schema version '$($document.schemaVersion)'."
    }

    $providerIds = @($document.providers.providerId)
    if ($providerIds.Count -lt 5 -or $providerIds[0] -ne "claude" -or $providerIds[1] -ne "codex") {
        throw "Provider order was not persisted: $($providerIds -join ', ')."
    }

    $antigravity = $document.providers | Where-Object providerId -eq "antigravity"
    if ($antigravity.isVisible -ne $false) {
        throw "Antigravity visibility was not persisted."
    }

    $grok = $document.providers | Where-Object providerId -eq "grok"
    if ($grok.isHighlighted -ne $true) {
        throw "Grok highlight was not persisted."
    }
}

function Assert-GrokHighlighted {
    $value = winapp ui get-value "SampleProvider.GrokBuild" -a $AppPid --json 2>$null |
        ConvertFrom-Json
    if ($LASTEXITCODE -ne 0 -or $value.text -notmatch "Highlighted|Destacado") {
        throw "Grok card did not expose highlighted state. Value: '$($value.text)'."
    }
}

function Assert-ControlNamesProvider([string]$AutomationId, [string]$ProviderName) {
    $property = winapp ui get-property $AutomationId -a $AppPid --property Name --json 2>$null |
        ConvertFrom-Json
    if ($LASTEXITCODE -ne 0 -or $property.properties.Name -notlike "*$ProviderName*") {
        throw "Control '$AutomationId' does not name provider '$ProviderName'."
    }
}

Wait-ForElement "SampleProvider.Codex" 10000

if ($Phase -eq "Configure") {
    Test-Ui "Options expose compact provider layout controls" {
        Open-LayoutOptions
        foreach ($id in @(
            "DashboardLayout.Provider.codex.MoveDown",
            "DashboardLayout.Provider.antigravity.Visibility",
            "DashboardLayout.Provider.grok.Highlight")) {
            Wait-ForElement $id
        }
        Assert-ControlNamesProvider "DashboardLayout.Provider.codex.MoveDown" "Codex"
        Assert-ControlNamesProvider "DashboardLayout.Provider.antigravity.Visibility" "Antigravity"
        Assert-ControlNamesProvider "DashboardLayout.Provider.grok.Highlight" "Grok Build"
        winapp ui screenshot -a $AppPid -o (Join-Path $ArtifactDirectory "01-layout-options.png") 2>$null |
            Out-Null
    }

    Test-Ui "Move hide and highlight save one coherent layout" {
        Invoke-Element "DashboardLayout.Provider.codex.MoveDown"
        Wait-ForElement "DashboardLayout.Provider.codex.MoveDown"
        Invoke-Element "DashboardLayout.Provider.antigravity.Visibility"
        Wait-ForElement "DashboardLayout.Provider.antigravity.Visibility"
        Invoke-Element "DashboardLayout.Provider.grok.Highlight"

        $deadline = [DateTime]::UtcNow.AddSeconds(5)
        do {
            Start-Sleep -Milliseconds 100
            if (Test-Path -LiteralPath $LayoutPath) {
                try {
                    Assert-PersistedLayout
                    $saved = $true
                }
                catch {
                    $saved = $false
                }
            }
        } while (-not $saved -and [DateTime]::UtcNow -lt $deadline)

        Assert-PersistedLayout
    }

    Test-Ui "Dashboard applies hidden and highlighted state" {
        Invoke-Element "OptionsBackButton"
        winapp ui wait-for "SampleProvider.Antigravity" -a $AppPid --gone -t 5000 2>$null | Out-Null
        if ($LASTEXITCODE -ne 0) { throw "Antigravity card remained visible." }
        Assert-GrokHighlighted
        winapp ui scroll-into-view "SampleProvider.GrokBuild" -a $AppPid 2>$null | Out-Null
        winapp ui screenshot -a $AppPid -o (Join-Path $ArtifactDirectory "02-layout-dashboard.png") 2>$null |
            Out-Null
    }
}
else {
    Test-Ui "Restart loads persisted provider order and states" {
        Assert-PersistedLayout
        winapp ui wait-for "SampleProvider.Antigravity" -a $AppPid --gone -t 5000 2>$null | Out-Null
        if ($LASTEXITCODE -ne 0) { throw "Antigravity returned after restart." }
        Assert-GrokHighlighted
    }

    Test-Ui "Restarted options reflect saved controls" {
        Open-LayoutOptions
        $visibility = winapp ui get-value "DashboardLayout.Provider.antigravity.Visibility" -a $AppPid --json 2>$null |
            ConvertFrom-Json
        if ($visibility.text -ne "Off") { throw "Antigravity toggle is '$($visibility.text)'." }
        $highlight = winapp ui get-value "DashboardLayout.Provider.grok.Highlight" -a $AppPid --json 2>$null |
            ConvertFrom-Json
        if ($highlight.text -ne "On") { throw "Grok highlight toggle is '$($highlight.text)'." }
        winapp ui screenshot -a $AppPid -o (Join-Path $ArtifactDirectory "03-layout-restart.png") 2>$null |
            Out-Null
    }
}

$resultsPath = Join-Path $ArtifactDirectory "ui-results-$($Phase.ToLowerInvariant()).json"
$results | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $resultsPath -Encoding utf8

Write-Output "Ticket 12B1 $Phase UI: $passed passed, $failed failed"
if ($failed -gt 0) {
    exit 1
}
