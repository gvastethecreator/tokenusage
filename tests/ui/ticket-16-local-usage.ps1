param([Parameter(Mandatory)][int]$AppPid)

$ErrorActionPreference = 'Stop'
$pass = 0
$fail = 0
$results = @()
$explorerPid = Get-Process explorer | Select-Object -First 1 -ExpandProperty Id

function Get-TraySelector {
    $deadline = [DateTime]::UtcNow.AddSeconds(5)
    do {
        $search = winapp ui search 'TokenUsage' -a $explorerPid --json 2>$null |
            ConvertFrom-Json
        $match = $search.matches | Where-Object type -eq 'Button' | Select-Object -First 1
        if (-not $match) { Start-Sleep -Milliseconds 100 }
    } while (-not $match -and [DateTime]::UtcNow -lt $deadline)
    if (-not $match) { throw 'Tray button not found.' }
    return $match.selector
}

function Open-Options([string]$TraySelector) {
    $menu = $null
    foreach ($attempt in 1..3) {
        winapp ui click $TraySelector -a $explorerPid --right 2>$null | Out-Null
        $deadline = [DateTime]::UtcNow.AddSeconds(1)
        do {
            Start-Sleep -Milliseconds 100
            $json = winapp ui list-windows -a $AppPid --json 2>$null
            if (-not [string]::IsNullOrWhiteSpace($json)) {
                $menu = @($json | ConvertFrom-Json) |
                    Where-Object className -eq '#32768' |
                    Select-Object -First 1
            }
        } while (-not $menu -and [DateTime]::UtcNow -lt $deadline)
        if ($menu) { break }
    }
    if (-not $menu) { throw 'Tray menu not found.' }
    winapp ui invoke '2' -w $menu.hwnd 2>$null | Out-Null
    winapp ui wait-for 'OptionsGeneralButton' -a $AppPid -t 3000 2>$null | Out-Null
    winapp ui invoke 'OptionsGeneralButton' -a $AppPid 2>$null | Out-Null
    winapp ui wait-for 'CloseWhenInactiveToggle' -a $AppPid -t 3000 2>$null | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Options did not open.' }
}

function Read-CloseToggle {
    $result = winapp ui search 'CloseWhenInactiveToggle' -a $AppPid --json 2>$null |
        ConvertFrom-Json
    return ($result.matches | Select-Object -First 1).value
}

function Test-Ui {
    param([string]$Name, [scriptblock]$Action)

    try {
        & $Action 2>&1 | Out-Null
        if ($LASTEXITCODE -ne 0) {
            throw "winapp exited with code $LASTEXITCODE"
        }

        $script:pass++
        $script:results += @{ name = $Name; status = 'PASS' }
    }
    catch {
        $script:fail++
        $script:results += @{ name = $Name; status = 'FAIL'; detail = "$_" }
    }
}

$existingCard = winapp ui search 'UsageProductCard' -a $AppPid --json 2>$null |
    ConvertFrom-Json
$originalCloseToggle = $null
if ($existingCard.matchCount -eq 0) {
    $traySelector = Get-TraySelector
    Open-Options $traySelector
    $originalCloseToggle = Read-CloseToggle
    if ($originalCloseToggle -ne 'Off') {
        winapp ui invoke 'CloseWhenInactiveToggle' -a $AppPid 2>$null | Out-Null
    }
    winapp ui invoke 'OptionsBackButton' -a $AppPid 2>$null | Out-Null
    winapp ui invoke 'OptionsBackButton' -a $AppPid 2>$null | Out-Null
}

Test-Ui 'Local usage card appears' {
    winapp ui wait-for 'UsageProductCard' -a $AppPid -t 10000
}
$usageDetails = winapp ui search 'UsageProductCard.DetailsToggle' -a $AppPid --json 2>$null |
    ConvertFrom-Json
