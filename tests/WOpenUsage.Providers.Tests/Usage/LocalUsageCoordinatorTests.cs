using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using WOpenUsage.App.Services;
using WOpenUsage.App.ViewModels;
using WOpenUsage.App.ViewModels.Sample;
using WOpenUsage.Core.Providers;
using WOpenUsage.Core.Usage;
using WOpenUsage.Providers.Fakes;
using WOpenUsage.Providers.Claude;
using System.Text.Json;

namespace WOpenUsage.Providers.Tests.Usage;

public sealed class LocalUsageCoordinatorTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 7, 22, 15, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task FixtureFlowsThroughSqliteIntoASeparatedUsageCard()
    {
        using var folder = new TemporaryFolder();
        var clock = new FixedTimeProvider(Now);
        var source = new SyntheticUsageEventSource(clock, "Argentina Standard Time");
        var coordinator = new LocalUsageCoordinator(folder.DatabasePath, source, clock);

        LocalUsageCard card = await coordinator.RefreshAsync(key => key switch
        {
            "LocalUsageTitle" => "Uso local",
            "LocalUsageSourceSynthetic" => "Fixture sintético · SQLite local",
            "LocalUsagePeriod30Days" => "Últimos 30 días",
            "LocalUsageNotice" => "Datos locales de prueba",
            "LocalUsageReportedCost" => "Coste informado",
            "LocalUsageEstimatedCost" => "Coste estimado",
            "LocalUsageUnpricedTokens" => "Tokens sin precio",
            "LocalUsageTotalTokens" => "Tokens",
            "LocalUsageCoverage" => "Cobertura de coste",
            "LocalUsageUsdFormat" => "${0:0.00} USD",
            "LocalUsageUsdCompactFormat" => "${0:0.00}",
            "CodexUsageMissing" => "Sin datos",
            _ => key,
        });

        Assert.Equal("Uso local", card.Title);
        Assert.Equal("Fixture sintético · SQLite local", card.SourceLabel);
        Assert.Equal(
            string.Format(CultureInfo.CurrentCulture, "${0:0.00} USD", 1.84m),
            FindValue(card, "UsageProductCard.ReportedCost"));
        Assert.Equal(
            string.Format(CultureInfo.CurrentCulture, "${0:0.00} USD", 0.62m),
            FindValue(card, "UsageProductCard.EstimatedCost"));
        Assert.Equal("9.460", FindValue(card, "UsageProductCard.UnpricedUsage"));

        UsageRepository cliRepository = await UsageRepository.OpenAsync(folder.DatabasePath);
        IReadOnlyList<DailyUsageRollup> cliRollups = await cliRepository.QueryDailyRollupsAsync(
            new DateOnly(2026, 6, 23),
            new DateOnly(2026, 7, 22));
        Assert.Equal(3, cliRollups.Sum(rollup => rollup.EventCount));
    }

    [Fact]
    public void MissingCostIsPresentedAsMissingInsteadOfZero()
    {
        var rollup = new DailyUsageRollup(
            new DateOnly(2026, 7, 22),
            "Argentina Standard Time",
            new AgentId("antigravity"),
            new ModelProviderId("google"),
            new ModelId("gemini-2.5-pro"),
            new TokenBreakdown(100, 0, 0, 0, 0),
            reportedCostUsd: null,
            estimatedCostUsd: null,
            unpricedTokens: 100,
            unavailableCostEventCount: 1,
            eventCount: 1,
            CoverageKind.Unpriced);

        LocalUsageCard card = LocalUsageCardProjector.Create([rollup], key => key switch
        {
            "CodexUsageMissing" => "Sin datos",
            "LocalUsageUsdFormat" => "${0:0.00} USD",
            "LocalUsageUsdCompactFormat" => "${0:0.00}",
            _ => key,
        });

        Assert.Equal("Sin datos", FindValue(card, "UsageProductCard.ReportedCost"));
        Assert.Equal("Sin datos", FindValue(card, "UsageProductCard.EstimatedCost"));
        Assert.DoesNotContain("0 USD", card.Metrics.Select(metric => metric.Value));
        Assert.Equal("100", FindValue(card, "UsageProductCard.UnpricedUsage"));
    }

    [Fact]
    public void BuildsCivilPeriodsWithoutLeakingPreviousMonthIntoCurrentMonth()
    {
        DateOnly today = new(2026, 7, 22);
        DailyUsageRollup[] rollups =
        [
            Rollup(new DateOnly(2026, 6, 30), "claude", "old", 100, 3m, null, 0),
            Rollup(new DateOnly(2026, 7, 16), "claude", "week-edge", 200, null, 2m, 0),
            Rollup(new DateOnly(2026, 7, 21), "grok", "yesterday", 300, 4m, null, 0),
            Rollup(today, "opencode", "today", 400, 1m, null, 100),
        ];

        LocalUsageCard card = LocalUsageCardProjector.Create(
            rollups,
            Strings,
            today: today);

        AssertPeriod(card, "UsageProductCard.Period.Today", "$1", "75%", "400");
        AssertPeriod(card, "UsageProductCard.Period.Yesterday", "$4", "100%", "300");
        AssertPeriod(card, "UsageProductCard.Period.7Days", "$5", "$2", "900");
        AssertPeriod(card, "UsageProductCard.Period.Month", "$5", "$2", "900");
        Assert.Equal(
            string.Format(CultureInfo.CurrentCulture, "${0:0.00} USD", 8m),
            FindValue(card, "UsageProductCard.ReportedCost"));
        Assert.Equal(
            string.Format(CultureInfo.CurrentCulture, "${0:0.00} USD", 2m),
            FindValue(card, "UsageProductCard.EstimatedCost"));
    }

    [Fact]
    public void HeatmapBuildsThirtyFiveCivilDaysAndCombinesDailyAgents()
    {
        DateOnly today = new(2026, 7, 22);
        DateOnly firstDay = today.AddDays(-34);
        LocalUsageCard card = LocalUsageCardProjector.Create(
            [
                Rollup(firstDay, "claude", "oldest", 100, null, null, 100),
                Rollup(today, "claude", "reported", 1_000, 1m, null, 0),
                Rollup(today, "grok", "estimated", 500, null, 2m, 0),
                Rollup(today.AddDays(-35), "opencode", "outside", 9_999, 9m, null, 0),
            ],
            Strings,
            today: today);

        Assert.Equal(35, card.Heatmap.Cells.Count);
        Assert.Equal(firstDay, card.Heatmap.Cells[0].Date);
        Assert.Equal(today, card.Heatmap.Cells[^1].Date);
        Assert.Equal(2, card.Heatmap.Cells.Count(cell => cell.HasActivity));
        Assert.Equal(1_500, card.Heatmap.Cells[^1].TotalTokens);
        Assert.Equal(2, card.Heatmap.Cells[^1].EventCount);
        Assert.Equal(4, card.Heatmap.Cells[^1].Level);
        Assert.Contains("US$", card.Heatmap.Cells[^1].AccessibleName, StringComparison.Ordinal);
        Assert.DoesNotContain(card.Heatmap.Cells, cell => cell.TotalTokens == 9_999);
    }

    [Fact]
    public void BreakdownKeepsZeroAndUnpricedModelsButChartsOnlyPositiveSpend()
    {
        DateOnly today = new(2026, 7, 22);
        DailyUsageRollup[] rollups =
        [
            Rollup(today, "claude", "claude-sonnet", 100, null, 2m, 0),
            Rollup(today.AddDays(-1), "claude", "claude-sonnet", 50, null, 1m, 0),
            Rollup(today, "opencode", "free-model", 200, null, null, 200),
            Rollup(today, "grok", "grok-zero", 25, 0m, null, 0),
        ];

        LocalUsageCard card = LocalUsageCardProjector.Create(
            rollups,
            Strings,
            today: today);

        SampleSpendSlice slice = Assert.Single(card.SpendBreakdown.AgentSlices);
        Assert.Equal("claude", slice.ProviderId);
        Assert.Equal(3d, slice.Amount);
        Assert.Equal(3, card.SpendBreakdown.Models.Count);
        Assert.Contains("3 agentes", card.SpendBreakdown.SummaryText, StringComparison.Ordinal);
        LocalUsageModelRow unpriced = Assert.Single(
            card.SpendBreakdown.Models,
            row => row.ModelName == "free-model");
        Assert.Contains("0%", unpriced.CoverageText, StringComparison.Ordinal);
        LocalUsageModelRow zero = Assert.Single(
            card.SpendBreakdown.Models,
            row => row.ModelName == "grok-zero");
        Assert.Contains(
            string.Format(CultureInfo.CurrentCulture, "${0:0.00} USD", 0m),
            zero.ReportedText,
            StringComparison.Ordinal);
        Assert.StartsWith("46", FindValue(card, "UsageProductCard.CostCoverage"), StringComparison.Ordinal);
    }

    [Fact]
    public void ReportedZeroIsDataAndDoesNotCreateAnEmptyRingSlice()
    {
        DateOnly today = new(2026, 7, 22);
        LocalUsageCard card = LocalUsageCardProjector.Create(
            [Rollup(today, "grok", "grok-zero", 25, 0m, null, 0)],
            Strings,
            today: today);

        Assert.Equal(
            string.Format(CultureInfo.CurrentCulture, "${0:0.00} USD", 0m),
            FindValue(card, "UsageProductCard.ReportedCost"));
        Assert.Equal("100%", FindValue(card, "UsageProductCard.CostCoverage"));
        Assert.Empty(card.SpendBreakdown.AgentSlices);
        Assert.Single(card.SpendBreakdown.Models);
        Assert.True(card.SpendBreakdown.HasContent);
    }

    [Fact]
    public void PartialClaudeReadUsesAnExplicitCoverageNotice()
    {
        LocalUsageCard card = LocalUsageCardProjector.Create(
            [],
            key => key,
            SourceKind.LocalLog,
            UsageSourceReadStatus.Partial);

        Assert.Equal("LocalUsageClaudePartialNotice", card.NoticeText);
    }

    [Fact]
    public void EmptyClaudeReadUsesAnExplicitNoDataNotice()
    {
        LocalUsageCard card = LocalUsageCardProjector.Create(
            [],
            key => key,
            SourceKind.LocalLog,
            UsageSourceReadStatus.NoData);

        Assert.Equal("LocalUsageClaudeNoDataNotice", card.NoticeText);
    }

    [Fact]
    public void ProviderStatusKeepsQuotaUsageSpendAndCoverageIndependent()
    {
        DateOnly today = new(2026, 7, 22);
        LocalUsageCard card = LocalUsageCardProjector.Create(
            [Rollup(today, "grok", "grok-4.5-build", 100, 2m, null, 0)],
            Strings,
            SourceKind.LocalLog,
            UsageSourceReadStatus.Partial,
            hasMultipleRealSources: true,
            today: today,
            sourceDiagnostics:
            [
                new(new AgentId("grok"), UsageSourceReadStatus.Complete, UsageSourceIssueKind.None, true),
                new(new AgentId("opencode"), UsageSourceReadStatus.NoData, UsageSourceIssueKind.RootUnavailable, true),
            ]);

        ProviderStatusRow grok = Assert.Single(card.ProviderStatuses, row => row.ProviderId == "grok");
        Assert.Equal("ProviderStatusBlocked", Capability(grok, "ProviderStatus.grok.Quota"));
        Assert.Equal("ProviderStatusComplete", Capability(grok, "ProviderStatus.grok.Usage"));
        Assert.Equal("ProviderStatusReported", Capability(grok, "ProviderStatus.grok.Spend"));
        Assert.Equal("100%", Capability(grok, "ProviderStatus.grok.Coverage"));

        ProviderStatusRow openCode = Assert.Single(card.ProviderStatuses, row => row.ProviderId == "opencode");
        Assert.Equal("ProviderStatusNotConfigured", Capability(openCode, "ProviderStatus.opencode.Usage"));
        Assert.Equal("ProviderStatusRootMissing", openCode.RootState);
        Assert.DoesNotContain(":\\", string.Join(' ', card.ProviderStatuses.Select(row => row.AutomationName)), StringComparison.Ordinal);
    }

    [Fact]
    public void RetainedSnapshotSpendIsLabeledAsLastReliable()
    {
        DateOnly today = new(2026, 7, 22);
        LocalUsageCard card = LocalUsageCardProjector.Create(
            [Rollup(today, "grok", "grok-4.5-build", 100, 2m, null, 0)],
            Strings,
            SourceKind.LocalLog,
            UsageSourceReadStatus.NoData,
            today: today,
            sourceDiagnostics:
            [
                new(
                    new AgentId("grok"),
                    UsageSourceReadStatus.NoData,
                    UsageSourceIssueKind.RootUnavailable,
                    RetainsLastReliableSnapshot: true),
            ]);

        ProviderStatusRow grok = Assert.Single(card.ProviderStatuses);
        Assert.Equal(
            "ProviderStatusReportedLastReliable",
            Capability(grok, "ProviderStatus.grok.Spend"));
        Assert.Equal(
            "ProviderStatusCoverageLastReliableFormat",
            Capability(grok, "ProviderStatus.grok.Coverage"));
    }

    [Fact]
    public async Task ClaudeCorpusFlowsIntoTheRealLocalUsageCard()
    {
        using var folder = new TemporaryFolder();
        string configRoot = Path.Combine(folder.Path, "claude");
        string projectRoot = Directory.CreateDirectory(
            Path.Combine(configRoot, "projects", "project-a")).FullName;
        File.WriteAllText(
            Path.Combine(projectRoot, "session.jsonl"),
            JsonSerializer.Serialize(new
            {
                type = "assistant",
                timestamp = "2026-07-22T12:00:00.000Z",
                requestId = "request-1",
                message = new
                {
                    id = "message-1",
                    model = "claude-sonnet-4-6",
                    content = "private fixture content",
                    usage = new { input_tokens = 1_000L, output_tokens = 200L },
                },
            }));
        var source = new ClaudeUsageEventSource(
            "UTC",
            folder.Path,
            configDirectoryOverride: configRoot);
        var coordinator = new LocalUsageCoordinator(
            folder.DatabasePath,
            source,
            new FixedTimeProvider(Now));

        LocalUsageCard card = await coordinator.RefreshAsync(key => key switch
        {
            "LocalUsageTitle" => "Uso local",
            "LocalUsageSourceClaude" => "Claude Code · logs locales",
            "LocalUsagePeriod30Days" => "Últimos 30 días",
            "LocalUsageClaudeNotice" => "Solo sesiones guardadas en este equipo.",
            "LocalUsageReportedCost" => "Coste informado",
            "LocalUsageEstimatedCost" => "Coste estimado",
            "LocalUsageUnpricedTokens" => "Tokens sin precio",
            "LocalUsageTotalTokens" => "Tokens",
            "LocalUsageCoverage" => "Cobertura de coste",
            "LocalUsageUsdFormat" => "${0:0.00} USD",
            "LocalUsageUsdCompactFormat" => "${0:0.00}",
            "CodexUsageMissing" => "Sin datos",
            _ => key,
        });

        Assert.Equal("Claude Code · logs locales", card.SourceLabel);
        Assert.Equal("1.200", FindValue(card, "UsageProductCard.TotalTokens"));
        Assert.NotEqual("Sin datos", FindValue(card, "UsageProductCard.EstimatedCost"));
        Assert.Equal("100%", FindValue(card, "UsageProductCard.CostCoverage"));
    }

    [Fact]
    public async Task PartialSnapshotReadKeepsTheLastReliableTotals()
    {
        using var folder = new TemporaryFolder();
        var source = new SequenceSnapshotSource(
        [
            Result(100, UsageSourceReadStatus.Complete),
            Result(900, UsageSourceReadStatus.Partial),
        ]);
        var coordinator = new LocalUsageCoordinator(
            folder.DatabasePath,
            source,
            new FixedTimeProvider(Now));
        Func<string, string> strings = key => key switch
        {
            "LocalUsageTotalTokens" => "Tokens",
            "CodexUsageMissing" => "Sin datos",
            "LocalUsageUsdFormat" => "${0:0.00} USD",
            "LocalUsageUsdCompactFormat" => "${0:0.00}",
            _ => key,
        };

        _ = await coordinator.RefreshAsync(strings);
        LocalUsageCard partial = await coordinator.RefreshAsync(strings);

        Assert.Equal("100", FindValue(partial, "UsageProductCard.TotalTokens"));
        Assert.Equal("LocalUsageClaudePartialNotice", partial.NoticeText);
    }

    [Fact]
    public async Task MissingSnapshotRootDoesNotEraseTheLastReliableTotals()
    {
        using var folder = new TemporaryFolder();
        var source = new SequenceSnapshotSource(
        [
            Result(100, UsageSourceReadStatus.Complete),
            new UsageSourceReadResult([], UsageSourceReadStatus.NoData),
        ]);
        var coordinator = new LocalUsageCoordinator(
            folder.DatabasePath,
            source,
            new FixedTimeProvider(Now));
        Func<string, string> strings = key => key switch
        {
            "LocalUsageTotalTokens" => "Tokens",
            "CodexUsageMissing" => "Sin datos",
            "LocalUsageUsdFormat" => "${0:0.00} USD",
            "LocalUsageUsdCompactFormat" => "${0:0.00}",
            _ => key,
        };

        _ = await coordinator.RefreshAsync(strings);
        LocalUsageCard missing = await coordinator.RefreshAsync(strings);

        Assert.Equal("100", FindValue(missing, "UsageProductCard.TotalTokens"));
        Assert.Equal("LocalUsageClaudeNoDataNotice", missing.NoticeText);
    }

    [Fact]
    public async Task MixedCompleteAndNoDataSourcesAreReportedAsPartial()
    {
        using var folder = new TemporaryFolder();
        var coordinator = new LocalUsageCoordinator(
            folder.DatabasePath,
            [
                new SequenceSnapshotSource([Result(100, UsageSourceReadStatus.Complete)]),
                new SequenceSnapshotSource([
                    new UsageSourceReadResult([], UsageSourceReadStatus.NoData),
                ]),
            ],
            new FixedTimeProvider(Now));

        LocalUsageCard card = await coordinator.RefreshAsync(Strings);

        Assert.Equal("LocalUsageAgentsPartialNotice", card.NoticeText);
    }

    [Fact]
    public async Task ConflictingGroupingTimeZonesFailBeforeWritingACombinedSnapshot()
    {
        using var folder = new TemporaryFolder();
        var coordinator = new LocalUsageCoordinator(
            folder.DatabasePath,
            [
                new SequenceSnapshotSource([
                    Result(100, UsageSourceReadStatus.Complete, "UTC"),
                ]),
                new SequenceSnapshotSource([
                    Result(200, UsageSourceReadStatus.Complete, "Argentina Standard Time"),
                ]),
            ],
            new FixedTimeProvider(Now));

        await Assert.ThrowsAsync<InvalidDataException>(() => coordinator.RefreshAsync(Strings));

        UsageRepository repository = await UsageRepository.OpenAsync(folder.DatabasePath);
        Assert.Empty(await repository.QueryDailyRollupsAsync(
            new DateOnly(2026, 7, 1),
            new DateOnly(2026, 7, 31)));
    }

    private static string FindValue(LocalUsageCard card, string automationId) =>
        Assert.Single(card.Metrics, metric => metric.AutomationId == automationId).Value;

    private static string Capability(ProviderStatusRow row, string automationId) =>
        Assert.Single(row.Capabilities, capability => capability.AutomationId == automationId).Value;

    private static void AssertPeriod(
        LocalUsageCard card,
        string automationId,
        params string[] expectedParts)
    {
        LocalUsagePeriodRow row = Assert.Single(
            card.OtherPeriods,
            item => item.AutomationId == automationId);
        string text = row.CostText + " " + row.DetailText;
        foreach (string part in expectedParts)
        {
            Assert.Contains(part, text, StringComparison.Ordinal);
        }
    }

    private static DailyUsageRollup Rollup(
        DateOnly date,
        string agent,
        string model,
        long tokens,
        decimal? reported,
        decimal? estimated,
        long unpriced) =>
        new(
            date,
            "UTC",
            new AgentId(agent),
            null,
            new ModelId(model),
            new TokenBreakdown(tokens, 0, 0, 0, 0),
            reported,
            estimated,
            unpriced,
            unpriced > 0 ? 1 : 0,
            eventCount: 1,
            unpriced > 0 ? CoverageKind.Unpriced : CoverageKind.Complete);

    private static string Strings(string key) => key switch
    {
        "CodexUsageMissing" => "Sin datos",
        "LocalUsageUsdFormat" => "${0:0.00} USD",
        "LocalUsageUsdCompactFormat" => "${0:0.00}",
        "LocalUsageUsdPerMillionFormat" => "${0:0.00}/1 M",
        "LocalUsageReportedShort" => "Inf.",
        "LocalUsageEstimatedShort" => "Est.",
        "LocalUsagePeriodCostFormat" => "{0} {1} · {2} {3}",
        "LocalUsagePeriodDetailFormat" => "{0} tokens · {1} · {2}",
        "LocalUsageModelReportedFormat" => "Informado {0}",
        "LocalUsageModelEstimatedFormat" => "Estimado {0}",
        "LocalUsageModelCoverageFormat" => "Cobertura {0}",
        "LocalUsageBreakdownSummaryFormat" => "{0} agentes · {1} modelos",
        "LocalUsageBreakdownAccessibleFormat" => "Total {0}. {1}",
        "UsageHeatmapTitle" => "Actividad diaria",
        "UsageHeatmapSummaryFormat" => "{0} días activos · últimos {1}",
        "UsageHeatmapEmptyDayFormat" => "{0}: sin actividad",
        "UsageHeatmapDayFormat" => "{0}: {1} tokens en {2} eventos; {3}",
        "UsageHeatmapCostFormat" => "{0:0.00} US$",
        "UsageHeatmapCostUnavailable" => "gasto no disponible",
        "LocalUsageAgentClaude" => "Claude",
        "LocalUsageAgentGrok" => "Grok Build",
        "LocalUsageAgentOpenCode" => "OpenCode",
        _ => key,
    };

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private static UsageSourceReadResult Result(
        long tokens,
        UsageSourceReadStatus status,
        string groupingTimeZoneId = "UTC") => new(
        [new UsageEvent(
            new UsageEventKey(Convert.ToHexString(SHA256.HashData(
                Encoding.UTF8.GetBytes($"grok-{tokens}"))).ToLowerInvariant()),
            new AgentId("grok"),
            new ModelProviderId("xai"),
            new ModelId("grok-4.5-build"),
            Now,
            groupingTimeZoneId,
            new TokenBreakdown(tokens, 0, 0, 0, 0),
            CostObservation.ProviderReported(0m),
            "grok-build/1",
            CoverageKind.Complete)],
        status);

    private sealed class SequenceSnapshotSource(
        IReadOnlyList<UsageSourceReadResult> results) : ISnapshotUsageEventSource
    {
        private int _index;

        public AgentId AgentId { get; } = new("grok");

        public SourceKind SourceKind => SourceKind.LocalLog;

        public Task<UsageSourceReadResult> ReadAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(results[Math.Min(_index++, results.Count - 1)]);
        }
    }

    private sealed class TemporaryFolder : IDisposable
    {
        private readonly string _path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "wopenusage-usage-integration",
            Guid.NewGuid().ToString("N"));

        public TemporaryFolder() => Directory.CreateDirectory(_path);

        public string Path => _path;

        public string DatabasePath => System.IO.Path.Combine(_path, "usage.v1.db");

        public void Dispose() => Directory.Delete(_path, recursive: true);
    }
}
