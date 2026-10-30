using TokenUsage.Core.Automation;
using TokenUsage.Core.Cache;
using TokenUsage.Core.Providers;
using TokenUsage.Core.Usage;
using TokenUsage.Providers.Codex;
using TokenUsage.Runtime.Windows.Codex;
using TokenUsage.Runtime.Windows.Providers;

namespace TokenUsage.Runtime.Windows.Automation;

public sealed class WindowsProviderDiagnosticsQuery
{
    private readonly string _dataDirectory;
    private readonly ICodexQuotaClientFactory _codexFactory;
    private readonly Func<string, bool> _detectLocalProvider;

    public WindowsProviderDiagnosticsQuery(string dataDirectory, TimeProvider clock)
        : this(
            dataDirectory,
            new CodexAppServerQuotaClientFactory(
                clock ?? throw new ArgumentNullException(nameof(clock))),
            DetectLocalProvider)
    {
    }

    public WindowsProviderDiagnosticsQuery(
        string dataDirectory,
        ICodexQuotaClientFactory codexFactory,
        Func<string, bool> detectLocalProvider)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);
        _dataDirectory = Path.GetFullPath(dataDirectory);
        _codexFactory = codexFactory ?? throw new ArgumentNullException(nameof(codexFactory));
        _detectLocalProvider = detectLocalProvider
            ?? throw new ArgumentNullException(nameof(detectLocalProvider));
    }

    public async Task<ProviderDiagnosticsSnapshot> ExecuteAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ProviderDetectionStatus codexDetection = await DetectCodexAsync(cancellationToken)
            .ConfigureAwait(false);
        Dictionary<string, ProviderDetectionStatus> localDetection =
            DetectLocalProviders(cancellationToken);
        UsageInspection usage = await InspectUsageAsync(
            Path.Combine(_dataDirectory, "scanner", "usage.v1.db"),
            cancellationToken).ConfigureAwait(false);
        Dictionary<string, SnapshotCacheProbeResult> caches = await InspectCachesAsync(
            cancellationToken).ConfigureAwait(false);

        ProviderDiagnostic[] providers = WindowsProviderCatalog.Entries
            .Select(entry => new ProviderDiagnostic(
                entry.Id.Value,
                entry.DisplayName,
                entry.Capabilities,
                entry.Id.Value switch
                {
                    "codex" => codexDetection,
                    _ => localDetection[entry.Id.Value],
                },
                entry.CacheDirectoryName is not null && entry.LocalUsageAgentId is not null
                    ? CombineData(
                        MapCacheData(caches[entry.Id.Value]),
                        usage.ProviderData[entry.Id.Value])
                    : entry.CacheDirectoryName is not null
                        ? MapCacheData(caches[entry.Id.Value])
                        : usage.ProviderData[entry.Id.Value]))
            .OrderBy(entry => entry.Id, StringComparer.Ordinal)
            .ToArray();
        List<DoctorCheck> checks = CreateChecks(
            codexDetection,
            usage,
            caches);

        return new ProviderDiagnosticsSnapshot(
            Array.AsReadOnly(providers),
            checks.AsReadOnly());
    }

    private async Task<ProviderDetectionStatus> DetectCodexAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            CodexClientAvailability availability = await _codexFactory
                .DetectAsync(cancellationToken)
                .ConfigureAwait(false);
            return availability switch
            {
                CodexClientAvailability.Available => ProviderDetectionStatus.Detected,
                CodexClientAvailability.MissingCli => ProviderDetectionStatus.Missing,
                _ => ProviderDetectionStatus.Unavailable,
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return ProviderDetectionStatus.Unavailable;
        }
    }

    private Dictionary<string, ProviderDetectionStatus> DetectLocalProviders(
        CancellationToken cancellationToken)
    {
        var detection = new Dictionary<string, ProviderDetectionStatus>(StringComparer.Ordinal);
        foreach (WindowsProviderCatalogEntry entry in WindowsProviderCatalog.Entries.Where(
                     candidate => candidate.LocalUsageAgentId is not null))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                detection[entry.Id.Value] = _detectLocalProvider(entry.Id.Value)
                    ? ProviderDetectionStatus.Detected
                    : ProviderDetectionStatus.Missing;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception)
            {
                detection[entry.Id.Value] = ProviderDetectionStatus.Unavailable;
            }
        }

        return detection;
    }

    private async Task<Dictionary<string, SnapshotCacheProbeResult>> InspectCachesAsync(
        CancellationToken cancellationToken)
    {
        var caches = new Dictionary<string, SnapshotCacheProbeResult>(StringComparer.Ordinal);
        foreach (WindowsProviderCatalogEntry entry in WindowsProviderCatalog.Entries.Where(
                     candidate => candidate.CacheDirectoryName is not null))
        {
            caches[entry.Id.Value] = await new SnapshotStore(
                    Path.Combine(
                        _dataDirectory,
                        "cache",
                        "providers",
                        entry.CacheDirectoryName!,
                        SnapshotStore.DefaultFileName))
                .ProbeProviderAsync(entry.Id, cancellationToken).ConfigureAwait(false);
        }

        return caches;
    }

    private static List<DoctorCheck> CreateChecks(
        ProviderDetectionStatus codexDetection,
        UsageInspection usage,
        Dictionary<string, SnapshotCacheProbeResult> caches)
    {
        var checks = new List<DoctorCheck>();
        foreach (WindowsProviderCatalogEntry entry in WindowsProviderCatalog.Entries)
        {
            if (entry.DetectionCheckId is not null)
            {
                checks.Add(new DoctorCheck(
                    entry.DetectionCheckId,
                    MapDetectionCheck(codexDetection)));
            }

            checks.Add(new DoctorCheck(
                entry.DataCheckId!,
                entry.CacheDirectoryName is not null && !entry.DataCheckId!.StartsWith("local-usage-", StringComparison.Ordinal)
                    ? MapCacheCheck(caches[entry.Id.Value])
                    : MapUsageCheck(usage.ProviderData[entry.Id.Value])));
        }

        checks.Add(new DoctorCheck("usage-db", usage.DatabaseStatus));
        return checks;
    }

    private static bool DetectLocalProvider(string providerId)
    {
        WindowsProviderCatalogEntry entry = WindowsProviderCatalog.Entries.Single(candidate =>
            string.Equals(candidate.Id.Value, providerId, StringComparison.Ordinal));
        return entry.CreateLocalUsageSource("UTC")?.IsRootAvailable
            ?? throw new ArgumentOutOfRangeException(nameof(providerId));
    }

    private static async Task<UsageInspection> InspectUsageAsync(
        string databasePath,
        CancellationToken cancellationToken)
    {
        var states = WindowsProviderCatalog.Entries
            .Where(entry => entry.LocalUsageAgentId is not null)
            .ToDictionary(
                entry => entry.Id.Value,
                _ => ProviderDataStatus.Absent,
                StringComparer.Ordinal);
        if (!File.Exists(databasePath))
        {
            return new UsageInspection(DoctorCheckStatus.Absent, states);
        }

        try
        {
            UsageRepository repository = await UsageRepository.OpenReadOnlyAsync(
                databasePath,
                cancellationToken).ConfigureAwait(false);
            foreach (string providerId in states.Keys.ToArray())
            {
                states[providerId] = await repository.HasUsageForAgentAsync(
                        new AgentId(providerId),
                        cancellationToken)
                    .ConfigureAwait(false)
                    ? ProviderDataStatus.Present
                    : ProviderDataStatus.Absent;
            }

            return new UsageInspection(DoctorCheckStatus.Present, states);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is UsageSchemaTooNewException
                                           or UsageSchemaTooOldException)
        {
            SetAll(states, ProviderDataStatus.UnsupportedSchema);
            return new UsageInspection(DoctorCheckStatus.UnsupportedSchema, states);
        }
        catch (Exception)
        {
            SetAll(states, ProviderDataStatus.Unreadable);
            return new UsageInspection(DoctorCheckStatus.Unreadable, states);
        }
    }

    private static void SetAll(
        IDictionary<string, ProviderDataStatus> states,
        ProviderDataStatus status)
    {
        foreach (string key in states.Keys.ToArray())
        {
            states[key] = status;
        }
    }

    private static ProviderDataStatus MapCacheData(SnapshotCacheProbeResult result) => result switch
    {
        SnapshotCacheProbeResult.Present => ProviderDataStatus.Present,
        SnapshotCacheProbeResult.Missing => ProviderDataStatus.Absent,
        SnapshotCacheProbeResult.UnsupportedVersion => ProviderDataStatus.UnsupportedSchema,
        _ => ProviderDataStatus.Unreadable,
    };

    private static ProviderDataStatus CombineData(
        ProviderDataStatus first,
        ProviderDataStatus second)
    {
        if (first == ProviderDataStatus.Present || second == ProviderDataStatus.Present)
        {
            return ProviderDataStatus.Present;
        }

        if (first == ProviderDataStatus.UnsupportedSchema
            || second == ProviderDataStatus.UnsupportedSchema)
        {
            return ProviderDataStatus.UnsupportedSchema;
        }

        return first == ProviderDataStatus.Unreadable || second == ProviderDataStatus.Unreadable
            ? ProviderDataStatus.Unreadable
            : ProviderDataStatus.Absent;
    }

    private static DoctorCheckStatus MapDetectionCheck(ProviderDetectionStatus status) =>
        status switch
        {
            ProviderDetectionStatus.Detected => DoctorCheckStatus.Detected,
            ProviderDetectionStatus.Missing => DoctorCheckStatus.Missing,
            _ => DoctorCheckStatus.Unavailable,
        };

    private static DoctorCheckStatus MapCacheCheck(SnapshotCacheProbeResult result) =>
        MapUsageCheck(MapCacheData(result));

    private static DoctorCheckStatus MapUsageCheck(ProviderDataStatus status) => status switch
    {
        ProviderDataStatus.Present => DoctorCheckStatus.Present,
        ProviderDataStatus.Absent => DoctorCheckStatus.Absent,
        ProviderDataStatus.UnsupportedSchema => DoctorCheckStatus.UnsupportedSchema,
        _ => DoctorCheckStatus.Unreadable,
    };

    private sealed record UsageInspection(
        DoctorCheckStatus DatabaseStatus,
        IReadOnlyDictionary<string, ProviderDataStatus> ProviderData);
}
