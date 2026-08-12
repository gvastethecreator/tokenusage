using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Diagnostics.CodeAnalysis;
using TokenUsage.Core.Providers;
using TokenUsage.Core.Usage;
using TokenUsage.Providers.LocalScan;
using TokenUsage.Providers.Pricing;

namespace TokenUsage.Providers.Mux;

/// <summary>
/// Reads Mux's aggregate session-usage.json files. These files contain model,
/// token, cost, and timestamp fields. The reader does not open Mux transcripts.
/// </summary>
public sealed class MuxUsageEventSource :
    ISnapshotUsageEventSource,
    IRootDetectingUsageEventSource
{
    public const string ParserVersion = "mux-session-usage/1";
    private readonly string _sessionsDirectory;
    private readonly string _groupingTimeZoneId;
    private readonly LocalScanBudget _budget;

    public MuxUsageEventSource(
        string groupingTimeZoneId,
        string? homeDirectory = null,
        string? sessionsDirectoryOverride = null,
        int maximumFiles = 10_000,
        long maximumFileBytes = 4 * 1024 * 1024)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(groupingTimeZoneId);
        _ = TimeZoneInfo.FindSystemTimeZoneById(groupingTimeZoneId);
        string home = homeDirectory
            ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        _sessionsDirectory = Path.GetFullPath(sessionsDirectoryOverride
            ?? Path.Combine(home, ".mux", "sessions"));
        _groupingTimeZoneId = groupingTimeZoneId;
        _budget = new LocalScanBudget(maximumFiles, maximumFileBytes);
    }

    public SourceKind SourceKind => SourceKind.LocalLog;

    public AgentId AgentId { get; } = new("mux");

    [SuppressMessage(
        "Performance",
        "CA1822:Mark members as static",
        Justification = "The property implements the usage-source contract.")]
    public string EventParserVersion => ParserVersion;

    public bool IsRootAvailable => Directory.Exists(_sessionsDirectory);

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

        var state = new LocalScanState(_budget);
        var output = new Dictionary<string, UsageEvent>(StringComparer.Ordinal);
        IEnumerable<string> files;
        try
        {
            files = Directory.EnumerateFiles(
                    _sessionsDirectory,
                    "session-usage.json",
                    SearchOption.AllDirectories)
                .Order(StringComparer.OrdinalIgnoreCase)
                .ToArray();
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

        foreach (string path in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!state.TryConsumeFile())
            {
                state.MarkPartial();
                break;
            }

            if (!ReadFile(path, output, cancellationToken))
            {
                state.MarkPartial();
            }
        }

        UsageEvent[] events = output.Values
            .OrderBy(item => item.OccurredAtUtc)
            .ThenBy(item => item.EventKey.Value, StringComparer.Ordinal)
            .ToArray();
        UsageSourceReadStatus status = state.IsPartial
            ? UsageSourceReadStatus.Partial
            : events.Length == 0
                ? UsageSourceReadStatus.NoData
                : UsageSourceReadStatus.Complete;
        return new UsageSourceReadResult(
            events,
            status,
            status == UsageSourceReadStatus.NoData
                ? UsageSourceIssueKind.Empty
                : state.IsPartial
                    ? UsageSourceIssueKind.PartialScan
                    : null);
    }

    private bool ReadFile(
        string path,
        Dictionary<string, UsageEvent> output,
        CancellationToken cancellationToken)
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists
                || info.Length <= 0
                || info.Length > _budget.MaximumFileBytes
                || (info.Attributes & FileAttributes.ReparsePoint) != 0)
            {
                return false;
            }

            using FileStream stream = new(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete,
                64 * 1024,
                FileOptions.SequentialScan);
            using JsonDocument document = JsonDocument.Parse(stream);
            JsonElement root = document.RootElement;
            if (!root.TryGetProperty("byModel", out JsonElement byModel)
                || byModel.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            DateTimeOffset timestamp = GetTimestamp(root, info.LastWriteTimeUtc);
            string sessionId = Path.GetFileName(Path.GetDirectoryName(path)) ?? "session";
            foreach (JsonProperty property in byModel.EnumerateObject())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!TryCreateEvent(
                        sessionId,
                        property.Name,
                        property.Value,
                        timestamp,
                        out UsageEvent? usageEvent)
                    || usageEvent is null)
                {
                    continue;
                }

                output[usageEvent.EventKey.Value] = usageEvent;
            }

            return true;
        }
        catch (Exception exception) when (exception is IOException
                                           or UnauthorizedAccessException
                                           or JsonException
                                           or InvalidOperationException
                                           or OverflowException)
        {
            return false;
        }
    }

    private bool TryCreateEvent(
        string sessionId,
        string modelKey,
        JsonElement modelUsage,
        DateTimeOffset timestamp,
        out UsageEvent? usageEvent)
    {
        usageEvent = null;
        if (modelUsage.ValueKind != JsonValueKind.Object
            || !TrySplitModelKey(modelKey, out string? provider, out string? model)
            || string.IsNullOrWhiteSpace(model))
        {
            return false;
        }

        Bucket input = ReadBucket(modelUsage, "input");
        Bucket cached = ReadBucket(modelUsage, "cached");
        Bucket cacheCreate = ReadBucket(modelUsage, "cacheCreate");
        Bucket output = ReadBucket(modelUsage, "output");
        Bucket reasoning = ReadBucket(modelUsage, "reasoning");
        var tokens = new TokenBreakdown(
            input.Tokens,
            output.Tokens,
            reasoning.Tokens,
            cached.Tokens,
            cacheCreate.Tokens);
        if (tokens.Total == 0)
        {
            return false;
        }

        bool hasReportedCost = input.HasCost || cached.HasCost || cacheCreate.HasCost
            || output.HasCost || reasoning.HasCost;
        decimal costUsd = input.CostUsd + cached.CostUsd + cacheCreate.CostUsd
            + output.CostUsd + reasoning.CostUsd;
        CostObservation cost = hasReportedCost
            ? CostObservation.ProviderReported(decimal.Round(
                costUsd,
                6,
                MidpointRounding.AwayFromZero))
            : KnownModelPricingCatalog.Resolve(model, timestamp, tokens);
        string normalizedModel = NormalizeId(model);
        usageEvent = new UsageEvent(
            new UsageEventKey(Hash($"mux\0{sessionId}\0{modelKey}")),
            AgentId,
            string.IsNullOrWhiteSpace(provider)
                ? null
                : new ModelProviderId(NormalizeId(provider)),
            new ModelId(normalizedModel),
            timestamp,
            _groupingTimeZoneId,
            tokens,
            cost,
            ParserVersion,
            cost.Kind switch
            {
                CostKind.ProviderReported => CoverageKind.Complete,
                CostKind.CatalogEstimated => CoverageKind.Partial,
                _ => CoverageKind.Unpriced,
            });
        return true;
    }

    private static Bucket ReadBucket(JsonElement modelUsage, string name)
    {
        if (!modelUsage.TryGetProperty(name, out JsonElement element)
            || element.ValueKind != JsonValueKind.Object)
        {
            return default;
        }

        long tokens = 0;
        if (element.TryGetProperty("tokens", out JsonElement tokenElement)
            && tokenElement.ValueKind == JsonValueKind.Number
            && tokenElement.TryGetInt64(out long parsedTokens)
            && parsedTokens >= 0)
        {
            tokens = parsedTokens;
        }

        decimal parsedCost = 0m;
        bool hasCost = element.TryGetProperty("cost_usd", out JsonElement costElement)
            && costElement.ValueKind == JsonValueKind.Number
            && costElement.TryGetDecimal(out parsedCost)
            && parsedCost >= 0
            && parsedCost <= long.MaxValue / 1_000_000m;
        return new Bucket(tokens, hasCost ? parsedCost : 0m, hasCost);
    }

    private static DateTimeOffset GetTimestamp(JsonElement root, DateTime fallbackUtc)
    {
        if (root.TryGetProperty("lastRequest", out JsonElement lastRequest)
            && lastRequest.ValueKind == JsonValueKind.Object
            && lastRequest.TryGetProperty("timestamp", out JsonElement timestamp)
            && timestamp.ValueKind == JsonValueKind.Number
            && timestamp.TryGetInt64(out long milliseconds))
        {
            try
            {
                return DateTimeOffset.FromUnixTimeMilliseconds(milliseconds);
            }
            catch (ArgumentOutOfRangeException)
            {
            }
        }

        return new DateTimeOffset(DateTime.SpecifyKind(fallbackUtc, DateTimeKind.Utc));
    }

    private static bool TrySplitModelKey(
        string key,
        out string? provider,
        out string? model)
    {
        int separator = key.IndexOf(':');
        provider = separator < 0 ? null : key[..separator].Trim();
        model = (separator < 0 ? key : key[(separator + 1)..]).Trim();
        return model.Length is > 0 and <= 200
            && (provider is null || provider.Length <= 100);
    }

    private static string NormalizeId(string value)
    {
        var builder = new StringBuilder(value.Length);
        bool previousWasSeparator = false;
        foreach (char character in value.Trim().ToLowerInvariant())
        {
            if (char.IsAsciiLetterOrDigit(character))
            {
                builder.Append(character);
                previousWasSeparator = false;
            }
            else if (!previousWasSeparator && builder.Length > 0)
            {
                builder.Append('-');
                previousWasSeparator = true;
            }
        }

        string normalized = builder.ToString().Trim('-');
        return string.IsNullOrWhiteSpace(normalized) ? "unknown" : normalized;
    }

    private static string Hash(string value) => Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(value)))
        .ToLowerInvariant();

    private readonly record struct Bucket(long Tokens, decimal CostUsd, bool HasCost);
}
