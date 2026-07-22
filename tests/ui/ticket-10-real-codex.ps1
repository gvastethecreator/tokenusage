param(
    [Parameter(Mandatory)] [int]$AppPid
)

$ErrorActionPreference = "Stop"
$explorerPid = Get-Process explorer | Select-Object -First 1 -ExpandProperty Id

function Get-TraySelector {
    $deadline = [DateTime]::UtcNow.AddSeconds(5)
    do {
        $search = & winapp ui search "WOpenUsage" -a $explorerPid --json 2>$null |
            ConvertFrom-Json
        $match = $search.matches | Where-Object type -eq "Button" | Select-Object -First 1
        if (-not $match) { Start-Sleep -Milliseconds 100 }
    } while (-not $match -and [DateTime]::UtcNow -lt $deadline)
    if (-not $match) { throw "Tray button not found." }
    return $match.selector
}

function Open-Options([string]$traySelector) {
    $menu = $null
    foreach ($attempt in 1..3) {
        & winapp ui click $traySelector -a $explorerPid --right 2>$null | Out-Null
        $deadline = [DateTime]::UtcNow.AddSeconds(1)
        do {
            Start-Sleep -Milliseconds 100
            $json = & winapp ui list-windows -a $AppPid --json 2>$null
            if (-not [string]::IsNullOrWhiteSpace($json)) {
                $menu = @($json | ConvertFrom-Json) |
                    Where-Object className -eq "#32768" |
                    Select-Object -First 1
            }
        } while (-not $menu -and [DateTime]::UtcNow -lt $deadline)
        if ($menu) { break }
    }
    if (-not $menu) { throw "Tray menu not found." }
    & winapp ui invoke "2" -w $menu.hwnd 2>$null | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "Options command failed." }
    & winapp ui wait-for "CloseWhenInactiveToggle" -a $AppPid -t 3000 2>$null | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "Options did not open." }
}

function Read-ToggleValue {
    $result = & winapp ui search "CloseWhenInactiveToggle" -a $AppPid --json 2>$null |
        ConvertFrom-Json
    return ($result.matches | Select-Object -First 1).value
}

$traySelector = Get-TraySelector
$originalToggle = $null
try {
    Open-Options $traySelector
    $originalToggle = Read-ToggleValue
    if ($originalToggle -ne "Off") {
        & winapp ui invoke "CloseWhenInactiveToggle" -a $AppPid 2>$null | Out-Null
    }

    & winapp ui invoke "OptionsBackButton" -a $AppPid 2>$null | Out-Null
    & winapp ui wait-for "CodexDataState" -a $AppPid -t 15000 2>$null | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "Live Codex card did not appear." }

    & winapp ui invoke "HeaderRefreshButton" -a $AppPid 2>$null | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "Manual refresh was unavailable." }
    Start-Sleep -Seconds 3
    & winapp ui wait-for "CodexDataState" -a $AppPid -t 10000 2>$null | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "Live Codex card was lost after refresh." }

    Write-Output "Real packaged Codex UI: PASS"
}
finally {
    if ($originalToggle -eq "On" -and (Get-Process -Id $AppPid -ErrorAction SilentlyContinue)) {
        & winapp ui invoke "FooterOptionsButton" -a $AppPid 2>$null | Out-Null
        if ($LASTEXITCODE -eq 0) {
            & winapp ui wait-for "CloseWhenInactiveToggle" -a $AppPid -t 2000 2>$null | Out-Null
            if ((Read-ToggleValue) -ne "On") {
                & winapp ui invoke "CloseWhenInactiveToggle" -a $AppPid 2>$null | Out-Null
            }
        }
    }
}
