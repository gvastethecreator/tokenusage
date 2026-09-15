using System.Globalization;
using TokenUsage.Core.Automation;
using TokenUsage.Core.Usage;

namespace TokenUsage.App.ViewModels.Reports;

public sealed record UsageDashboardBar(string Id, string Name, string ValueText, double Percent, string AutomationName);

public sealed partial class UsageReportViewModel
{
    public IReadOnlyList<UsageDashboardBar> DashboardModels { get; private set; } = [];
    public IReadOnlyList<UsageDashboardBar> DashboardProjects { get; private set; } = [];
    public IReadOnlyList<UsageDashboardBar> DashboardComponents { get; private set; } = [];
    public string DashboardModelSummary { get; private set; } = string.Empty;
    public string DashboardProjectSummary { get; private set; } = string.Empty;
    public string DashboardModelRemainder { get; private set; } = string.Empty;
    public string DashboardProjectRemainder { get; private set; } = string.Empty;
    public string DashboardTrendSummary { get; private set; } = string.Empty;
    public string DashboardUnit => GetString(IsCostMetric ? "UsageDashboardKnownCostUnit" : "UsageDashboardTokensUnit");
    public bool HasDashboardProjects => _codexProjectsAllowed && DashboardProjects.Count > 0;
    public string DashboardCacheSummary => Overview?.CacheShareAvailability == UsageOverviewFactKind.Measured
        ? string.Format(CultureInfo.CurrentCulture, GetString("UsageDashboardCacheSummaryFormat"),
            FormatPercent((Overview.CacheSharePercent ?? 0) / 100m))
        : OverviewCacheCard?.Value ?? GetString("UsageExplorerNotAvailable");

    public IReadOnlyList<UsageDashboardBar> DashboardOperationBars { get; private set; } = [];
    public IReadOnlyList<UsageDashboardBar> DashboardActivityBars { get; private set; } = [];
    public string DashboardOperationSummary { get; private set; } = string.Empty;
    public string DashboardActivitySummary { get; private set; } = string.Empty;
    public string DashboardOperationStatus { get; private set; } = string.Empty;
    public string DashboardMixedNotice { get; private set; } = string.Empty;
    public string DashboardActivityMethodText { get; private set; } = string.Empty;
    public bool IsDashboardOperationsLoading { get; private set; }
    public bool DashboardShowsMcp { get; private set; }
    public bool HasDashboardOperationStatus => !string.IsNullOrEmpty(DashboardOperationStatus);
    public bool HasDashboardMixedNotice => !string.IsNullOrEmpty(DashboardMixedNotice);
    public bool HasDashboardRankedOperations => DashboardOperationBars.Count > 0;
    public bool HasDashboardActivity => DashboardActivityBars.Count > 0;
    public bool HasDashboardCoreTools => _dashboardExactRows.Any(row => row.Kind == UsageOperationKind.Tool);
    public bool HasDashboardMcpTools => _dashboardExactRows.Any(row => row.Kind == UsageOperationKind.Mcp);
    public bool HasDashboardOperationKindSwitch => HasDashboardCoreTools && HasDashboardMcpTools;
    public bool IsDashboardCoreToolsSelected => !DashboardShowsMcp;
    public bool HasDashboardOperationsCard => !IsCompareScope
        && (IsDashboardOperationsLoading
            || HasDashboardRankedOperations
            || HasDashboardActivity
            || HasDashboardOperationStatus
            || HasDashboardMixedNotice);
    public bool CanOpenDashboardOperations => HasDashboardOperationsCard
        && (_codexMcpAllowed || _codexSkillsAllowed || _codexCommandsAllowed || _codexFilesAllowed)
        && !DashboardOmitsCodexOperations;
    public int DashboardOperationsApplyCount { get; private set; }
    public int DashboardExactOperationCount => DashboardKindRows.Length;
    public bool DashboardOmitsCodexOperations =>
        (IsProviderScope && SelectedProvider is { } provider
            && !string.Equals(provider.ProviderId, "codex", StringComparison.Ordinal))
        || !_report.Agents.Any(agent => string.Equals(agent.AgentId.Value, "codex", StringComparison.Ordinal));

    public void SetDashboardShowsMcp(bool mcp)
    {
        if (DashboardShowsMcp == mcp)
        {
            return;
        }

        DashboardShowsMcp = mcp;
        RebuildDashboardOperationPreview();
        NotifyDashboardOperations();
    }

    public async Task WaitForDashboardOperationsAsync()
    {
        DateTime limit = DateTime.UtcNow.AddSeconds(20);
        while (!_disposed && IsDashboardOperationsLoading && DateTime.UtcNow < limit)
        {
            await Task.Delay(20).ConfigureAwait(true);
        }

        if (IsDashboardOperationsLoading)
        {
            throw new TimeoutException("Dashboard operations did not settle.");
        }
    }

