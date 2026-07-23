param(
    [Parameter(Mandatory)]
    [int]$AppPid,
    [Parameter(Mandatory)]
    [ValidateSet("Configure", "Verify")]
    [string]$Phase,
    [Parameter(Mandatory)]
    [string]$AppearancePath,
    [string]$ArtifactDirectory = "artifacts\ticket-13"
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

function Wait-ForElement([string]$Selector, [int]$Timeout = 5000) {
    winapp ui wait-for $Selector -a $AppPid -t $Timeout 2>$null | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "Element '$Selector' did not appear." }
}

function Wait-ForToggle([string]$Selector, [string]$Value) {
    winapp ui wait-for $Selector -a $AppPid --value $Value -t 5000 2>$null | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "Toggle '$Selector' did not become '$Value'." }
}

function Get-ControlValue([string]$Selector) {
    $value = winapp ui get-value $Selector -a $AppPid 2>$null
    if ($LASTEXITCODE -ne 0) { throw "Could not read '$Selector'." }
    return "$value".Trim()
}

function Assert-OneOf([string]$Actual, [string[]]$Expected, [string]$Label) {
    if ($Expected -notcontains $Actual) {
        throw "$Label was '$Actual'; expected one of: $($Expected -join ', ')."
    }
}

function Send-KeyTo([string]$Selector, [string]$Keys) {
    $keyboard = New-Object -ComObject WScript.Shell
    if (-not $keyboard.AppActivate($AppPid)) { throw "Could not activate process $AppPid." }
    Start-Sleep -Milliseconds 100
    winapp ui focus $Selector -a $AppPid 2>$null | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "Could not focus '$Selector'." }
    Start-Sleep -Milliseconds 100
    $keyboard.SendKeys($Keys)
    Start-Sleep -Milliseconds 150
}

function Open-Appearance {
    Wait-ForElement "SampleSpendDonut" 10000
    Invoke-Element "FooterOptionsButton"
    Wait-ForElement "AppearanceExpander"
    winapp ui scroll-into-view "AppearanceExpander" -a $AppPid 2>$null | Out-Null
    Invoke-Element "AppearanceExpander"
    foreach ($selector in @(
        "AppearanceThemeSelector",
        "AppearanceDensitySelector",
        "AppearanceTransparencyToggle",
        "AppearanceUsageSelector",
        "AppearanceResetTimeSelector")) {
        Wait-ForElement $selector
    }
}

function Get-AppearanceDocument {
    if (-not (Test-Path -LiteralPath $AppearancePath -PathType Leaf)) {
        throw "Appearance file was not created at '$AppearancePath'."
    }

    return Get-Content -LiteralPath $AppearancePath -Raw | ConvertFrom-Json
}

