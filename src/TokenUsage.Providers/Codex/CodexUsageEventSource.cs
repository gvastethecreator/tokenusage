using TokenUsage.Core.Providers;
using TokenUsage.Core.Usage;
using TokenUsage.Providers.LocalScan;

namespace TokenUsage.Providers.Codex;

public sealed partial class CodexUsageEventSource :
    IWindowedSnapshotUsageEventSource,
    IRootDetectingUsageEventSource
{
    // Version 9 retains numeric observations and separates official account totals.
    public const string ParserVersion = "codex-jsonl/9";
    private const int DefaultLineBytes = 64 * 1024;
    private const int RecentLocalWindowDays = UsagePeriodPolicy.ReconciliationDays;
    private const long MaximumInitialRecentScanBytes = 16L * 1024 * 1024 * 1024;
    private readonly string _codexHome;
    private readonly string _groupingTimeZoneId;
    private readonly LocalScanBudget _budget;
    private readonly ICodexQuotaClientFactory? _clientFactory;
    private readonly TimeProvider _clock;
    private readonly CodexUsageCheckpointStore? _checkpointStore;

    public CodexUsageEventSource(
        string groupingTimeZoneId,
        string? homeDirectory = null,
        string? codexHomeOverride = null,
        int maximumFiles = 10_000,
        int maximumLineCharacters = DefaultLineBytes,
        ICodexQuotaClientFactory? clientFactory = null,
        string? checkpointPath = null,
        TimeProvider? clock = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(groupingTimeZoneId);
        _ = TimeZoneInfo.FindSystemTimeZoneById(groupingTimeZoneId);

        string userHome = homeDirectory
            ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string? configured = codexHomeOverride
            ?? Environment.GetEnvironmentVariable("CODEX_HOME");
        _codexHome = ResolveHome(configured, userHome);
        _groupingTimeZoneId = groupingTimeZoneId;
        _budget = new LocalScanBudget(maximumFiles, maximumLineBytes: maximumLineCharacters);
        _clientFactory = clientFactory;
        _clock = clock ?? TimeProvider.System;
        _checkpointStore = checkpointPath is null
            ? null
            : new CodexUsageCheckpointStore(checkpointPath, _clock);
    }

    public SourceKind SourceKind => _clientFactory is null
        ? SourceKind.LocalLog
        : SourceKind.OfficialLocalApi;

    public AgentId AgentId { get; } = new("codex");

    public string EventParserVersion => ParserVersion;

    public int ReconciliationWindowDays => UsagePeriodPolicy.ReconciliationDays;


    public async Task<UsageSourceReadResult> ReadAsync(
        CancellationToken cancellationToken = default)
    {
        ScanResult scan = _checkpointStore is null
            ? await Task.Run(
                    () => ScanCore(new CodexUsageCheckpointState(), cancellationToken),
                    cancellationToken)
                .ConfigureAwait(false)
            : await _checkpointStore.UpdateAsync(
                    checkpoints => ScanCore(checkpoints, cancellationToken),
                    cancellationToken)
                .ConfigureAwait(false);
        return _clientFactory is null
            ? CreateFallbackResult(scan)
            : await ReadOfficialUsageAsync(scan, cancellationToken).ConfigureAwait(false);
    }


    private sealed record SessionFile(string Path, string SessionIdentity, string? Model);

    private sealed record ScanResult(
        UsageSourceReadStatus Status,
        UsageSourceIssueKind Issue)
    {
        public IReadOnlyList<UsageEvent> Observations { get; init; } = [];
    }

}
