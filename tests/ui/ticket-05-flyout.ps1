param(
    [Parameter(Mandatory)]
    [int]$AppPid,
    [string]$ArtifactDirectory = "artifacts\ticket-05"
)

$ErrorActionPreference = "Stop"
$passed = 0
$failed = 0
$results = @()
$explorerPid = Get-Process explorer | Select-Object -First 1 -ExpandProperty Id

New-Item -ItemType Directory -Force -Path $ArtifactDirectory | Out-Null

function Test-Ui {
    param(
        [string]$Name,
        [scriptblock]$Body
    )

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
    if ([string]::IsNullOrWhiteSpace($json)) {
        return @()
    }

    return @($json | ConvertFrom-Json)
}

function Get-TraySelector {
    $match = $null
    $deadline = [DateTime]::UtcNow.AddSeconds(3)
    while (-not $match -and [DateTime]::UtcNow -lt $deadline) {
        $search = & winapp ui search "WOpenUsage" -a $explorerPid --json 2>$null |
            ConvertFrom-Json
        $match = $search.matches | Where-Object { $_.type -eq "Button" } | Select-Object -First 1
        if (-not $match) {
            Start-Sleep -Milliseconds 150
        }
    }

    if (-not $match) {
        throw "WOpenUsage tray button was not found."
    }

    return $match.selector
}

function Invoke-Tray {
    $selector = Get-TraySelector
    & winapp ui click $selector -a $explorerPid 2>$null | Out-Null
    if ($LASTEXITCODE -ne 0) {
        throw "Tray click failed."
    }
}

function Open-TrayMenu {
    $selector = Get-TraySelector
    $menu = $null
    foreach ($attempt in 1..2) {
        & winapp ui click $selector -a $explorerPid --right 2>$null | Out-Null
        if ($LASTEXITCODE -ne 0) {
            throw "Tray context click failed."
        }

        $deadline = [DateTime]::UtcNow.AddSeconds(1)
        while (-not $menu -and [DateTime]::UtcNow -lt $deadline) {
            Start-Sleep -Milliseconds 100
            $menu = Get-AppWindows |
                Where-Object { $_.className -eq "#32768" } |
                Select-Object -First 1
        }

        if ($menu) {
            break
        }

        if ((Get-AppWindows).Count -gt 0) {
            & winapp ui click $selector -a $explorerPid 2>$null | Out-Null
            Start-Sleep -Milliseconds 200
        }
    }

    if (-not $menu) {
        throw "Native tray menu was not found."
    }

    Start-Sleep -Milliseconds 200
    return $menu.hwnd
}

function Invoke-Element {
    param([string]$Selector)

    if ((Get-AppWindows).Count -eq 0) {
        Invoke-Tray
        Start-Sleep -Milliseconds 250
    }

    $windowActivator = New-Object -ComObject WScript.Shell
    $null = $windowActivator.AppActivate($AppPid)
    Start-Sleep -Milliseconds 75
    & winapp ui invoke $Selector -a $AppPid 2>$null | Out-Null
    if ($LASTEXITCODE -ne 0) {
        throw "Could not invoke '$Selector'."
    }
}

function Wait-ForElement {
    param(
        [string]$Selector,
        [int]$Timeout = 1500
    )

    & winapp ui wait-for $Selector -a $AppPid -t $Timeout 2>$null | Out-Null
    if ($LASTEXITCODE -ne 0) {
        throw "Element '$Selector' did not appear."
    }
}

Test-Ui "Process starts in tray mode" {
    $process = Get-Process -Id $AppPid
    if (-not $process.Responding) {
        throw "Process is not responding."
    }

    if ((Get-AppWindows).Count -ne 0) {
        throw "A visible app window was present before tray activation."
    }
}

Test-Ui "Tray icon is visible and enabled" {
    $null = Get-TraySelector
}

Test-Ui "Native context menu exposes all commands" {
    $menuHwnd = Open-TrayMenu
    Start-Sleep -Milliseconds 100
    $menu = & winapp ui inspect -w $menuHwnd --interactive --json 2>$null |
        ConvertFrom-Json
    $names = @($menu.windows.elements.name)
    foreach ($expected in @("Actualizar", "Opciones", "Salir")) {
        if ($expected -notin $names) {
            throw "Missing menu command '$expected'."
        }
    }

    & winapp ui invoke "2" -w $menuHwnd 2>$null | Out-Null
    if ($LASTEXITCODE -ne 0) {
        throw "Options menu command failed."
    }
}

