param(
    [Parameter(Mandatory)]
    [int]$AppPid,
    [string]$ArtifactDirectory = "artifacts\ticket-08e"
)

$ErrorActionPreference = "Stop"
$passed = 0
$failed = 0
$results = @()
$explorerPid = Get-Process explorer | Select-Object -First 1 -ExpandProperty Id
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
    & winapp ui click $selector -a $explorerPid --right 2>$null | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "Tray context click failed." }

    $menu = $null
    $deadline = [DateTime]::UtcNow.AddSeconds(2)
    while (-not $menu -and [DateTime]::UtcNow -lt $deadline) {
        Start-Sleep -Milliseconds 100
        $menu = Get-AppWindows | Where-Object { $_.className -eq "#32768" } | Select-Object -First 1
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
    param([string]$Selector, [int]$Timeout = 3000)

    & winapp ui wait-for $Selector -a $AppPid -t $Timeout 2>$null | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "Element '$Selector' did not appear." }
}

function Assert-VisibleText {
    param([string]$Text, [int]$Timeout = 3000)

    $deadline = [DateTime]::UtcNow.AddMilliseconds($Timeout)
    do {
        $search = & winapp ui search $Text -a $AppPid --json 2>$null | ConvertFrom-Json
        if ($search.matchCount -lt 1) { Start-Sleep -Milliseconds 100 }
    } while ($search.matchCount -lt 1 -and [DateTime]::UtcNow -lt $deadline)

    if ($search.matchCount -lt 1) { throw "Visible text '$Text' was not found." }
}

Test-Ui "App starts hidden while live Codex refresh runs" {
    if (-not (Get-Process -Id $AppPid -ErrorAction SilentlyContinue)) {
        throw "Process is not running."
    }

    if ((Get-AppWindows).Count -ne 0) { throw "The app opened before tray activation." }
}

Test-Ui "Live Codex surface renders without synthetic spend or account data" {
    $menuHwnd = Open-TrayMenu
    & winapp ui invoke "2" -w $menuHwnd 2>$null | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "Options menu command failed." }
    Start-Sleep -Milliseconds 100
    Wait-ForElement "OptionsGeneralButton"
    Invoke-AppElement "OptionsGeneralButton"
    Wait-ForElement "CloseWhenInactiveToggle"
    & winapp ui invoke "CloseWhenInactiveToggle" -a $AppPid 2>$null | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "Close-when-inactive could not be disabled." }
    & winapp ui wait-for "CloseWhenInactiveToggle" -a $AppPid --value "Off" -t 1000 2>$null | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "Close-when-inactive did not turn off." }
    & winapp ui invoke "OptionsBackButton" -a $AppPid 2>$null | Out-Null
    & winapp ui invoke "OptionsBackButton" -a $AppPid 2>$null | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "Options could not close." }

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
        throw "Neither the Codex dashboard nor its explicit unavailable state appeared."
    }

    Assert-VisibleText "Codex"
    if ($live.matchCount -gt 0) {
        Assert-VisibleText "58% remaining"
        Assert-VisibleText "82% remaining"
    }

    foreach ($forbidden in @('$48.12', 'private-live@example.invalid', 'auth.json', 'Bearer')) {
        $search = & winapp ui search $forbidden -a $AppPid --json 2>$null | ConvertFrom-Json
        if ($search.matchCount -ne 0) { throw "Forbidden live text appeared: $forbidden" }
    }

    & winapp ui screenshot -a $AppPid -o (Join-Path $ArtifactDirectory "01-live-codex.png") 2>$null | Out-Null
}

Test-Ui "Sample dashboard remains available after live quota" {
    Invoke-AppElement "FooterOptionsButton"
    Invoke-AppElement "OptionsGeneralButton"
    Wait-ForElement "SampleModeToggle"
    Invoke-AppElement "SampleModeToggle"
    & winapp ui wait-for "SampleModeToggle" -a $AppPid --value "On" -t 1000 2>$null | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "Sample mode did not turn on." }

    Invoke-AppElement "OptionsBackButton"
    Invoke-AppElement "OptionsBackButton"
    Wait-ForElement "SampleSpendDonut" 3000
    Assert-VisibleText '$48.12'
    & winapp ui screenshot -a $AppPid -o (Join-Path $ArtifactDirectory "02-sample-preserved.png") 2>$null | Out-Null
}

Test-Ui "Interactive controls keep AutomationIds" {
    Invoke-AppElement "FooterOptionsButton"
    $tree = & winapp ui inspect -a $AppPid --interactive --json 2>$null | ConvertFrom-Json
    $controls = @($tree.windows.elements | Where-Object { $_.type -match "Button|ToggleSwitch|ComboBox" })
    $missing = @($controls | Where-Object { -not $_.automationId })
    if ($missing.Count -ne 0) {
        throw "Missing AutomationId: $(($missing.name) -join ', ')"
    }
}

Test-Ui "Tray exit closes the process" {
    $menuHwnd = Open-TrayMenu
    & winapp ui invoke "3" -w $menuHwnd 2>$null | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "Exit menu command failed." }

    $deadline = [DateTime]::UtcNow.AddSeconds(3)
    while (([DateTime]::UtcNow -lt $deadline) -and
        (Get-Process -Id $AppPid -ErrorAction SilentlyContinue)) {
        Start-Sleep -Milliseconds 100
    }

    if (Get-Process -Id $AppPid -ErrorAction SilentlyContinue) {
        throw "Process remained alive after Exit."
    }
}

$results | ConvertTo-Json -Depth 4 | Set-Content (Join-Path $ArtifactDirectory "ui-results.json")
Write-Output "Passed: $passed | Failed: $failed"
$results | Where-Object { $_.status -eq "FAIL" } | ForEach-Object {
    Write-Output "FAIL: $($_.name) - $($_.detail)"
}

if ($failed -gt 0) { exit 1 }
