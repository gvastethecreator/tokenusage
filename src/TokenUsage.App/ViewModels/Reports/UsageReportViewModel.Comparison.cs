using System.Globalization;
using TokenUsage.Core.Automation;
using TokenUsage.Core.Usage;
using TokenUsage.Providers.Codex;
using TokenUsage.Providers.Cursor;
using TokenUsage.Providers.Pricing;

namespace TokenUsage.App.ViewModels.Reports;

public sealed record UsageComparisonOption(UsageComparisonPreset Preset, string Name);
public sealed record UsageComparisonModelOption(string AgentId, string ModelId, string Name);
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
    public bool HasPeriodComparisonOptions => IsComparePeriodsAxis || IsCompareModelsAxis && CompareModelPeriods;
    public bool CanChangeComparison => _savedComparison is null && !IsLoading && !_savingComparison;
    public bool HasSavedComparison => _savedComparison is not null;
    public bool CanUseReferencePrices => CanChangeComparison && !IsCompareCyclesAxis;
    public DateTimeOffset? PriceReferenceDate
    {
        get => _priceReferenceUtc;
        set
        {
            if (value is null || !CanUseReferencePrices) return;
            _priceReferenceUtc = new DateTimeOffset(value.Value.Date, TimeSpan.Zero);
            OnPropertyChanged(); OnPropertyChanged(nameof(PriceReferenceLabel));
            if (UseReferencePrices) _ = LoadAsync();
        }
    }
    public string MeasurementEvidence => _measurementEvidence;
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
        set { if (value == _useReferencePrices) return; _useReferencePrices = value; OnPropertyChanged(); _ = LoadAsync(); }
    }

    private void InitializeComparisons()
    {
        _priceReferenceUtc = _clock.GetUtcNow().ToUniversalTime();
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
        string? baseline = _baselineModel is null ? null : _baselineModel.AgentId + "/" + _baselineModel.ModelId;
        string? current = _currentModel is null ? null : _currentModel.AgentId + "/" + _currentModel.ModelId;
        ComparisonModels = _globalReport.Models.Select(row => new UsageComparisonModelOption(row.AgentId.Value,
            row.ModelId.Value, ProviderName(row.AgentId.Value) + " · " + ReportDataProjection.ModelName(row.ModelId.Value)))
            .DistinctBy(row => (row.AgentId, row.ModelId)).OrderBy(row => row.Name, StringComparer.CurrentCulture).ToArray();
        _baselineModel = ComparisonModels.FirstOrDefault(row => row.AgentId + "/" + row.ModelId == baseline) ?? (ComparisonModels.Count == 0 ? null : ComparisonModels[0]);
        _currentModel = ComparisonModels.FirstOrDefault(row => row.AgentId + "/" + row.ModelId == current)
            ?? (ComparisonModels.Count > 1 ? ComparisonModels[1] : _baselineModel);
        OnPropertyChanged(nameof(ComparisonModels));
        OnPropertyChanged(nameof(BaselineModel));
        OnPropertyChanged(nameof(CurrentModel));
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
        _report = _baselineModel is { } baseline ? UsageReportQuery.FilterByModel(_report, new AgentId(baseline.AgentId), new ModelId(baseline.ModelId)) : UsageReportQuery.Build([]);
        _compareRightReport = _currentModel is { } current ? UsageReportQuery.FilterByModel(_compareRightReport, new AgentId(current.AgentId), new ModelId(current.ModelId)) : UsageReportQuery.Build([]);
        UsageReport a = await ReadModelProfilesAsync(_report, _compareLeftStart, _compareLeftEnd, token);
        token.ThrowIfCancellationRequested();
        UsageReport b = await ReadModelProfilesAsync(_compareRightReport, _compareRightStart, _compareRightEnd, token);
        token.ThrowIfCancellationRequested();
        _report = a;
        _compareRightReport = b;
        _measurementEvidence += " " + GetString("UsageComparisonModelEvidence");
    }

    private Task<UsageReport> ReadModelProfilesAsync(UsageReport report, DateOnly from, DateOnly to, CancellationToken token) =>
        Task.Run(async () =>
        {
            if (to < from || report.Models.Count == 0) return report;
            UsageRepository repository = await UsageRepository.OpenReadOnlyAsync(_databasePath, token);
            var keys = report.ModelDays.Select(row => (row.Date, row.AgentId, row.ModelId)).ToHashSet();
            IReadOnlyList<UsageEvent> events = await repository.QueryUsageEventsAsync(
                new DateTimeOffset(from.AddDays(-1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero),
                new DateTimeOffset(to.AddDays(2).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero), cancellationToken: token);
            token.ThrowIfCancellationRequested();
            return report with
            {
                ModelProfiles = events.Where(row => row.TimePrecision == UsageTimePrecision.Timestamp && keys.Contains(
                    (DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(row.OccurredAtUtc,
                        TimeZoneInfo.FindSystemTimeZoneById(row.GroupingTimeZoneId)).DateTime), row.AgentId, row.ModelId)))
                    .GroupBy(row => (row.AgentId.Value, Model: row.ModelId.Value, Raw: row.ObservedModelId?.Value, row.ReasoningEffort, row.ServiceTier))
                    .Select(group => new UsageModelProfile(group.Key.Value, group.Key.Model, group.Key.Raw, group.Key.ReasoningEffort, group.Key.ServiceTier,
                        group.Min(row => row.OccurredAtUtc), group.Max(row => row.OccurredAtUtc), group.Sum(row => row.Tokens.Total))).ToArray(),
            };
        }, token);

    private async Task<UsageReport> RepriceAsync(UsageReport original, DateOnly from, DateOnly to, CancellationToken token)
    {
        if (to < from) return original;
        return await Task.Run(async () =>
        {
            UsageRepository repository = await UsageRepository.OpenReadOnlyAsync(_databasePath, token);
            DateTimeOffset fromUtc = new(from.AddDays(-1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
            DateTimeOffset toUtc = new(to.AddDays(2).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
            IReadOnlyList<UsageEvent> events = await repository.QueryUsageEventsAsync(fromUtc, toUtc, cancellationToken: token);
            return UsageReferencePricing.Apply(original, events, row =>
                row.AgentId.Value == "cursor" ? CursorPricingCatalog.Resolve(row.ModelId.Value, _priceReferenceUtc, row.Tokens)
                : row.AgentId.Value == "codex" ? CodexPricingCatalog.Resolve(row.ModelId.Value, row.Tokens, _priceReferenceUtc)
                : CostObservation.Unavailable());
        }, token);
    }

    private IEnumerable<UsageReportCompareRow> CreateMeasurementRows(UsageReport currentReport)
    {
        yield return new(GetString("UsageComparisonActiveDays"), UsageComparison.ActiveDays(_report).ToString(CultureInfo.CurrentCulture), UsageComparison.ActiveDays(currentReport).ToString(CultureInfo.CurrentCulture),
            FormatSignedCount(UsageComparison.ActiveDays(currentReport) - UsageComparison.ActiveDays(_report)));
        yield return new(GetString("UsageComparisonPriceCoverage"), FormatOptionalPercent(_report.Totals.PriceCoveragePercent), FormatOptionalPercent(currentReport.Totals.PriceCoveragePercent),
            FormatSignedPercentagePoints(currentReport.Totals.PriceCoveragePercent - _report.Totals.PriceCoveragePercent));
        yield return new(GetString("UsageComparisonCostPerMillion"), FormatOptionalUsd(UsageComparison.CostPerMillionPricedTokens(_report.Totals)),
            FormatOptionalUsd(UsageComparison.CostPerMillionPricedTokens(currentReport.Totals)),
            FormatOptionalSignedUsd(UsageComparison.CostPerMillionPricedTokens(_report.Totals), UsageComparison.CostPerMillionPricedTokens(currentReport.Totals)));
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

    public async Task SaveComparisonAsync()
    {
        if (!IsCompareScope || !CanChangeComparison || HasError || !_hasCurrentReport) return;
        _savingComparison = true;
        OnPropertyChanged(nameof(CanChangeComparison));
        try
        {
            var definition = new UsageComparisonDefinition(CompareAxis.ToString(), ComparisonPreset?.Preset.ToString() ?? "",
                _compareLeftStart, _compareLeftEnd, _compareRightStart, _compareRightEnd, TimeZoneInfo.Local.Id,
                UseReferencePrices ? _priceReferenceUtc : null, BaselineModel?.Name, CurrentModel?.Name,
                MeasurementEvidence, UsageRepository.ComparisonDataRevision(IsCompareCyclesAxis ? _cycleReports.Select(entry => entry.Report).ToArray() : [_report, _compareRightReport]))
            {
                BaselineLabel = CompareLeftLabel,
                CurrentLabel = CompareRightLabel,
                CycleComparison = CreateCycleComparison(),
            };
            var snapshot = new SavedUsageComparison(Guid.NewGuid().ToString("N"), _clock.GetUtcNow(), definition, _report, _compareRightReport)
            { Cycles = IsCompareCyclesAxis ? _cycleReports : [] };
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
            _cycleReports = snapshot.Cycles;
            _useReferencePrices = snapshot.Definition.PriceReferenceUtc.HasValue;
            if (snapshot.Definition.PriceReferenceUtc is { } reference) _priceReferenceUtc = reference;
            _comparisonPreset = ComparisonPresets.FirstOrDefault(option => option.Preset.ToString() == snapshot.Definition.Preset);
            _baselineModel = ComparisonModels.FirstOrDefault(option => option.Name == snapshot.Definition.BaselineModel);
            _currentModel = ComparisonModels.FirstOrDefault(option => option.Name == snapshot.Definition.CurrentModel);
            _compareModelPeriods = snapshot.Definition.BaselineStart != snapshot.Definition.CurrentStart
                || snapshot.Definition.BaselineEnd != snapshot.Definition.CurrentEnd;
            _compareAxis = Enum.Parse<UsageReportCompareAxis>(snapshot.Definition.Axis);
            _report = snapshot.Baseline;
            _compareRightReport = snapshot.Current;
            _compareLeftStart = snapshot.Definition.BaselineStart;
            _compareLeftEnd = snapshot.Definition.BaselineEnd;
            _compareRightStart = snapshot.Definition.CurrentStart;
            _compareRightEnd = snapshot.Definition.CurrentEnd;
            _measurementEvidence = GetString("UsageComparisonSavedEvidence") + " " + snapshot.Definition.Evidence;
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
            OnPropertyChanged(nameof(HasPeriodComparisonOptions));
            OnPropertyChanged(nameof(HasSavedComparison));
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
        OnPropertyChanged(nameof(CanChangeComparison)); OnPropertyChanged(nameof(HasSavedComparison));
        _ = LoadAsync();
    }
}
