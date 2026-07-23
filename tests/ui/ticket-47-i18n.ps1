param(
    [Parameter(Mandatory)][int]$AppPid,
    [string[]]$ExpectedRestartArgument = @()
)

$ErrorActionPreference = 'Stop'
$script:CurrentPid = $AppPid
$pass = 0
$fail = 0
$results = @()
$artifactDirectory = Join-Path $PSScriptRoot '..\..\artifacts\ticket-47'
New-Item -ItemType Directory -Force -Path $artifactDirectory | Out-Null

function Test-Ui([string]$Name, [scriptblock]$Action) {
    try {
        & $Action
        $script:pass++
        $script:results += @{ name = $Name; status = 'PASS' }
    }
    catch {
        $script:fail++
        $script:results += @{ name = $Name; status = 'FAIL'; detail = "$_" }
    }
}

function Open-Options {
    $existing = winapp ui search 'LanguageSelector' -a $script:CurrentPid --json 2>$null |
        ConvertFrom-Json
    if ($existing.matchCount -gt 0) { return }

    winapp ui invoke 'FooterOptionsButton' -a $script:CurrentPid 2>$null | Out-Null
    winapp ui wait-for 'LanguageSelector' -a $script:CurrentPid -t 5000 2>$null | Out-Null
}

function Get-SelectedLanguage {
    Open-Options
    return (winapp ui get-value 'LanguageSelector' -a $script:CurrentPid --json 2>$null |
        ConvertFrom-Json).text
}

function Get-AllElements([object[]]$Nodes) {
    $items = @()
    foreach ($node in @($Nodes)) {
        if ($null -eq $node) { continue }
        $items += $node
        if ($node.children) {
            $items += Get-AllElements @($node.children)
        }
    }
    return $items
}

function Get-AppElements([switch]$Interactive) {
    $arguments = @('ui', 'inspect', '-a', "$script:CurrentPid", '-d', '12', '--json')
    if ($Interactive) { $arguments += '--interactive' }
    $tree = & winapp @arguments 2>$null | ConvertFrom-Json
    return @(Get-AllElements @($tree.windows | ForEach-Object { $_.elements }))
}

function Assert-UiTextContains([string]$Expected) {
    $text = (Get-AppElements | ForEach-Object { "$($_.name) $($_.value)" }) -join "`n"
    if (-not $text.Contains($Expected, [StringComparison]::Ordinal)) {
        throw "UI text does not contain '$Expected'."
    }
}

function Select-Language([string]$OptionName, [string]$ScreenshotName) {
    Open-Options
    winapp ui invoke 'LanguageSelector' -a $script:CurrentPid 2>$null | Out-Null
    Start-Sleep -Milliseconds 200
    $matches = winapp ui search $OptionName -a $script:CurrentPid --json 2>$null |
        ConvertFrom-Json
    $selector = ($matches.matches |
        Where-Object type -eq 'ListItem' |
        Select-Object -First 1 -ExpandProperty selector)
    if (-not $selector) { throw "Language option '$OptionName' was not found." }

    winapp ui invoke $selector -a $script:CurrentPid 2>$null | Out-Null
    winapp ui wait-for 'LanguageRestartButton' -a $script:CurrentPid -t 5000 2>$null | Out-Null
    winapp ui screenshot -a $script:CurrentPid -o (Join-Path $artifactDirectory $ScreenshotName) 2>$null |
        Out-Null
}

function Restart-CurrentApp {
    $oldPid = $script:CurrentPid
    $existingPids = @(Get-Process WOpenUsage.App -ErrorAction SilentlyContinue |
        Select-Object -ExpandProperty Id)

    & winapp ui invoke 'LanguageRestartButton' -a $oldPid 2>$null | Out-Null
    $deadline = [DateTime]::UtcNow.AddSeconds(20)
    $newProcess = $null
    do {
        Start-Sleep -Milliseconds 250
        $newProcess = Get-Process WOpenUsage.App -ErrorAction SilentlyContinue |
            Where-Object { $existingPids -notcontains $_.Id } |
            Sort-Object StartTime -Descending |
            Select-Object -First 1
    } while (-not $newProcess -and [DateTime]::UtcNow -lt $deadline)

    if (-not $newProcess) { throw 'Restarted TokenUsage process was not found.' }
    $script:CurrentPid = $newProcess.Id
    winapp ui wait-for 'FooterOptionsButton' -a $script:CurrentPid -t 10000 2>$null | Out-Null
}

function Switch-ToEnglish {
    $current = Get-SelectedLanguage
    if ($current -eq 'English (United States)') { return }
    if ($current -ne 'Español (España)') { throw "Unexpected current language '$current'." }

    Select-Language 'Inglés (Estados Unidos)' '01-spanish-restart-english.png'
    Restart-CurrentApp
    if ((Get-SelectedLanguage) -ne 'English (United States)') {
        throw 'English selection did not survive restart.'
    }
}

