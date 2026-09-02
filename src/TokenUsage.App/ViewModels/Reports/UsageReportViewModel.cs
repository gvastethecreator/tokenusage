using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.Windows.ApplicationModel.Resources;
using TokenUsage.App.Controls;
using TokenUsage.App.Localization;
using TokenUsage.App.ViewModels;
using TokenUsage.App.ViewModels.Dashboard;
using TokenUsage.Core.Automation;
using TokenUsage.Core.Providers;
using TokenUsage.Core.Usage;

namespace TokenUsage.App.ViewModels.Reports;

public sealed partial class UsageReportViewModel : ObservableObject, IDisposable
{
    private readonly ResourceLoader _resources = new();
    private readonly string _databasePath;
    private readonly Func<Task> _refreshSourceAsync;
    private readonly Func<string, IReadOnlyList<QuotaWindow>> _getProviderLimits;
    private readonly QuotaResetHistoryStore? _resetHistoryStore;
    private readonly TimeProvider _clock;
    private readonly Dictionary<string, Brush> _providerBrushes = new(StringComparer.Ordinal);
    private CancellationTokenSource? _loadCancellation;
    private UsageReport _report = UsageReportQuery.Build([]);
    private UsageReport _globalReport = UsageReportQuery.Build([]);
    private UsageReport _compareRightReport = UsageReportQuery.Build([]);
    private (DateOnly Start, DateOnly End)? _globalReportRange;
    private int _windowDays = 30;
    private UsageReportMetric _metric = UsageReportMetric.Cost;
    private UsageReportBreakdown _breakdown = UsageReportBreakdown.Model;
    private UsageReportScope _scope = UsageReportScope.Global;
    private UsageReportCompareAxis _compareAxis = UsageReportCompareAxis.Providers;
    private UsageReportValueMode _valueMode = UsageReportValueMode.Absolute;
    private UsageReportProviderOption? _selectedProvider;
    private UsageReportPeriodOption? _selectedPeriod;
    private UsageReportProviderOption? _compareLeftProvider;
    private UsageReportProviderOption? _compareRightProvider;
    private UsageReportResetCycleOption? _compareLeftCycle;
    private UsageReportResetCycleOption? _compareRightCycle;
    private DateOnly _compareLeftStart;
    private DateOnly _compareLeftEnd;
    private DateOnly _compareRightStart;
    private DateOnly _compareRightEnd;
    private IReadOnlyList<UsageReportCompareRow> _compareRows = [];
    private bool _isLoading;
    private bool _hasData;
    private bool _hasError;
    private bool _hasCoverageHint;
    private string _statusText = string.Empty;
    private string _coverageHintText = string.Empty;
    private IReadOnlyList<UsageReportProviderRow> _providers = [];
    private IReadOnlyList<UsageReportMetricCard> _metricCards = [];
    private IReadOnlyList<UsageReportModelRow> _modelRows = [];
    private IReadOnlyList<UsageReportDayRow> _dayRows = [];
    private IReadOnlyList<UsageReportQualityRow> _qualityRows = [];
    private IReadOnlyList<UsageReportSourceRow> _sourceRows = [];
    private IReadOnlyList<UsageReportProviderOption> _providerOptions = [];
    private IReadOnlyList<QuotaWindow> _providerLimits = [];
    private QuotaResetHistory _resetHistory = QuotaResetHistory.Empty;
    private IReadOnlyList<UsageReportResetCycleOption> _resetCycleOptions = [];
    private UsageReportResetCycleOption? _selectedResetCycle;
    private bool _usesResetCycle;
    private UsageReportTrendDataset _trend = UsageReportTrendDataset.Empty;
    private bool _disposed;

    public UsageReportViewModel(
        string databasePath,
        Func<Task> refreshSourceAsync,
        UsageReportRequest? initialRequest = null,
        Func<string, IReadOnlyList<QuotaWindow>>? getProviderLimits = null,
        QuotaResetHistoryStore? resetHistoryStore = null,
        TimeProvider? clock = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        _databasePath = Path.GetFullPath(databasePath);
        _refreshSourceAsync = refreshSourceAsync
            ?? throw new ArgumentNullException(nameof(refreshSourceAsync));
        _getProviderLimits = getProviderLimits ?? (_ => []);
        _resetHistoryStore = resetHistoryStore;
        _clock = clock ?? TimeProvider.System;
        PeriodOptions =
        [
            new(1, GetString("UsageReportPeriod1Day")),
            new(3, GetString("UsageReportPeriod3Days")),
            new(7, GetString("UsageReportPeriod7Days")),
            new(30, GetString("UsageReportPeriod30Days")),
            new(90, GetString("UsageReportPeriod90Days")),
        ];
        ApplyRequestCore(initialRequest ?? UsageReportRequest.Global);
        ProviderLimits = _selectedProvider is null
            ? []
            : _getProviderLimits(_selectedProvider.ProviderId);
        RefreshCommand = new AsyncRelayCommand(
            () => LoadAsync(refreshSource: true),
            () => !IsLoading && !_disposed);
        RebuildProjection();
    }

    public IAsyncRelayCommand RefreshCommand { get; }

    public int WindowDays => _windowDays;

    public IReadOnlyList<UsageReportPeriodOption> PeriodOptions { get; }

    public UsageReportPeriodOption SelectedPeriod => _selectedPeriod
        ?? throw new InvalidOperationException("The report period is unavailable.");

    public UsageReportMetric Metric => _metric;

    public UsageReportBreakdown Breakdown => _breakdown;

    public UsageReportScope Scope => _scope;

    public UsageReportValueMode ValueMode => _valueMode;

    public bool IsCostMetric => Metric == UsageReportMetric.Cost;

    public bool IsTokenMetric => Metric == UsageReportMetric.Tokens;

    public bool IsModelBreakdown => Breakdown == UsageReportBreakdown.Model;

    public bool IsDayBreakdown => Breakdown == UsageReportBreakdown.Day;

    public bool IsSourceBreakdown => Breakdown == UsageReportBreakdown.Source;

    public bool IsGlobalScope => Scope == UsageReportScope.Global;

    public bool IsProviderScope => Scope == UsageReportScope.Provider;

    public bool IsCompareScope => Scope == UsageReportScope.Compare;

    public bool IsStandardReportVisible => !IsCompareScope;

    public UsageReportCompareAxis CompareAxis => _compareAxis;

    public bool IsCompareProvidersAxis => CompareAxis == UsageReportCompareAxis.Providers;

    public bool IsComparePeriodsAxis => CompareAxis == UsageReportCompareAxis.Periods;

    public bool IsCompareCyclesAxis => CompareAxis == UsageReportCompareAxis.Cycles;

    public bool IsCompareProviderPickersVisible =>
        IsCompareScope && IsCompareProvidersAxis && HasProviderOptions;

    public bool IsCompareCyclePickersVisible =>
        IsCompareScope && IsCompareCyclesAxis && ResetCycleOptions.Count > 0;

    public bool IsPeriodPickerVisible => !IsCompareCyclesAxis;

    public bool IsAbsoluteValueMode => ValueMode == UsageReportValueMode.Absolute;

    public bool IsShareValueMode => ValueMode == UsageReportValueMode.Share;

    public bool IsValueModeVisible => IsGlobalScope;

    public bool CanUseResetCycles => IsProviderScope
        && string.Equals(SelectedProvider?.ProviderId, "codex", StringComparison.Ordinal)
        && ResetCycleOptions.Count > 0;

    public bool IsResetCycleWindow => _usesResetCycle && CanUseResetCycles;

    public bool HasMultipleResetCycles => IsResetCycleWindow && ResetCycleOptions.Count > 1;

    public bool CanSelectPreviousResetCycle => IsResetCycleWindow
        && SelectedResetCycleIndex >= 0
        && SelectedResetCycleIndex < ResetCycleOptions.Count - 1;

    public bool CanSelectNextResetCycle => IsResetCycleWindow
        && SelectedResetCycleIndex > 0;

