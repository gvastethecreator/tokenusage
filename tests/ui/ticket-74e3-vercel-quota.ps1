param([Parameter(Mandatory)][int]$AppPid)

$ErrorActionPreference = 'Stop'
$pass = 0
$fail = 0
$results = @()
$artifactDirectory = Join-Path $PSScriptRoot '..\..\artifacts\ticket-74e3'
$explorerPid = Get-Process explorer | Select-Object -First 1 -ExpandProperty Id
New-Item -ItemType Directory -Force -Path $artifactDirectory | Out-Null

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

function Get-AppWindows {
    $json = winapp ui list-windows -a $AppPid --json 2>$null
    if ([string]::IsNullOrWhiteSpace($json)) { return @() }
    return @($json | ConvertFrom-Json)
}

function Open-OptionsFromTray {
    $search = winapp ui search 'TokenUsage' -a $explorerPid --json 2>$null |
        ConvertFrom-Json
    $trayButton = $search.matches |
        Where-Object type -eq 'Button' |
        Select-Object -First 1
    if (-not $trayButton) { throw 'TokenUsage tray button was not found.' }

    winapp ui click $trayButton.selector -a $explorerPid --right 2>$null | Out-Null
    $deadline = [DateTime]::UtcNow.AddSeconds(3)
    do {
        Start-Sleep -Milliseconds 100
        $menu = Get-AppWindows |
            Where-Object className -eq '#32768' |
            Select-Object -First 1
    } while (-not $menu -and [DateTime]::UtcNow -lt $deadline)

    if (-not $menu) { throw 'TokenUsage tray menu was not found.' }
    winapp ui invoke '2' -w $menu.hwnd 2>$null | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Options tray command failed.' }
}

function Open-Options {
    if ((Get-AppWindows).Count -eq 0) {
        Open-OptionsFromTray
        winapp ui wait-for 'VercelConnectionSection' -a $AppPid -t 5000 2>$null | Out-Null
        winapp ui scroll-into-view 'VercelConnectionSection' -a $AppPid 2>$null | Out-Null
        return
    }

    $existing = winapp ui search 'VercelConnectionSection' -a $AppPid --json 2>$null |
        ConvertFrom-Json
    if ($existing.matchCount -gt 0) {
        winapp ui scroll-into-view 'VercelConnectionSection' -a $AppPid 2>$null | Out-Null
        return
    }

    winapp ui invoke 'FooterOptionsButton' -a $AppPid 2>$null | Out-Null
    winapp ui wait-for 'VercelConnectionSection' -a $AppPid -t 5000 2>$null | Out-Null
    winapp ui scroll-into-view 'VercelConnectionSection' -a $AppPid 2>$null | Out-Null
}

Open-Options
$closeState = (winapp ui get-value 'CloseWhenInactiveToggle' -a $AppPid --json 2>$null |
    ConvertFrom-Json).text
if ($closeState -ne 'Off') {
    winapp ui invoke 'CloseWhenInactiveToggle' -a $AppPid 2>$null | Out-Null
}

Test-Ui 'Disconnected state exposes quota input and guarded action' {
    winapp ui wait-for 'VercelRiskInfo' -a $AppPid -t 5000
    winapp ui wait-for 'VercelApiKeyBox' -a $AppPid -t 5000
    winapp ui wait-for 'VercelKeyIdBox' -a $AppPid -t 5000
    winapp ui wait-for 'VercelConnectButton' -a $AppPid -p IsEnabled --value 'False' -t 5000
}

Test-Ui 'Invalid key ID stays local and blocks connect' {
    winapp ui set-value 'VercelApiKeyBox' 'test-api-key' -a $AppPid
    winapp ui set-value 'VercelKeyIdBox' 'api_key_id_wrong' -a $AppPid
    winapp ui invoke 'VercelConsentCheckBox' -a $AppPid
    winapp ui wait-for 'VercelKeyIdError' -a $AppPid -t 5000
    winapp ui wait-for 'VercelConnectButton' -a $AppPid -p IsEnabled --value 'False' -t 5000
}

Test-Ui 'Raw key ID and consent enable connect' {
    winapp ui set-value 'VercelKeyIdBox' 'key_ui-test-123' -a $AppPid
    winapp ui wait-for 'VercelConnectButton' -a $AppPid -p IsEnabled --value 'True' -t 5000
}

Test-Ui 'Connect loads synthetic report and quota through real composition' {
    winapp ui invoke 'VercelConnectButton' -a $AppPid
    winapp ui wait-for 'VercelDisconnectButton' -a $AppPid -t 10000
    winapp ui wait-for 'VercelConnectionStatus' -a $AppPid -t 10000
}

Test-Ui 'Provider status exposes independent capability rows' {
    foreach ($capability in 'Quota', 'Usage', 'Spend', 'Coverage') {
        winapp ui wait-for "ProviderStatus.vercel-ai-gateway.$capability" -a $AppPid -t 5000
    }
}

winapp ui screenshot -a $AppPid -o (Join-Path $artifactDirectory '01-connected-options.png') 2>$null |
    Out-Null
winapp ui invoke 'OptionsBackButton' -a $AppPid 2>$null | Out-Null

Test-Ui 'Dashboard shows spend, tokens and key budget' {
    winapp ui wait-for 'Provider.VercelAiGateway' -a $AppPid -t 10000
    winapp ui wait-for 'VercelGateway.TotalSpend30Days' -a $AppPid --value '12' --contains -t 5000
    winapp ui wait-for 'VercelGateway.InputTokens30Days' -a $AppPid --value '1' --contains -t 5000
    winapp ui wait-for 'VercelGateway.KeyBudgetState' -a $AppPid --value '6' --contains -t 5000
    winapp ui scroll-into-view 'Provider.VercelAiGateway' -a $AppPid
}

winapp ui screenshot -a $AppPid -o (Join-Path $artifactDirectory '02-connected-dashboard.png') 2>$null |
    Out-Null
Open-Options

Test-Ui 'Disconnect clears credential state and provider card' {
    winapp ui invoke 'VercelDisconnectButton' -a $AppPid
    winapp ui wait-for 'VercelApiKeyBox' -a $AppPid -t 10000
    winapp ui wait-for 'VercelConnectionStatus' -a $AppPid -t 10000
    winapp ui invoke 'OptionsBackButton' -a $AppPid
    winapp ui wait-for 'Provider.VercelAiGateway' -a $AppPid --gone -t 10000
}

Test-Ui 'Interactive controls keep AutomationIds' {
    Open-Options
    $tree = winapp ui inspect -a $AppPid --interactive --json 2>$null | ConvertFrom-Json
    $missing = @($tree.elements | Where-Object {
        $_.type -match 'Button|TextBox|CheckBox|ComboBox|ToggleSwitch' -and
        [string]::IsNullOrWhiteSpace($_.automationId)
    })
    if ($missing.Count -gt 0) {
        throw "Missing AutomationId: $(($missing.name) -join ', ')"
    }
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
