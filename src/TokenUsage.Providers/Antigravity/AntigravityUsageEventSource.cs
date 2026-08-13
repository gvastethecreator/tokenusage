using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using TokenUsage.Core.Providers;
using TokenUsage.Core.Usage;
using TokenUsage.Providers.LocalScan;
using TokenUsage.Providers.Pricing;

namespace TokenUsage.Providers.Antigravity;

public sealed class AntigravityUsageEventSource :
    IWindowedSnapshotUsageEventSource,
    IRootDetectingUsageEventSource
{
    public const string ParserVersion = "antigravity-gen-metadata/1";
    private const int LookbackDays = 35;
    private const int DefaultMaximumBlobBytes = 2 * 1024 * 1024;
    private readonly IReadOnlyList<string> _roots;
    private readonly string _groupingTimeZoneId;
    private readonly LocalScanBudget _budget;
    private readonly int _maximumRows;
    private readonly int _maximumBlobBytes;

    public AntigravityUsageEventSource(
        string groupingTimeZoneId,
        string? homeDirectory = null,
        string? dataDirectoryOverride = null,
        int maximumFiles = 10_000,
        long maximumFileBytes = 256 * 1024 * 1024,
        int maximumRows = 1_000_000,
        int maximumBlobBytes = DefaultMaximumBlobBytes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(groupingTimeZoneId);
        _ = TimeZoneInfo.FindSystemTimeZoneById(groupingTimeZoneId);
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumRows, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumBlobBytes, 1);

        string home = homeDirectory
            ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        _roots = ResolveRoots(dataDirectoryOverride, home);
        _groupingTimeZoneId = groupingTimeZoneId;
        _budget = new LocalScanBudget(maximumFiles, maximumFileBytes);
        _maximumRows = maximumRows;
        _maximumBlobBytes = maximumBlobBytes;
    }

    public SourceKind SourceKind => SourceKind.LocalDatabase;

    public AgentId AgentId { get; } = new("antigravity");

    public string EventParserVersion => ParserVersion;

    public int ReconciliationWindowDays => LookbackDays;

    public bool IsRootAvailable => _roots.Any(Directory.Exists);

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
        var events = new Dictionary<string, UsageEvent>(StringComparer.Ordinal);
        int rowsRead = 0;
        bool accessBlocked = false;

        foreach ((string rootName, string path) in EnumerateDatabases(state, cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!state.TryConsumeFile())
            {
                break;
            }

            try
            {
                var info = new FileInfo(path);
                if ((info.Attributes & FileAttributes.ReparsePoint) != 0
                    || state.IsFileTooLarge(info.Length))
                {
                    continue;
                }

                ReadDatabase(
                    rootName,
                    path,
                    events,
                    state,
                    ref rowsRead,
                    cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception) when (
                exception is SqliteException or IOException or UnauthorizedAccessException)
            {
                state.MarkPartial();
                accessBlocked = true;
            }
        }

        UsageSourceReadStatus status = state.IsPartial
            ? UsageSourceReadStatus.Partial
            : events.Count == 0
                ? UsageSourceReadStatus.NoData
                : UsageSourceReadStatus.Complete;
        UsageSourceIssueKind issue = state.UnsupportedSchema
            ? UsageSourceIssueKind.UnsupportedSchema
            : accessBlocked
                ? UsageSourceIssueKind.AccessBlocked
                : status == UsageSourceReadStatus.NoData
                    ? UsageSourceIssueKind.Empty
                    : status == UsageSourceReadStatus.Partial
                        ? UsageSourceIssueKind.PartialScan
                        : UsageSourceIssueKind.None;

        return new UsageSourceReadResult(
            events.Values
                .OrderBy(usageEvent => usageEvent.OccurredAtUtc)
                .ThenBy(usageEvent => usageEvent.EventKey.Value, StringComparer.Ordinal)
                .ToArray(),
            status,
            issue);
    }

    private IEnumerable<(string RootName, string Path)> EnumerateDatabases(
        LocalScanState state,
        CancellationToken cancellationToken)
    {
        foreach (string root in _roots)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!Directory.Exists(root))
            {
                continue;
            }

            string conversations = Path.Combine(root, "conversations");
            if (!Directory.Exists(conversations))
            {
                continue;
            }

            string[] paths;
            try
            {
                paths = Directory.EnumerateFiles(
                        conversations,
                        "*.db",
                        SearchOption.TopDirectoryOnly)
                    .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                    .ToArray();
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException)
            {
                state.MarkPartial();
                continue;
            }

            string rootName = Path.GetFileName(root);
            foreach (string path in paths)
            {
                yield return (rootName, path);
            }
        }
    }

    private void ReadDatabase(
        string rootName,
        string path,
        Dictionary<string, UsageEvent> output,
        LocalScanState state,
        ref int rowsRead,
        CancellationToken cancellationToken)
    {
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadOnly,
            Cache = SqliteCacheMode.Private,
            Pooling = false,
        };
        using var connection = new SqliteConnection(builder.ToString());
        connection.Open();
        ExecuteControl(connection, "PRAGMA busy_timeout=250", cancellationToken);
        ExecuteControl(connection, "PRAGMA query_only=ON", cancellationToken);

        HashSet<string> metadataColumns = GetColumns(
            connection,
            "gen_metadata",
            cancellationToken);
        HashSet<string> stepColumns = GetColumns(connection, "steps", cancellationToken);
        if (!HasColumns(metadataColumns, "idx", "data")
            || !HasColumns(stepColumns, "idx", "step_payload"))
        {
            state.MarkPartial();
            state.UnsupportedSchema = true;
            return;
        }

        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT
              g.idx,
              CASE WHEN length(g.data) <= $blob_limit THEN g.data END,
              CASE WHEN length(s.step_payload) <= $blob_limit THEN s.step_payload END,
              length(g.data),
              length(s.step_payload)
            FROM gen_metadata g
            LEFT JOIN steps s ON s.idx = g.idx
            ORDER BY g.idx
            LIMIT $limit
            """;
        command.Parameters.AddWithValue("$blob_limit", _maximumBlobBytes);
        command.Parameters.AddWithValue("$limit", checked(_maximumRows + 1L));
        using CancellationTokenRegistration registration = cancellationToken.Register(command.Cancel);
        using SqliteDataReader reader = command.ExecuteReader();
        while (reader.Read())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (++rowsRead > _maximumRows)
            {
                state.MarkPartial();
                break;
            }

            if (reader.IsDBNull(0)
                || reader.IsDBNull(1)
                || reader.IsDBNull(2)
                || reader.IsDBNull(3)
                || reader.IsDBNull(4)
                || reader.GetInt64(3) > _maximumBlobBytes
                || reader.GetInt64(4) > _maximumBlobBytes)
            {
                state.MarkPartial();
                continue;
            }

            if (reader.GetValue(1) is not byte[] metadata
                || reader.GetValue(2) is not byte[] stepPayload)
            {
                state.MarkPartial();
                continue;
            }

            if (metadata.Length > _maximumBlobBytes || stepPayload.Length > _maximumBlobBytes)
            {
                state.MarkPartial();
                continue;
            }

            if (!AntigravityProtoDecoder.TryDecodeUsage(
                    metadata,
                    out AntigravityTokenBlock tokenBlock,
                    out string? rawModel)
                || !AntigravityProtoDecoder.TryDecodeTimestamp(
                    stepPayload,
                    out DateTimeOffset timestamp))
            {
                state.MarkPartial();
                continue;
            }

            string model = NormalizeModel(rawModel);
            var tokens = new TokenBreakdown(
                tokenBlock.Input,
                tokenBlock.Output,
                reasoning: 0,
                tokenBlock.CacheRead,
                cacheWrite: 0);
            CostObservation cost = AntigravityPricingCatalog.Resolve(model, tokens);
            if (cost.Kind == CostKind.Unavailable)
            {
                cost = KnownModelPricingCatalog.Resolve(model, timestamp, tokens);
            }
            string identity = $"{rootName}\0{Path.GetFileName(path)}\0{reader.GetInt64(0)}";
            var usageEvent = new UsageEvent(
                new UsageEventKey(Hash(identity)),
                AgentId,
                ResolveProvider(model),
                new ModelId(model),
                timestamp,
                _groupingTimeZoneId,
                tokens,
                cost,
                ParserVersion,
                cost.Kind == CostKind.Unavailable
                    ? CoverageKind.Unpriced
                    : CoverageKind.Partial);
            output.TryAdd(usageEvent.EventKey.Value, usageEvent);
        }
    }

    private static IReadOnlyList<string> ResolveRoots(string? configured, string home)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(configured))
            {
                return [Path.GetFullPath(Environment.ExpandEnvironmentVariables(configured))];
            }
        }
        catch (Exception exception) when (
            exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
        }

        string gemini = Path.Combine(home, ".gemini");
        return
        [
            Path.Combine(gemini, "antigravity"),
            Path.Combine(gemini, "antigravity-cli"),
            Path.Combine(gemini, "antigravity-ide"),
        ];
    }

    private static string NormalizeModel(string? rawModel)
    {
        if (string.IsNullOrWhiteSpace(rawModel))
        {
            return "antigravity-unknown";
        }

        string normalized = string.Join(
            '-',
            rawModel.Trim().ToLowerInvariant()
                .Split(
                    rawModel.Where(character => !char.IsLetterOrDigit(character))
                        .Distinct()
                        .ToArray(),
                    StringSplitOptions.RemoveEmptyEntries));
        if (normalized is "gemini-3-6-flash" or "gemini-3-6-flash-high")
        {
            return "gemini-3.6-flash";
        }

        if (normalized is "claude-sonnet-4-6" or "claude-sonnet-4-6-thinking")
        {
            return "claude-sonnet-4-6";
        }

        return "antigravity-unknown";
    }

    private static ModelProviderId? ResolveProvider(string model) => model switch
    {
        "gemini-3.6-flash" => new ModelProviderId("google"),
        "claude-sonnet-4-6" => new ModelProviderId("anthropic"),
        _ => null,
    };

    private static void ExecuteControl(
        SqliteConnection connection,
        string text,
        CancellationToken cancellationToken)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = text;
        using CancellationTokenRegistration registration = cancellationToken.Register(command.Cancel);
        command.ExecuteNonQuery();
    }

    private static HashSet<string> GetColumns(
        SqliteConnection connection,
        string table,
        CancellationToken cancellationToken)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = $"PRAGMA table_info({table})";
        using CancellationTokenRegistration registration = cancellationToken.Register(command.Cancel);
        using SqliteDataReader reader = command.ExecuteReader();
        var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (reader.Read())
        {
            cancellationToken.ThrowIfCancellationRequested();
            columns.Add(reader.GetString(1));
        }

        return columns;
    }

    private static bool HasColumns(HashSet<string> columns, params string[] expected) =>
        expected.All(columns.Contains);

    private static string Hash(string value) => Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(value)))
        .ToLowerInvariant();
}

internal readonly record struct AntigravityTokenBlock(long Input, long Output, long CacheRead);

internal static class AntigravityProtoDecoder
{
    private const int MaximumDepth = 32;
    private const int MaximumFields = 200_000;
    private const ulong MaximumPlausibleTokens = 2_000_000;
    private const long EarliestTimestampSeconds = 946_684_800;
    private const long LatestTimestampSeconds = 4_102_444_800;

    public static bool TryDecodeUsage(
        byte[] data,
        out AntigravityTokenBlock block,
        out string? model)
    {
        var state = new UsageDecoderState();
        int fieldsRemaining = MaximumFields;
        bool parsed = WalkUsage(data, 0, ref fieldsRemaining, state);
        block = state.Block;
        model = state.Model;
        return parsed && state.HasBlock;
    }

    public static bool TryDecodeTimestamp(byte[] data, out DateTimeOffset timestamp)
    {
        int fieldsRemaining = MaximumFields;
        DateTimeOffset? earliest = null;
        bool parsed = WalkTimestamp(data, 0, ref fieldsRemaining, ref earliest);
        timestamp = earliest ?? default;
        return parsed && earliest is not null;
    }

    private static bool WalkUsage(
        ReadOnlySpan<byte> data,
        int depth,
        ref int fieldsRemaining,
        UsageDecoderState state)
    {
        if (depth > MaximumDepth)
        {
            return false;
        }

        var fields = new List<WireField>();
        if (!TryReadFields(data, ref fieldsRemaining, fields))
        {
            return false;
        }

        if (!state.HasBlock && TryCreateTokenBlock(fields, out AntigravityTokenBlock block))
        {
            state.Block = block;
            state.HasBlock = true;
        }

        foreach (WireField field in fields.Where(field => field.WireType == 2))
        {
            ReadOnlySpan<byte> nested = data.Slice(field.Offset, field.Length);
            if (state.Model is null && field.Number is 19 or 21
                && TryReadModel(nested, out string? model))
            {
                state.Model = model;
            }

            _ = WalkUsage(nested, depth + 1, ref fieldsRemaining, state);
        }

        return true;
    }

    private static bool WalkTimestamp(
        ReadOnlySpan<byte> data,
        int depth,
        ref int fieldsRemaining,
        ref DateTimeOffset? earliest)
    {
        if (depth > MaximumDepth)
        {
            return false;
        }

        var fields = new List<WireField>();
        if (!TryReadFields(data, ref fieldsRemaining, fields))
        {
            return false;
        }

        if (TryCreateTimestamp(fields, out DateTimeOffset candidate)
            && (earliest is null || candidate < earliest.Value))
        {
            earliest = candidate;
        }

        foreach (WireField field in fields.Where(field => field.WireType == 2))
        {
            _ = WalkTimestamp(
                data.Slice(field.Offset, field.Length),
                depth + 1,
                ref fieldsRemaining,
                ref earliest);
        }

        return true;
    }

    private static bool TryReadFields(
        ReadOnlySpan<byte> data,
        ref int fieldsRemaining,
        List<WireField> output)
    {
        int offset = 0;
        while (offset < data.Length)
        {
            if (--fieldsRemaining < 0 || !TryReadVarint(data, ref offset, out ulong tag))
            {
                return false;
            }

            ulong rawNumber = tag >> 3;
            int wireType = (int)(tag & 7);
            if (rawNumber is 0 or > 100_000)
            {
                return false;
            }

            int number = (int)rawNumber;

            switch (wireType)
            {
                case 0:
                    if (!TryReadVarint(data, ref offset, out ulong value))
                    {
                        return false;
                    }

                    output.Add(new WireField(number, wireType, value, 0, 0));
                    break;
                case 1:
                    if (data.Length - offset < 8)
                    {
                        return false;
                    }

                    offset += 8;
                    output.Add(new WireField(number, wireType, 0, 0, 0));
                    break;
                case 2:
                    if (!TryReadVarint(data, ref offset, out ulong rawLength)
                        || rawLength > int.MaxValue
                        || rawLength > (ulong)(data.Length - offset))
                    {
                        return false;
                    }

                    int length = (int)rawLength;
                    output.Add(new WireField(number, wireType, 0, offset, length));
                    offset += length;
                    break;
                case 5:
                    if (data.Length - offset < 4)
                    {
                        return false;
                    }

                    offset += 4;
                    output.Add(new WireField(number, wireType, 0, 0, 0));
                    break;
                default:
                    return false;
            }
        }

        return output.Count > 0;
    }

    private static bool TryCreateTokenBlock(
        IReadOnlyList<WireField> fields,
        out AntigravityTokenBlock block)
    {
        block = default;
        if (!TryFindVarint(fields, 1, out ulong kind)
            || !TryFindVarint(fields, 2, out ulong input)
            || !TryFindVarint(fields, 3, out ulong output)
            || kind is < 1000 or >= 5000
            || input > MaximumPlausibleTokens
            || output > MaximumPlausibleTokens
            || input + output > MaximumPlausibleTokens)
        {
            return false;
        }

        ulong cacheRead = 0;
        if (fields.Any(field => field.Number == 4
                                && (field.WireType != 0
                                    || field.Varint > MaximumPlausibleTokens))
            || fields.Any(field => field.Number == 5 && field.WireType != 0)
            || (TryFindVarint(fields, 5, out cacheRead)
                && cacheRead > MaximumPlausibleTokens))
        {
            return false;
        }

        block = new AntigravityTokenBlock((long)input, (long)output, (long)cacheRead);
        return true;
    }

    private static bool TryCreateTimestamp(
        IReadOnlyList<WireField> fields,
        out DateTimeOffset timestamp)
    {
        timestamp = default;
        if (fields.Count is < 1 or > 2
            || fields.Any(field => field.WireType != 0 || field.Number is not (1 or 2))
            || !TryFindVarint(fields, 1, out ulong rawSeconds)
            || rawSeconds > long.MaxValue)
        {
            return false;
        }

        long seconds = (long)rawSeconds;
        ulong nanos = 0;
        if ((TryFindVarint(fields, 2, out nanos) && nanos >= 1_000_000_000)
            || seconds <= EarliestTimestampSeconds
            || seconds >= LatestTimestampSeconds)
        {
            return false;
        }

        timestamp = DateTimeOffset.FromUnixTimeSeconds(seconds)
            .AddTicks((long)nanos / 100);
        return true;
    }

    private static bool TryReadModel(ReadOnlySpan<byte> data, out string? model)
    {
        model = null;
        if (data.Length is 0 or > 64)
        {
            return false;
        }

        try
        {
            string value = new UTF8Encoding(false, true).GetString(data);
            if (!value.Any(char.IsLetter) || value.Any(char.IsControl))
            {
                return false;
            }

            model = value;
            return true;
        }
        catch (DecoderFallbackException)
        {
            return false;
        }
    }

    private static bool TryFindVarint(
        IEnumerable<WireField> fields,
        int number,
        out ulong value)
    {
        WireField? field = fields.FirstOrDefault(
            candidate => candidate.Number == number && candidate.WireType == 0);
        value = field?.Varint ?? 0;
        return field is not null;
    }

    private static bool TryReadVarint(
        ReadOnlySpan<byte> data,
        ref int offset,
        out ulong value)
    {
        value = 0;
        for (int shift = 0; shift < 64 && offset < data.Length; shift += 7)
        {
            byte current = data[offset++];
            value |= (ulong)(current & 0x7f) << shift;
            if ((current & 0x80) == 0)
            {
                return true;
            }
        }

        return false;
    }

    private sealed class UsageDecoderState
    {
        public bool HasBlock { get; set; }

        public AntigravityTokenBlock Block { get; set; }

        public string? Model { get; set; }
    }

    private sealed record WireField(
        int Number,
        int WireType,
        ulong Varint,
        int Offset,
        int Length);
}