    public async Task OpenDashboardOperationsAsync()
    {
        if (!CanOpenDashboardOperations && !HasOperations)
        {
            return;
        }

        _operationsFromDashboard = true;
        await OpenOperationsAsync().ConfigureAwait(true);
    }

    private UsageExplorerOperationRow[] DashboardKindRows => _dashboardExactRows
        .Where(row => DashboardShowsMcp
            ? row.Kind == UsageOperationKind.Mcp
            : row.Kind == UsageOperationKind.Tool)
        .ToArray();

    private void QueueDashboardOperations()
    {
        _dashboardOperationsCancellation?.Cancel();
        _dashboardOperationsCancellation?.Dispose();
        _dashboardOperationsCancellation = null;
        if (IsCompareScope || _disposed)
        {
            _dashboardOperationsKey = string.Empty;
            ClearDashboardOperations(status: string.Empty);
            return;
        }

        string key = string.Join('|',
            StartDate.ToString("o", CultureInfo.InvariantCulture),
            EndDate.ToString("o", CultureInfo.InvariantCulture),
            _consentGeneration.ToString(CultureInfo.InvariantCulture),
            CurrentOperationScope().Kind,
            SelectedProvider?.ProviderId ?? string.Empty,
            DashboardOmitsCodexOperations,
            _codexMcpAllowed,
            _codexSkillsAllowed,
            _codexCommandsAllowed,
            _codexFilesAllowed);
        if (key == _dashboardOperationsKey && !IsDashboardOperationsLoading)
        {
            return;
        }

        _dashboardOperationsKey = key;
        IsDashboardOperationsLoading = true;
        OnPropertyChanged(nameof(IsDashboardOperationsLoading));
        OnPropertyChanged(nameof(HasDashboardOperationsCard));
        var cancellation = CancellationTokenSource.CreateLinkedTokenSource(
            _selectionCancellation?.Token ?? CancellationToken.None,
            _loadCancellation?.Token ?? CancellationToken.None);
        _dashboardOperationsCancellation = cancellation;
        _ = RefreshDashboardOperationsAsync(_selectionGeneration, _consentGeneration, cancellation.Token);
    }

    private async Task RefreshDashboardOperationsAsync(
        long generation,
        long consentGeneration,
        CancellationToken token)
    {
        try
        {
            if (DashboardOmitsCodexOperations)
            {
                if (IsStaleDashboardCompletion(generation, consentGeneration))
                {
                    return;
                }

                ClearDashboardOperations(GetString("UsageExplorerOperationsUnsupported"));
                return;
            }

            if (!_codexMcpAllowed && !_codexSkillsAllowed && !_codexCommandsAllowed && !_codexFilesAllowed)
            {
                if (IsStaleDashboardCompletion(generation, consentGeneration))
                {
                    return;
                }

                ClearDashboardOperations(GetString("UsageExplorerOperationsDisabled"));
                return;
            }

            OperationLoadScope scope = CurrentOperationScope();
            OperationCohort cohort = await RunReportWorkAsync(
                () => LoadOperationCohortAsync(scope, token),
                token).ConfigureAwait(true);
            if (IsStaleDashboardCompletion(generation, consentGeneration))
            {
                return;
            }

            ApplyDashboardOperationCohort(cohort);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            if (!IsStaleDashboardCompletion(generation, consentGeneration))
            {
                ClearDashboardOperations(
                    GetString("UsageReportReadFailed") + " " + exception.GetType().Name);
            }
        }
        finally
        {
            if (!IsStaleDashboardCompletion(generation, consentGeneration))
            {
                IsDashboardOperationsLoading = false;
                OnPropertyChanged(nameof(IsDashboardOperationsLoading));
                OnPropertyChanged(nameof(HasDashboardOperationsCard));
            }
        }
    }

    private bool IsStaleDashboardCompletion(long generation, long consentGeneration) =>
        _disposed || generation != _selectionGeneration || consentGeneration != _consentGeneration;

    private void ApplyDashboardOperationCohort(OperationCohort cohort)
    {
        _dashboardExactRows = cohort.Proved.Select(MapOperationRow).ToArray();
        _dashboardMixedRows = cohort.Mixed.Select(MapOperationRow).ToArray();
        if (!HasDashboardCoreTools && HasDashboardMcpTools)
        {
            DashboardShowsMcp = true;
        }
        else if (HasDashboardCoreTools && !HasDashboardMcpTools)
        {
            DashboardShowsMcp = false;
        }

        IReadOnlyList<UsageExplorerDerivedActivityRow> activity = MapDerivedActivityRows(
            UsageDerivedActivity.Summarize(cohort.Proved));
        _dashboardActivityRows = activity.Where(row => row.Count > 0).ToArray();
        DashboardActivityMethodText = string.Format(
            CultureInfo.CurrentCulture,
            GetString("UsageExplorerDerivedActivityMethodFormat"),
            UsageDerivedActivity.MethodVersion,
            UsageDerivedActivity.CountUnit);
        DashboardMixedNotice = _dashboardMixedRows.Length > 0
            ? string.Format(
                CultureInfo.CurrentCulture,
                GetString("UsageDashboardMixedNoticeFormat"),
                _dashboardMixedRows.Length)
            : string.Empty;
        DashboardOperationStatus = _dashboardExactRows.Length == 0 && _dashboardActivityRows.Length == 0
            ? GetString("UsageDashboardNoRankedOperations")
            : string.Empty;
        RebuildDashboardOperationPreview();
        DashboardOperationsApplyCount++;
        NotifyDashboardOperations();
    }