if (($usageDetails.matches | Select-Object -First 1).toggleState -ne 'on') {
    winapp ui invoke 'UsageProductCard.DetailsToggle' -a $AppPid 2>$null | Out-Null
}
winapp ui wait-for 'UsageProductCard.ReportedCost' -a $AppPid -t 3000 2>$null | Out-Null
Test-Ui 'SQLite origin is visible' {
    winapp ui wait-for 'UsageProductCard.DataOrigin' -a $AppPid --value 'SQLite' --contains -t 3000
}
Test-Ui 'Reported cost remains separate' {
    $value = winapp ui get-value 'UsageProductCard.ReportedCost' -a $AppPid --json 2>$null |
        ConvertFrom-Json
    if ($value.text -notmatch '\$1[\.,]84 USD') {
        throw "Unexpected reported cost: $($value.text)"
    }
}
Test-Ui 'Estimated cost remains separate' {
    $value = winapp ui get-value 'UsageProductCard.EstimatedCost' -a $AppPid --json 2>$null |
        ConvertFrom-Json
    if ($value.text -notmatch '\$0[\.,]62 USD') {
        throw "Unexpected estimated cost: $($value.text)"
    }
}
Test-Ui 'Unpriced usage remains visible' {
    $value = winapp ui get-value 'UsageProductCard.UnpricedUsage' -a $AppPid --json 2>$null |
        ConvertFrom-Json
    if ($value.text -match 'Sin datos|No data') {
        throw "Unpriced usage was shown as missing: $($value.text)"
    }
}

$elements = (winapp ui inspect -a $AppPid --interactive --json 2>$null |
    ConvertFrom-Json).elements
$missingIds = @($elements | Where-Object {
    $_.type -match 'Button|TextBox|ComboBox|CheckBox|ToggleSwitch' -and
    $_.name -notmatch 'Minimize|Maximize|Close|System' -and
    -not $_.automationId
})
if ($missingIds.Count -eq 0) {
    $pass++
    $results += @{ name = 'Interactive controls expose AutomationIds'; status = 'PASS' }
}
else {
    $fail++
    $results += @{
        name = 'Interactive controls expose AutomationIds'
        status = 'FAIL'
        detail = (($missingIds | ForEach-Object { "$($_.type) '$($_.name)'" }) -join ', ')
    }
}

$artifactDirectory = Join-Path $PSScriptRoot '..\..\artifacts\ticket-16'
New-Item -ItemType Directory -Force -Path $artifactDirectory | Out-Null
$detailsSource = winapp ui search 'Provider.Codex.Details.Source' -a $AppPid --json 2>$null |
    ConvertFrom-Json
if ($detailsSource.matchCount -gt 0) {
    winapp ui invoke 'Provider.Codex.Details' -a $AppPid 2>$null | Out-Null
}
winapp ui scroll 'BodyScrollViewer' -a $AppPid --to top 2>$null | Out-Null
winapp ui screenshot -a $AppPid -o (Join-Path $artifactDirectory 'local-usage-card.png') 2>$null |
    Out-Null
$results | ConvertTo-Json -Depth 4 |
    Set-Content (Join-Path $artifactDirectory 'ui-results.json')

if ($originalCloseToggle -eq 'On') {
    winapp ui invoke 'FooterOptionsButton' -a $AppPid 2>$null | Out-Null
    if ($LASTEXITCODE -eq 0) {
        winapp ui invoke 'OptionsGeneralButton' -a $AppPid 2>$null | Out-Null
        if ((Read-CloseToggle) -ne 'On') {
            winapp ui invoke 'CloseWhenInactiveToggle' -a $AppPid 2>$null | Out-Null
        }
    }
}

Write-Host "Passed: $pass | Failed: $fail"
$results | Where-Object { $_.status -eq 'FAIL' } | ForEach-Object {
    Write-Host "FAIL: $($_.name) - $($_.detail)"
}

if ($fail -gt 0) {
    exit 1
}
