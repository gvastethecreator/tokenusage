param(
    [Parameter(Mandatory)]
    [int]$AppPid,
    [Parameter(Mandatory)]
    [string]$Aumid,
    [string]$ArtifactDirectory = "artifacts\ticket-06"
)

$ErrorActionPreference = "Stop"
New-Item -ItemType Directory -Force -Path $ArtifactDirectory | Out-Null

if (-not (Get-Process -Id $AppPid -ErrorAction SilentlyContinue)) {
    throw "Primary TokenUsage process '$AppPid' is not running."
}

Start-Process explorer.exe -ArgumentList "shell:AppsFolder\$Aumid" -WindowStyle Hidden

$visibleWindowObserved = $false
$deadline = [DateTime]::UtcNow.AddSeconds(4)
while ([DateTime]::UtcNow -lt $deadline) {
    Start-Sleep -Milliseconds 100
    $windowJson = & winapp ui list-windows -a $AppPid --json 2>$null
    if (@($windowJson | ConvertFrom-Json).Count -gt 0) {
        $visibleWindowObserved = $true
    }
}

$appProcesses = @(Get-Process TokenUsage.App -ErrorAction SilentlyContinue)
$samePrimaryProcess = $appProcesses.Count -eq 1 -and $appProcesses[0].Id -eq $AppPid
$result = [ordered]@{
    primaryPid = $AppPid
    processIdsAfterRedirect = @($appProcesses.Id)
    samePrimaryProcess = $samePrimaryProcess
    visibleWindowObserved = $visibleWindowObserved
}

$result | ConvertTo-Json -Depth 4 |
    Set-Content (Join-Path $ArtifactDirectory "single-instance-results.json")

if (-not $samePrimaryProcess) {
    throw "The second activation did not return to the primary process."
}

if (-not $visibleWindowObserved) {
    throw "The primary flyout was not observed after redirected activation."
}

Write-Output "PASS: redirected activation kept PID $AppPid and showed the flyout."
