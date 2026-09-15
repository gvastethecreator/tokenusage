using System.Globalization;
using TokenUsage.Core.Automation;
using TokenUsage.Core.Usage;
using TokenUsage.Providers.Codex;
using TokenUsage.Providers.Cursor;
using TokenUsage.Providers.Pricing;

namespace TokenUsage.App.ViewModels.Reports;

public sealed record UsageComparisonOption(UsageComparisonPreset Preset, string Name);
public sealed record UsageComparisonModelOption(string AgentId, string? ModelProviderId, string ModelId, string Name);
public sealed record UsageSavedComparisonOption(string Id, string Name);

public sealed partial class UsageReportViewModel
{
    private UsageComparisonOption? _comparisonPreset;
    private UsageComparisonModelOption? _baselineModel;
    private UsageComparisonModelOption? _currentModel;
    private bool _compareModelPeriods;
    private bool _useReferencePrices;
    private DateTimeOffset _priceReferenceUtc;
    private string _measurementEvidence = string.Empty;
    private SavedUsageComparison? _savedComparison;
    private bool _savingComparison;

    public IReadOnlyList<UsageComparisonOption> ComparisonPresets { get; private set; } = [];
    public IReadOnlyList<UsageComparisonModelOption> ComparisonModels { get; private set; } = [];
    public IReadOnlyList<UsageSavedComparisonOption> SavedComparisons { get; private set; } = [];
    public bool IsCompareModelsAxis => CompareAxis == UsageReportCompareAxis.Models;
    public bool IsCompareRatesAxis => CompareAxis == UsageReportCompareAxis.Rates;
    public bool HasPeriodComparisonOptions => IsComparePeriodsAxis
        || IsCompareModelsAxis && CompareModelPeriods;
    public bool CanChangeComparison => _savedComparison is null && !IsLoading && !_savingComparison;
    public bool HasSavedComparison => _savedComparison is not null;
    public bool CanUseReferencePrices => CanChangeComparison && !IsCompareCyclesAxis;
    private bool _markBestValues = true;
    public bool MarkBestValues
    {
        get => _markBestValues;
        set
        {
            if (value == _markBestValues) return;
            _markBestValues = value;
            OnPropertyChanged();
            RebuildProjection();
        }
    }
    public DateTimeOffset? RateBaselineDate
    {
        get => CatalogPickerDate(_rateBaselineUtc);
        set
        {
            if (value is null || !CanUseReferencePrices) return;
            _rateBaselineUtc = new DateTimeOffset(value.Value.Date, TimeSpan.Zero);
            OnPropertyChanged();
            if (IsCompareRatesAxis || IsLinearPriceScenario) _ = LoadAsync();
        }
    }
    private DateTimeOffset _rateBaselineUtc;
    public DateTimeOffset? PriceReferenceDate
    {
        get => CatalogPickerDate(_priceReferenceUtc);
        set
        {
            if (value is null || !CanUseReferencePrices) return;
            _priceReferenceUtc = new DateTimeOffset(value.Value.Date, TimeSpan.Zero);
            OnPropertyChanged(); OnPropertyChanged(nameof(PriceReferenceLabel));
            if (UsageComparison.ReloadsForCatalogDate(UseReferencePrices, IsCompareRatesAxis) || IsLinearPriceScenario) _ = LoadAsync();
        }
    }
    private static DateTimeOffset CatalogPickerDate(DateTimeOffset utc)
    {
        // CalendarDatePicker displays the instant in the device zone. Keep the UTC catalog day as a local civil date.
        DateTime localNoon = DateTime.SpecifyKind(utc.UtcDateTime.Date.AddHours(12), DateTimeKind.Unspecified);
        return new DateTimeOffset(localNoon, TimeZoneInfo.Local.GetUtcOffset(localNoon));
    }
    public string MeasurementEvidence => string.Join(Environment.NewLine + Environment.NewLine,
        MeasurementSections.Select(section => section.Title + Environment.NewLine + section.Text));
    public string PriceReferenceLabel => string.Format(CultureInfo.CurrentCulture,
        GetString("UsageComparisonPriceReferenceFormat"), _priceReferenceUtc.ToString("d", CultureInfo.CurrentCulture) + " UTC");

