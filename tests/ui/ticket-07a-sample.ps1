param(
    [Parameter(Mandatory)]
    [int]$AppPid,
    [string]$ArtifactDirectory = "artifacts\ticket-07d"
)

$ErrorActionPreference = "Stop"
$passed = 0
$failed = 0
$results = @()
$explorerPid = Get-Process explorer | Select-Object -First 1 -ExpandProperty Id
$package = Get-AppxPackage -Name "D6C94EDD-3747-465C-9A81-05DF5A4108C5" -ErrorAction Stop
$sampleCacheRoot = [System.IO.Path]::GetFullPath(
    (Join-Path $env:LOCALAPPDATA "Packages\$($package.PackageFamilyName)\LocalState\cache\sample"))
$normalCacheDirectory = [System.IO.Path]::GetFullPath(
    (Join-Path $sampleCacheRoot "normal"))
if (-not $normalCacheDirectory.StartsWith(
    $sampleCacheRoot + [System.IO.Path]::DirectorySeparatorChar,
    [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "The synthetic sample cache target escaped its LocalState root."
}
if (Test-Path -LiteralPath $normalCacheDirectory) {
    Remove-Item -LiteralPath $normalCacheDirectory -Recurse -Force
}

New-Item -ItemType Directory -Force -Path $ArtifactDirectory | Out-Null

function Test-Ui {
    param([string]$Name, [scriptblock]$Body)

    try {
        & $Body
        $script:passed++
        $script:results += @{ name = $Name; status = "PASS" }
    }
    catch {
        $script:failed++
        $script:results += @{ name = $Name; status = "FAIL"; detail = "$_" }
    }
}

function Get-AppWindows {
    $json = & winapp ui list-windows -a $AppPid --json 2>$null
    if ([string]::IsNullOrWhiteSpace($json)) { return @() }
    return @($json | ConvertFrom-Json)
}

function Get-TraySelector {
    $match = $null
    $deadline = [DateTime]::UtcNow.AddSeconds(3)
    while (-not $match -and [DateTime]::UtcNow -lt $deadline) {
        $search = & winapp ui search "TokenUsage" -a $explorerPid --json 2>$null | ConvertFrom-Json
        $match = $search.matches | Where-Object { $_.type -eq "Button" } | Select-Object -First 1
        if (-not $match) { Start-Sleep -Milliseconds 150 }
    }
    if (-not $match) { throw "TokenUsage tray button was not found." }
    return $match.selector
}

function Click-Tray {
    & winapp ui click (Get-TraySelector) -a $explorerPid 2>$null | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "Tray click failed." }
}

function Open-TrayMenu {
    $selector = Get-TraySelector
    $menu = $null
    foreach ($attempt in 1..3) {
        & winapp ui click $selector -a $explorerPid --right 2>$null | Out-Null
        if ($LASTEXITCODE -ne 0) { throw "Tray context click failed." }

        $deadline = [DateTime]::UtcNow.AddSeconds(1)
        while (-not $menu -and [DateTime]::UtcNow -lt $deadline) {
            Start-Sleep -Milliseconds 100
            $menu = Get-AppWindows | Where-Object { $_.className -eq "#32768" } | Select-Object -First 1
        }

        if ($menu) { break }
        if ((Get-AppWindows).Count -gt 0) {
            & winapp ui click $selector -a $explorerPid 2>$null | Out-Null
            Start-Sleep -Milliseconds 150
        }
    }

    if (-not $menu) { throw "Native tray menu was not found." }
    return $menu.hwnd
}

function Activate-App {
    $shell = New-Object -ComObject WScript.Shell
    $null = $shell.AppActivate($AppPid)
    Start-Sleep -Milliseconds 75
}

function Invoke-AppElement {
    param([string]$Selector)

    if ((Get-AppWindows).Count -eq 0) {
        Click-Tray
        Start-Sleep -Milliseconds 200
    }

    Activate-App
    & winapp ui invoke $Selector -a $AppPid 2>$null | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "Could not invoke '$Selector'." }
}

function Wait-ForElement {
    param([string]$Selector, [int]$Timeout = 1500)

    & winapp ui wait-for $Selector -a $AppPid -t $Timeout 2>$null | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "Element '$Selector' did not appear." }
}

function Assert-VisibleText {
    param(
        [string]$Text,
        [int]$Timeout = 1000
    )

    $search = $null
    $deadline = [DateTime]::UtcNow.AddMilliseconds($Timeout)
    do {
        $search = & winapp ui search $Text -a $AppPid --json 2>$null | ConvertFrom-Json
        if ($search.matchCount -lt 1) {
            Start-Sleep -Milliseconds 100
        }
    } while ($search.matchCount -lt 1 -and [DateTime]::UtcNow -lt $deadline)

    if ($search.matchCount -lt 1) { throw "Visible text '$Text' was not found." }
}

