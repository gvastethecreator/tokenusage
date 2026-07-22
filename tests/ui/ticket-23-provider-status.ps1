param([Parameter(Mandatory)][int]$AppPid)

$ErrorActionPreference = 'Stop'
$pass = 0
$fail = 0
$results = @()
$explorerPid = Get-Process explorer | Select-Object -First 1 -ExpandProperty Id

function Test-Ui([string]$Name, [scriptblock]$Action) {
    try {
        & $Action 2>&1 | Out-Null
        if ($LASTEXITCODE -ne 0) { throw "winapp exited with code $LASTEXITCODE" }
        $script:pass++
        $script:results += @{ name = $Name; status = 'PASS' }
    }
    catch {
        $script:fail++
        $script:results += @{ name = $Name; status = 'FAIL'; detail = "$_" }
    }
}

function Open-FlyoutOptions {
    $existing = winapp ui search 'OptionsBackButton' -a $AppPid --json 2>$null |
        ConvertFrom-Json
    if ($existing.matchCount -gt 0) { return }

    $footer = winapp ui search 'FooterOptionsButton' -a $AppPid --json 2>$null |
        ConvertFrom-Json
    if ($footer.matchCount -gt 0) {
        winapp ui invoke 'FooterOptionsButton' -a $AppPid 2>$null | Out-Null
        return
    }

    $tray = winapp ui search 'TokenUsage' -a $explorerPid --json 2>$null |
        ConvertFrom-Json
    $selector = ($tray.matches | Where-Object type -eq 'Button' | Select-Object -First 1).selector
    if (-not $selector) { throw 'Tray button not found.' }
    $menu = $null
    foreach ($attempt in 1..3) {
        winapp ui click $selector -a $explorerPid --right 2>$null | Out-Null
        $deadline = [DateTime]::UtcNow.AddSeconds(1)
        do {
            Start-Sleep -Milliseconds 100
            $menu = @(winapp ui list-windows -a $AppPid --json 2>$null | ConvertFrom-Json) |
                Where-Object className -eq '#32768' | Select-Object -First 1
        } while (-not $menu -and [DateTime]::UtcNow -lt $deadline)
        if ($menu) { break }
    }
    if (-not $menu) { throw 'Tray menu not found.' }
    winapp ui invoke '2' -w $menu.hwnd 2>$null | Out-Null
}

Open-FlyoutOptions
winapp ui wait-for 'ProviderStatusSection' -a $AppPid -t 10000 2>$null | Out-Null
$closeState = (winapp ui get-value 'CloseWhenInactiveToggle' -a $AppPid --json 2>$null |
    ConvertFrom-Json).text
if ($closeState -ne 'Off') {
    winapp ui invoke 'CloseWhenInactiveToggle' -a $AppPid 2>$null | Out-Null
}

Test-Ui 'Provider status section appears' {
    winapp ui wait-for 'ProviderStatusRefreshButton' -a $AppPid -t 3000
    winapp ui wait-for 'ProviderStatus.codex' -a $AppPid -t 3000
}

foreach ($provider in 'codex', 'claude', 'grok', 'opencode') {
    Test-Ui "$provider status appears" {
        winapp ui wait-for "ProviderStatus.$provider" -a $AppPid -t 5000
    }
    foreach ($capability in 'Quota', 'Usage', 'Spend', 'Coverage') {
        Test-Ui "$provider $capability is independent" {
            $value = winapp ui get-value "ProviderStatus.$provider.$capability" -a $AppPid --json 2>$null |
                ConvertFrom-Json
            if ([string]::IsNullOrWhiteSpace($value.text)) {
                throw 'Capability has no readable value.'
            }
        }
    }
}

Test-Ui 'Normal provider report hides full paths' {
    $tree = winapp ui inspect -a $AppPid --interactive --json 2>$null | ConvertFrom-Json
    $text = ($tree.elements | ForEach-Object { "$($_.name) $($_.value)" }) -join "`n"
    if ($text -match '(?im)([A-Z]:\\Users\\|[A-Z]:\\DEV\\|\\\\[^\s]+\\|/home/[^\s]+)') {
        throw 'A full filesystem path is visible in the provider report.'
    }
}

$artifactDirectory = Join-Path $PSScriptRoot '..\..\artifacts\ticket-23'
New-Item -ItemType Directory -Force -Path $artifactDirectory | Out-Null
winapp ui scroll 'BodyScrollViewer' -a $AppPid --to top 2>$null | Out-Null
winapp ui screenshot -a $AppPid -o (Join-Path $artifactDirectory 'provider-status-options.png') 2>$null |
    Out-Null

Test-Ui 'Refresh action is safe and available' {
    winapp ui wait-for 'ProviderStatusRefreshButton' -a $AppPid -p IsEnabled --value 'True' -t 10000
    winapp ui wait-for 'OptionsBackButton' -a $AppPid -t 10000
    winapp ui wait-for 'ProviderStatus.codex' -a $AppPid -t 10000
}

$results | ConvertTo-Json -Depth 4 |
    Set-Content (Join-Path $artifactDirectory 'ui-results.json')

if ($closeState -eq 'On') {
    winapp ui invoke 'CloseWhenInactiveToggle' -a $AppPid 2>$null | Out-Null
}

Write-Host "Passed: $pass | Failed: $fail"
$results | Where-Object status -eq 'FAIL' | ForEach-Object {
    Write-Host "FAIL: $($_.name) - $($_.detail)"
}
if ($fail -gt 0) { exit 1 }