    public UsageComparisonOption? ComparisonPreset
    {
        get => _comparisonPreset;
        set { if (value is null || value == _comparisonPreset) return; _comparisonPreset = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsPeriodPickerVisible)); _ = LoadAsync(); }
    }

    public UsageComparisonModelOption? BaselineModel
    {
        get => _baselineModel;
        set { if (value is null || value == _baselineModel) return; _baselineModel = value; OnPropertyChanged(); _ = LoadAsync(); }
    }

    public UsageComparisonModelOption? CurrentModel
    {
        get => _currentModel;
        set { if (value is null || value == _currentModel) return; _currentModel = value; OnPropertyChanged(); _ = LoadAsync(); }
    }

    public bool CompareModelPeriods
    {
        get => _compareModelPeriods;
        set { if (value == _compareModelPeriods) return; _compareModelPeriods = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasPeriodComparisonOptions)); _ = LoadAsync(); }
    }

    public bool UseReferencePrices
    {
        get => _useReferencePrices;
        set
        {
            if (value == _useReferencePrices) return;
            _useReferencePrices = value;
            if (value) { _useLinearPriceScenario = false; _linearPriceResult = null; NotifyPriceScenarioOptions(); }
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsCatalogDatePickerVisible));
            _ = LoadAsync();
        }
    }

    public bool IsReferencePriceOverlayVisible => IsPairComparison && !IsCompareRatesAxis && !IsLinearPriceScenario;

    public bool IsCatalogDatePickerVisible => IsCompareRatesAxis || UseReferencePrices || IsLinearPriceScenario;

    private void InitializeComparisons()
    {
        _priceReferenceUtc = new DateTimeOffset(_clock.GetUtcNow().UtcDateTime.Date, TimeSpan.Zero);
        _rateBaselineUtc = _priceReferenceUtc.AddDays(-30);
        ComparisonPresets = Enum.GetValues<UsageComparisonPreset>()
            .Select(preset => new UsageComparisonOption(preset, GetString(preset switch
            {
                UsageComparisonPreset.LastCompleteWeek => "UsageComparisonPresetLastCompleteWeek",
                UsageComparisonPreset.CurrentWeek => "UsageComparisonPresetCurrentWeek",
                UsageComparisonPreset.RollingSevenDays => "UsageComparisonPresetRollingSevenDays",
                _ => "UsageComparisonPresetSelectedPeriod",
            }))).ToArray();
        _comparisonPreset = ComparisonPresets[0];
    }

    private void RebuildComparisonModels()
    {
        var baseline = _baselineModel;
        var current = _currentModel;
        ComparisonModels = _globalReport.Models.Select(row => new UsageComparisonModelOption(row.AgentId.Value,
            row.ModelProviderId?.Value, row.ModelId.Value,
            ProviderName(row.AgentId.Value) + " · " + (row.ModelProviderId?.Value ?? GetString("UsageReportUnknownHost"))
                + " · " + ReportDataProjection.ModelName(row.ModelId.Value)))
            .OrderBy(row => row.Name, StringComparer.CurrentCulture)
            .ThenBy(row => row.AgentId, StringComparer.Ordinal)
            .ThenBy(row => row.ModelProviderId, StringComparer.Ordinal)
            .ThenBy(row => row.ModelId, StringComparer.Ordinal).ToArray();
        _baselineModel = ComparisonModels.FirstOrDefault(row => SameModel(row, baseline)) ?? (ComparisonModels.Count == 0 ? null : ComparisonModels[0]);
        _currentModel = ComparisonModels.FirstOrDefault(row => SameModel(row, current))
            ?? (ComparisonModels.Count > 1 ? ComparisonModels[1] : _baselineModel);
        OnPropertyChanged(nameof(ComparisonModels));
        OnPropertyChanged(nameof(BaselineModel));
        OnPropertyChanged(nameof(CurrentModel));

        static bool SameModel(UsageComparisonModelOption row, UsageComparisonModelOption? previous) =>
            previous is not null && (row.AgentId, row.ModelProviderId, row.ModelId)
                == (previous.AgentId, previous.ModelProviderId, previous.ModelId);
    }

    private async Task ApplyPeriodComparisonAsync(Func<DateOnly, DateOnly, Task<UsageReport>> read,
        DateOnly selectedStart, DateOnly selectedEnd)
    {
        UsageComparisonPeriods range = UsageComparisonPeriods.Resolve(
            ComparisonPreset?.Preset ?? UsageComparisonPreset.LastCompleteWeek,
            DateOnly.FromDateTime(_clock.GetLocalNow().DateTime), selectedStart, selectedEnd);
        _compareLeftStart = range.BaselineStart;
        _compareLeftEnd = range.BaselineEnd;
        _compareRightStart = range.CurrentStart;
        _compareRightEnd = range.CurrentEnd;
        _report = range.HasElapsedDays ? await read(range.BaselineStart, range.BaselineEnd) : UsageReportQuery.Build([]);
        _compareRightReport = range.HasElapsedDays ? await read(range.CurrentStart, range.CurrentEnd) : UsageReportQuery.Build([]);
        _measurementEvidence = GetString(range.HasElapsedDays ? "UsageComparisonCompletedDaysEvidence" : "UsageComparisonNoElapsedDays");
    }

    private async Task ApplyModelComparisonAsync(Func<DateOnly, DateOnly, Task<UsageReport>> read, DateOnly start, DateOnly end, CancellationToken token)
    {
        if (CompareModelPeriods) await ApplyPeriodComparisonAsync(read, start, end);
        else
        {
            _report = _compareRightReport = _globalReport;
            _compareLeftStart = _compareRightStart = start;
            _compareLeftEnd = _compareRightEnd = end;
        }
        _report = _baselineModel is { } baseline ? UsageReportQuery.FilterByModel(_report, new AgentId(baseline.AgentId),
            baseline.ModelProviderId is { } baselineHost ? new ModelProviderId(baselineHost) : null, new ModelId(baseline.ModelId)) : UsageReportQuery.Build([]);
        _compareRightReport = _currentModel is { } current ? UsageReportQuery.FilterByModel(_compareRightReport, new AgentId(current.AgentId),
            current.ModelProviderId is { } currentHost ? new ModelProviderId(currentHost) : null, new ModelId(current.ModelId)) : UsageReportQuery.Build([]);
        _measurementEvidence += " " + GetString("UsageComparisonModelEvidence");
    }

    private Task<UsageReport> RepriceAsync(UsageReport original, DateOnly from, DateOnly to, CancellationToken token) =>
        RepriceAtAsync(original, from, to, _priceReferenceUtc, token);

    private static CostObservation ResolveCatalogCost(UsageEvent row, DateTimeOffset atUtc) =>
        row.AgentId.Value == "cursor" ? CursorPricingCatalog.Resolve(row.ModelId.Value, atUtc, row.Tokens)
        : row.AgentId.Value == "codex" ? CodexPricingCatalog.Resolve(row.ModelId.Value, row.Tokens, atUtc)
        : CostObservation.Unavailable();

    private async Task ApplyRatesComparisonAsync(DateOnly start, DateOnly end, CancellationToken token)
    {
        _compareLeftStart = start;
        _compareLeftEnd = end;
        _compareRightStart = start;
        _compareRightEnd = end;
        UsageRateScenarioComparison comparison = await Task.Run(async () =>
        {
            var query = new UsageReportQuery(_databasePath);
            return await query.CompareCatalogDatesAsync(
                start, end, _rateBaselineUtc, _priceReferenceUtc, ResolveCatalogCost,
                selection: ComparisonConfigurationSelection(), cancellationToken: token);
        }, token);
        token.ThrowIfCancellationRequested();
        _report = comparison.Baseline;
        _compareRightReport = comparison.Current;
        _measurementEvidence = string.Format(CultureInfo.CurrentCulture,
            GetString("UsageComparisonFixedCohortEvidence"),
            comparison.MethodId, comparison.ComparableEvents, comparison.ComparableTokens);
        RebuildRateSteps();
    }

    public IReadOnlyList<UsageReportRateStep> RateSteps { get; private set; } = [];

    private void RebuildRateSteps()
    {
        RateSteps = IsCompareRatesAxis
            ? PricingEvidenceCatalog.AllRates
                .OrderByDescending(item => item.EffectiveFromUtc)
                .Select(item => new UsageReportRateStep(
                    item.ExactPriceMatch,
                    item.CatalogVersion,
                    item.EffectiveFromUtc.UtcDateTime.ToString("d", CultureInfo.CurrentCulture)
                        + (item.EffectiveUntilUtc is { } until
                            ? " – " + until.UtcDateTime.ToString("d", CultureInfo.CurrentCulture)
                            : "")))
                .Take(24)
                .ToArray()
            : [];
        OnPropertyChanged(nameof(RateSteps));
    }

    private async Task<UsageReport> RepriceAtAsync(
        UsageReport original, DateOnly from, DateOnly to, DateTimeOffset atUtc, CancellationToken token)
    {
        if (to < from) return original;
        return await Task.Run(async () =>
        {
            var query = new UsageReportQuery(_databasePath);
            return await query.RepriceAtAsync(original, from, to, atUtc, ResolveCatalogCost, token);
        }, token);
    }

    private string ActiveRateMethodId =>
        _savedComparison?.Definition.MethodId
        ?? (IsCompareRatesAxis ? UsageReferencePricing.FixedCohortMethodId : "");

    private UsageReportCompareRow NotApplicableFixedCohortRow(string metric)
    {
        string value = GetString("UsageComparisonFixedCohortNotApplicable");
        return MetricRow(metric, value, value, value);
    }

    private UsageReportCompareRow MetricRow(
        string metric, string left, string right, string delta,
        decimal? leftValue = null, decimal? rightValue = null,
        UsageBestDirection direction = UsageBestDirection.None)
    {
        int? winner = _markBestValues ? UsageComparison.Winner(leftValue, rightValue, direction) : null;
        return new(metric, left, right, delta, winner < 0, winner > 0);
    }

    private IEnumerable<UsageReportCompareRow> CreateMeasurementRows(UsageReport currentReport)
    {
        yield return MetricRow(GetString("UsageComparisonActiveDays"), UsageComparison.ActiveDays(_report).ToString(CultureInfo.CurrentCulture), UsageComparison.ActiveDays(currentReport).ToString(CultureInfo.CurrentCulture),
            FormatSignedCount(UsageComparison.ActiveDays(currentReport) - UsageComparison.ActiveDays(_report)));
        yield return MetricRow(GetString("UsageComparisonPriceCoverage"), FormatOptionalPercent(_report.Totals.PriceCoveragePercent), FormatOptionalPercent(currentReport.Totals.PriceCoveragePercent),
            FormatSignedPercentagePoints(currentReport.Totals.PriceCoveragePercent - _report.Totals.PriceCoveragePercent),
            _report.Totals.PriceCoveragePercent, currentReport.Totals.PriceCoveragePercent, UsageBestDirection.Higher);
        yield return MetricRow(GetString("UsageComparisonCostPerMillion"), FormatOptionalUsd(UsageComparison.CostPerMillionPricedTokens(_report.Totals)),
            FormatOptionalUsd(UsageComparison.CostPerMillionPricedTokens(currentReport.Totals)),
            FormatOptionalSignedUsd(UsageComparison.CostPerMillionPricedTokens(_report.Totals), UsageComparison.CostPerMillionPricedTokens(currentReport.Totals)),
            UsageComparison.CostPerMillionPricedTokens(_report.Totals), UsageComparison.CostPerMillionPricedTokens(currentReport.Totals), UsageBestDirection.Lower);
        foreach ((string name, long a, long b) in new[]
        {
            ("UsageComparisonCachedInput", _report.Totals.Tokens.CacheRead, currentReport.Totals.Tokens.CacheRead),
            ("UsageComparisonCacheWrite", _report.Totals.Tokens.CacheWrite, currentReport.Totals.Tokens.CacheWrite),
            ("UsageComparisonUncachedInput", _report.Totals.Tokens.Input, currentReport.Totals.Tokens.Input),
            ("UsageComparisonOutput", _report.Totals.Tokens.Output + _report.Totals.Tokens.Reasoning, currentReport.Totals.Tokens.Output + currentReport.Totals.Tokens.Reasoning),
        }) yield return new(GetString(name), FormatTokens(a), FormatTokens(b), FormatSignedTokens(b - a));
        if (IsCompareModelsAxis)
        {
            decimal? aShare = _report.PopulationTokens is > 0 ? 100m * _report.Totals.Tokens.Total / _report.PopulationTokens : null;
            decimal? bShare = currentReport.PopulationTokens is > 0 ? 100m * currentReport.Totals.Tokens.Total / currentReport.PopulationTokens : null;
            yield return new(GetString("UsageComparisonModelShare"), FormatOptionalPercent(aShare), FormatOptionalPercent(bShare),
                aShare is { } a && bShare is { } b ? FormatSignedPercentagePoints(b - a) : GetString("UsageReportCompareUnavailable"));
            yield return new(GetString("UsageComparisonProfiles"), FormatProfiles(_report), FormatProfiles(currentReport), GetString("UsageComparisonDescriptive"));
        }
        if (!IsCompareModelsAxis)
            foreach (UsageModelContribution row in UsageComparison.Contributions(_report, currentReport))
                yield return new(ProviderName(row.AgentId) + " · " + ReportDataProjection.ModelName(row.ModelId),
                    IsCostMetric ? FormatOptionalUsd(row.Cost.Baseline) : FormatOptionalTokens(row.Tokens.Baseline),
                    IsCostMetric ? FormatOptionalUsd(row.Cost.Current) : FormatOptionalTokens(row.Tokens.Current),
                    IsCostMetric ? FormatOptionalSignedUsd(row.Cost.Baseline, row.Cost.Current) : FormatOptionalSignedTokens(row.Tokens.Baseline, row.Tokens.Current));
        if (IsCompareRatesAxis && UsageComparison.UsesFixedCohortRows(ActiveRateMethodId))
        {
            yield return NotApplicableFixedCohortRow(GetString("UsageComparisonVolumeChange"));
            yield return NotApplicableFixedCohortRow(GetString("UsageComparisonMixChange"));
            yield return MetricRow(GetString("UsageComparisonRateChange"),
                FormatKnownCost(_report.Totals), FormatKnownCost(currentReport.Totals),
                FormatOptionalSignedUsd(ComparableCost(_report.Totals), ComparableCost(currentReport.Totals)),
                ComparableCost(_report.Totals), ComparableCost(currentReport.Totals), UsageBestDirection.None);
        }
    }

    private string FormatChangeWithPercent(string absolute, decimal? baseline, decimal? current)
    {
        decimal? percent = UsageNumericChange.Between(baseline, current).RelativePercent;
        return percent is { } value
            ? absolute + " · " + (value > 0 ? "+" : "") + FormatOptionalPercent(value)
            : absolute;
    }

    private string FormatProfiles(UsageReport report) => report.ModelProfiles.Count == 0
        ? GetString("UsageReportCompareUnavailable")
        : string.Join("\n", report.ModelProfiles.Select(row =>
            (row.ObservedModel ?? GetString("UsageComparisonUnknownConfiguration")) + " → " + row.CanonicalModel
            + " · " + (row.Effort ?? "?") + " / " + (row.Tier ?? "?")
            + " · " + FormatTokens(row.Tokens) + " · "
            + row.FirstObservedAtUtc.ToLocalTime().ToString("g", CultureInfo.CurrentCulture) + " – "
            + row.LastObservedAtUtc.ToLocalTime().ToString("g", CultureInfo.CurrentCulture)));

    private async Task LoadSavedComparisonsAsync(CancellationToken token)
    {
        IReadOnlyList<SavedUsageComparisonInfo> saved = await Task.Run(async () =>
        {
            UsageRepository repository = await UsageRepository.OpenReadOnlyAsync(_databasePath, token);
            return await repository.ReadComparisonsAsync(token);
        }, token);
        token.ThrowIfCancellationRequested();
        SavedComparisons = saved.Select(row => new UsageSavedComparisonOption(row.RevisionId,
            row.Axis + " · " + row.CreatedAtUtc.ToLocalTime().ToString("g", CultureInfo.CurrentCulture) + " · " + row.RevisionId[..8])).ToArray();
        OnPropertyChanged(nameof(SavedComparisons));
    }

    private UsageComparisonDefinition CreateComparisonDefinition(string evidence) => new(CompareAxis.ToString(), ComparisonPreset?.Preset.ToString() ?? "",
        _compareLeftStart, _compareLeftEnd, _compareRightStart, _compareRightEnd, TimeZoneInfo.Local.Id,
        UseReferencePrices || IsCompareRatesAxis || IsLinearPriceScenario ? _priceReferenceUtc : null, BaselineModel?.Name, CurrentModel?.Name,
        evidence, UsageRepository.ComparisonDataRevision(IsCompareCyclesAxis ? _cycleReports.Select(entry => entry.Report).ToArray() : [_report, _compareRightReport]))
    {
        BaselineLabel = CompareLeftLabel,
        CurrentLabel = CompareRightLabel,
        CycleComparison = CreateCycleComparison(),
        RateBaselineUtc = IsCompareRatesAxis || IsLinearPriceScenario ? _rateBaselineUtc : null,
        MethodId = IsCompareRatesAxis ? UsageReferencePricing.FixedCohortMethodId
            : IsLinearPriceScenario ? _linearPriceResult?.MethodId ?? UsageLinearPriceScenario.MethodId : "",
    };

    public async Task SaveComparisonAsync()
    {
        if (!IsCompareScope || !CanChangeComparison || HasError || !_hasCurrentReport) return;
        _savingComparison = true;
        OnPropertyChanged(nameof(CanChangeComparison));
        try
        {
            var definition = CreateComparisonDefinition(MeasurementEvidence);
            var snapshot = new SavedUsageComparison(Guid.NewGuid().ToString("N"), _clock.GetUtcNow(), definition, _report, _compareRightReport)
            { Cycles = IsCompareCyclesAxis ? _cycleReports : [], Explanation = _comparisonExplanation,
                PriceScenario = IsLinearPriceScenario ? _linearPriceResult : null };
            await Task.Run(async () =>
            {
                UsageRepository repository = await UsageRepository.OpenAsync(_databasePath);
                await repository.SaveComparisonAsync(snapshot);
            });
            await LoadSavedComparisonsAsync(CancellationToken.None);
        }
        catch { StatusText = GetString("UsageComparisonSaveFailed"); HasError = true; }
        finally { _savingComparison = false; OnPropertyChanged(nameof(CanChangeComparison)); }
    }

    public async Task ShowSavedComparisonAsync(string id)
    {
        _loadCancellation?.Cancel();
        using var cancellation = new CancellationTokenSource();
        _loadCancellation = cancellation;
        IsLoading = true;
        HasError = false;
        NotifyEmptyStateChanged();
        try
        {
            SavedUsageComparison snapshot = await Task.Run(async () =>
            {
                UsageRepository repository = await UsageRepository.OpenReadOnlyAsync(_databasePath, cancellation.Token);
                return await repository.ReadComparisonAsync(id, cancellation.Token);
            }, cancellation.Token);
            cancellation.Token.ThrowIfCancellationRequested();
            if (!ReferenceEquals(_loadCancellation, cancellation)) return;
            _savedComparison = snapshot;
            _linearPriceResult = snapshot.PriceScenario;
            _useLinearPriceScenario = snapshot.PriceScenario is not null || snapshot.Definition.MethodId == UsageLinearPriceScenario.MethodId;
            _cycleReports = snapshot.Cycles;
            _useReferencePrices = !_useLinearPriceScenario && snapshot.Definition.PriceReferenceUtc.HasValue && snapshot.Definition.Axis != nameof(UsageReportCompareAxis.Rates);
            if (snapshot.Definition.PriceReferenceUtc is { } reference) _priceReferenceUtc = reference;
            if (snapshot.Definition.RateBaselineUtc is { } baseline) _rateBaselineUtc = baseline;
            _comparisonPreset = ComparisonPresets.FirstOrDefault(option => option.Preset.ToString() == snapshot.Definition.Preset);
            _baselineModel = ComparisonModels.FirstOrDefault(option => option.Name == snapshot.Definition.BaselineModel);
            _currentModel = ComparisonModels.FirstOrDefault(option => option.Name == snapshot.Definition.CurrentModel);
            _compareModelPeriods = snapshot.Definition.BaselineStart != snapshot.Definition.CurrentStart
                || snapshot.Definition.BaselineEnd != snapshot.Definition.CurrentEnd;
            _compareAxis = Enum.Parse<UsageReportCompareAxis>(snapshot.Definition.Axis);
            NotifyPriceScenarioOptions();
            _report = snapshot.Baseline;
            _compareRightReport = snapshot.Current;
            _compareLeftStart = snapshot.Definition.BaselineStart;
            _compareLeftEnd = snapshot.Definition.BaselineEnd;
            _compareRightStart = snapshot.Definition.CurrentStart;
            _compareRightEnd = snapshot.Definition.CurrentEnd;
            _measurementEvidence = GetString("UsageComparisonSavedEvidence") + " " + snapshot.Definition.Evidence;
            if (snapshot.Definition.Axis == nameof(UsageReportCompareAxis.Rates)
                && string.IsNullOrEmpty(snapshot.Definition.MethodId))
                _measurementEvidence = GetString("UsageComparisonLegacyMethod") + " " + _measurementEvidence;
            MeasurementSections = [new(GetString("UsageMeasurementMethod"), _measurementEvidence)];
            OnPropertyChanged(nameof(MeasurementSections));
            _hasCurrentReport = true;
            StatusText = string.Empty;
            RebuildProjection();
            OnPropertyChanged(nameof(CompareAxis));
            OnPropertyChanged(nameof(UseReferencePrices)); OnPropertyChanged(nameof(PriceReferenceDate));
            OnPropertyChanged(nameof(PriceReferenceLabel)); OnPropertyChanged(nameof(ComparisonPreset));
            OnPropertyChanged(nameof(BaselineModel)); OnPropertyChanged(nameof(CurrentModel));
            OnPropertyChanged(nameof(CompareModelPeriods));
            OnPropertyChanged(nameof(IsComparePeriodsAxis));
            OnPropertyChanged(nameof(IsCompareProvidersAxis));
            OnPropertyChanged(nameof(IsCompareCyclesAxis));
            OnPropertyChanged(nameof(IsCompareModelsAxis));
            OnPropertyChanged(nameof(IsCompareRatesAxis));
            OnPropertyChanged(nameof(RateBaselineDate));
            OnPropertyChanged(nameof(IsCatalogDatePickerVisible));
            OnPropertyChanged(nameof(IsReferencePriceOverlayVisible));
            OnPropertyChanged(nameof(HasPeriodComparisonOptions));
            OnPropertyChanged(nameof(HasSavedComparison));
            OnPropertyChanged(nameof(ComparisonStateText));
            OnPropertyChanged(nameof(ComparisonUsageDatesText));
            OnPropertyChanged(nameof(ComparisonCatalogDatesText));
            OnPropertyChanged(nameof(HasComparisonCatalogDates));
            OnPropertyChanged(nameof(ComparisonCoverageText));
            OnPropertyChanged(nameof(ComparisonResultCaptureText));
            OnPropertyChanged(nameof(IsCompareCyclePickersVisible));
            OnPropertyChanged(nameof(MeasurementEvidence));
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        catch
        {
            if (ReferenceEquals(_loadCancellation, cancellation))
            { StatusText = GetString("UsageComparisonReadSavedFailed"); HasError = true; }
        }
        finally
        {
            if (ReferenceEquals(_loadCancellation, cancellation))
            {
                _loadCancellation = null;
                IsLoading = false;
                NotifyEmptyStateChanged();
            }
        }
    }

    public void ReturnToLiveComparison()
    {
        _savedComparison = null;
        OnPropertyChanged(nameof(CanChangeComparison));
        OnPropertyChanged(nameof(HasSavedComparison));
        OnPropertyChanged(nameof(ComparisonStateText));
        _ = LoadAsync();
    }
}