function Select-ScenarioByOffset {
    param([int]$DownCount)

    Activate-App
    & winapp ui focus "SampleScenarioCombo" -a $AppPid 2>$null | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "Scenario combo could not receive focus." }

    $keyboard = New-Object -ComObject WScript.Shell
    $keyboard.SendKeys("{HOME}")
    if ($DownCount -gt 0) {
        foreach ($step in 1..$DownCount) {
            $keyboard.SendKeys("{DOWN}")
        }
    }
    $keyboard.SendKeys("{ENTER}")
    Start-Sleep -Milliseconds 150
}

Test-Ui "Process starts with sample mode off" {
    if (-not (Get-Process -Id $AppPid -ErrorAction SilentlyContinue)) { throw "Process is not running." }
    if ((Get-AppWindows).Count -ne 0) { throw "The app opened a window before tray activation." }
}

Test-Ui "Options expose session-only sample controls" {
    $menuHwnd = Open-TrayMenu
    & winapp ui invoke "2" -w $menuHwnd 2>$null | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "Options menu command failed." }

    Start-Sleep -Milliseconds 100
    & winapp ui invoke "CloseWhenInactiveToggle" -a $AppPid 2>$null | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "Close-when-inactive could not be disabled." }
    & winapp ui wait-for "CloseWhenInactiveToggle" -a $AppPid --value "Off" -t 1000 2>$null | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "Close-when-inactive did not turn off." }

    Wait-ForElement "SampleModeToggle"
    Wait-ForElement "SampleScenarioCombo"
    & winapp ui screenshot -a $AppPid -o (Join-Path $ArtifactDirectory "01-options-sample.png") 2>$null | Out-Null
}

Test-Ui "First error without Normal cache shows retry instead of fixture data" {
    Invoke-AppElement "SampleModeToggle"
    & winapp ui wait-for "SampleModeToggle" -a $AppPid --value "On" -t 1000 2>$null | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "Sample mode did not turn on." }

    Select-ScenarioByOffset 4
    Invoke-AppElement "OptionsBackButton"
    Wait-ForElement "SampleRetryButton" 2000
    $spend = & winapp ui search '$48.12' -a $AppPid --json 2>$null | ConvertFrom-Json
    if ($spend.matchCount -ne 0) { throw "Fixture spend appeared without a Normal last-good." }
    & winapp ui screenshot -a $AppPid -o (Join-Path $ArtifactDirectory "00-error-no-cache.png") 2>$null | Out-Null

    Invoke-AppElement "FooterOptionsButton"
    Wait-ForElement "SampleScenarioCombo"
    Select-ScenarioByOffset 0
    Invoke-AppElement "OptionsBackButton"
    Wait-ForElement "SampleStateFresh" 2000
}

Test-Ui "Normal sample shows coherent spend and five providers" {
    Wait-ForElement "SampleSpendDonut" 500
    & winapp ui scroll "BodyScrollViewer" -a $AppPid --to top 2>$null | Out-Null
    Start-Sleep -Milliseconds 150
    Assert-VisibleText '$48.12'
    foreach ($amount in @('$22.40', '$12.30', '$7.10', '$5.92', '$0.40')) {
        Assert-VisibleText $amount
    }
    & winapp ui wait-for "SampleSpendDonut" -a $AppPid --value '$48.12' --contains -t 1000 2>$null | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "The spend donut did not expose its accessible total." }
    foreach ($provider in @("Codex", "Claude", "Grok Build", "OpenCode", "Antigravity CLI")) {
        Assert-VisibleText $provider
    }
    & winapp ui screenshot -a $AppPid -o (Join-Path $ArtifactDirectory "02-normal.png") 2>$null | Out-Null
}

Test-Ui "Refresh returns to the selected sample" {
    Invoke-AppElement "HeaderRefreshButton"
    Wait-ForElement "SampleRefreshProgressRing" 500
    Wait-ForElement "SampleSpendDonut" 500
    Assert-VisibleText '$48.12'
    Wait-ForElement "SampleStateFresh" 2000
}

Test-Ui "Near-limit scenario renders deterministic values" {
    Invoke-AppElement "FooterOptionsButton"
    Wait-ForElement "SampleScenarioCombo"
    Select-ScenarioByOffset 1
    Invoke-AppElement "OptionsBackButton"
    Wait-ForElement "SampleStateFresh" 2000
    Wait-ForElement "SampleSpendDonut" 500
    & winapp ui scroll "BodyScrollViewer" -a $AppPid --to top 2>$null | Out-Null
    Start-Sleep -Milliseconds 750
    Assert-VisibleText '$96.40'
    Assert-VisibleText 'Codex'

    & winapp ui screenshot -a $AppPid -o (Join-Path $ArtifactDirectory "03-near-limit.png") 2>$null | Out-Null
}