function Switch-ToSpanish {
    $current = Get-SelectedLanguage
    if ($current -eq 'Español (España)') { return }
    if ($current -ne 'English (United States)') { throw "Unexpected current language '$current'." }

    Select-Language 'Spanish (Spain)' '04-english-restart-spanish.png'
    Restart-CurrentApp
    if ((Get-SelectedLanguage) -ne 'Español (España)') {
        throw 'Spanish selection did not survive restart.'
    }
}

function Select-SampleScenarioByOffset([int]$DownCount) {
    Open-Options
    winapp ui focus 'SampleScenarioCombo' -a $script:CurrentPid 2>$null | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Sample scenario could not receive focus.' }

    $keyboard = New-Object -ComObject WScript.Shell
    $keyboard.SendKeys('{HOME}')
    if ($DownCount -gt 0) {
        $keyboard.SendKeys("{DOWN $DownCount}")
    }
    $keyboard.SendKeys('{ENTER}')
    Start-Sleep -Milliseconds 250
}

function Capture-ErrorState([string]$FileName, [string]$StateText, [string]$NoticeText) {
    Select-SampleScenarioByOffset 4
    winapp ui invoke 'OptionsBackButton' -a $script:CurrentPid 2>$null | Out-Null
    winapp ui wait-for 'SampleStateError' -a $script:CurrentPid -t 5000 2>$null | Out-Null
    Assert-UiTextContains $StateText
    Assert-UiTextContains $NoticeText
    winapp ui scroll 'BodyScrollViewer' -a $script:CurrentPid --to top 2>$null | Out-Null
    winapp ui screenshot -a $script:CurrentPid -o (Join-Path $artifactDirectory $FileName) 2>$null |
        Out-Null

    Select-SampleScenarioByOffset 0
    winapp ui invoke 'OptionsBackButton' -a $script:CurrentPid 2>$null | Out-Null
    winapp ui wait-for 'SampleStateFresh' -a $script:CurrentPid -t 5000 2>$null | Out-Null
}

function Capture-Sample([string]$FileName) {
    Open-Options
    winapp ui invoke 'OptionsBackButton' -a $script:CurrentPid 2>$null | Out-Null
    winapp ui wait-for 'HeaderRefreshButton' -a $script:CurrentPid -t 5000 2>$null | Out-Null
    winapp ui scroll 'BodyScrollViewer' -a $script:CurrentPid --to top 2>$null | Out-Null
    winapp ui screenshot -a $script:CurrentPid -o (Join-Path $artifactDirectory $FileName) 2>$null |
        Out-Null
}

function Get-TrayMenuNames {
    $explorerPid = Get-Process explorer | Select-Object -First 1 -ExpandProperty Id
    $tray = winapp ui search 'TokenUsage' -a $explorerPid --json 2>$null | ConvertFrom-Json
    $selector = ($tray.matches | Where-Object type -eq 'Button' |
        Select-Object -First 1 -ExpandProperty selector)
    if (-not $selector) { throw 'TokenUsage tray button was not found.' }

    winapp ui click $selector -a $explorerPid --right 2>$null | Out-Null
    $deadline = [DateTime]::UtcNow.AddSeconds(3)
    $menu = $null
    do {
        Start-Sleep -Milliseconds 100
        $menu = @(winapp ui list-windows -a $script:CurrentPid --json 2>$null | ConvertFrom-Json) |
            Where-Object className -eq '#32768' |
            Select-Object -First 1
    } while (-not $menu -and [DateTime]::UtcNow -lt $deadline)
    if (-not $menu) { throw 'TokenUsage tray menu was not found.' }

    $tree = winapp ui inspect -w $menu.hwnd -d 4 --json 2>$null | ConvertFrom-Json
    $names = @(Get-AllElements @($tree.windows | ForEach-Object { $_.elements }) |
        Where-Object name |
        Select-Object -ExpandProperty name -Unique)
    winapp ui invoke '2' -w $menu.hwnd 2>$null | Out-Null
    return $names
}

function Assert-CurrentProcessArguments {
    foreach ($expected in $ExpectedRestartArgument) {
        $commandLine = (Get-CimInstance Win32_Process -Filter "ProcessId = $script:CurrentPid").CommandLine
        if ([string]::IsNullOrWhiteSpace($commandLine) -or
            -not $commandLine.Contains($expected, [StringComparison]::Ordinal)) {
            throw "Restarted process command line does not contain '$expected'."
        }
    }
}

$initialLanguage = Get-SelectedLanguage

Test-Ui 'Initial language is supported' {
    if ($initialLanguage -notin @('English (United States)', 'Español (España)')) {
        throw "Unsupported initial language '$initialLanguage'."
    }
}