    private void ClearDashboardOperations(string status)
    {
        _dashboardExactRows = [];
        _dashboardMixedRows = [];
        _dashboardActivityRows = [];
        DashboardShowsMcp = false;
        DashboardOperationBars = [];
        DashboardActivityBars = [];
        DashboardOperationSummary = string.Empty;
        DashboardActivitySummary = string.Empty;
        DashboardMixedNotice = string.Empty;
        DashboardActivityMethodText = string.Empty;
        DashboardOperationStatus = status;
        IsDashboardOperationsLoading = false;
        NotifyDashboardOperations();
    }

    private void RebuildDashboardOperationPreview()
    {
        UsageExplorerOperationRow[] rows = DashboardKindRows;
        int maximum = rows.Select(row => row.InvocationCount).DefaultIfEmpty().Max();
        DashboardOperationBars = rows
            .OrderByDescending(row => row.InvocationCount)
            .ThenBy(row => row.Id, StringComparer.Ordinal)
            .Take(5)
            .Select(row => new UsageDashboardBar(
                row.Id,
                row.Label,
                row.CountText,
                maximum > 0 ? 100d * row.InvocationCount / maximum : 0,
                row.Label + ", " + row.CountText + ", " + row.OutcomeText))
            .ToArray();
        DashboardOperationSummary = rows.Length == 0
            ? string.Empty
            : string.Format(
                CultureInfo.CurrentCulture,
                GetString("UsageDashboardOperationsSummaryFormat"),
                Math.Min(5, rows.Length),
                rows.Length,
                rows.Sum(row => (long)row.InvocationCount));
        int activityMaximum = _dashboardActivityRows.Select(row => row.Count).DefaultIfEmpty().Max();
        DashboardActivityBars = _dashboardActivityRows
            .OrderByDescending(row => row.Count)
            .ThenBy(row => row.Category, StringComparer.Ordinal)
            .Take(5)
            .Select(row => new UsageDashboardBar(
                row.Category,
                row.Label,
                row.CountText,
                activityMaximum > 0 ? 100d * row.Count / activityMaximum : 0,
                row.Label + ", " + row.CountText))
            .ToArray();
        var largest = _dashboardActivityRows.OrderByDescending(row => row.Count).FirstOrDefault();
        long activityTotal = _dashboardActivityRows.Sum(row => (long)row.Count);
        DashboardActivitySummary = largest is not null && activityTotal > 0
            ? string.Format(
                CultureInfo.CurrentCulture,
                GetString("UsageDashboardActivitySummaryFormat"),
                largest.Label,
                FormatPercent((decimal)largest.Count / activityTotal))
            : string.Empty;
    }

    private void NotifyDashboardOperations()
    {
        foreach (string property in new[]
        {
            nameof(DashboardOperationBars),
            nameof(DashboardActivityBars),
            nameof(DashboardOperationSummary),
            nameof(DashboardActivitySummary),
            nameof(DashboardOperationStatus),
            nameof(DashboardMixedNotice),
            nameof(DashboardActivityMethodText),
            nameof(HasDashboardOperationStatus),
            nameof(HasDashboardMixedNotice),
            nameof(HasDashboardRankedOperations),
            nameof(HasDashboardActivity),
            nameof(HasDashboardCoreTools),
            nameof(HasDashboardMcpTools),
            nameof(HasDashboardOperationKindSwitch),
            nameof(IsDashboardCoreToolsSelected),
            nameof(HasDashboardOperationsCard),
            nameof(CanOpenDashboardOperations),
            nameof(DashboardShowsMcp),
            nameof(DashboardExactOperationCount),
            nameof(DashboardOperationsApplyCount),
            nameof(IsDashboardOperationsLoading),
        })
        {
            OnPropertyChanged(property);
        }
    }

