using TokenUsage.App.ViewModels.Reports;
using TokenUsage.Core.Automation;
using TokenUsage.Core.Providers;
using TokenUsage.Core.Usage;

namespace TokenUsage.Architecture.Tests;

public sealed class ReportProjectionTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 30, 12, 0, 0, TimeSpan.Zero);

    private static UsageReportMetrics Metrics(long tokens) =>
        new(1, new TokenBreakdown(tokens, 0, 0, 0, 0), null, tokens / 1000m, 0, 0, CoverageKind.Complete);

    private static UsageReport Report(params (string Agent, string Model, long Tokens)[] models) => new(
        Metrics(models.Sum(model => model.Tokens)),
        models.GroupBy(model => model.Agent).Select(group => new UsageAgentReport(new AgentId(group.Key), Metrics(group.Sum(item => item.Tokens)))).ToArray(),
        models.Select(model => new UsageModelReport(new AgentId(model.Agent), null, new ModelId(model.Model), Metrics(model.Tokens))).ToArray(),
        [], [], []);

    [Fact]
    public void WaterfallChangesAddUpToTheDifferenceBetweenPeriods()
    {
        UsageReport previous = Report(("codex", "gpt-a", 60), ("claude", "opus", 40));
        UsageReport current = Report(("codex", "gpt-a", 100), ("claude", "opus", 30), ("claude", "sonnet", 20));

        IReadOnlyList<ReportStep> steps = ReportProjection.Waterfall(current, previous, key => key);

        Assert.Equal(100, steps[0].Tokens);
        Assert.Equal(150, steps[^1].Tokens);
        Assert.Equal([40L, 20L, -10L], steps.Skip(1).SkipLast(1).Select(step => step.Tokens));
        Assert.Equal("gpt-a", steps[1].Detail);
    }

    [Theory]
    [InlineData(70, ReportTone.Warning)]
    [InlineData(100, ReportTone.Critical)]
    [InlineData(20, ReportTone.Neutral)]
    public void PaceProjectsTheCurrentAverageToTheReset(decimal used, ReportTone tone)
    {
        DateTimeOffset start = Now.AddDays(-3.5);
        var cycle = new QuotaResetCycle("claude", "quota.seven-day", start, Now, true, 10080m, used, Now,
            start.AddDays(7), null, null, null);
        var quota = new ReportQuotaInput("Claude · weekly", "claude", [cycle],
            new Dictionary<DateTimeOffset, IReadOnlyList<QuotaObservation>>(), null);

        ReportPace pace = Assert.IsType<ReportPace>(ReportProjection.Pace(quota, Now, key => key + " {0}"));

        Assert.Equal(tone, pace.Tone);
        Assert.Equal(used >= 100 ? null : (double)used * 2, pace.ProjectedAtReset);
        Assert.Equal(0.5, pace.Current.Points[^1].Elapsed, 3);
    }

    [Fact]
    public void PaceFollowsTheScheduleWhenAResetWentUnobserved()
    {
        // History still holds the start from 20 days ago, but the 30-day window restarted 3 days ago.
        var cycle = new QuotaResetCycle("codex", "quota.primary", Now.AddDays(-20), Now, true, 43200m, 10m, Now,
            Now.AddDays(27), null, null, null);
        var quota = new ReportQuotaInput("Codex", "codex", [cycle],
            new Dictionary<DateTimeOffset, IReadOnlyList<QuotaObservation>>(), null);

        ReportPace pace = Assert.IsType<ReportPace>(ReportProjection.Pace(quota, Now, key => key + " {0}"));

        Assert.Equal(Now.AddDays(-3), ReportProjection.Window(cycle, Now).Start);
        Assert.Equal(0.1, pace.Current.Points[^1].Elapsed, 3);
        Assert.Equal(100, pace.ProjectedAtReset!.Value, 3);
    }

    [Fact]
    public void ResetsShowTokensPerQuotaPointOnlyWhenTokensAreAttributable()
    {
        DateTimeOffset start = Now.AddDays(-7);
        var closed = new QuotaResetCycle("codex", "quota.weekly", start, Now, false, 10080m, 50m, Now, Now, null, null, null);
        var observations = new Dictionary<DateTimeOffset, IReadOnlyList<QuotaObservation>>();

        ReportReset attributable = Assert.Single(ReportProjection.Resets(new ReportQuotaInput("Codex", "codex", [closed], observations,
            new Dictionary<DateTimeOffset, long> { [start] = 50_000 }), 6, Now, key => key));
        ReportReset shared = Assert.Single(ReportProjection.Resets(new ReportQuotaInput("Codex · Profile 1", "codex", [closed],
            observations, null), 6, Now, key => key));

        Assert.Equal("1K", attributable.TokensPerPercent);
        Assert.Equal("ReportNotAttributable", shared.Tokens);
        Assert.Equal("—", shared.TokensPerPercent);
    }
}