Test-Ui 'English selection persists after restart' { Switch-ToEnglish }
Test-Ui 'Restart keeps every supplied debug harness argument' { Assert-CurrentProcessArguments }
Test-Ui 'English options copy is active' {
    Open-Options
    winapp ui wait-for 'OptionsBackButton' -a $script:CurrentPid -p Name --value 'Back' -t 3000 2>$null |
        Out-Null
    winapp ui wait-for 'LanguageSelector' -a $script:CurrentPid --value 'English (United States)' -t 3000 2>$null |
        Out-Null
}
Test-Ui 'English tray menu is localized' {
    $names = Get-TrayMenuNames
    foreach ($expected in 'Update', 'Options', 'Exit') {
        if ($expected -notin $names) { throw "Tray menu is missing '$expected'." }
    }
}
Test-Ui 'English sample formats currency and period' {
    Capture-Sample '02-english-sample.png'
    Assert-UiTextContains 'Total spend'
    Assert-UiTextContains '$48.12'
    Assert-UiTextContains '30 days · updated now'
}
Test-Ui 'English error state is localized' {
    Capture-ErrorState '03-english-error.png' '30 days · error, showing cache' `
        'Sample error · keeping the last value.'
}

Test-Ui 'Spanish selection persists after restart' { Switch-ToSpanish }
Test-Ui 'Spanish options copy is active' {
    Open-Options
    winapp ui wait-for 'OptionsBackButton' -a $script:CurrentPid -p Name --value 'Atrás' -t 3000 2>$null |
        Out-Null
    winapp ui wait-for 'LanguageSelector' -a $script:CurrentPid --value 'Español (España)' -t 3000 2>$null |
        Out-Null
    winapp ui screenshot -a $script:CurrentPid -o (Join-Path $artifactDirectory '05-spanish-options.png') 2>$null |
        Out-Null
}
Test-Ui 'Spanish tray menu is localized' {
    $names = Get-TrayMenuNames
    foreach ($expected in 'Actualizar', 'Opciones', 'Salir') {
        if ($expected -notin $names) { throw "El menú de bandeja no contiene '$expected'." }
    }
}
Test-Ui 'Spanish sample formats currency and period' {
    Capture-Sample '06-spanish-sample.png'
    Assert-UiTextContains 'Gasto total'
    Assert-UiTextContains '48,12 US$'
    Assert-UiTextContains '30 días · actualizado ahora'
}
Test-Ui 'Spanish error state is localized' {
    Capture-ErrorState '07-spanish-error.png' '30 días · error, mostrando caché' `
        'Error de muestra · se conserva el último dato.'
}
Test-Ui 'Language controls expose automation IDs' {
    Open-Options
    $controls = Get-AppElements -Interactive
    foreach ($automationId in 'LanguageSelector', 'OptionsBackButton', 'CloseWhenInactiveToggle') {
        if (-not ($controls | Where-Object automationId -eq $automationId)) {
            throw "Missing interactive AutomationId '$automationId'."
        }
    }
}
Test-Ui 'Visible Spanish text stays inside the flyout' {
    $elements = Get-AppElements
    $window = $elements | Where-Object type -eq 'Window' | Select-Object -First 1
    if (-not $window -or $window.width -le 0) { throw 'Flyout bounds are unavailable.' }
    $right = $window.x + $window.width
    $overflow = @($elements | Where-Object {
        $_.type -eq 'Text' -and -not $_.isOffscreen -and $_.width -gt 0 -and
        ($_.x -lt $window.x -or ($_.x + $_.width) -gt ($right + 1))
    })
    if ($overflow.Count -gt 0) {
        throw "Visible text exceeds flyout bounds: $(($overflow.name -join ', '))"
    }
}

Test-Ui 'Original language is restored' {
    $current = Get-SelectedLanguage
    if ($initialLanguage -eq 'English (United States)' -and $current -ne $initialLanguage) {
        Select-Language 'Inglés (Estados Unidos)' '08-restore-english.png'
        Restart-CurrentApp
    }
    elseif ($initialLanguage -eq 'Español (España)' -and $current -ne $initialLanguage) {
        Select-Language 'Spanish (Spain)' '08-restore-spanish.png'
        Restart-CurrentApp
    }

    if ((Get-SelectedLanguage) -ne $initialLanguage) {
        throw 'Original language was not restored.'
    }
}

$results | ConvertTo-Json -Depth 4 |
    Set-Content (Join-Path $artifactDirectory 'ui-results.json')

Write-Host "Passed: $pass | Failed: $fail | FinalPid: $script:CurrentPid"
$results | Where-Object status -eq 'FAIL' | ForEach-Object {
    Write-Host "FAIL: $($_.name) - $($_.detail)"
}
if ($fail -gt 0) { exit 1 }