    public bool IsLoading
    {
        get => _isLoading;
        private set
        {
            if (SetProperty(ref _isLoading, value))
            {
                RefreshCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public bool HasData
    {
        get => _hasData;
        private set => SetProperty(ref _hasData, value);
    }

    public bool HasError
    {
        get => _hasError;
        private set => SetProperty(ref _hasError, value);
    }

    public bool IsEmpty => !HasData && !HasError && !IsLoading;

    public bool HasCoverageHint
    {
        get => _hasCoverageHint;
        private set => SetProperty(ref _hasCoverageHint, value);
    }

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public string CoverageHintText
    {
        get => _coverageHintText;
        private set => SetProperty(ref _coverageHintText, value);
    }

    public IReadOnlyList<UsageReportProviderRow> Providers
    {
        get => _providers;
        private set => SetProperty(ref _providers, value);
    }

    public IReadOnlyList<UsageReportMetricCard> MetricCards
    {
        get => _metricCards;
        private set => SetProperty(ref _metricCards, value);
    }

    public IReadOnlyList<UsageReportModelRow> ModelRows
    {
        get => _modelRows;
        private set => SetProperty(ref _modelRows, value);
    }

    public IReadOnlyList<UsageReportDayRow> DayRows
    {
        get => _dayRows;
        private set => SetProperty(ref _dayRows, value);
    }

    public IReadOnlyList<UsageReportQualityRow> QualityRows
    {
        get => _qualityRows;
        private set => SetProperty(ref _qualityRows, value);
    }

    public IReadOnlyList<UsageReportSourceRow> SourceRows
    {
        get => _sourceRows;
        private set => SetProperty(ref _sourceRows, value);
    }

    public IReadOnlyList<UsageReportCompareRow> CompareRows
    {
        get => _compareRows;
        private set => SetProperty(ref _compareRows, value);
    }

    public string CompareLeftLabel { get; private set; } = string.Empty;

    public string CompareRightLabel { get; private set; } = string.Empty;

    public string CompareLeftCostText { get; private set; } = string.Empty;

    public string CompareLeftTokensText { get; private set; } = string.Empty;

    public string CompareRightCostText { get; private set; } = string.Empty;

    public string CompareRightTokensText { get; private set; } = string.Empty;

    public string CompareDeltaCostText { get; private set; } = string.Empty;

    public string CompareDeltaTokensText { get; private set; } = string.Empty;

    public bool HasProviderOptions => ProviderOptions.Count > 0;

    public bool IsProviderPickerVisible => IsProviderScope && HasProviderOptions;

    public IReadOnlyList<UsageReportProviderOption> ProviderOptions
    {
        get => _providerOptions;
        private set => SetProperty(ref _providerOptions, value);
    }

    public UsageReportProviderOption? SelectedProvider
    {
        get => _selectedProvider;
        set
        {
            if (SetProperty(ref _selectedProvider, value) && value is not null)
            {
                ProviderLimits = _getProviderLimits(value.ProviderId);
                RebuildResetCycleOptions();
                if (!string.Equals(value.ProviderId, "codex", StringComparison.Ordinal))
                {
                    _usesResetCycle = false;
                }
                OnPropertyChanged(nameof(SelectedProviderName));
                OnPropertyChanged(nameof(HasProviderLimits));
                NotifyRangeChanged();
                if (IsProviderScope && !IsLoading)
                {
                    _ = LoadAsync();
                }
            }
        }
    }

    public UsageReportProviderOption? CompareLeftProvider
    {
        get => _compareLeftProvider;
        set
        {
            if (SetProperty(ref _compareLeftProvider, value)
                && value is not null
                && IsCompareScope
                && IsCompareProvidersAxis
                && !IsLoading)
            {
                _ = LoadAsync();
            }
        }
    }

    public UsageReportProviderOption? CompareRightProvider
    {
        get => _compareRightProvider;
        set
        {
            if (SetProperty(ref _compareRightProvider, value)
                && value is not null
                && IsCompareScope
                && IsCompareProvidersAxis
                && !IsLoading)
            {
                _ = LoadAsync();
            }
        }
    }

    public UsageReportResetCycleOption? CompareLeftCycle
    {
        get => _compareLeftCycle;
        set
        {
            if (SetProperty(ref _compareLeftCycle, value)
                && value is not null
                && IsCompareScope
                && IsCompareCyclesAxis
                && !IsLoading)
            {
                _ = LoadAsync();
            }
        }
    }

    public UsageReportResetCycleOption? CompareRightCycle
    {
        get => _compareRightCycle;
        set
        {
            if (SetProperty(ref _compareRightCycle, value)
                && value is not null
                && IsCompareScope
                && IsCompareCyclesAxis
                && !IsLoading)
            {
                _ = LoadAsync();
            }
        }
    }

    public IReadOnlyList<QuotaWindow> ProviderLimits
    {
        get => _providerLimits;
        private set => SetProperty(ref _providerLimits, value);
    }

    public string SelectedProviderName => SelectedProvider?.Name ?? string.Empty;

    public bool HasProviderLimits => IsProviderScope && ProviderLimits.Count > 0;

    public IReadOnlyList<UsageReportResetCycleOption> ResetCycleOptions
    {
        get => _resetCycleOptions;
        private set => SetProperty(ref _resetCycleOptions, value);
    }

    public UsageReportResetCycleOption? SelectedResetCycle
    {
        get => _selectedResetCycle;
        set
        {
            if (SetProperty(ref _selectedResetCycle, value) && value is not null)
            {
                NotifyRangeChanged();
                if (IsResetCycleWindow && !IsLoading)
                {
                    _ = LoadAsync();
                }
            }
        }
    }

    public string ResetCycleHelpText => SelectedResetCycle is null
        ? GetString("UsageReportResetCycleUnavailable")
        : $"{SelectedResetCycle.AutomationName}{Environment.NewLine}{GetString("UsageReportResetCycleExactBoundaryNote")}";

    public bool HasResetCountSummary => IsProviderScope
        && string.Equals(SelectedProvider?.ProviderId, "codex", StringComparison.Ordinal);

    public int ObservedResetCount
    {
        get
        {
            if (!HasResetCountSummary)
            {
                return 0;
            }

            DateTimeOffset fromUtc;
            DateTimeOffset toUtcExclusive;
            string? metricId = null;
            if (IsResetCycleWindow && SelectedResetCycle is not null)
            {
                fromUtc = SelectedResetCycle.FromUtc;
                toUtcExclusive = SelectedResetCycle.ToUtc;
                metricId = SelectedResetCycle.MetricId;
            }
            else
            {
                fromUtc = LocalDateStartUtc(StartDate);
                toUtcExclusive = LocalDateStartUtc(EndDate.AddDays(1));
            }

            return QuotaResetCountQuery.Count(
                _resetHistory,
                "codex",
                fromUtc,
                toUtcExclusive,
                metricId);
        }
    }

    public string ResetCountText => string.Format(
        CultureInfo.CurrentCulture,
        GetString(ObservedResetCount == 1
            ? "UsageReportResetCountOneFormat"
            : "UsageReportResetCountManyFormat"),
        ObservedResetCount);

    public string ResetCountHelpText => GetString("UsageReportResetCountHelpText");

    public UsageReportTrendDataset Trend
    {
        get => _trend;
        private set => SetProperty(ref _trend, value);
    }

    public string PeriodText
    {
        get
        {
            if (IsCompareScope && IsCompareCyclesAxis
                && CompareLeftCycle is not null
                && CompareRightCycle is not null)
            {
                return $"{CompareLeftCycle.RangeText} · {CompareRightCycle.RangeText}";
            }

            return IsResetCycleWindow && SelectedResetCycle is not null
                ? SelectedResetCycle.RangeText
                : string.Format(
                    CultureInfo.CurrentCulture,
                    GetString("UsageReportPeriodFormat"),
                    StartDate.ToString("d MMM", CultureInfo.CurrentCulture),
                    EndDate.ToString("d MMM", CultureInfo.CurrentCulture));
        }
    }

    public string HeadlineLabel => GetString(
        IsCostMetric ? "UsageReportTotalCostLabel" : "UsageReportProcessedTokensLabel");

    public string HeadlineValue => IsCostMetric
        ? FormatUsd(_report.Totals.TotalCostUsd)
        : FormatTokens(_report.Totals.Tokens.Total);

    public string HeadlineDetail => IsCostMetric
        ? GetString("UsageReportCostBasisHint")
        : string.Format(
            CultureInfo.CurrentCulture,
            GetString("UsageReportTokenEventFormat"),
            _report.Totals.EventCount.ToString("N0", CultureInfo.CurrentCulture));

    public string ChartTitle => GetString(
        IsShareValueMode && IsGlobalScope
            ? "UsageReportDailyShareTitle"
            : IsCostMetric
                ? "UsageReportDailyCostTitle"
                : "UsageReportDailyTokensTitle");

    public string ScopeTitle => IsGlobalScope
        ? GetString("UsageReportGlobalScope")
        : IsCompareScope
            ? GetString("UsageReportCompareScope")
            : SelectedProviderName;

    public string SummaryTokensText => FormatTokens(_report.Totals.Tokens.Total);

    public string SummaryCostText => FormatUsd(_report.Totals.TotalCostUsd);

    public string SummaryCoverageText => FormatPercent(
        _report.Totals.PriceCoveragePercent / 100m);

    public string SummaryQualityText => string.Format(
        CultureInfo.CurrentCulture,
        GetString("UsageReportQualitySummaryFormat"),
        QualityShare(_report.Totals.ReportedCostUsd),
        QualityShare(_report.Totals.EstimatedCostUsd),
        FormatPercent(_report.Totals.Tokens.Total == 0
            ? 0
            : (decimal)_report.Totals.UnpricedTokens / _report.Totals.Tokens.Total));

    public double ReportedCostSharePercent => _report.Totals.TotalCostUsd == 0
        ? 0
        : decimal.ToDouble(
            (_report.Totals.ReportedCostUsd ?? 0) * 100m / _report.Totals.TotalCostUsd);

    public double PriceCoveragePercent => decimal.ToDouble(
        _report.Totals.PriceCoveragePercent);

    public string CacheSummaryText => string.Format(
        CultureInfo.CurrentCulture,
        GetString("UsageReportCacheSummaryFormat"),
        FormatTokens(_report.Totals.Tokens.CacheRead),
        FormatTokens(_report.Totals.Tokens.Input),
        FormatTokens(_report.Totals.Tokens.Output));

    public string CachedInputText => FormatTokens(_report.Totals.Tokens.CacheRead);

    public string UncachedInputText => FormatTokens(_report.Totals.Tokens.Input);

    public string OutputTokensText => FormatTokens(_report.Totals.Tokens.Output);

    private DateOnly EndDate => IsResetCycleWindow && SelectedResetCycle is not null
        ? SelectedResetCycle.ToDate
        : DateOnly.FromDateTime(_clock.GetLocalNow().DateTime);

    private DateOnly StartDate => IsResetCycleWindow && SelectedResetCycle is not null
        ? SelectedResetCycle.FromDate
        : EndDate.AddDays(-(_windowDays - 1));

    private int RangeDayCount => Math.Max(1, EndDate.DayNumber - StartDate.DayNumber + 1);

    public async Task LoadAsync(bool refreshSource = false)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _loadCancellation?.Cancel();
        _loadCancellation?.Dispose();
        var cancellation = new CancellationTokenSource();
        _loadCancellation = cancellation;
        IsLoading = true;
        HasError = false;
        OnPropertyChanged(nameof(IsEmpty));

        try
        {
            if (refreshSource)
            {
                await _refreshSourceAsync();
                cancellation.Token.ThrowIfCancellationRequested();
            }

            await LoadResetCyclesAsync(cancellation.Token).ConfigureAwait(true);
            DateOnly startDate = StartDate;
            DateOnly endDate = EndDate;

            if (File.Exists(_databasePath))
            {
                var query = new UsageReportQuery(_databasePath);

                async Task<UsageReport> ReadRangeAsync(DateOnly start, DateOnly end)
                {
                    return await query.ReadAsync(
                        start,
                        end,
                        cancellationToken: cancellation.Token);
                }

                async Task<UsageReport> ReadResetCycleAsync(
                    UsageReportResetCycleOption cycle)
                {
                    return await query.ReadExactAsync(
                        cycle.FromUtc,
                        cycle.ToUtc,
                        new AgentId("codex"),
                        cancellation.Token);
                }

                // Switching provider keeps the period, and the all-provider read only feeds the
                // picker and the all-provider scope. Reading it again per provider switch doubled
                // the wait for a result the range already produced.
                if (refreshSource || _globalReportRange != (startDate, endDate))
                {
                    _globalReport = await ReadRangeAsync(startDate, endDate);
                    _globalReportRange = (startDate, endDate);
                    RebuildProviderOptions();
                }

                if (IsCompareScope)
                {
                    await ApplyCompareReportsAsync(
                        ReadRangeAsync,
                        ReadResetCycleAsync,
                        startDate,
                        endDate)
                        .ConfigureAwait(true);
                }
                else
                {
                    _report = IsResetCycleWindow && SelectedResetCycle is not null
                        ? await ReadResetCycleAsync(SelectedResetCycle).ConfigureAwait(true)
                        : IsProviderScope && SelectedProvider is not null
                        ? UsageReportQuery.FilterByAgent(
                            _globalReport,
                            new AgentId(SelectedProvider.ProviderId))
                        : _globalReport;
                    _compareRightReport = UsageReportQuery.Build([]);
                }
            }
            else
            {
                _globalReport = UsageReportQuery.Build([]);
                _globalReportRange = null;
                _report = _globalReport;
                _compareRightReport = _globalReport;
                RebuildProviderOptions();
            }
            if (!ReferenceEquals(_loadCancellation, cancellation))
            {
                return;
            }

            StatusText = string.Empty;
            RebuildProjection();
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
        }
        catch (Exception)
        {
            if (ReferenceEquals(_loadCancellation, cancellation))
            {
                HasError = true;
                HasData = false;
                StatusText = GetString("UsageReportReadFailed");
            }
        }
        finally
        {
            if (ReferenceEquals(_loadCancellation, cancellation))
            {
                IsLoading = false;
                OnPropertyChanged(nameof(IsEmpty));
            }
        }
    }

    public void SetWindowDays(int days)
    {
        if (days is not (1 or 3 or 7 or 30 or 90)
            || (days == _windowDays && !IsResetCycleWindow))
        {
            return;
        }

        _windowDays = days;
        _selectedPeriod = FindPeriod(days);
        _usesResetCycle = false;
        OnPropertyChanged(nameof(WindowDays));
        OnPropertyChanged(nameof(SelectedPeriod));
        NotifyRangeChanged();
        _ = LoadAsync();
    }

    public void SetResetCycleWindow()
    {
        if (!CanUseResetCycles || IsResetCycleWindow)
        {
            return;
        }

        _usesResetCycle = true;
        _selectedResetCycle ??= ResetCycleOptions.Count == 0
            ? null
            : ResetCycleOptions[0];
        NotifyRangeChanged();
        _ = LoadAsync();
    }

    public void SelectPreviousResetCycle()
    {
        int index = SelectedResetCycleIndex;
        if (IsResetCycleWindow && index >= 0 && index < ResetCycleOptions.Count - 1)
        {
            SelectedResetCycle = ResetCycleOptions[index + 1];
        }
    }

    public void SelectNextResetCycle()
    {
        int index = SelectedResetCycleIndex;
        if (IsResetCycleWindow && index > 0)
        {
            SelectedResetCycle = ResetCycleOptions[index - 1];
        }
    }

    public void SetMetric(UsageReportMetric metric)
    {
        if (metric == UsageReportMetric.Share || _metric == metric)
        {
            return;
        }

        _metric = metric;
        OnPropertyChanged(nameof(Metric));
        OnPropertyChanged(nameof(IsCostMetric));
        OnPropertyChanged(nameof(IsTokenMetric));
        RebuildProjection();
    }

    public void SetBreakdown(UsageReportBreakdown breakdown)
    {
        if (_breakdown == breakdown)
        {
            return;
        }

        _breakdown = breakdown;
        OnPropertyChanged(nameof(Breakdown));
        OnPropertyChanged(nameof(IsModelBreakdown));
        OnPropertyChanged(nameof(IsSourceBreakdown));
        OnPropertyChanged(nameof(IsDayBreakdown));
    }

    public void SetScope(UsageReportScope scope)
    {
        if (!Enum.IsDefined(scope) || _scope == scope)
        {
            return;
        }

        if (scope == UsageReportScope.Provider && ProviderOptions.Count == 0)
        {
            return;
        }

        _scope = scope;
        if (scope != UsageReportScope.Provider)
        {
            _usesResetCycle = false;
        }
        if (scope == UsageReportScope.Provider && SelectedProvider is null)
        {
            _selectedProvider = ProviderOptions.Count == 0 ? null : ProviderOptions[0];
            ProviderLimits = _selectedProvider is null
                ? []
                : _getProviderLimits(_selectedProvider.ProviderId);
            OnPropertyChanged(nameof(SelectedProvider));
            OnPropertyChanged(nameof(SelectedProviderName));
            OnPropertyChanged(nameof(HasProviderLimits));
        }
        if (scope != UsageReportScope.Global)
        {
            _valueMode = UsageReportValueMode.Absolute;
        }
        if (scope == UsageReportScope.Compare)
        {
            EnsureCompareSelections();
            if (IsCompareCyclesAxis)
            {
                RebuildResetCycleOptions();
            }
        }
        NotifyScopeChanged();
        _ = LoadAsync();
    }

    public void SetCompareAxis(UsageReportCompareAxis axis)
    {
        if (!Enum.IsDefined(axis) || !IsCompareScope || _compareAxis == axis)
        {
            return;
        }

        _compareAxis = axis;
        if (axis == UsageReportCompareAxis.Cycles)
        {
            RebuildResetCycleOptions();
            EnsureCompareCycleSelections();
        }
        else
        {
            _usesResetCycle = false;
        }

        NotifyScopeChanged();
        _ = LoadAsync();
    }

    public void SetValueMode(UsageReportValueMode valueMode)
    {
        if (!Enum.IsDefined(valueMode)
            || !IsGlobalScope
            || _valueMode == valueMode)
        {
            return;
        }

        _valueMode = valueMode;
        OnPropertyChanged(nameof(ValueMode));
        OnPropertyChanged(nameof(IsAbsoluteValueMode));
        OnPropertyChanged(nameof(IsShareValueMode));
        RebuildProjection();
    }

    public void ApplyRequest(UsageReportRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ApplyRequestCore(request);
        NotifyScopeChanged();
        _ = LoadAsync();
    }

    private void ApplyRequestCore(UsageReportRequest request)
    {
        _scope = request.Scope;
        _windowDays = request.WindowDays;
        _selectedPeriod = FindPeriod(_windowDays);
        _usesResetCycle = false;
        _metric = request.Metric;
        _breakdown = request.Breakdown;
        _valueMode = UsageReportValueMode.Absolute;
        _selectedProvider = request.ProviderId is null
            ? null
            : new UsageReportProviderOption(
                request.ProviderId,
                ProviderName(request.ProviderId));
    }

    private void NotifyScopeChanged()
    {
        OnPropertyChanged(nameof(Scope));
        OnPropertyChanged(nameof(IsGlobalScope));
        OnPropertyChanged(nameof(IsProviderScope));
        OnPropertyChanged(nameof(IsCompareScope));
        OnPropertyChanged(nameof(IsStandardReportVisible));
        OnPropertyChanged(nameof(CompareAxis));
        OnPropertyChanged(nameof(IsCompareProvidersAxis));
        OnPropertyChanged(nameof(IsComparePeriodsAxis));
        OnPropertyChanged(nameof(IsCompareCyclesAxis));
        OnPropertyChanged(nameof(IsCompareProviderPickersVisible));
        OnPropertyChanged(nameof(IsCompareCyclePickersVisible));
        OnPropertyChanged(nameof(IsPeriodPickerVisible));
        OnPropertyChanged(nameof(IsProviderPickerVisible));
        OnPropertyChanged(nameof(HasProviderOptions));
        OnPropertyChanged(nameof(IsValueModeVisible));
        OnPropertyChanged(nameof(CanUseResetCycles));
        OnPropertyChanged(nameof(IsResetCycleWindow));
        OnPropertyChanged(nameof(HasMultipleResetCycles));
        OnPropertyChanged(nameof(ResetCycleHelpText));
        OnPropertyChanged(nameof(HasProviderLimits));
        OnPropertyChanged(nameof(ValueMode));
        OnPropertyChanged(nameof(IsAbsoluteValueMode));
        OnPropertyChanged(nameof(IsShareValueMode));
        OnPropertyChanged(nameof(WindowDays));
        OnPropertyChanged(nameof(SelectedPeriod));
        OnPropertyChanged(nameof(PeriodText));
        OnPropertyChanged(nameof(Metric));
        OnPropertyChanged(nameof(IsCostMetric));
        OnPropertyChanged(nameof(IsTokenMetric));
        OnPropertyChanged(nameof(Breakdown));
        OnPropertyChanged(nameof(IsModelBreakdown));
        OnPropertyChanged(nameof(IsSourceBreakdown));
        OnPropertyChanged(nameof(IsDayBreakdown));
        OnPropertyChanged(nameof(ScopeTitle));
        OnPropertyChanged(nameof(ChartTitle));
    }

    private void RebuildProviderOptions()
    {
        // Only providers with usage in the current range. The active catalog still lists
        // unused readers, and putting those in the picker replaces the report with the
        // empty state.
        string[] ids = UsageReportProviderOptionReconciler.SelectUsedProviderIds(
                _globalReport.Agents.Select(agent => (
                    agent.AgentId.Value,
                    agent.Metrics.EventCount,
                    agent.Metrics.Tokens.Total)))
            .ByCuratedRank(id => id)
            .ToArray();
        string? selectedId = _selectedProvider?.ProviderId;
        UsageReportProviderSelectionState state = UsageReportProviderOptionReconciler.Reconcile(
            ProviderOptions,
            selectedId,
            ids,
            ProviderName);
        if (state.OptionsChanged)
        {
            ProviderOptions = state.Options;
        }

        OnPropertyChanged(nameof(HasProviderOptions));
        OnPropertyChanged(nameof(IsProviderPickerVisible));
        if (IsProviderScope && state.Selected is null)
        {
            _scope = UsageReportScope.Global;
            _usesResetCycle = false;
            NotifyScopeChanged();
        }

        if (!ReferenceEquals(_selectedProvider, state.Selected))
        {
            _selectedProvider = state.Selected;
            OnPropertyChanged(nameof(SelectedProvider));
            OnPropertyChanged(nameof(SelectedProviderName));
        }
        ReconcileCompareProviders(state.Options);
        ProviderLimits = _selectedProvider is null
            ? []
            : _getProviderLimits(_selectedProvider.ProviderId);
        RebuildResetCycleOptions();
        OnPropertyChanged(nameof(HasProviderLimits));
        OnPropertyChanged(nameof(ScopeTitle));
        OnPropertyChanged(nameof(IsCompareProviderPickersVisible));
    }

    private async Task LoadResetCyclesAsync(CancellationToken cancellationToken)
    {
        if (_resetHistoryStore is null)
        {
            _resetHistory = QuotaResetHistory.Empty;
            RebuildResetCycleOptions();
            return;
        }

        try
        {
            _resetHistory = await _resetHistoryStore
                .LoadAsync(cancellationToken)
                .ConfigureAwait(true);
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException
            or TimeoutException
            or InvalidOperationException
            or System.Security.SecurityException)
        {
            _resetHistory = QuotaResetHistory.Empty;
        }

        RebuildResetCycleOptions();
    }

    private void RebuildResetCycleOptions()
    {
        if (!NeedsCodexResetCycles)
        {
            ResetCycleOptions = [];
            _selectedResetCycle = null;
            OnPropertyChanged(nameof(SelectedResetCycle));
            OnPropertyChanged(nameof(IsCompareCyclePickersVisible));
            NotifyRangeChanged();
            return;
        }

        IReadOnlyList<QuotaWindow> cycleLimits =
            string.Equals(SelectedProvider?.ProviderId, "codex", StringComparison.Ordinal)
                ? ProviderLimits
                : _getProviderLimits("codex");

        var windowNames = cycleLimits
            .Where(window => !string.IsNullOrWhiteSpace(window.LayoutMetricId))
            .GroupBy(window => window.LayoutMetricId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First().Title, StringComparer.Ordinal);
        foreach (QuotaResetWindowState window in _resetHistory.Windows
                     .Where(window => string.Equals(
                         window.ProviderId,
                         "codex",
                         StringComparison.Ordinal)))
        {
            windowNames.TryAdd(window.MetricId, ResetWindowName(window));
        }

        var windowOrder = cycleLimits
            .Select((window, index) => (window.LayoutMetricId, index))
            .Where(item => !string.IsNullOrWhiteSpace(item.LayoutMetricId))
            .GroupBy(item => item.LayoutMetricId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First().index, StringComparer.Ordinal);
        foreach (QuotaResetWindowState window in _resetHistory.Windows
                     .Where(window => string.Equals(
                         window.ProviderId,
                         "codex",
                         StringComparison.Ordinal))
                     .OrderBy(window => ResetWindowOrder(window.MetricId)))
        {
            windowOrder.TryAdd(window.MetricId, windowOrder.Count);
        }
        string? selectedId = _selectedResetCycle?.Id;
        UsageReportResetCycleOption[] options = QuotaResetCycleQuery.Build(
                _resetHistory,
                "codex",
                _clock.GetUtcNow().ToUniversalTime())
            .Where(cycle => windowNames.Count == 0 || windowNames.ContainsKey(cycle.MetricId))
            .OrderBy(cycle => windowOrder.GetValueOrDefault(cycle.MetricId, int.MaxValue))
            .ThenByDescending(cycle => cycle.IsCurrent)
            .ThenByDescending(cycle => cycle.FromUtc)
            .Select(cycle => CreateResetCycleOption(
                cycle,
                windowNames.GetValueOrDefault(cycle.MetricId, cycle.MetricId)))
            .ToArray();
        ResetCycleOptions = options;
        _selectedResetCycle = options.FirstOrDefault(option => string.Equals(
                option.Id,
                selectedId,
                StringComparison.Ordinal))
            ?? options.FirstOrDefault();
        if (_selectedResetCycle is null)
        {
            _usesResetCycle = false;
        }

        OnPropertyChanged(nameof(SelectedResetCycle));
        if (IsCompareScope && IsCompareCyclesAxis)
        {
            EnsureCompareCycleSelections();
        }

        OnPropertyChanged(nameof(IsCompareCyclePickersVisible));
        NotifyRangeChanged();
    }

    private UsageReportResetCycleOption CreateResetCycleOption(
        QuotaResetCycle cycle,
        string windowName)
    {
        DateTimeOffset localFrom = cycle.FromUtc.ToLocalTime();
        DateTimeOffset localTo = cycle.ToUtc.ToLocalTime();
        DateOnly fromDate = DateOnly.FromDateTime(localFrom.DateTime);
        DateOnly toDate = DateOnly.FromDateTime(localTo.DateTime);
        if (toDate < fromDate)
        {
            toDate = fromDate;
        }

        string displayName = string.Format(
            CultureInfo.CurrentCulture,
            GetString(cycle.IsCurrent
                ? "UsageReportResetCycleCurrentFormat"
                : "UsageReportResetCyclePreviousFormat"),
            windowName,
            localFrom.ToString("d MMM", CultureInfo.CurrentCulture));
        string endText = cycle.IsCurrent
            ? GetString("UsageReportResetCycleNow")
            : localTo.ToString("g", CultureInfo.CurrentCulture);
        string reasonText = cycle.EndingResetKind switch
        {
            QuotaResetDetectionKind.Early => GetString("UsageReportResetCycleEarly"),
            QuotaResetDetectionKind.Scheduled => GetString("UsageReportResetCycleScheduled"),
            QuotaResetDetectionKind.Observed => GetString("UsageReportResetCycleObserved"),
            _ => GetString("UsageReportResetCycleActive"),
        };
        string rangeText = string.Format(
            CultureInfo.CurrentCulture,
            GetString("UsageReportResetCycleRangeFormat"),
            localFrom.ToString("g", CultureInfo.CurrentCulture),
            endText,
            FormatCycleDuration(cycle.ToUtc - cycle.FromUtc),
            reasonText);
        return new UsageReportResetCycleOption(
            $"{cycle.MetricId}:{cycle.FromUtc:O}",
            cycle.MetricId,
            displayName,
            rangeText,
            cycle.FromUtc,
            cycle.ToUtc,
            fromDate,
            toDate,
            cycle.IsCurrent);
    }

    private string ResetWindowName(QuotaResetWindowState window)
    {
        if (window.MetricId.Contains("bengalfox", StringComparison.Ordinal))
        {
            return GetString("CodexWindowSpark");
        }

        if (string.Equals(window.MetricId, "quota.primary", StringComparison.Ordinal))
        {
            return window.WindowDurationMinutes is >= 1_440m
                ? GetString("SampleWindowWeekly")
                : GetString("CodexWindowPrimary");
        }

        return window.MetricId;
    }

    private static int ResetWindowOrder(string metricId) => metricId switch
    {
        "quota.primary" => 0,
        _ when metricId.Contains("bengalfox", StringComparison.Ordinal) => 1,
        _ => 2,
    };

    private string FormatCycleDuration(TimeSpan duration) => duration.TotalDays >= 1d
        ? string.Format(
            CultureInfo.CurrentCulture,
            GetString("UsageReportResetCycleDaysFormat"),
            duration.TotalDays)
        : string.Format(
            CultureInfo.CurrentCulture,
            GetString("UsageReportResetCycleHoursFormat"),
            Math.Max(0d, duration.TotalHours));

    private void NotifyRangeChanged()
    {
        OnPropertyChanged(nameof(CanUseResetCycles));
        OnPropertyChanged(nameof(IsResetCycleWindow));
        OnPropertyChanged(nameof(HasMultipleResetCycles));
        OnPropertyChanged(nameof(CanSelectPreviousResetCycle));
        OnPropertyChanged(nameof(CanSelectNextResetCycle));
        OnPropertyChanged(nameof(SelectedPeriod));
        OnPropertyChanged(nameof(PeriodText));
        OnPropertyChanged(nameof(ResetCycleHelpText));
        OnPropertyChanged(nameof(HasResetCountSummary));
        OnPropertyChanged(nameof(ObservedResetCount));
        OnPropertyChanged(nameof(ResetCountText));
        OnPropertyChanged(nameof(ResetCountHelpText));
    }

    private int SelectedResetCycleIndex
    {
        get
        {
            if (SelectedResetCycle is null)
            {
                return -1;
            }

            for (int index = 0; index < ResetCycleOptions.Count; index++)
            {
                if (string.Equals(
                    ResetCycleOptions[index].Id,
                    SelectedResetCycle.Id,
                    StringComparison.Ordinal))
                {
                    return index;
                }
            }

            return -1;
        }
    }

    private UsageReportPeriodOption FindPeriod(int days) => PeriodOptions.Single(option =>
        option.Days == days);

    private static DateTimeOffset LocalDateStartUtc(DateOnly date)
    {
        DateTime local = DateTime.SpecifyKind(
            date.ToDateTime(TimeOnly.MinValue),
            DateTimeKind.Unspecified);
        return new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(local, TimeZoneInfo.Local));
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _loadCancellation?.Cancel();
        _loadCancellation?.Dispose();
        _loadCancellation = null;
        RefreshCommand.NotifyCanExecuteChanged();
        GC.SuppressFinalize(this);
    }

    private void RebuildProjection()
    {
        HasData = IsCompareScope
            ? _report.Totals.EventCount > 0 || _compareRightReport.Totals.EventCount > 0
            : _report.Totals.EventCount > 0;
        OnPropertyChanged(nameof(IsEmpty));

        Providers = CreateProviderRows();
        MetricCards = CreateMetricCards();
        ModelRows = CreateModelRows();
        SourceRows = CreateSourceRows();
        DayRows = CreateDayRows();
        QualityRows = CreateQualityRows();
        if (IsCompareScope)
        {
            AssignCompareLabels();
            CompareRows = CreateCompareRows();
            Trend = CreateCompareTrend();
        }
        else
        {
            CompareLeftLabel = string.Empty;
            CompareRightLabel = string.Empty;
            CompareLeftCostText = string.Empty;
            CompareLeftTokensText = string.Empty;
            CompareRightCostText = string.Empty;
            CompareRightTokensText = string.Empty;
            CompareDeltaCostText = string.Empty;
            CompareDeltaTokensText = string.Empty;
            CompareRows = [];
            Trend = CreateTrend();
        }

        HasCoverageHint = _report.Totals.Coverage != CoverageKind.Complete
            || _report.Totals.UnpricedTokens > 0
            || _report.Totals.UnavailableCostEventCount > 0
            || (IsCompareScope
                && (_compareRightReport.Totals.Coverage != CoverageKind.Complete
                    || _compareRightReport.Totals.UnpricedTokens > 0
                    || _compareRightReport.Totals.UnavailableCostEventCount > 0));
        CoverageHintText = HasCoverageHint
            ? string.Format(
                CultureInfo.CurrentCulture,
                GetString("UsageReportCoverageHintFormat"),
                CoverageLabel(_report.Totals.Coverage),
                FormatTokens(_report.Totals.UnpricedTokens),
                _report.Totals.UnavailableCostEventCount.ToString(
                    "N0",
                    CultureInfo.CurrentCulture))
            : string.Empty;

        OnPropertyChanged(nameof(HeadlineLabel));
        OnPropertyChanged(nameof(HeadlineValue));
        OnPropertyChanged(nameof(HeadlineDetail));
        OnPropertyChanged(nameof(ChartTitle));
        OnPropertyChanged(nameof(SummaryTokensText));
        OnPropertyChanged(nameof(SummaryCostText));
        OnPropertyChanged(nameof(SummaryCoverageText));
        OnPropertyChanged(nameof(SummaryQualityText));
        OnPropertyChanged(nameof(ReportedCostSharePercent));
        OnPropertyChanged(nameof(PriceCoveragePercent));
        OnPropertyChanged(nameof(CacheSummaryText));
        OnPropertyChanged(nameof(CachedInputText));
        OnPropertyChanged(nameof(UncachedInputText));
        OnPropertyChanged(nameof(OutputTokensText));
        OnPropertyChanged(nameof(CompareLeftLabel));
        OnPropertyChanged(nameof(CompareRightLabel));
        OnPropertyChanged(nameof(CompareLeftCostText));
        OnPropertyChanged(nameof(CompareLeftTokensText));
        OnPropertyChanged(nameof(CompareRightCostText));
        OnPropertyChanged(nameof(CompareRightTokensText));
        OnPropertyChanged(nameof(CompareDeltaCostText));
        OnPropertyChanged(nameof(CompareDeltaTokensText));
        OnPropertyChanged(nameof(PeriodText));
    }

    private bool NeedsCodexResetCycles =>
        string.Equals(SelectedProvider?.ProviderId, "codex", StringComparison.Ordinal)
        || (IsCompareScope && IsCompareCyclesAxis);

    private async Task ApplyCompareReportsAsync(
        Func<DateOnly, DateOnly, Task<UsageReport>> readRangeAsync,
        Func<UsageReportResetCycleOption, Task<UsageReport>> readResetCycleAsync,
        DateOnly startDate,
        DateOnly endDate)
    {
        EnsureCompareSelections();
        switch (CompareAxis)
        {
            case UsageReportCompareAxis.Providers:
                _report = CompareLeftProvider is null
                    ? UsageReportQuery.Build([])
                    : UsageReportQuery.FilterByAgent(
                        _globalReport,
                        new AgentId(CompareLeftProvider.ProviderId));
                _compareRightReport = CompareRightProvider is null
                    ? UsageReportQuery.Build([])
                    : UsageReportQuery.FilterByAgent(
                        _globalReport,
                        new AgentId(CompareRightProvider.ProviderId));
                _compareLeftStart = startDate;
                _compareLeftEnd = endDate;
                _compareRightStart = startDate;
                _compareRightEnd = endDate;
                break;
            case UsageReportCompareAxis.Periods:
                int days = InclusiveDayCount(startDate, endDate);
                DateOnly previousEnd = startDate.AddDays(-1);
                DateOnly previousStart = previousEnd.AddDays(-(days - 1));
                _report = _globalReport;
                _compareRightReport = await readRangeAsync(previousStart, previousEnd)
                    .ConfigureAwait(true);
                _compareLeftStart = startDate;
                _compareLeftEnd = endDate;
                _compareRightStart = previousStart;
                _compareRightEnd = previousEnd;
                break;
            case UsageReportCompareAxis.Cycles:
                UsageReportResetCycleOption? leftCycle = CompareLeftCycle;
                UsageReportResetCycleOption? rightCycle = CompareRightCycle;
                UsageReport leftRaw = leftCycle is null
                    ? UsageReportQuery.Build([])
                    : await readResetCycleAsync(leftCycle)
                        .ConfigureAwait(true);
                UsageReport rightRaw = rightCycle is null
                    ? UsageReportQuery.Build([])
                    : await readResetCycleAsync(rightCycle)
                        .ConfigureAwait(true);
                AgentId codex = new("codex");
                _report = leftCycle is null
                    ? leftRaw
                    : UsageReportQuery.FilterByAgent(leftRaw, codex);
                _compareRightReport = rightCycle is null
                    ? rightRaw
                    : UsageReportQuery.FilterByAgent(rightRaw, codex);
                _compareLeftStart = leftCycle?.FromDate ?? startDate;
                _compareLeftEnd = leftCycle?.ToDate ?? endDate;
                _compareRightStart = rightCycle?.FromDate ?? startDate;
                _compareRightEnd = rightCycle?.ToDate ?? endDate;
                break;
            default:
                _report = _globalReport;
                _compareRightReport = UsageReportQuery.Build([]);
                break;
        }
    }

    private void EnsureCompareSelections()
    {
        ReconcileCompareProviders(ProviderOptions);
        if (IsCompareCyclesAxis)
        {
            EnsureCompareCycleSelections();
        }
    }

    private void ReconcileCompareProviders(IReadOnlyList<UsageReportProviderOption> options)
    {
        string? leftId = _compareLeftProvider?.ProviderId;
        string? rightId = _compareRightProvider?.ProviderId;
        UsageReportProviderOption? left = FindProvider(options, leftId)
            ?? (options.Count == 0 ? null : options[0]);
        UsageReportProviderOption? right = FindProvider(options, rightId)
            ?? FindOtherProvider(options, left?.ProviderId)
            ?? left;
        _compareLeftProvider = left;
        _compareRightProvider = right;
        OnPropertyChanged(nameof(CompareLeftProvider));
        OnPropertyChanged(nameof(CompareRightProvider));
        OnPropertyChanged(nameof(IsCompareProviderPickersVisible));
    }

    private static UsageReportProviderOption? FindProvider(
        IReadOnlyList<UsageReportProviderOption> options,
        string? providerId)
    {
        if (string.IsNullOrWhiteSpace(providerId))
        {
            return null;
        }

        for (int index = 0; index < options.Count; index++)
        {
            if (string.Equals(options[index].ProviderId, providerId, StringComparison.Ordinal))
            {
                return options[index];
            }
        }

        return null;
    }

    private static UsageReportProviderOption? FindOtherProvider(
        IReadOnlyList<UsageReportProviderOption> options,
        string? excludeProviderId)
    {
        for (int index = 0; index < options.Count; index++)
        {
            if (!string.Equals(
                options[index].ProviderId,
                excludeProviderId,
                StringComparison.Ordinal))
            {
                return options[index];
            }
        }

        return null;
    }

    private void EnsureCompareCycleSelections()
    {
        if (ResetCycleOptions.Count == 0)
        {
            _compareLeftCycle = null;
            _compareRightCycle = null;
            OnPropertyChanged(nameof(CompareLeftCycle));
            OnPropertyChanged(nameof(CompareRightCycle));
            return;
        }

        string? leftId = _compareLeftCycle?.Id;
        string? rightId = _compareRightCycle?.Id;
        UsageReportResetCycleOption left = FindCycle(ResetCycleOptions, leftId)
            ?? FindCurrentCycle(ResetCycleOptions)
            ?? ResetCycleOptions[0];
        UsageReportResetCycleOption right = FindCycle(ResetCycleOptions, rightId)
            ?? FindOtherCycle(ResetCycleOptions, left.Id)
            ?? left;
        _compareLeftCycle = left;
        _compareRightCycle = right;
        OnPropertyChanged(nameof(CompareLeftCycle));
        OnPropertyChanged(nameof(CompareRightCycle));
    }

    private static UsageReportResetCycleOption? FindCycle(
        IReadOnlyList<UsageReportResetCycleOption> options,
        string? cycleId)
    {
        if (string.IsNullOrWhiteSpace(cycleId))
        {
            return null;
        }

        for (int index = 0; index < options.Count; index++)
        {
            if (string.Equals(options[index].Id, cycleId, StringComparison.Ordinal))
            {
                return options[index];
            }
        }

        return null;
    }

    private static UsageReportResetCycleOption? FindCurrentCycle(
        IReadOnlyList<UsageReportResetCycleOption> options)
    {
        for (int index = 0; index < options.Count; index++)
        {
            if (options[index].IsCurrent)
            {
                return options[index];
            }
        }

        return null;
    }

    private static UsageReportResetCycleOption? FindOtherCycle(
        IReadOnlyList<UsageReportResetCycleOption> options,
        string excludeId)
    {
        for (int index = 0; index < options.Count; index++)
        {
            if (!string.Equals(options[index].Id, excludeId, StringComparison.Ordinal))
            {
                return options[index];
            }
        }

        return null;
    }

    private void AssignCompareLabels()
    {
        CompareLeftLabel = CompareAxis switch
        {
            UsageReportCompareAxis.Providers => CompareLeftProvider?.Name ?? string.Empty,
            UsageReportCompareAxis.Periods => FormatCompareRange(
                "UsageReportCompareCurrentRangeFormat",
                _compareLeftStart,
                _compareLeftEnd),
            UsageReportCompareAxis.Cycles => CompareLeftCycle?.DisplayName ?? string.Empty,
            _ => string.Empty,
        };
        CompareRightLabel = CompareAxis switch
        {
            UsageReportCompareAxis.Providers => CompareRightProvider?.Name ?? string.Empty,
            UsageReportCompareAxis.Periods => FormatCompareRange(
                "UsageReportComparePreviousRangeFormat",
                _compareRightStart,
                _compareRightEnd),
            UsageReportCompareAxis.Cycles => CompareRightCycle?.DisplayName ?? string.Empty,
            _ => string.Empty,
        };

        UsageReportMetricDelta delta = UsageReportQuery.Subtract(
            _report.Totals,
            _compareRightReport.Totals);
        CompareLeftCostText = FormatUsd(_report.Totals.TotalCostUsd);
        CompareLeftTokensText = FormatTokens(_report.Totals.Tokens.Total);
        CompareRightCostText = FormatUsd(_compareRightReport.Totals.TotalCostUsd);
        CompareRightTokensText = FormatTokens(_compareRightReport.Totals.Tokens.Total);
        CompareDeltaCostText = FormatSignedUsd(delta.TotalCostUsd);
        CompareDeltaTokensText = FormatSignedTokens(delta.Tokens);
    }

    private string FormatCompareRange(string resourceKey, DateOnly start, DateOnly end) =>
        string.Format(
            CultureInfo.CurrentCulture,
            GetString(resourceKey),
            start.ToString("d MMM", CultureInfo.CurrentCulture),
            end.ToString("d MMM", CultureInfo.CurrentCulture));

    private UsageReportCompareRow[] CreateCompareRows()
    {
        UsageReportMetricDelta delta = UsageReportQuery.Subtract(
            _report.Totals,
            _compareRightReport.Totals);
        return
        [
            new(
                GetString("UsageReportCompareTokensMetric"),
                FormatTokens(_report.Totals.Tokens.Total),
                FormatTokens(_compareRightReport.Totals.Tokens.Total),
                FormatSignedTokens(delta.Tokens)),
            new(
                GetString("UsageReportCompareCostMetric"),
                FormatUsd(_report.Totals.TotalCostUsd),
                FormatUsd(_compareRightReport.Totals.TotalCostUsd),
                FormatSignedUsd(delta.TotalCostUsd)),
            new(
                GetString("UsageReportCompareReportedCostMetric"),
                FormatUsd(_report.Totals.ReportedCostUsd ?? 0m),
                FormatUsd(_compareRightReport.Totals.ReportedCostUsd ?? 0m),
                FormatSignedUsd(delta.ReportedCostUsd)),
            new(
                GetString("UsageReportCompareEstimatedCostMetric"),
                FormatUsd(_report.Totals.EstimatedCostUsd ?? 0m),
                FormatUsd(_compareRightReport.Totals.EstimatedCostUsd ?? 0m),
                FormatSignedUsd(delta.EstimatedCostUsd)),
            new(
                GetString("UsageReportCompareUnpricedMetric"),
                FormatTokens(_report.Totals.UnpricedTokens),
                FormatTokens(_compareRightReport.Totals.UnpricedTokens),
                FormatSignedTokens(delta.UnpricedTokens)),
            new(
                GetString("UsageReportCompareEventsMetric"),
                UsageValueFormatter.Count(_report.Totals.EventCount),
                UsageValueFormatter.Count(_compareRightReport.Totals.EventCount),
                FormatSignedCount(delta.EventCount)),
        ];
    }

    private UsageReportTrendDataset CreateCompareTrend()
    {
        bool alignByIndex = CompareAxis != UsageReportCompareAxis.Providers;
        int leftCount = InclusiveDayCount(_compareLeftStart, _compareLeftEnd);
        int rightCount = InclusiveDayCount(_compareRightStart, _compareRightEnd);
        int dayCount = alignByIndex ? Math.Max(leftCount, rightCount) : leftCount;
        UsageReportTrendDay[] days = Enumerable.Range(0, dayCount)
            .Select(offset =>
            {
                DateOnly date = _compareLeftStart.AddDays(offset);
                string label = alignByIndex
                    ? string.Format(
                        CultureInfo.CurrentCulture,
                        GetString("UsageReportCompareDayFormat"),
                        offset + 1)
                    : date.ToString("d MMM", CultureInfo.CurrentCulture);
                return new UsageReportTrendDay(date, label);
            })
            .ToArray();
        double[] leftValues = DailyCompareValues(_report, _compareLeftStart, dayCount);
        double[] rightValues = DailyCompareValues(
            _compareRightReport,
            alignByIndex ? _compareRightStart : _compareLeftStart,
            dayCount);
        return new UsageReportTrendDataset(
            Metric,
            days,
            [
                new UsageReportTrendSeries(
                    "compare-left",
                    CompareLeftLabel,
                    CompareSeriesColor(isRight: false),
                    leftValues),
                new UsageReportTrendSeries(
                    "compare-right",
                    CompareRightLabel,
                    CompareSeriesColor(isRight: true),
                    rightValues),
            ]);
    }

    private double[] DailyCompareValues(UsageReport report, DateOnly start, int dayCount)
    {
        var metricsByDate = report.Days.ToDictionary(day => day.Date, day => day.Metrics);
        return Enumerable.Range(0, dayCount)
            .Select(offset =>
            {
                DateOnly date = start.AddDays(offset);
                if (!metricsByDate.TryGetValue(date, out UsageReportMetrics? metrics))
                {
                    return 0d;
                }

                return IsCostMetric
                    ? (double)metrics.TotalCostUsd
                    : metrics.Tokens.Total;
            })
            .ToArray();
    }

    private string CompareSeriesColor(bool isRight)
    {
        if (CompareAxis == UsageReportCompareAxis.Providers)
        {
            string? providerId = isRight
                ? CompareRightProvider?.ProviderId
                : CompareLeftProvider?.ProviderId;
            if (!string.IsNullOrWhiteSpace(providerId))
            {
                return ProviderColorPalette.GetEffectiveHex(providerId, null);
            }
        }

        return isRight ? "#F97316" : "#3B82F6";
    }

    private static int InclusiveDayCount(DateOnly start, DateOnly end) =>
        Math.Max(1, end.DayNumber - start.DayNumber + 1);

    private string FormatSignedUsd(decimal amount)
    {
        if (amount == 0)
        {
            return FormatUsd(0);
        }

        string magnitude = FormatUsd(Math.Abs(amount));
        return amount > 0 ? "+" + magnitude : "\u2212" + magnitude;
    }

    private static string FormatSignedTokens(long amount)
    {
        if (amount == 0)
        {
            return FormatTokens(0);
        }

        string magnitude = FormatTokens(Math.Abs(amount));
        return amount > 0 ? "+" + magnitude : "\u2212" + magnitude;
    }

    private static string FormatSignedCount(int amount)
    {
        if (amount == 0)
        {
            return UsageValueFormatter.Count(0);
        }

        string magnitude = UsageValueFormatter.Count(Math.Abs(amount));
        return amount > 0 ? "+" + magnitude : "\u2212" + magnitude;
    }

    private UsageReportProviderRow[] CreateProviderRows()
    {
        decimal totalCost = _report.Totals.TotalCostUsd;
        long totalTokens = _report.Totals.Tokens.Total;
        return _report.Agents
            .ByCuratedRank(agent => agent.AgentId.Value)
            .Select(agent =>
            {
                decimal share = IsCostMetric
                    ? totalCost == 0 ? 0 : agent.Metrics.TotalCostUsd / totalCost
                    : totalTokens == 0 ? 0 : (decimal)agent.Metrics.Tokens.Total / totalTokens;
                string providerId = agent.AgentId.Value;
                string colorHex = ProviderColorPalette.GetEffectiveHex(providerId, null);
                return new UsageReportProviderRow(
                    providerId,
                    ProviderName(providerId),
                    IsCostMetric
                        ? FormatUsd(agent.Metrics.TotalCostUsd)
                        : FormatTokens(agent.Metrics.Tokens.Total),
                    IsCostMetric
                        ? string.Format(
                            CultureInfo.CurrentCulture,
                            GetString("UsageReportProviderCostDetailFormat"),
                            FormatPercent(share),
                            FormatTokens(agent.Metrics.Tokens.Total))
                        : string.Format(
                            CultureInfo.CurrentCulture,
                            GetString("UsageReportProviderTokenDetailFormat"),
                            FormatPercent(share),
                            FormatUsd(agent.Metrics.TotalCostUsd)),
                    (double)(share * 100m),
                    FormatPercent(share),
                    GetProviderBrush(colorHex),
                    Math.Max(2d, (double)(share * 1080m)),
                    CreateProviderTrend(providerId));
            })
            .ToArray();
    }

    /// <summary>
    /// One brush per color. The report rebuilds its rows on every metric, scope, and period
    /// change, and a fresh brush per row per rebuild leaves the old ones for the collector while
    /// the color has not changed.
    /// </summary>
    private Brush GetProviderBrush(string colorHex)
    {
        if (_providerBrushes.TryGetValue(colorHex, out Brush? brush))
        {
            return brush;
        }

        brush = new SolidColorBrush(ProviderColorPalette.Parse(colorHex));
        _providerBrushes[colorHex] = brush;
        return brush;
    }

    private UsageReportTrendDataset CreateProviderTrend(string providerId)
    {
        UsageReportTrendDay[] days = Enumerable.Range(0, RangeDayCount)
            .Select(offset => StartDate.AddDays(offset))
            .Select(date => new UsageReportTrendDay(
                date,
                date.ToString("d MMM", CultureInfo.CurrentCulture)))
            .ToArray();
        var metricsByDate = _report.AgentDays
            .Where(item => string.Equals(item.AgentId.Value, providerId, StringComparison.Ordinal))
            .ToDictionary(item => item.Date, item => item.Metrics);
        var series = new UsageReportTrendSeries(
            providerId,
            ProviderName(providerId),
            ProviderColorPalette.GetEffectiveHex(providerId, null),
            days.Select(day => metricsByDate.TryGetValue(day.Date, out UsageReportMetrics? metrics)
                    ? IsCostMetric
                        ? (double)metrics.TotalCostUsd
                        : metrics.Tokens.Total
                    : 0d)
                .ToArray());
        return new UsageReportTrendDataset(Metric, days, [series]);
    }

    private string QualityShare(decimal? value)
    {
        decimal total = _report.Totals.TotalCostUsd;
        return FormatPercent(total == 0 ? 0 : (value ?? 0) / total);
    }

    private IReadOnlyList<UsageReportMetricCard> CreateMetricCards()
    {
        TokenBreakdown tokens = _report.Totals.Tokens;
        long observedInput = checked(tokens.Input + tokens.CacheRead);
        decimal cacheShare = observedInput == 0
            ? 0
            : (decimal)tokens.CacheRead / observedInput;
        return
        [
            new(
                GetString("UsageReportProcessedTokensLabel"),
                FormatTokens(tokens.Total),
                string.Format(
                    CultureInfo.CurrentCulture,
                    GetString("UsageReportActiveDayAverageFormat"),
                    FormatTokens(AveragePerActiveDay()))),
            new(
                GetString("UsageReportCachedInputLabel"),
                FormatTokens(tokens.CacheRead),
                string.Format(
                    CultureInfo.CurrentCulture,
                    GetString("UsageReportCachedShareFormat"),
                    FormatPercent(cacheShare))),
            new(
                GetString("UsageReportUncachedInputLabel"),
                FormatTokens(tokens.Input),
                string.Format(
                    CultureInfo.CurrentCulture,
                    GetString("UsageReportCacheWriteFormat"),
                    FormatTokens(tokens.CacheWrite))),
            new(
                GetString("UsageReportOutputLabel"),
                FormatTokens(tokens.Output),
                string.Format(
                    CultureInfo.CurrentCulture,
                    GetString("UsageReportReasoningFormat"),
                    FormatTokens(tokens.Reasoning))),
            new(
                GetString("UsageReportPriceCoverageLabel"),
                FormatPercent(_report.Totals.PriceCoveragePercent / 100m),
                string.Format(
                    CultureInfo.CurrentCulture,
                    GetString("UsageReportUnpricedTokensFormat"),
                    FormatTokens(_report.Totals.UnpricedTokens))),
        ];
    }

    private UsageReportModelRow[] CreateModelRows()
    {
        decimal totalCost = _report.Totals.TotalCostUsd;
        return _report.Models
            .Where(model => !string.Equals(
                model.ModelId.Value,
                "codex-account",
                StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(model => IsCostMetric
                ? (double)model.Metrics.TotalCostUsd
                : model.Metrics.Tokens.Total)
            .ThenByDescending(model => IsCostMetric
                ? model.Metrics.Tokens.Total
                : (double)model.Metrics.TotalCostUsd)
            .ThenBy(model => model.AgentId.Value, StringComparer.Ordinal)
            .ThenBy(model => model.ModelId.Value, StringComparer.Ordinal)
            .Take(30)
            .Select(model => new UsageReportModelRow(
                model.AgentId.Value,
                ProviderName(model.AgentId.Value),
                model.ModelId.Value,
                FormatUsd(model.Metrics.TotalCostUsd),
                FormatPercent(totalCost == 0
                    ? 0
                    : model.Metrics.TotalCostUsd / totalCost),
                FormatTokens(model.Metrics.Tokens.Total),
                FormatPercent(model.Metrics.PriceCoveragePercent / 100m)))
            .ToArray();
    }

    private UsageReportSourceRow[] CreateSourceRows() => _report.Agents
        .BySpend(
            agent => agent.Metrics.TotalCostUsd,
            agent => agent.Metrics.Tokens.Total,
            agent => agent.AgentId.Value)
        .Select(agent => new UsageReportSourceRow(
            agent.AgentId.Value,
            ProviderName(agent.AgentId.Value),
            agent.Metrics.ReportedCostUsd is decimal reported
                ? FormatUsd(reported)
                : "—",
            agent.Metrics.EstimatedCostUsd is decimal estimated
                ? FormatUsd(estimated)
                : "—",
            FormatTokens(agent.Metrics.Tokens.Total),
            FormatPercent(agent.Metrics.PriceCoveragePercent / 100m)))
        .ToArray();

    private UsageReportDayRow[] CreateDayRows() => _report.Days
        .OrderByDescending(day => day.Date)
        .Select(day => new UsageReportDayRow(
            day.Date.ToString("ddd d MMM", CultureInfo.CurrentCulture),
            FormatUsd(day.Metrics.TotalCostUsd),
            FormatTokens(day.Metrics.Tokens.Total),
            day.Metrics.EventCount.ToString("N0", CultureInfo.CurrentCulture),
            FormatPercent(day.Metrics.PriceCoveragePercent / 100m)))
        .ToArray();

    private IReadOnlyList<UsageReportQualityRow> CreateQualityRows()
    {
        decimal totalCost = _report.Totals.TotalCostUsd;
        long totalTokens = _report.Totals.Tokens.Total;
        return
        [
            new(
                GetString("UsageReportReportedCostLabel"),
                FormatPercent(totalCost == 0
                    ? 0
                    : (_report.Totals.ReportedCostUsd ?? 0) / totalCost)),
            new(
                GetString("UsageReportEstimatedCostLabel"),
                FormatPercent(totalCost == 0
                    ? 0
                    : (_report.Totals.EstimatedCostUsd ?? 0) / totalCost)),
            new(
                GetString("UsageReportPricedTokensLabel"),
                FormatPercent(_report.Totals.PriceCoveragePercent / 100m)),
            new(
                GetString("UsageReportUnpricedLabel"),
                FormatPercent(totalTokens == 0
                    ? 0
                    : (decimal)_report.Totals.UnpricedTokens / totalTokens)),
        ];
    }

    private UsageReportTrendDataset CreateTrend()
    {
        UsageReportTrendDay[] days = Enumerable.Range(0, RangeDayCount)
            .Select(offset => StartDate.AddDays(offset))
            .Select(date => new UsageReportTrendDay(
                date,
                date.ToString("d MMM", CultureInfo.CurrentCulture)))
            .ToArray();
        var agentDays = _report.AgentDays.ToDictionary(
            item => (item.Date, item.AgentId.Value),
            item => item.Metrics);
        UsageReportTrendSeries[] series = _report.Agents
            .Select(agent =>
            {
                string providerId = agent.AgentId.Value;
                return new UsageReportTrendSeries(
                    providerId,
                    ProviderName(providerId),
                    ProviderColorPalette.GetEffectiveHex(providerId, null),
                    days.Select(day => agentDays.TryGetValue(
                                (day.Date, providerId),
                                out UsageReportMetrics? metrics)
                            ? IsShareValueMode && IsGlobalScope
                                ? DailyShare(day.Date, providerId, metrics)
                                : IsCostMetric
                                    ? (double)metrics.TotalCostUsd
                                    : metrics.Tokens.Total
                            : 0d)
                        .ToArray());
            })
            .OrderByDescending(series => series.Values.Sum())
            .ToArray();
        return new UsageReportTrendDataset(
            IsShareValueMode && IsGlobalScope ? UsageReportMetric.Share : Metric,
            days,
            series);
    }

    private double DailyShare(
        DateOnly date,
        string providerId,
        UsageReportMetrics metrics)
    {
        _ = providerId;
        double total = _report.AgentDays
            .Where(item => item.Date == date)
            .Sum(item => IsCostMetric
                ? (double)item.Metrics.TotalCostUsd
                : item.Metrics.Tokens.Total);
        double value = IsCostMetric
            ? (double)metrics.TotalCostUsd
            : metrics.Tokens.Total;
        return total <= 0 ? 0 : value * 100d / total;
    }

    private long AveragePerActiveDay()
    {
        UsageDayReport[] activeDays = _report.Days
            .Where(day => day.Metrics.Tokens.Total > 0)
            .ToArray();
        return activeDays.Length == 0
            ? 0
            : _report.Totals.Tokens.Total / activeDays.Length;
    }

    private string CoverageLabel(CoverageKind coverage) => GetString(coverage switch
    {
        CoverageKind.Complete => "UsageReportCoverageComplete",
        CoverageKind.Partial => "UsageReportCoveragePartial",
        CoverageKind.SummaryOnly => "UsageReportCoverageSummaryOnly",
        CoverageKind.Unpriced => "UsageReportCoverageUnpriced",
        _ => throw new ArgumentOutOfRangeException(nameof(coverage)),
    });

    private string ProviderName(string providerId) =>
        ProviderDisplayName.Resolve(providerId, GetString);

    private string FormatUsd(decimal amount) => UsageValueFormatter.Usd(amount, GetString);

    internal static string FormatCompactUsd(double amount) =>
        UsageValueFormatter.CompactUsd(amount);

    internal static string FormatCompactTokens(double value) =>
        UsageValueFormatter.CompactTokens(value);

    private static string FormatTokens(long value) => FormatCompactTokens(value);

    private static string FormatPercent(decimal share) =>
        UsageValueFormatter.PercentText(share * 100m);

    private string GetString(string key)
    {
        string value = _resources.GetString(key);
        return string.IsNullOrWhiteSpace(value)
            ? throw new InvalidOperationException($"The resource '{key}' is missing.")
            : value;
    }
}
