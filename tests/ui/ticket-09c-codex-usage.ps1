param(
    [Parameter(Mandatory)] [int]$AppPid,
    [string]$ArtifactDirectory = "artifacts\ticket-09c"
)

$ErrorActionPreference = "Stop"
$results = @()
$explorerPid = Get-Process explorer | Select-Object -First 1 -ExpandProperty Id
New-Item -ItemType Directory -Force -Path $ArtifactDirectory | Out-Null

function Test-Ui([string]$Name, [scriptblock]$Body) {
    try {
        & $Body
        $script:results += @{ name = $Name; status = "PASS" }
    }
    catch {
        $script:results += @{ name = $Name; status = "FAIL"; detail = "$_" }
    }
}

function Get-TraySelector {
    $deadline = [DateTime]::UtcNow.AddSeconds(3)
    do {
        $search = & winapp ui search "TokenUsage" -a $explorerPid --json 2>$null | ConvertFrom-Json
        $match = $search.matches | Where-Object type -eq "Button" | Select-Object -First 1
        if (-not $match) { Start-Sleep -Milliseconds 100 }
    } while (-not $match -and [DateTime]::UtcNow -lt $deadline)
    if (-not $match) { throw "Tray button not found." }
    return $match.selector
}

function Show-Options {
    $active = Get-Process WOpenUsage.App -ErrorAction SilentlyContinue |
        Sort-Object StartTime -Descending |
        Select-Object -First 1
    if (-not $active) { throw "App process not found." }
    $script:AppPid = $active.Id
    & winapp ui invoke "FooterOptionsButton" -a $AppPid 2>$null | Out-Null
    if ($LASTEXITCODE -ne 0) {
        $selector = Get-TraySelector
        $menu = $null
        foreach ($attempt in 1..3) {
            & winapp ui click $selector -a $explorerPid --right 2>$null | Out-Null
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
        if (-not $menu) { throw "Options menu not found." }
        & winapp ui invoke "2" -w $menu.hwnd 2>$null | Out-Null
        if ($LASTEXITCODE -ne 0) { throw "Options menu command failed." }
    }
    Wait-Id "OptionsBackButton"
}

function Invoke-App([string]$Id) {
    & winapp ui invoke $Id -a $AppPid 2>$null | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "Could not invoke $Id." }
}

function Wait-Id([string]$Id, [int]$Timeout = 3000) {
    & winapp ui wait-for $Id -a $AppPid -t $Timeout 2>$null | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "$Id did not appear." }
}

function Wait-ToggleState([string]$Id, [string]$State) {
    & winapp ui wait-for $Id -a $AppPid -p ToggleState --value $State -t 2000 2>$null | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "$Id did not reach toggle state $State." }
}

function Assert-Text([string]$Text, [int]$Timeout = 3000) {
    $deadline = [DateTime]::UtcNow.AddMilliseconds($Timeout)
    do {
        $search = & winapp ui search $Text -a $AppPid --json 2>$null | ConvertFrom-Json
        if ($search.matchCount -lt 1) { Start-Sleep -Milliseconds 100 }
    } while ($search.matchCount -lt 1 -and [DateTime]::UtcNow -lt $deadline)
    if ($search.matchCount -lt 1) { throw "Text '$Text' not found." }
}

function Select-Scenario([int]$Offset) {
    & winapp ui focus "SampleScenarioCombo" -a $AppPid 2>$null | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "Scenario combo not found." }
    $keyboard = New-Object -ComObject WScript.Shell
    $keyboard.SendKeys("{HOME}")
    foreach ($step in 1..$Offset) { $keyboard.SendKeys("{DOWN}") }
    $keyboard.SendKeys("{ENTER}")
    Start-Sleep -Milliseconds 1400
}

function Open-SampleScenario([int]$Offset) {
    Show-Options
    & winapp ui wait-for "CloseWhenInactiveToggle" -a $AppPid --value "Off" -t 100 2>$null | Out-Null
    if ($LASTEXITCODE -ne 0) { Invoke-App "CloseWhenInactiveToggle" }
    Wait-Id "SampleModeToggle"
    & winapp ui wait-for "SampleModeToggle" -a $AppPid --value "On" -t 100 2>$null | Out-Null
    if ($LASTEXITCODE -ne 0) { Invoke-App "SampleModeToggle" }
    Select-Scenario $Offset
    Invoke-App "OptionsBackButton"
    Start-Sleep -Milliseconds 1400
    Wait-Id "SampleProvider.Codex.SecondaryMetrics" 5000
    Wait-ToggleState "SampleProvider.Codex.SecondaryMetrics" "Off"
    Invoke-App "SampleProvider.Codex.SecondaryMetrics"
    Wait-ToggleState "SampleProvider.Codex.SecondaryMetrics" "On"
}

