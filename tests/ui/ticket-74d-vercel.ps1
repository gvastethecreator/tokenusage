param([Parameter(Mandatory)][int]$AppPid)

$ErrorActionPreference = 'Stop'
$pass = 0
$fail = 0
$results = @()
$artifactDirectory = Join-Path $PSScriptRoot '..\..\artifacts\ticket-74d'
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

function Open-Options {
    $existing = winapp ui search 'VercelConnectionSection' -a $AppPid --json 2>$null |
        ConvertFrom-Json
    if ($existing.matchCount -gt 0) {
        winapp ui scroll-into-view 'VercelConnectionSection' -a $AppPid 2>$null | Out-Null
        return
    }

    winapp ui invoke 'FooterOptionsButton' -a $AppPid 2>$null | Out-Null
    winapp ui wait-for 'OptionsGeneralButton' -a $AppPid -t 5000 2>$null | Out-Null
    winapp ui invoke 'OptionsGeneralButton' -a $AppPid 2>$null | Out-Null
    if ($null -eq $script:closeState) {
        $script:closeState = (winapp ui get-value 'CloseWhenInactiveToggle' -a $AppPid --json 2>$null |
            ConvertFrom-Json).text
    }
    if ((winapp ui get-value 'CloseWhenInactiveToggle' -a $AppPid --json 2>$null | ConvertFrom-Json).text -ne 'Off') {
        winapp ui invoke 'CloseWhenInactiveToggle' -a $AppPid 2>$null | Out-Null
    }
    winapp ui invoke 'OptionsBackButton' -a $AppPid 2>$null | Out-Null
    winapp ui invoke 'OptionsProvidersButton' -a $AppPid 2>$null | Out-Null
    winapp ui invoke 'OptionsVercelButton' -a $AppPid 2>$null | Out-Null
    winapp ui wait-for 'VercelConnectionSection' -a $AppPid -t 5000 2>$null | Out-Null
    winapp ui scroll-into-view 'VercelConnectionSection' -a $AppPid 2>$null | Out-Null
}

function Close-Options {
    1..3 | ForEach-Object { winapp ui invoke 'OptionsBackButton' -a $AppPid 2>$null | Out-Null }
}

Open-Options

Test-Ui 'Disconnected state exposes risk and guarded input' {
    winapp ui wait-for 'VercelRiskInfo' -a $AppPid -t 5000
    winapp ui wait-for 'VercelApiKeyBox' -a $AppPid -t 5000
    winapp ui wait-for 'VercelConnectButton' -a $AppPid -p IsEnabled --value 'False' -t 5000
}

Test-Ui 'Consent and key enable connect' {
    winapp ui set-value 'VercelApiKeyBox' 'test-api-key' -a $AppPid
    winapp ui invoke 'VercelConsentCheckBox' -a $AppPid
    winapp ui wait-for 'VercelConnectButton' -a $AppPid -p IsEnabled --value 'True' -t 5000
}

Test-Ui 'Connect loads synthetic report through real composition' {
    winapp ui invoke 'VercelConnectButton' -a $AppPid
    winapp ui wait-for 'VercelDisconnectButton' -a $AppPid -t 10000
    winapp ui wait-for 'VercelConnectionStatus' -a $AppPid -t 10000
}

Test-Ui 'Provider status exposes independent capability rows' {
    winapp ui invoke 'OptionsBackButton' -a $AppPid
    winapp ui invoke 'OptionsProviderStatusButton' -a $AppPid
    foreach ($capability in 'Quota', 'Usage', 'Spend', 'Coverage') {
        winapp ui wait-for "ProviderStatus.vercel-ai-gateway.$capability" -a $AppPid -t 5000
    }
    winapp ui invoke 'OptionsBackButton' -a $AppPid
    winapp ui invoke 'OptionsVercelButton' -a $AppPid
}

winapp ui screenshot -a $AppPid -o (Join-Path $artifactDirectory '01-connected-options.png') 2>$null |
    Out-Null
Close-Options

Test-Ui 'Dashboard shows Vercel spend and token card' {
    winapp ui wait-for 'Provider.VercelAiGateway' -a $AppPid -t 10000
    winapp ui wait-for 'VercelGateway.TotalSpend30Days' -a $AppPid --value '12' --contains -t 5000
    winapp ui wait-for 'VercelGateway.InputTokens30Days' -a $AppPid --value '1' --contains -t 5000
    winapp ui scroll-into-view 'Provider.VercelAiGateway' -a $AppPid
}

winapp ui screenshot -a $AppPid -o (Join-Path $artifactDirectory '02-connected-dashboard.png') 2>$null |
    Out-Null
Open-Options

Test-Ui 'Disconnect clears credential state and report card' {
    winapp ui invoke 'VercelDisconnectButton' -a $AppPid
    winapp ui wait-for 'VercelApiKeyBox' -a $AppPid -t 10000
    winapp ui wait-for 'VercelConnectionStatus' -a $AppPid -t 10000
    Close-Options
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

if ($script:closeState -eq 'On') {
    winapp ui invoke 'OptionsBackButton' -a $AppPid 2>$null | Out-Null
    winapp ui invoke 'OptionsBackButton' -a $AppPid 2>$null | Out-Null
    winapp ui invoke 'OptionsGeneralButton' -a $AppPid 2>$null | Out-Null
    winapp ui invoke 'CloseWhenInactiveToggle' -a $AppPid 2>$null | Out-Null
}

Write-Host "Passed: $pass | Failed: $fail"
$results | Where-Object status -eq 'FAIL' | ForEach-Object {
    Write-Host "FAIL: $($_.name) - $($_.detail)"
}
if ($fail -gt 0) { exit 1 }
