$ErrorActionPreference = "Stop"

function Get-TokenCount {
    param(
        [Parameter(Mandatory = $true)] $Payload,
        [Parameter(Mandatory = $true)] [string] $Name
    )

    $property = $Payload.PSObject.Properties[$Name]
    if ($null -eq $property) {
        return $null
    }

    [long] $number = 0
    if (-not [long]::TryParse(
        [string] $property.Value,
        [Globalization.NumberStyles]::Integer,
        [Globalization.CultureInfo]::InvariantCulture,
        [ref] $number)) {
        return $null
    }

    if ($number -lt 0) {
        return $null
    }

    return $number
}

try {
    $rawInput = [Console]::In.ReadToEnd()
    $payload = $rawInput | ConvertFrom-Json
    if ([string] $payload.hook_event_name -ne "stop") {
        throw "Unsupported hook event."
    }

    $inputTokens = Get-TokenCount -Payload $payload -Name "input_tokens"
    $outputTokens = Get-TokenCount -Payload $payload -Name "output_tokens"
    $cacheReadTokens = Get-TokenCount -Payload $payload -Name "cache_read_tokens"
    $cacheWriteTokens = Get-TokenCount -Payload $payload -Name "cache_write_tokens"
    if ($null -eq $inputTokens -or $null -eq $outputTokens) {
        throw "This Cursor build did not provide token counters."
    }

    if ($null -eq $cacheReadTokens) { $cacheReadTokens = 0 }
    if ($null -eq $cacheWriteTokens) { $cacheWriteTokens = 0 }

    $conversationId = [string] $payload.conversation_id
    $generationId = [string] $payload.generation_id
    $cursorVersion = [string] $payload.cursor_version
    $model = [string] $payload.model_id
    if ([string]::IsNullOrWhiteSpace($model)) {
        $model = [string] $payload.model
    }
    if ([string]::IsNullOrWhiteSpace($conversationId) -or
        [string]::IsNullOrWhiteSpace($generationId) -or
        [string]::IsNullOrWhiteSpace($model)) {
        throw "Cursor omitted the event identity or model."
    }

    $identity = "cursor`0hook-v1`0$cursorVersion`0$conversationId`0$generationId"
    $sha = [Security.Cryptography.SHA256]::Create()
    try {
        $hashBytes = $sha.ComputeHash([Text.Encoding]::UTF8.GetBytes($identity))
    }
    finally {
        $sha.Dispose()
    }
    $eventKey = ([BitConverter]::ToString($hashBytes)).Replace("-", "").ToLowerInvariant()

    $record = [ordered] @{
        version = 1
        event_key = $eventKey
        occurred_at_utc = [DateTimeOffset]::UtcNow.ToString("O", [Globalization.CultureInfo]::InvariantCulture)
        cursor_version = $cursorVersion.Substring(0, [Math]::Min($cursorVersion.Length, 32))
        model = $model.Substring(0, [Math]::Min($model.Length, 200))
        input_tokens = $inputTokens
        output_tokens = $outputTokens
        cache_read_tokens = $cacheReadTokens
        cache_write_tokens = $cacheWriteTokens
    }

    $spoolDirectory = Join-Path $env:LOCALAPPDATA "TokenUsage\cursor"
    $spoolPath = Join-Path $spoolDirectory "usage.v1.jsonl"
    [IO.Directory]::CreateDirectory($spoolDirectory) | Out-Null
    $mutex = New-Object Threading.Mutex($false, "Local\TokenUsage.CursorHook")
    $hasMutex = $false
    try {
        $hasMutex = $mutex.WaitOne([TimeSpan]::FromSeconds(2))
        if (-not $hasMutex) {
            throw "Cursor usage spool is busy."
        }

        if ([IO.File]::Exists($spoolPath) -and
            (Get-Item -LiteralPath $spoolPath).Length -ge 16777216) {
            $rotatedPath = "$spoolPath.1"
            if ([IO.File]::Exists($rotatedPath)) {
                [IO.File]::Delete($rotatedPath)
            }
            [IO.File]::Move($spoolPath, $rotatedPath)
        }

        $utf8 = New-Object Text.UTF8Encoding($false)
        $line = $record | ConvertTo-Json -Compress
        [IO.File]::AppendAllText($spoolPath, $line + [Environment]::NewLine, $utf8)
    }
    finally {
        if ($hasMutex) { $mutex.ReleaseMutex() }
        $mutex.Dispose()
    }
}
catch {
    # Hooks are observational and fail open. Never echo the input or exception.
}

[Console]::Out.WriteLine("{}")
exit 0