$ready = $false
foreach ($attempt in 1..4) {
    try {
        Show-Options
        & winapp ui wait-for "CloseWhenInactiveToggle" -a $AppPid --value "Off" -t 100 2>$null | Out-Null
        if ($LASTEXITCODE -ne 0) { Invoke-App "CloseWhenInactiveToggle" }
        Invoke-App "OptionsBackButton"
        $ready = $true
        break
    }
    catch {
        Start-Sleep -Milliseconds 500
    }
}
if (-not $ready) { throw "The tray surface did not become ready for UI proof." }

Test-Ui "Provider details expose source and observation data" {
    Open-SampleScenario 0
    $details = & winapp ui get-property "SampleProvider.Codex.Details" -a $AppPid -p HelpText --json 2>$null |
        ConvertFrom-Json
    if ($details.properties.HelpText -notmatch "Datos de muestra|Sample data") {
        throw "Provider details did not expose focus-accessible help text."
    }

    $shell = New-Object -ComObject WScript.Shell
    $null = $shell.AppActivate($AppPid)
    Start-Sleep -Milliseconds 100
    & winapp ui click "SampleProvider.Codex.Details" -a $AppPid 2>$null | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "Provider details button could not open its flyout." }
    Start-Sleep -Milliseconds 250
    Wait-Id "SampleProvider.Codex.Details.Source"
    Wait-Id "SampleProvider.Codex.Details.Observed"
    & winapp ui wait-for "SampleProvider.Codex.Details.Source" -a $AppPid --value "Datos de muestra" --contains -t 1000 2>$null | Out-Null
    if ($LASTEXITCODE -ne 0) {
        & winapp ui wait-for "SampleProvider.Codex.Details.Source" -a $AppPid --value "Sample data" --contains -t 1000 2>$null | Out-Null
    }
    if ($LASTEXITCODE -ne 0) { throw "Provider source was not visible in the details flyout." }
    & winapp ui click "SampleProvider.Codex.Details" -a $AppPid 2>$null | Out-Null
}

Test-Ui "Usage rows and pace expose stable UIA" {
    Open-SampleScenario 0
    Wait-Id "CodexUsage.Today" 5000
    Wait-Id "CodexUsage.Last30Days" 5000
    & winapp ui wait-for "CodexPace.Session" -a $AppPid --value "74%" --contains -t 5000 2>$null | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "Normal session pace did not render." }
    & winapp ui screenshot -a $AppPid -o (Join-Path $ArtifactDirectory "01-usage-surface.png") 2>$null | Out-Null
}

Test-Ui "Near limit: caution pace" {
    Open-SampleScenario 1
    $nearLimitUsage = & winapp ui get-property "CodexUsage.Today" -a $AppPid --json 2>$null |
        ConvertFrom-Json
    if ($nearLimitUsage.properties.Name -notmatch "490.*tokens") {
        throw "Near-limit usage did not render."
    }
    & winapp ui wait-for "CodexPace.Session" -a $AppPid --value "135%" --contains -t 5000 2>$null | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "Near-limit pace did not render." }
    & winapp ui screenshot -a $AppPid -o (Join-Path $ArtifactDirectory "02-near-limit.png") 2>$null | Out-Null
}

Test-Ui "Partial: zero and missing stay distinct" {
    Open-SampleScenario 2
    & winapp ui wait-for "CodexUsage.Today" -a $AppPid --value "0 tokens" --contains -t 5000 2>$null | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "Observed zero did not render." }
    Wait-Id "CodexUsage.Yesterday" 5000
    & winapp ui screenshot -a $AppPid -o (Join-Path $ArtifactDirectory "03-partial.png") 2>$null | Out-Null
}

Test-Ui "Interactive controls keep AutomationIds" {
    Show-Options
    $tree = & winapp ui inspect -a $AppPid --interactive --json 2>$null | ConvertFrom-Json
    $controls = @($tree.windows.elements | Where-Object type -match "Button|ToggleSwitch|ComboBox")
    $missing = @($controls | Where-Object { -not $_.automationId })
    if ($missing.Count -gt 0) { throw "Missing AutomationId: $(($missing.name) -join ', ')" }
}

Test-Ui "Session controls return to defaults" {
    & winapp ui wait-for "SampleModeToggle" -a $AppPid --value "On" -t 1000 2>$null | Out-Null
    if ($LASTEXITCODE -eq 0) { Invoke-App "SampleModeToggle" }
    & winapp ui wait-for "CloseWhenInactiveToggle" -a $AppPid --value "Off" -t 1000 2>$null | Out-Null
    if ($LASTEXITCODE -eq 0) { Invoke-App "CloseWhenInactiveToggle" }
}

$results | ConvertTo-Json -Depth 4 | Set-Content (Join-Path $ArtifactDirectory "ui-results.json")
$failed = @($results | Where-Object status -eq "FAIL")
Write-Output "Passed: $($results.Count - $failed.Count) | Failed: $($failed.Count)"
$failed | ForEach-Object { Write-Output "FAIL: $($_.name) - $($_.detail)" }
if ($failed.Count -gt 0) { exit 1 }
