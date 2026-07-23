using WOpenUsage.Core.Cache;
using WOpenUsage.Core.Providers;
using WOpenUsage.Core.Usage;
using WOpenUsage.Providers.Claude;
using WOpenUsage.Providers.Codex;
using WOpenUsage.Providers.Grok;
using WOpenUsage.Providers.OpenCode;
using WOpenUsage.Runtime.Windows.Codex;

namespace WOpenUsage.Cli;

public static class LocalProviderDiagnosticsAccess
{
    private static readonly CatalogEntry[] Catalog =
    [
        new("claude", "Claude", ProviderCapability.LocalUsage),
        new("codex", "Codex", ProviderCapability.Limits),
        new("grok", "Grok Build", ProviderCapability.LocalUsage),
        new("opencode", "OpenCode", ProviderCapability.LocalUsage),
    ];

    public static Task<ProviderDiagnosticsSnapshot> ReadAsync(
        string dataDirectory,
        TimeProvider clock,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(clock);
        var codexFactory = new CodexAppServerQuotaClientFactory(clock);
        return ReadAsync(
            dataDirectory,
            codexFactory,
            DetectLocalProvider,
            cancellationToken);
    }

    internal static async Task<ProviderDiagnosticsSnapshot> ReadAsync(
        string dataDirectory,
        ICodexQuotaClientFactory codexFactory,
        Func<string, bool> detectLocalProvider,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);
        ArgumentNullException.ThrowIfNull(codexFactory);
        ArgumentNullException.ThrowIfNull(detectLocalProvider);
        cancellationToken.ThrowIfCancellationRequested();

        string fullDataDirectory = Path.GetFullPath(dataDirectory);
        CodexClientAvailability codexAvailability;
        try
        {
            codexAvailability = await codexFactory.DetectAsync(cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            codexAvailability = CodexClientAvailability.Unavailable;
        }

        var localDetection = new Dictionary<string, ProviderDetectionStatus>(StringComparer.Ordinal);
        foreach (CatalogEntry entry in Catalog.Where(item => item.Id != "codex"))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                localDetection[entry.Id] = detectLocalProvider(entry.Id)
                    ? ProviderDetectionStatus.Detected
                    : ProviderDetectionStatus.Missing;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception)
            {
                localDetection[entry.Id] = ProviderDetectionStatus.Unavailable;
            }
        }

        UsageInspection usage = await InspectUsageAsync(
            Path.Combine(fullDataDirectory, "scanner", "usage.v1.db"),
            cancellationToken).ConfigureAwait(false);
        SnapshotCacheProbeResult cache = await new SnapshotStore(
                Path.Combine(
                    fullDataDirectory,
                    "cache",
                    "providers",
                    "codex",
                    SnapshotStore.DefaultFileName))
            .ProbeProviderAsync(new ProviderId("codex"), cancellationToken).ConfigureAwait(false);

        ProviderDiagnostic[] providers = Catalog
            .Select(entry => new ProviderDiagnostic(
                entry.Id,
                entry.Name,
                Array.AsReadOnly([entry.Capability]),
                entry.Id == "codex"
                    ? MapCodexDetection(codexAvailability)
                    : localDetection[entry.Id],
                entry.Id == "codex"
                    ? MapCacheData(cache)
                    : usage.ProviderData[entry.Id]))
            .OrderBy(entry => entry.Id, StringComparer.Ordinal)
            .ToArray();
        DoctorCheck[] checks =
        [
            new("codex-cache", MapCacheCheck(cache)),
            new("codex-cli", MapCodexCheck(codexAvailability)),
            new("local-usage-claude", MapUsageCheck(usage.ProviderData["claude"])),
            new("local-usage-grok", MapUsageCheck(usage.ProviderData["grok"])),
            new("local-usage-opencode", MapUsageCheck(usage.ProviderData["opencode"])),
            new("usage-db", usage.DatabaseStatus),
        ];

        return new ProviderDiagnosticsSnapshot(
            Array.AsReadOnly(providers),
            Array.AsReadOnly(checks));
    }

    private static bool DetectLocalProvider(string providerId) => providerId switch
    {
        "claude" => new ClaudeUsageEventSource("UTC").IsRootAvailable,
        "grok" => new GrokUsageEventSource("UTC").IsRootAvailable,
        "opencode" => new OpenCodeUsageEventSource("UTC").IsRootAvailable,
        _ => throw new ArgumentOutOfRangeException(nameof(providerId)),
    };

    private static async Task<UsageInspection> InspectUsageAsync(
        string databasePath,
        CancellationToken cancellationToken)
    {
        var states = new Dictionary<string, ProviderDataStatus>(StringComparer.Ordinal)
        {
            ["claude"] = ProviderDataStatus.Absent,
            ["grok"] = ProviderDataStatus.Absent,
            ["opencode"] = ProviderDataStatus.Absent,
        };
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

    private static ProviderDetectionStatus MapCodexDetection(
        CodexClientAvailability availability) => availability switch
        {
            CodexClientAvailability.Available => ProviderDetectionStatus.Detected,
            CodexClientAvailability.MissingCli => ProviderDetectionStatus.Missing,
            _ => ProviderDetectionStatus.Unavailable,
        };

    private static DoctorCheckStatus MapCodexCheck(CodexClientAvailability availability) =>
        MapCodexDetection(availability) switch
        {
            ProviderDetectionStatus.Detected => DoctorCheckStatus.Detected,
            ProviderDetectionStatus.Missing => DoctorCheckStatus.Missing,
            _ => DoctorCheckStatus.Unavailable,
        };

    private static ProviderDataStatus MapCacheData(SnapshotCacheProbeResult result) => result switch
    {
        SnapshotCacheProbeResult.Present => ProviderDataStatus.Present,
        SnapshotCacheProbeResult.Missing => ProviderDataStatus.Absent,
        SnapshotCacheProbeResult.UnsupportedVersion => ProviderDataStatus.UnsupportedSchema,
        _ => ProviderDataStatus.Unreadable,
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

    private sealed record CatalogEntry(
        string Id,
        string Name,
        ProviderCapability Capability);

    private sealed record UsageInspection(
        DoctorCheckStatus DatabaseStatus,
        IReadOnlyDictionary<string, ProviderDataStatus> ProviderData);
}