function Test-ExpectedDocument($Document) {
    return $Document.schemaVersion -eq 1 `
        -and $Document.theme -eq "dark" `
        -and $Document.density -eq "compact" `
        -and $Document.increaseTransparency -eq $true `
        -and $Document.usageDisplay -eq "used" `
        -and $Document.resetTimeDisplay -eq "exact"
}

function Wait-ForExpectedDocument {
    $deadline = [DateTime]::UtcNow.AddSeconds(5)
    do {
        Start-Sleep -Milliseconds 100
        try {
            $document = Get-AppearanceDocument
            if (Test-ExpectedDocument $document) { return $document }
        }
        catch { }
    } while ([DateTime]::UtcNow -lt $deadline)

    $actual = if (Test-Path -LiteralPath $AppearancePath) {
        Get-Content -LiteralPath $AppearancePath -Raw -ErrorAction SilentlyContinue
    } else {
        "<missing>"
    }
    throw "Appearance settings did not reach the expected state. Actual: $actual"
}

function Assert-AnyVisibleText([string[]]$Candidates) {
    foreach ($candidate in $Candidates) {
        $search = winapp ui search $candidate -a $AppPid --json 2>$null | ConvertFrom-Json
        if ($LASTEXITCODE -eq 0 -and $search.matchCount -gt 0) { return }
    }

    throw "None of the expected texts were visible: $($Candidates -join ', ')."
}

function Assert-NoVisibleText([string[]]$Candidates) {
    foreach ($candidate in $Candidates) {
        $search = winapp ui search $candidate -a $AppPid --json 2>$null | ConvertFrom-Json
        if ($LASTEXITCODE -eq 0 -and $search.matchCount -gt 0) {
            throw "Relative reset text remained visible in exact mode: '$candidate'."
        }
    }
}

function Assert-RestoredControls {
    Assert-OneOf (Get-ControlValue "AppearanceThemeSelector") @("Dark", "Oscuro") "Theme"
    Assert-OneOf (Get-ControlValue "AppearanceDensitySelector") @("Compact", "Compacta") "Density"
    Assert-OneOf (Get-ControlValue "AppearanceUsageSelector") @("Used", "Usado") "Usage display"
    Assert-OneOf (Get-ControlValue "AppearanceResetTimeSelector") @("Exact", "Exacta") "Reset display"
    Wait-ForToggle "AppearanceTransparencyToggle" "On"
}

Test-Ui "Appearance controls open" { Open-Appearance }

if ($Phase -eq "Configure") {
    Test-Ui "Defaults are coherent" {
        Assert-OneOf (Get-ControlValue "AppearanceThemeSelector") @("System", "Sistema") "Theme"
        Assert-OneOf (Get-ControlValue "AppearanceDensitySelector") @("Regular", "Predeterminada") "Density"
        Assert-OneOf (Get-ControlValue "AppearanceUsageSelector") @("Remaining", "Restante") "Usage display"
        Assert-OneOf (Get-ControlValue "AppearanceResetTimeSelector") @("Relative", "Relativa") "Reset display"
        Wait-ForToggle "AppearanceTransparencyToggle" "Off"
    }

    Test-Ui "Keyboard updates every appearance setting" {
        foreach ($selector in @(
            "AppearanceDensitySelector",
            "AppearanceUsageSelector",
            "AppearanceResetTimeSelector")) {
            Send-KeyTo $selector "{END}{ENTER}"
        }
        Invoke-Element "AppearanceTransparencyToggle"
        Wait-ForToggle "AppearanceTransparencyToggle" "On"
        Send-KeyTo "AppearanceThemeSelector" "{END}"
        $null = Wait-ForExpectedDocument
        Assert-RestoredControls
        winapp ui screenshot -a $AppPid -o (Join-Path $ArtifactDirectory "01-configured-options.png") 2>$null | Out-Null
    }

    Test-Ui "Used and exact formats reach the dashboard" {
        Invoke-Element "OptionsBackButton"
        Wait-ForElement "SampleSpendDonut"
        Assert-AnyVisibleText @("42% used", "42% usado")
        Assert-AnyVisibleText @("Resets", "Se reinicia el")
        Assert-NoVisibleText @("Resets in", "Reinicia en")
        winapp ui screenshot -a $AppPid -o (Join-Path $ArtifactDirectory "02-configured-dashboard.png") 2>$null | Out-Null
    }
}
else {
    Test-Ui "Persisted document survives restart" {
        $document = Get-AppearanceDocument
        if (-not (Test-ExpectedDocument $document)) {
            throw "Persisted appearance document does not contain the expected settings."
        }
    }

    Test-Ui "Controls restore persisted values" {
        Assert-RestoredControls
        winapp ui screenshot -a $AppPid -o (Join-Path $ArtifactDirectory "03-restored-options.png") 2>$null | Out-Null
    }

    Test-Ui "Restored formats reach the dashboard" {
        Invoke-Element "OptionsBackButton"
        Wait-ForElement "SampleSpendDonut"
        Assert-AnyVisibleText @("42% used", "42% usado")
        Assert-AnyVisibleText @("Resets", "Se reinicia el")
        Assert-NoVisibleText @("Resets in", "Reinicia en")
        winapp ui screenshot -a $AppPid -o (Join-Path $ArtifactDirectory "04-restored-dashboard.png") 2>$null | Out-Null
    }
}

[pscustomobject]@{
    phase = $Phase
    passed = $passed
    failed = $failed
    results = $results
} | ConvertTo-Json -Depth 6 -Compress

if ($failed -gt 0) { exit 1 }
