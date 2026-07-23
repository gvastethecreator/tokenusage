param(
    [Parameter(Mandatory)]
    [int]$AppPid,
    [Parameter(Mandatory)]
    [ValidateSet("Configure", "Verify")]
    [string]$Phase,
    [Parameter(Mandatory)]
    [string]$LayoutPath,
    [string]$ArtifactDirectory = "artifacts\ticket-12c"
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

function Invoke-Element([string]$Selector) {
    winapp ui invoke $Selector -a $AppPid 2>$null | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "Could not invoke '$Selector'." }
}

function Invoke-First([string[]]$Selectors) {
    foreach ($selector in $Selectors) {
        winapp ui invoke $selector -a $AppPid 2>$null | Out-Null
        if ($LASTEXITCODE -eq 0) { return }
    }

    throw "Could not invoke any of: $($Selectors -join ', ')."
}

function Wait-ForElement([string]$Selector, [int]$Timeout = 5000) {
    winapp ui wait-for $Selector -a $AppPid -t $Timeout 2>$null | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "Element '$Selector' did not appear." }
}

function Send-KeyTo([string]$Selector, [string]$Keys) {
    $keyboard = New-Object -ComObject WScript.Shell
    if (-not $keyboard.AppActivate($AppPid)) { throw "Could not activate process $AppPid." }
    Start-Sleep -Milliseconds 100
    winapp ui focus $Selector -a $AppPid 2>$null | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "Could not focus '$Selector'." }
    Start-Sleep -Milliseconds 100
    $keyboard.SendKeys($Keys)
}

function Open-DashboardLayout {
    Invoke-Element "FooterOptionsButton"
    Wait-ForElement "DashboardLayoutExpander"
    winapp ui scroll-into-view "DashboardLayoutExpander" -a $AppPid 2>$null | Out-Null
    Invoke-Element "DashboardLayoutExpander"
    Wait-ForElement "DashboardLayout.Provider.codex.MoveDown"
}

function Get-Layout {
    if (-not (Test-Path -LiteralPath $LayoutPath)) {
        throw "Layout file was not created at '$LayoutPath'."
    }

    return Get-Content -LiteralPath $LayoutPath -Raw | ConvertFrom-Json
}

function Wait-ForLayout([scriptblock]$Predicate, [string]$Failure) {
    $deadline = [DateTime]::UtcNow.AddSeconds(5)
    do {
        Start-Sleep -Milliseconds 100
        try {
            $layout = Get-Layout
            if (& $Predicate $layout) { return }
        }
        catch { }
    } while ([DateTime]::UtcNow -lt $deadline)

    throw $Failure
}

function Test-CustomizedLayout($Layout) {
    $providers = @($Layout.providers)
    if ($providers.Count -lt 2) { return $false }
    $codexIndex = [Array]::IndexOf(@($providers.providerId), "codex")
    $antigravity = $providers | Where-Object providerId -eq "antigravity"
    return $codexIndex -eq 1 -and $antigravity.isVisible -eq $true
}

Wait-ForElement "SampleProvider.Codex" 10000

if ($Phase -eq "Configure") {
    Test-Ui "Keyboard moves and toggles providers" {
        Open-DashboardLayout
        Send-KeyTo "DashboardLayout.Provider.codex.MoveDown" "{ENTER}"
        Wait-ForLayout {
            param($layout)
            [Array]::IndexOf(@($layout.providers.providerId), "codex") -eq 1
        } "Keyboard move was not saved."

        Send-KeyTo "DashboardLayout.Provider.antigravity.Visibility" " "
        Wait-ForLayout {
            param($layout)
            ($layout.providers | Where-Object providerId -eq "antigravity").isVisible -eq $false
        } "Keyboard visibility change was not saved."
    }

    Test-Ui "Ctrl Z undoes the last session change" {
        Send-KeyTo "DashboardLayoutUndoButton" "^z"
        Wait-ForLayout { param($layout) Test-CustomizedLayout $layout } "Ctrl+Z did not restore visibility."
    }

    Test-Ui "Reset requires confirmation and supports session undo" {
        Invoke-Element "DashboardLayoutResetButton"
        Wait-ForElement "DashboardLayoutResetDialog"
        winapp ui screenshot -a $AppPid -o (Join-Path $ArtifactDirectory "01-reset-confirmation.png") 2>$null | Out-Null
        Invoke-First @("Cancel", "Cancelar")
        Wait-ForLayout { param($layout) Test-CustomizedLayout $layout } "Cancel changed the layout."

        Invoke-Element "DashboardLayoutResetButton"
        Wait-ForElement "DashboardLayoutResetDialog"
        Invoke-First @("Reset dashboard", "Restablecer panel")
        Wait-ForLayout { param($layout) @($layout.providers).Count -eq 0 } "Reset did not save defaults."
        Wait-ForElement "DashboardLayout.Provider.codex"

        Invoke-Element "DashboardLayoutUndoButton"
        Wait-ForLayout { param($layout) Test-CustomizedLayout $layout } "Undo did not restore the pre-reset layout."
        winapp ui screenshot -a $AppPid -o (Join-Path $ArtifactDirectory "02-restored-layout.png") 2>$null | Out-Null
    }
}
else {
    Test-Ui "Restart loads the restored custom layout" {
        Wait-ForLayout { param($layout) Test-CustomizedLayout $layout } "Restored layout did not survive restart."
        Open-DashboardLayout
        $undo = winapp ui get-property "DashboardLayoutUndoButton" -a $AppPid --property IsEnabled --json 2>$null |
            ConvertFrom-Json
        if ($undo.properties.IsEnabled -ne "False") {
            throw "Session undo unexpectedly survived restart."
        }
        winapp ui screenshot -a $AppPid -o (Join-Path $ArtifactDirectory "03-restart-layout.png") 2>$null | Out-Null
    }
}

$resultsPath = Join-Path $ArtifactDirectory "ui-results-$($Phase.ToLowerInvariant()).json"
$results | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $resultsPath -Encoding utf8
Write-Output "Ticket 12C $Phase UI: $passed passed, $failed failed"
if ($failed -gt 0) { exit 1 }