Test-Ui "Options state opens and toggle is real" {
    Start-Sleep -Milliseconds 100
    & winapp ui invoke "CloseWhenInactiveToggle" -a $AppPid 2>$null | Out-Null
    if ($LASTEXITCODE -ne 0) {
        throw "Close-when-inactive could not be toggled."
    }

    & winapp ui wait-for "CloseWhenInactiveToggle" -a $AppPid --value "Off" -t 1000 2>$null | Out-Null
    if ($LASTEXITCODE -ne 0) {
        throw "Close-when-inactive did not turn off."
    }

    & winapp ui screenshot -a $AppPid -o (Join-Path $ArtifactDirectory "03-options.png") 2>$null | Out-Null
}

Test-Ui "Empty state renders and opens from options" {
    Invoke-Element "OptionsBackButton"
    Wait-ForElement "EmptyOpenOptionsButton"
    & winapp ui screenshot -a $AppPid -o (Join-Path $ArtifactDirectory "01-empty.png") 2>$null | Out-Null
}

Test-Ui "Refresh shows loading then returns to empty" {
    $loadingScreenshot = Join-Path $ArtifactDirectory "02-loading.png"
    $screenshotJob = Start-ThreadJob -ScriptBlock {
        param($TargetPid, $TargetPath)
        Start-Sleep -Milliseconds 75
        & winapp ui screenshot -a $TargetPid -o $TargetPath 2>$null | Out-Null
    } -ArgumentList $AppPid, $loadingScreenshot

    Invoke-Element "HeaderRefreshButton"
    Wait-ForElement "LoadingProgressRing" 500
    $screenshotJob | Wait-Job -Timeout 4 | Out-Null
    if ($screenshotJob.State -ne "Completed") {
        $screenshotJob | Remove-Job -Force
        throw "Loading screenshot did not complete."
    }

    $screenshotJob | Receive-Job | Out-Null
    $screenshotJob | Remove-Job -Force
    Wait-ForElement "EmptyOpenOptionsButton" 2000
}

Test-Ui "All interactive app controls have AutomationId" {
    $tree = & winapp ui inspect -a $AppPid --interactive --json 2>$null |
        ConvertFrom-Json
    $controls = @($tree.windows.elements | Where-Object {
        $_.type -match "Button|ToggleSwitch|TextBox|ComboBox|CheckBox"
    })
    $missing = @($controls | Where-Object { -not $_.automationId })
    if ($missing.Count -ne 0) {
        throw "Missing AutomationId: $(($missing.name) -join ', ')"
    }
}

Test-Ui "Escape hides the usage surface" {
    & winapp ui focus "EmptyOpenOptionsButton" -a $AppPid 2>$null | Out-Null
    $keyboard = New-Object -ComObject WScript.Shell
    $keyboard.SendKeys("{ESC}")
    Start-Sleep -Milliseconds 350
    if ((Get-AppWindows).Count -ne 0) {
        throw "Flyout remained visible after Escape."
    }
}

Test-Ui "UI Automation tray activation focuses the primary action" {
    $selector = Get-TraySelector
    & winapp ui invoke $selector -a $explorerPid 2>$null | Out-Null
    if ($LASTEXITCODE -ne 0) {
        throw "Tray InvokePattern activation failed."
    }

    Wait-ForElement "EmptyOpenOptionsButton" 2500
    $focused = & winapp ui get-focused -a $AppPid --json 2>$null | ConvertFrom-Json
    if ($focused.element.automationId -ne "EmptyOpenOptionsButton") {
        throw "Tray activation focused '$($focused.element.automationId)'."
    }
}

Test-Ui "Mouse tray activation toggles visibility" {
    Invoke-Tray
    Start-Sleep -Milliseconds 250
    if ((Get-AppWindows).Count -ne 0) {
        throw "Visible flyout did not hide on tray activation."
    }

    Invoke-Tray
    Wait-ForElement "EmptyOpenOptionsButton"
}

Test-Ui "Context Update runs the loading state" {
    $menuHwnd = Open-TrayMenu
    & winapp ui invoke "1" -w $menuHwnd 2>$null | Out-Null
    if ($LASTEXITCODE -ne 0) {
        throw "Update menu command failed."
    }

    Wait-ForElement "LoadingProgressRing" 500
    Wait-ForElement "EmptyOpenOptionsButton" 2000
}

Test-Ui "Context Exit shuts down cleanly" {
    $menuHwnd = Open-TrayMenu
    & winapp ui invoke "3" -w $menuHwnd 2>$null | Out-Null
    if ($LASTEXITCODE -ne 0) {
        throw "Exit menu command failed."
    }

    $deadline = [DateTime]::UtcNow.AddSeconds(3)
    while ([DateTime]::UtcNow -lt $deadline -and (Get-Process -Id $AppPid -ErrorAction SilentlyContinue)) {
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

if ($failed -gt 0) {
    exit 1
}