Test-Ui "Partial scenario exposes partial and policy states" {
    Invoke-AppElement "FooterOptionsButton"
    Wait-ForElement "SampleScenarioCombo"
    Select-ScenarioByOffset 2
    Invoke-AppElement "OptionsBackButton"
    Wait-ForElement "SampleStatePartial" 2000
    Wait-ForElement "SampleSpendDonut" 500
    Assert-VisibleText '$31.05'
    Assert-VisibleText 'Grok Build'

    & winapp ui scroll "BodyScrollViewer" -a $AppPid --to bottom 2>$null | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "The sample dashboard could not scroll to its last card." }
    Assert-VisibleText 'Antigravity CLI'
    & winapp ui screenshot -a $AppPid -o (Join-Path $ArtifactDirectory "04-partial-stale.png") 2>$null | Out-Null
}

Test-Ui "Stale scenario remains a successful visible snapshot" {
    Invoke-AppElement "FooterOptionsButton"
    Wait-ForElement "SampleScenarioCombo"
    Select-ScenarioByOffset 3
    Invoke-AppElement "OptionsBackButton"
    Wait-ForElement "SampleStateStale" 2000
    Wait-ForElement "SampleSpendDonut" 500
    Assert-VisibleText '$48.12'
    & winapp ui screenshot -a $AppPid -o (Join-Path $ArtifactDirectory "05-stale.png") 2>$null | Out-Null
}

Test-Ui "Error scenario keeps the cached dashboard visible" {
    Invoke-AppElement "FooterOptionsButton"
    Wait-ForElement "SampleScenarioCombo"
    Select-ScenarioByOffset 4
    Invoke-AppElement "OptionsBackButton"
    Wait-ForElement "SampleRefreshProgressRing" 500
    Wait-ForElement "SampleSpendDonut" 500
    Wait-ForElement "SampleStateError" 2000
    Assert-VisibleText '$48.12'
    & winapp ui screenshot -a $AppPid -o (Join-Path $ArtifactDirectory "06-error-cache.png") 2>$null | Out-Null
}

Test-Ui "Turning sample mode off restores the live Codex surface" {
    Invoke-AppElement "FooterOptionsButton"
    Invoke-AppElement "SampleModeToggle"
    & winapp ui wait-for "SampleModeToggle" -a $AppPid --value "Off" -t 1000 2>$null | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "Sample mode did not turn off." }
    Invoke-AppElement "OptionsBackButton"
    $deadline = [DateTime]::UtcNow.AddSeconds(3)
    do {
        $live = & winapp ui search "CodexDataState" -a $AppPid --json 2>$null | ConvertFrom-Json
        $unavailable = & winapp ui search "SampleRetryButton" -a $AppPid --json 2>$null | ConvertFrom-Json
        if ($live.matchCount -lt 1 -and $unavailable.matchCount -lt 1) {
            Start-Sleep -Milliseconds 100
        }
    } while (($live.matchCount -lt 1 -and $unavailable.matchCount -lt 1) -and
        [DateTime]::UtcNow -lt $deadline)

    if ($live.matchCount -lt 1 -and $unavailable.matchCount -lt 1) {
        throw "The live Codex surface did not appear."
    }
}

Test-Ui "Interactive controls keep AutomationIds" {
    Invoke-AppElement "FooterOptionsButton"
    $tree = & winapp ui inspect -a $AppPid --interactive --json 2>$null | ConvertFrom-Json
    $controls = @($tree.windows.elements | Where-Object { $_.type -match "Button|ToggleSwitch|ComboBox" })
    $missing = @($controls | Where-Object { -not $_.automationId })
    if ($missing.Count -ne 0) { throw "Missing AutomationId: $(($missing.name) -join ', ')" }
}

Test-Ui "Close-on-deactivate still hides the taller surface" {
    Invoke-AppElement "CloseWhenInactiveToggle"
    $shell = New-Object -ComObject WScript.Shell
    $null = $shell.AppActivate($explorerPid)
    Start-Sleep -Milliseconds 650
    if ((Get-AppWindows).Count -ne 0) {
        throw "The flyout remained visible after close-on-deactivate was restored."
    }
}

Test-Ui "Exit shuts down the sample build" {
    $menuHwnd = Open-TrayMenu
    & winapp ui invoke "3" -w $menuHwnd 2>$null | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "Exit menu command failed." }

    $deadline = [DateTime]::UtcNow.AddSeconds(3)
    while ([DateTime]::UtcNow -lt $deadline -and (Get-Process -Id $AppPid -ErrorAction SilentlyContinue)) {
        Start-Sleep -Milliseconds 100
    }
    if (Get-Process -Id $AppPid -ErrorAction SilentlyContinue) { throw "Process remained alive after Exit." }
}

$results | ConvertTo-Json -Depth 4 | Set-Content (Join-Path $ArtifactDirectory "ui-results.json")
Write-Output "Passed: $passed | Failed: $failed"
$results | Where-Object { $_.status -eq "FAIL" } | ForEach-Object {
    Write-Output "FAIL: $($_.name) - $($_.detail)"
}

if ($failed -gt 0) { exit 1 }
