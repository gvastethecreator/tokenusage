using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TokenUsage.Core.Providers;
using TokenUsage.Core.Usage;
using TokenUsage.Providers.Pricing;

namespace TokenUsage.Providers.Amp;

/// <summary>
/// Reads Amp's numeric ledger.jsonl projection. The reader never opens Amp
/// thread files and never persists message identifiers in clear text.
/// </summary>
public sealed class AmpUsageEventSource :
    ISnapshotUsageEventSource,
    IRootDetectingUsageEventSource
{
    public const string ParserVersion = "amp-ledger/1";
    private const long DefaultMaximumFileBytes = 256L * 1024 * 1024;
    private const int DefaultMaximumLineCharacters = 8 * 1024 * 1024;
    private readonly string _ledgerPath;
    private readonly string _groupingTimeZoneId;
    private readonly int _maximumRecords;
    private readonly long _maximumFileBytes;
    private readonly int _maximumLineCharacters;

    public AmpUsageEventSource(
        string groupingTimeZoneId,
        string? homeDirectory = null,
        string? roamingAppDataDirectory = null,
        string? ledgerPathOverride = null,
        int maximumRecords = 200_000,
        long maximumFileBytes = DefaultMaximumFileBytes,
        int maximumLineCharacters = DefaultMaximumLineCharacters)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(groupingTimeZoneId);
        _ = TimeZoneInfo.FindSystemTimeZoneById(groupingTimeZoneId);
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumRecords, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumFileBytes, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumLineCharacters, 1);
        string home = homeDirectory
            ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string roaming = roamingAppDataDirectory
            ?? Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        if (string.IsNullOrWhiteSpace(roaming))
        {
            roaming = Path.Combine(home, "AppData", "Roaming");
        }

        _ledgerPath = Path.GetFullPath(ledgerPathOverride
            ?? Path.Combine(roaming, "amp", "ledger.jsonl"));
        _groupingTimeZoneId = groupingTimeZoneId;
        _maximumRecords = maximumRecords;
        _maximumFileBytes = maximumFileBytes;
        _maximumLineCharacters = maximumLineCharacters;
    }

    public SourceKind SourceKind => SourceKind.LocalLog;

    public AgentId AgentId { get; } = new("amp");

    [SuppressMessage(
        "Performance",
        "CA1822:Mark members as static",
        Justification = "The property implements the usage-source contract.")]
    public string EventParserVersion => ParserVersion;

    public bool IsRootAvailable => File.Exists(_ledgerPath);

    public async Task<UsageSourceReadResult> ReadAsync(
        CancellationToken cancellationToken = default) =>
        await Task.Run(() => ReadCore(cancellationToken), cancellationToken)
            .ConfigureAwait(false);

    private UsageSourceReadResult ReadCore(CancellationToken cancellationToken)
    {
        if (!IsRootAvailable)
        {
            return new UsageSourceReadResult(
                [],
                UsageSourceReadStatus.NoData,
                UsageSourceIssueKind.RootUnavailable);
        }

        try
        {
            var info = new FileInfo(_ledgerPath);
            if (info.Length <= 0
                || info.Length > _maximumFileBytes
                || (info.Attributes & FileAttributes.ReparsePoint) != 0)
            {
                return new UsageSourceReadResult(
                    [],
                    UsageSourceReadStatus.NoData,
                    UsageSourceIssueKind.AccessBlocked);
            }

            return ReadLedger(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException
                                           or UnauthorizedAccessException
                                           or System.Security.SecurityException)
        {
            return new UsageSourceReadResult(
                [],
                UsageSourceReadStatus.NoData,
                UsageSourceIssueKind.AccessBlocked);
        }
    }

    private UsageSourceReadResult ReadLedger(CancellationToken cancellationToken)
    {
        using FileStream stream = new(
            _ledgerPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            64 * 1024,
            FileOptions.SequentialScan);
        using var reader = new StreamReader(
            stream,
            Encoding.UTF8,
            detectEncodingFromByteOrderMarks: true,
            64 * 1024,
            leaveOpen: false);
        var records = new Dictionary<string, LedgerRecord>(StringComparer.Ordinal);
        bool isPartial = false;
        int rowsRead = 0;
        while (reader.ReadLine() is string line)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            if (++rowsRead > _maximumRecords)
            {
                isPartial = true;
                break;
            }

            if (line.Length > _maximumLineCharacters
                || !TryReadRecord(line, out LedgerRecord? record)
                || record is null)
            {
                isPartial = true;
                continue;
            }

            records[record.MessageId] = records.TryGetValue(
                record.MessageId,
                out LedgerRecord? existing)
                ? Merge(existing, record)
                : record;
        }

        UsageEvent[] events = records.Values
            .Select(CreateEvent)
            .OrderBy(usageEvent => usageEvent.OccurredAtUtc)
            .ThenBy(usageEvent => usageEvent.EventKey.Value, StringComparer.Ordinal)
            .ToArray();
        UsageSourceReadStatus status = isPartial
            ? UsageSourceReadStatus.Partial
            : events.Length == 0
                ? UsageSourceReadStatus.NoData
                : UsageSourceReadStatus.Complete;
        return new UsageSourceReadResult(
            events,
            status,
            status == UsageSourceReadStatus.NoData
                ? UsageSourceIssueKind.Empty
                : isPartial
                    ? UsageSourceIssueKind.PartialScan
                    : null);
    }

    private static bool TryReadRecord(string line, out LedgerRecord? record)
    {
        record = null;
        try
        {
            using JsonDocument document = JsonDocument.Parse(line);
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !TryString(root, out string messageId, "to_message_id", "toMessageId")
                || messageId.Length > 500
                || !TryString(root, out string timestampText, "timestamp", "created_at", "createdAt")
                || !DateTimeOffset.TryParse(
                    timestampText,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                    out DateTimeOffset timestamp))
            {
                return false;
            }

            JsonElement tokenBag;
            if (!(root.TryGetProperty("tokens", out tokenBag)
                  && tokenBag.ValueKind == JsonValueKind.Object)
                && !(root.TryGetProperty("usage", out tokenBag)
                     && tokenBag.ValueKind == JsonValueKind.Object))
            {
                return false;
            }

            var tokens = new TokenBreakdown(
                ReadToken(tokenBag, "input", "input_tokens", "inputTokens"),
                ReadToken(tokenBag, "output", "output_tokens", "outputTokens"),
                reasoning: 0,
                ReadToken(
                    tokenBag,
                    "cache_read",
                    "cache_read_input_tokens",
                    "cacheReadInputTokens"),
                ReadToken(
                    tokenBag,
                    "cache_write",
                    "cache_creation_input_tokens",
                    "cacheCreationInputTokens"));
            if (tokens.Total == 0)
            {
                return false;
            }

            string model = TryString(root, out string parsedModel, "model")
                ? parsedModel
                : "unknown";
            record = new LedgerRecord(messageId, model, timestamp, tokens);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private UsageEvent CreateEvent(LedgerRecord record)
    {
        CostObservation cost = string.Equals(
            record.Model,
            "unknown",
            StringComparison.OrdinalIgnoreCase)
            ? CostObservation.Unavailable()
            : KnownModelPricingCatalog.Resolve(
                record.Model,
                record.Timestamp,
                record.Tokens);
        return new UsageEvent(
            new UsageEventKey(Hash($"amp\0{record.MessageId}")),
            AgentId,
            null,
            new ModelId(NormalizeId(record.Model)),
            record.Timestamp,
            _groupingTimeZoneId,
            record.Tokens,
            cost,
            ParserVersion,
            cost.Kind == CostKind.CatalogEstimated
                ? CoverageKind.Partial
                : CoverageKind.Unpriced);
    }

    private static LedgerRecord Merge(LedgerRecord first, LedgerRecord second) => new(
        first.MessageId,
        string.Equals(first.Model, "unknown", StringComparison.OrdinalIgnoreCase)
            ? second.Model
            : first.Model,
        first.Timestamp <= second.Timestamp ? first.Timestamp : second.Timestamp,
        new TokenBreakdown(
            Math.Max(first.Tokens.Input, second.Tokens.Input),
            Math.Max(first.Tokens.Output, second.Tokens.Output),
            0,
            Math.Max(first.Tokens.CacheRead, second.Tokens.CacheRead),
            Math.Max(first.Tokens.CacheWrite, second.Tokens.CacheWrite)));

    private static long ReadToken(JsonElement element, params string[] names)
    {
        foreach (string name in names)
        {
            if (element.TryGetProperty(name, out JsonElement value)
                && value.ValueKind == JsonValueKind.Number
                && value.TryGetInt64(out long parsed)
                && parsed > 0)
            {
                return parsed;
            }
        }

        return 0;
    }

    private static bool TryString(
        JsonElement element,
        out string value,
        params string[] names)
    {
        foreach (string name in names)
        {
            if (element.TryGetProperty(name, out JsonElement property)
                && property.ValueKind == JsonValueKind.String
                && property.GetString() is string text
                && !string.IsNullOrWhiteSpace(text))
            {
                value = text.Trim();
                return true;
            }
        }

        value = string.Empty;
        return false;
    }

    private static string NormalizeId(string value)
    {
        var output = new StringBuilder(value.Length);
        bool separator = false;
        foreach (char character in value.Trim().ToLowerInvariant())
        {
            if (char.IsAsciiLetterOrDigit(character))
            {
                output.Append(character);
                separator = false;
            }
            else if (!separator && output.Length > 0)
            {
                output.Append('-');
                separator = true;
            }
        }

        string normalized = output.ToString().Trim('-');
        return string.IsNullOrWhiteSpace(normalized) ? "unknown" : normalized;
    }

    private static string Hash(string value) => Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(value)))
        .ToLowerInvariant();

    private sealed record LedgerRecord(
        string MessageId,
        string Model,
        DateTimeOffset Timestamp,
        TokenBreakdown Tokens);
}
