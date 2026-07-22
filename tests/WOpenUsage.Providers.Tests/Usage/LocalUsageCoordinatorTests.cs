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
            _ => key,
        });

        Assert.Equal("Sin datos", FindValue(card, "UsageProductCard.ReportedCost"));
        Assert.Equal("Sin datos", FindValue(card, "UsageProductCard.EstimatedCost"));
        Assert.DoesNotContain("0 USD", card.Metrics.Select(metric => metric.Value));
        Assert.Equal("100", FindValue(card, "UsageProductCard.UnpricedUsage"));
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
            _ => key,
        };

        _ = await coordinator.RefreshAsync(strings);
        LocalUsageCard missing = await coordinator.RefreshAsync(strings);

        Assert.Equal("100", FindValue(missing, "UsageProductCard.TotalTokens"));
        Assert.Equal("LocalUsageClaudeNoDataNotice", missing.NoticeText);
    }

    private static string FindValue(LocalUsageCard card, string automationId) =>
        Assert.Single(card.Metrics, metric => metric.AutomationId == automationId).Value;

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private static UsageSourceReadResult Result(
        long tokens,
        UsageSourceReadStatus status) => new(
        [new UsageEvent(
            new UsageEventKey(Convert.ToHexString(SHA256.HashData(
                Encoding.UTF8.GetBytes($"grok-{tokens}"))).ToLowerInvariant()),
            new AgentId("grok"),
            new ModelProviderId("xai"),
            new ModelId("grok-4.5-build"),
            Now,
            "UTC",
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