    private void RebuildDashboard()
    {
        decimal Value(UsageReportMetrics metrics) => IsCostMetric
            ? ReportDataProjection.KnownCost(metrics) ?? 0 : metrics.Tokens.Total;
        decimal denominator = Value(_report.Totals);
        string Display(UsageReportMetrics metrics) => IsCostMetric
            ? ExactCost(ReportDataProjection.KnownCost(metrics)) : FormatTokens(metrics.Tokens.Total);
        string Summary(string name, decimal amount) => denominator > 0
            ? string.Format(CultureInfo.CurrentCulture, GetString("UsageDashboardLeaderFormat"), name,
                FormatPercent(amount / denominator), DashboardUnit)
            : GetString("UsageDashboardNoRankedValue");
        string Remainder(int shown, int count, IEnumerable<UsageReportMetrics> remainder) => string.Format(
            CultureInfo.CurrentCulture, GetString("UsageDashboardRemainderFormat"), shown, count,
            IsCostMetric ? ExactCost(remainder.Sum(Value)) : FormatTokens((long)remainder.Sum(Value)));
        var models = ModelRows.OrderByDescending(row => Value(row.Metrics))
            .ThenBy(row => row.Id, StringComparer.Ordinal).ToArray();
        decimal modelMaximum = models.Select(row => Value(row.Metrics)).DefaultIfEmpty().Max();
        DashboardModels = models.Take(5).Select(row => new UsageDashboardBar(row.Id,
            row.ModelName + " · " + row.ProviderName, Display(row.Metrics),
            modelMaximum > 0 ? (double)(100 * Value(row.Metrics) / modelMaximum) : 0, row.AutomationName)).ToArray();
        DashboardModelSummary = models.Length > 0 ? Summary(models[0].ModelName, Value(models[0].Metrics)) : string.Empty;
        DashboardModelRemainder = Remainder(Math.Min(5, models.Length), models.Length, models.Skip(5).Select(row => row.Metrics));
        // The full overview is built before presentation limits. Never rank a pre-truncated Other group.
        var projects = ProjectOverviewRows.Where(row => !row.IsOther)
            .OrderByDescending(row => Value(row.Metrics)).ThenBy(row => row.Id, StringComparer.Ordinal).ToArray();
        decimal projectMaximum = projects.Select(row => Value(row.Metrics)).DefaultIfEmpty().Max();
        DashboardProjects = projects.Take(5).Select(row => new UsageDashboardBar(row.Id, row.Name,
            Display(row.Metrics), projectMaximum > 0 ? (double)(100 * Value(row.Metrics) / projectMaximum) : 0,
            row.AutomationName)).ToArray();
        DashboardProjectSummary = projects.Length > 0 ? Summary(projects[0].Name, Value(projects[0].Metrics)) : string.Empty;
        DashboardProjectRemainder = Remainder(Math.Min(5, projects.Length), projects.Length, projects.Skip(5).Select(row => row.Metrics))
            + " · " + string.Format(CultureInfo.CurrentCulture, GetString("UsageDashboardUnassignedFormat"),
                FormatTokens(projects.Where(row => row.IsUnassigned).Sum(row => row.Metrics.Tokens.Total)));
        TokenBreakdown tokens = _report.Totals.Tokens;
        (string Key, long Value)[] components =
        [
            ("UsageDashboardInput", tokens.Input), ("UsageDashboardCacheRead", tokens.CacheRead),
            ("UsageDashboardOutput", tokens.Output), ("UsageDashboardReasoning", tokens.Reasoning),
            ("UsageDashboardCacheWrite", tokens.CacheWrite),
        ];
        DashboardComponents = components.Where(item => item.Value > 0).Select(item => new UsageDashboardBar(
            item.Key, GetString(item.Key), FormatTokens(item.Value),
            tokens.Total > 0 ? 100d * item.Value / tokens.Total : 0,
            GetString(item.Key) + ": " + FormatTokens(item.Value))).ToArray();
        var peak = _report.Days.Where(day => !IsCostMetric || ReportDataProjection.KnownCost(day.Metrics) is not null)
            .OrderByDescending(day => Value(day.Metrics)).ThenBy(day => day.Date).FirstOrDefault();
        DashboardTrendSummary = peak is not null && denominator > 0
            ? string.Format(CultureInfo.CurrentCulture, GetString("UsageDashboardPeakFormat"),
                peak.Date.ToString("d MMM", CultureInfo.CurrentCulture), FormatPercent(Value(peak.Metrics) / denominator), DashboardUnit)
            : GetString("UsageDashboardNoRankedValue");
        foreach (string property in new[] { nameof(DashboardModels), nameof(DashboardProjects), nameof(DashboardComponents),
            nameof(DashboardModelSummary), nameof(DashboardProjectSummary), nameof(DashboardModelRemainder),
            nameof(DashboardProjectRemainder), nameof(DashboardTrendSummary), nameof(DashboardUnit),
            nameof(HasDashboardProjects), nameof(DashboardCacheSummary) }) OnPropertyChanged(property);
        QueueDashboardOperations();
    }
}
