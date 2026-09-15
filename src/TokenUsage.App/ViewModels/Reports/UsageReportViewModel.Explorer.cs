using System.Globalization;
using System.ComponentModel;
using TokenUsage.Core.Automation;
using TokenUsage.Core.Providers;
using TokenUsage.Core.Usage;

namespace TokenUsage.App.ViewModels.Reports;

public sealed record UsageExplorerSessionRow(
    string? SessionKey,
    string Label,
    string ContributionText,
    string SessionTotalText,
    bool IsUnassigned,
    bool CanExpand = false,
    bool IsExpanded = false,
    int Depth = 0,
    string InclusiveText = "",
    string? ParentSessionKey = null,
    string ExpandGlyph = "");

public sealed record UsageExplorerProjectRow(
    string? ProjectKey,
    string Label,
    string MappingText,
    string ContributionText,
    string ProjectTotalText,
    bool IsUnassigned,
    bool IsAmbiguous);

public sealed record UsageExplorerOperationRow(
    string Id,
    string Label,
    string KindText,
    string CountText,
    string OutcomeText,
    int InvocationCount,
    UsageOperationKind Kind,
    string Tool,
    string? Server,
    string? SessionKey = null);

public sealed record UsageExplorerDerivedActivityRow(
    string Category,
    string Label,
    string CountText,
    int Count);

public sealed record UsageExplorerWorkflowRow(
    string Id,
    string Label,
    string DetailText,
    bool CanOpenEvidence);

public sealed record UsageExplorerEvidenceRow(
    string SequenceText,
    string TimeText,
    string KindText,
    string ToolText,
    string OutcomeText,
    string SessionText,
    string AutomationName);

public sealed record UsageExplorerOption(string? Id, string Name, bool IsAll = false);

public sealed class UsageConfigurationOption(string? id, string name, bool selected, Action changed) : INotifyPropertyChanged
{
    private bool _isSelected = selected;
    public string? Id { get; } = id;
    public string Name { get; } = name;
    public event PropertyChangedEventHandler? PropertyChanged;
    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (value == _isSelected) return;
            _isSelected = value;
            PropertyChanged?.Invoke(this, new(nameof(IsSelected)));
            changed();
        }
    }
}

public sealed partial class UsageReportViewModel
{
    private readonly SemaphoreSlim _reportWorkGate = new(1, 1);
    private UsageReport _explorerSource = UsageReportQuery.Build([]);
    private CancellationTokenSource? _selectionCancellation;
    private long _selectionGeneration;
    private string _modelSearch = string.Empty;
    private UsageExplorerOption? _explorerTool;
    private UsageExplorerOption? _explorerHost;
    private UsageExplorerOption? _explorerModel;
    private bool _clearingConfigurations;
    private bool _loadConfigurations;
    private string? _detailModelId;
    private ExplorerReturnState? _explorerReturn;
    private bool _codexSessionsAllowed;
    private bool _codexProjectsAllowed;
    private bool _cursorSessionsAllowed;
    private bool _codexMcpAllowed;
    private bool _codexSkillsAllowed;
    private bool _codexCommandsAllowed;
    private bool _codexFilesAllowed;
    private long _consentGeneration;
    private string _projectAliasDraft = string.Empty;
    private string? _distributionOutlierSessionKey;
    private bool _returnToProjects;
    private bool _returnToSessions;
    private bool _projectsFromOverview;
    private string? _overviewProjectReturnId;
    private IReadOnlyList<UsageSessionContribution> _sessionContributions = [];
    private IReadOnlyList<UsageSessionLineageNode> _sessionLineage = [];
    private IReadOnlyList<UsageExplorerOperationRow> _allOperationRows = [];
    private IReadOnlyList<UsageExplorerOperationRow> _allMixedOperationRows = [];
    private string? _operationEvidenceFilter;
    private IReadOnlyList<UsageWorkflowEvidenceEvent> _workflowContributingEvents = [];
    private bool _hasUnlinkedOperations;
    private bool _legacyUnreconciled;
    private OperationLoadScope _activeOperationScope = OperationLoadScope.Global;
    private readonly HashSet<string> _expandedSessions = new(StringComparer.Ordinal);
    private sealed record ExplorerReturnState(UsageReportRequest Request, UsageReportValueMode ValueMode,
        bool UsesResetCycle, UsageReportResetCycleOption? Cycle, string ModelId);

    public IReadOnlyList<UsageExplorerOption> ExplorerTools { get; private set; } = [];
    public IReadOnlyList<UsageExplorerOption> ExplorerHosts { get; private set; } = [];
    public IReadOnlyList<UsageExplorerOption> ExplorerModels { get; private set; } = [];
    public IReadOnlyList<UsageConfigurationOption> ExplorerObservedModels { get; private set; } = [];
    public IReadOnlyList<UsageConfigurationOption> ExplorerEfforts { get; private set; } = [];
    public IReadOnlyList<UsageConfigurationOption> ExplorerTiers { get; private set; } = [];
    public bool HasConfigurationOptions => HasExplorer && _explorerSource.HasConfigurationDetails;
    public bool CanLoadConfigurations => CanFilterExplorer && !HasConfigurationOptions;
    public string ConfigurationCoverageText => DescribeConfigurationCoverage(_report);
    private string DescribeConfigurationCoverage(UsageReport report) => report.ConfigurationCoverage is { } coverage
        ? string.Format(CultureInfo.CurrentCulture, GetString("UsageConfigurationCoverageFormat"),
            coverage.AvailableRecords, coverage.RetainedRecords, coverage.MissingRecords,
            coverage.FirstDate?.ToString("d", CultureInfo.CurrentCulture) ?? GetString("UsageExplorerNotAvailable"),
            coverage.LastDate?.ToString("d", CultureInfo.CurrentCulture) ?? GetString("UsageExplorerNotAvailable"))
            + (report.IsExactInterval ? " " + GetString("UsageConfigurationExactCoverage") : string.Empty)
        : GetString("UsageConfigurationLoadHint");

    private string DescribeConfiguration(UsageReport report)
    {
        UsageReportSelection selection = report.ConfigurationSelection ?? new();
        string Dimension(IEnumerable<string?> values, string all)
        {
            string[] names = values.Select(value => value ?? GetString("UsageConfigurationUnknown")).ToArray();
            return names.Length == 0 ? GetString(all) : string.Join(", ", names);
        }
        return string.Format(CultureInfo.CurrentCulture, GetString("UsageConfigurationSelectionFormat"),
            Dimension(selection.ObservedModels.Select(model => model?.Value), "UsageConfigurationAllObserved"),
            Dimension(selection.ReasoningEfforts, "UsageConfigurationAllEfforts"),
            Dimension(selection.ServiceTiers, "UsageConfigurationAllTiers")) + " " + DescribeConfigurationCoverage(report);
    }
    public bool HasExplorer => !IsCompareScope;
    public bool HasExplorerAnalysisBody =>
        HasExplorer && (HasModelDetail || HasProjects || HasSessions || HasOperations);
    public string ExplorerContextPath { get; private set; } = string.Empty;
    public bool IsLoadingConfigurations => _loadConfigurations && IsLoading && !HasConfigurationOptions;
    public bool HasConfigurationEmpty =>
        _loadConfigurations && !IsLoading && !HasError && !HasConfigurationOptions;
    public bool HasExplorerSourceWarning { get; private set; }
    public bool HasExplorerSourceOk => !HasExplorerSourceWarning;
    private bool HasExplorerFilters => HasExplorer && (IsGlobalScope && _explorerTool is { IsAll: false }
        || _explorerHost is { IsAll: false } || _explorerModel is { IsAll: false }
        || ExplorerObservedModels.Any(option => option.IsSelected) || ExplorerEfforts.Any(option => option.IsSelected)
        || ExplorerTiers.Any(option => option.IsSelected) || !string.IsNullOrWhiteSpace(ModelSearch));
    public bool HasExplorerReturn => IsCompareScope && _explorerReturn is not null;
    public bool CanFilterExplorer => HasExplorer && !IsLoading && !_disposed;
    public bool IsExplorerToolVisible => IsGlobalScope;
    public bool IsFilteringModels { get; private set; }
    public string ExplorerNotice { get; private set; } = string.Empty;
    public string ExplorerSelectionContext { get; private set; } = string.Empty;
    public bool HasHiddenExplorerFilters { get; private set; }
    public string HiddenExplorerFilterSummary { get; private set; } = string.Empty;
    public string ExplorerSourceStatus { get; private set; } = string.Empty;
    public UsageReportModelRow? DetailModel { get; private set; }
    public bool HasModelDetail => HasExplorer && DetailModel is not null && !HasSessions && !HasProjects && !HasOperations;
    public bool CanOpenSessions => HasExplorer && !HasSessions
        && (HasModelDetail
            || (HasProjects && DetailProject is not null)
            || (_projectsFromOverview && _codexSessionsAllowed))
        && ((_projectsFromOverview && _codexSessionsAllowed)
            || (DetailModel is { } model && SessionCapabilityAllowed(model.ProviderId)));
    public bool CanOpenProjects => _codexProjectsAllowed
        && ((HasModelDetail && DetailModel is { ProviderId: "codex" }) || _projectsFromOverview);
    public bool CanOpenOperations =>
        (_codexMcpAllowed || _codexSkillsAllowed || _codexCommandsAllowed || _codexFilesAllowed)
        && ((HasModelDetail && DetailModel is { ProviderId: "codex" })
            || HasProjectDetail
            || HasSessionDetail
            || _projectsFromOverview);
    public bool CanOpenDistributionOutlier => HasModelDetail
        && _distributionOutlierSessionKey is not null
        && DetailModel is { } outlierModel
        && SessionCapabilityAllowed(outlierModel.ProviderId);
    public bool HasSessions { get; private set; }
    public bool HasProjects { get; private set; }
    public bool IsOverviewProjectNavigation => _projectsFromOverview;
    public string? OverviewProjectReturnId => _overviewProjectReturnId;
    public string ProjectsHeading { get; private set; } = string.Empty;
    public string OpenOperationsLabel => GetString("UsageExplorerOpenOperationsLabel");
    public bool HasOperations { get; private set; }
    public IReadOnlyList<UsageExplorerSessionRow> SessionRows { get; private set; } = [];
    public IReadOnlyList<UsageExplorerProjectRow> ProjectRows { get; private set; } = [];
    public IReadOnlyList<UsageExplorerOperationRow> OperationRows { get; private set; } = [];
    public UsageExplorerSessionRow? DetailSession { get; private set; }
    public UsageExplorerProjectRow? DetailProject { get; private set; }
    public UsageExplorerOperationRow? DetailOperation { get; private set; }
    public bool HasSessionDetail => HasSessions && DetailSession is not null;
    public bool HasProjectDetail => HasProjects && DetailProject is not null;
    public bool HasOperationDetail => HasOperations && DetailOperation is not null;
    public bool CanOpenOperationSession => HasOperationDetail
        && DetailOperation?.SessionKey is { Length: > 0 }
        && ((_projectsFromOverview && _codexSessionsAllowed)
            || (DetailModel is { } linkedModel && SessionCapabilityAllowed(linkedModel.ProviderId)));
    public string SessionDetailValues { get; private set; } = string.Empty;
    public string ProjectDetailValues { get; private set; } = string.Empty;
    public string OperationDetailValues { get; private set; } = string.Empty;
    public string OperationsAvailabilityText { get; private set; } = string.Empty;
    public bool HasOperationsAvailabilityNotice => !HasOperations
        && !string.IsNullOrEmpty(OperationsAvailabilityText);
    public string SkillsAvailabilityText { get; private set; } = string.Empty;
    public bool HasSkillsAvailabilityNotice => !string.IsNullOrEmpty(SkillsAvailabilityText);
    public string OperationsDerivedNote { get; private set; } = string.Empty;
    public bool HasOperationsDerivedNote => !string.IsNullOrEmpty(OperationsDerivedNote);
    public IReadOnlyList<UsageExplorerDerivedActivityRow> DerivedActivityRows { get; private set; } = [];
    public bool HasDerivedActivity => DerivedActivityRows.Count > 0;
    public string DerivedActivityNote { get; private set; } = string.Empty;
    public bool HasDerivedActivityNote => !string.IsNullOrEmpty(DerivedActivityNote);
    public bool HasDerivedActivityReturn { get; private set; }
    public IReadOnlyList<UsageExplorerWorkflowRow> WorkflowRows { get; private set; } = [];
    public bool HasWorkflowIndicators => WorkflowRows.Count > 0;
    public bool HasWorkflowReturn { get; private set; }
    public IReadOnlyList<UsageExplorerEvidenceRow> WorkflowEvidenceRows { get; private set; } = [];
    public bool HasWorkflowEvidence => WorkflowEvidenceRows.Count > 0;
    public bool HasRankedOperations => HasOperations && !HasWorkflowEvidence;
    public string DerivedActivityMethodText { get; private set; } = string.Empty;
    public string ProjectAliasDraft
    {
        get => _projectAliasDraft;
        set
        {
            if (_projectAliasDraft == value)
            {
                return;
            }

            _projectAliasDraft = value;
            OnPropertyChanged(nameof(ProjectAliasDraft));
            OnPropertyChanged(nameof(CanSaveProjectAlias));
        }
    }

    public bool CanSaveProjectAlias =>
        HasProjectDetail && DetailProject is { IsUnassigned: false, ProjectKey: not null }
        && !string.IsNullOrWhiteSpace(ProjectAliasDraft);
    public string DistributionSummary { get; private set; } = string.Empty;
    public string ModelDetailTitle => DetailModel is { } row
        ? row.ModelName + " · " + row.ProviderName + " · " + row.HostName : string.Empty;
    public string ModelDetailValues { get; private set; } = string.Empty;
    public string ModelDetailConfigurations { get; private set; } = string.Empty;
    public UsageReportTrendDataset ModelDetailTrend { get; private set; } = UsageReportTrendDataset.Empty;
    public string ExplorerValueComponents => string.Format(CultureInfo.CurrentCulture,
        GetString("UsageExplorerValuesFormat"), ExactCost(_report.Totals.ReportedCostUsd),
        ExactCost(_report.Totals.EstimatedCostUsd), _report.Totals.UnpricedTokens.ToString("N0", CultureInfo.CurrentCulture));

    public string ModelSearch
    {
        get => _modelSearch;
        set { if (_modelSearch == value) return; _modelSearch = value; OnPropertyChanged(); NotifyExplorerFilterContext(); QueueExplorerSelection(); }
    }

    public UsageExplorerOption? ExplorerTool
    {
        get => _explorerTool;
        set { if (value is null || value == _explorerTool) return; _explorerTool = value; OnPropertyChanged(); NotifyExplorerFilterContext(); QueueExplorerSelection(); }
    }

    public UsageExplorerOption? ExplorerHost
    {
        get => _explorerHost;
        set { if (value is null || value == _explorerHost) return; _explorerHost = value; OnPropertyChanged(); NotifyExplorerFilterContext(); QueueExplorerSelection(); }
    }

    public UsageExplorerOption? ExplorerModel
    {
        get => _explorerModel;
        set { if (value is null || value == _explorerModel) return; _explorerModel = value; OnPropertyChanged(); NotifyExplorerFilterContext(); QueueExplorerSelection(); }
    }

    public async Task LoadConfigurationsAsync()
    {
        if (!CanLoadConfigurations) return;
        _loadConfigurations = true;
        _globalReportRange = null;
        await LoadAsync();
    }

    private UsageReportSelection ExplorerSelection() => new()
    {
        Agents = IsGlobalScope && _explorerTool is { IsAll: false, Id: { } tool } ? [new AgentId(tool)] : [],
        ModelProviders = _explorerHost is { IsAll: false } host
            ? [host.Id is { } id ? new ModelProviderId(id) : null] : [],
        Models = _explorerModel is { IsAll: false, Id: { } model } ? [new ModelId(model)] : [],
        ObservedModels = ExplorerObservedModels.Where(option => option.IsSelected)
            .Select(option => option.Id is { } id ? new ModelId(id) : null).ToArray(),
        ReasoningEfforts = ExplorerEfforts.Where(option => option.IsSelected).Select(option => option.Id).ToArray(),
        ServiceTiers = ExplorerTiers.Where(option => option.IsSelected).Select(option => option.Id).ToArray(),
        Search = ModelSearch,
    };

    private UsageDetailSelection CurrentDetailSelection()
    {
        UsageReportSelection selection = ExplorerSelection();
        return new UsageDetailSelection(
            selection.ObservedModels,
            selection.ReasoningEfforts,
            selection.ServiceTiers);
    }

    private bool SessionCapabilityAllowed(string providerId) =>
        string.Equals(providerId, "cursor", StringComparison.Ordinal)
            ? _cursorSessionsAllowed
            : _codexSessionsAllowed;

    private UsageReportSelection ComparisonConfigurationSelection()
    {
        UsageReportSelection selection = ExplorerSelection();
        return new()
        {
            ObservedModels = selection.ObservedModels,
            ReasoningEfforts = selection.ReasoningEfforts,
            ServiceTiers = selection.ServiceTiers,
        };
    }

    private Task<UsageReport> SelectComparisonConfigurationAsync(UsageReport report, CancellationToken token)
    {
        UsageReportSelection selection = ComparisonConfigurationSelection();
        return selection.HasConfigurationFilters ? RunReportWorkAsync(() => Task.FromResult(UsageReportQuery.Select(report with
        {
            // An empty comparison period has no eligible observations to load.
            HasConfigurationDetails = report.HasConfigurationDetails || report.Totals.EventCount == 0,
        }, selection)), token) : Task.FromResult(report);
    }

    private void RebuildExplorerOptions()
    {
        ExplorerTools = [new(null, GetString("UsageExplorerAllTools"), true), .. _explorerSource.Agents
            .OrderBy(row => row.AgentId.Value, StringComparer.Ordinal)
            .Select(row => new UsageExplorerOption(row.AgentId.Value, ProviderName(row.AgentId.Value)))];
        ExplorerHosts = [new(null, GetString("UsageExplorerAllHosts"), true), .. _explorerSource.Models
            .Select(row => row.ModelProviderId?.Value).Distinct().Order(StringComparer.Ordinal)
            .Select(id => new UsageExplorerOption(id, id ?? GetString("UsageReportUnknownHost")))];
        ExplorerModels = [new(null, GetString("UsageExplorerAllModels"), true), .. _explorerSource.Models
            .Select(row => row.ModelId.Value).Distinct().Order(StringComparer.Ordinal)
            .Select(id => new UsageExplorerOption(id, ReportDataProjection.ModelName(id)))];
        ExplorerTools = PreserveOption(ExplorerTools, _explorerTool);
        ExplorerHosts = PreserveOption(ExplorerHosts, _explorerHost);
        ExplorerModels = PreserveOption(ExplorerModels, _explorerModel);
        ExplorerObservedModels = ConfigurationOptions(_explorerSource.Configurations.Select(row => row.ObservedModel?.Value),
            ExplorerObservedModels);
        ExplorerEfforts = ConfigurationOptions(_explorerSource.Configurations.Select(row => row.Effort),
            ExplorerEfforts);
        ExplorerTiers = ConfigurationOptions(_explorerSource.Configurations.Select(row => row.Tier),
            ExplorerTiers);
        OnPropertyChanged(nameof(ExplorerObservedModels)); OnPropertyChanged(nameof(ExplorerEfforts)); OnPropertyChanged(nameof(ExplorerTiers));
        _explorerTool = Retain(ExplorerTools, _explorerTool);
        _explorerHost = Retain(ExplorerHosts, _explorerHost);
        _explorerModel = Retain(ExplorerModels, _explorerModel);
        OnPropertyChanged(nameof(ExplorerTools)); OnPropertyChanged(nameof(ExplorerHosts)); OnPropertyChanged(nameof(ExplorerModels));
        OnPropertyChanged(nameof(ExplorerTool)); OnPropertyChanged(nameof(ExplorerHost)); OnPropertyChanged(nameof(ExplorerModel));

        static UsageExplorerOption Retain(IReadOnlyList<UsageExplorerOption> options, UsageExplorerOption? previous) =>
            previous is null ? options[0] : options.FirstOrDefault(row => row.Id == previous.Id && row.IsAll == previous.IsAll)
                ?? previous;
        static IReadOnlyList<UsageExplorerOption> PreserveOption(IReadOnlyList<UsageExplorerOption> options, UsageExplorerOption? previous) =>
            previous is not null && !options.Any(row => row.Id == previous.Id && row.IsAll == previous.IsAll)
                ? [.. options, previous] : options;

        IReadOnlyList<UsageConfigurationOption> ConfigurationOptions(IEnumerable<string?> values, IReadOnlyList<UsageConfigurationOption> previous)
        {
            var selected = previous.Where(option => option.IsSelected).Select(option => option.Id).ToHashSet();
            return values.Concat(selected).Distinct().Order(StringComparer.Ordinal)
                .Select(id => new UsageConfigurationOption(id, id ?? GetString("UsageConfigurationUnknown"), selected.Contains(id),
                    () => { if (!_clearingConfigurations) QueueExplorerSelection(); })).ToArray();
        }
    }

    public void ClearExplorerFilters()
    {
        _explorerTool = ExplorerTools.Count > 0 ? ExplorerTools[0] : null;
        _explorerHost = ExplorerHosts.Count > 0 ? ExplorerHosts[0] : null;
        _explorerModel = ExplorerModels.Count > 0 ? ExplorerModels[0] : null;
        _clearingConfigurations = true;
        try
        {
            foreach (UsageConfigurationOption option in ExplorerObservedModels.Concat(ExplorerEfforts).Concat(ExplorerTiers))
                option.IsSelected = false;
        }
        finally { _clearingConfigurations = false; }
        _modelSearch = string.Empty;
        OnPropertyChanged(nameof(ExplorerTool)); OnPropertyChanged(nameof(ExplorerHost));
        OnPropertyChanged(nameof(ExplorerModel)); OnPropertyChanged(nameof(ModelSearch));
        NotifyExplorerFilterContext();
        QueueExplorerSelection();
    }

    public void ClearHiddenExplorerFilters()
    {
        _explorerHost = ExplorerHosts.Count > 0 ? ExplorerHosts[0] : null;
        _clearingConfigurations = true;
        try
        {
            foreach (UsageConfigurationOption option in ExplorerObservedModels.Concat(ExplorerEfforts).Concat(ExplorerTiers))
                option.IsSelected = false;
        }
        finally { _clearingConfigurations = false; }
        OnPropertyChanged(nameof(ExplorerHost));
        NotifyExplorerFilterContext();
        QueueExplorerSelection();
    }

    private void CancelExplorerSelection()
    {
        _selectionGeneration++;
        _selectionCancellation?.Cancel();
        _selectionCancellation = null;
        IsFilteringModels = false;
        OnPropertyChanged(nameof(IsFilteringModels));
        OnPropertyChanged(nameof(CanCaptureReport));
    }

    private void QueueExplorerSelection()
    {
        if (!CanFilterExplorer) return;
        CancelExplorerSelection();
        var cancellation = new CancellationTokenSource();
        _selectionCancellation = cancellation;
        _ = ApplyExplorerSelectionAsync(_explorerSource, ExplorerSelection(), _selectionGeneration, cancellation);
    }

    private async Task ApplyExplorerSelectionAsync(UsageReport source, UsageReportSelection selection,
        long generation, CancellationTokenSource cancellation)
    {
        IsFilteringModels = true;
        OnPropertyChanged(nameof(IsFilteringModels));
        OnPropertyChanged(nameof(CanCaptureReport));
        try
        {
            UsageReport result = await RunReportWorkAsync(
                () => Task.FromResult(UsageReportQuery.Select(source, selection)), cancellation.Token);
            if (_disposed || IsLoading || generation != _selectionGeneration) return;
            _report = result;
            HasError = false;
            StatusText = string.Empty;
            RebuildProjection();
            if (HasSessions)
            {
                await OpenSessionsAsync();
            }
            else if (HasProjects)
            {
                await OpenProjectsAsync();
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        catch (Exception exception)
        {
            if (!_disposed && generation == _selectionGeneration)
            {
                HasError = true;
                StatusText = GetString("UsageReportReadFailed") + " " + exception.GetType().Name;
            }
        }
        finally
        {
            if (ReferenceEquals(_selectionCancellation, cancellation))
            {
                _selectionCancellation = null;
                IsFilteringModels = false;
                OnPropertyChanged(nameof(IsFilteringModels));
                OnPropertyChanged(nameof(CanCaptureReport));
            }
            cancellation.Dispose();
        }
    }

    private async Task<T> RunReportWorkAsync<T>(Func<Task<T>> read, CancellationToken token)
    {
        await _reportWorkGate.WaitAsync(token);
        try { return await Task.Run(read, token); }
        finally { _reportWorkGate.Release(); }
    }

    public void OpenModelDetail(string id)
    {
        ExplorerNotice = string.Empty;
        OnPropertyChanged(nameof(ExplorerNotice));
        _detailModelId = id;
        RebuildExplorerDetail();
        _ = RefreshDistributionAsync();
    }

    public void CloseModelDetail()
    {
        _detailModelId = null;
        _returnToProjects = false;
        _projectsFromOverview = false;
        HasSessions = false;
        HasProjects = false;
        HasOperations = false;
        DetailSession = null;
        DetailProject = null;
        DetailOperation = null;
        SessionRows = [];
        ProjectRows = [];
        OperationRows = [];
        RebuildExplorerDetail();
    }

    public async Task OpenSessionsAsync()
    {
        if (_attributionConsent is null)
        {
            return;
        }

        if (!_projectsFromOverview && DetailModel is null)
        {
            return;
        }

        if (!HasSessions && !CanOpenSessions)
        {
            return;
        }

        long generation = _selectionGeneration;
        long consentGeneration = _consentGeneration;
        CancellationToken token = _selectionCancellation?.Token ?? CancellationToken.None;
        UsageDetailSelection detail = CurrentDetailSelection();
        bool omitModel = _projectsFromOverview;
        string providerId = omitModel
            ? "codex"
            : DetailModel?.ProviderId ?? "codex";
        IReadOnlyList<UsageSessionContribution> contributions = await RunReportWorkAsync(async () =>
        {
            AttributionCapability capability = SessionCapabilityFor(providerId);
            AttributionConsent consent = await _attributionConsent.LoadAsync(capability, token)
                .ConfigureAwait(false);
            long epoch = consent.ActiveLinkEpoch;
            AttributionConsent projectConsent = await _attributionConsent
                .LoadAsync(AttributionCapability.CodexProject, token)
                .ConfigureAwait(false);
            UsageRepository repository = await UsageRepository.OpenReadOnlyAsync(_databasePath, token)
                .ConfigureAwait(false);
            IReadOnlyList<UsageSessionContribution> rows = await repository.ReadSessionContributionsAsync(
                StartDate,
                EndDate,
                epoch,
                new AgentId(providerId),
                omitModel ? null : DetailModel?.ModelProviderId is { } host ? new ModelProviderId(host) : null,
                omitModel ? null : DetailModel is { } selected ? new ModelId(selected.ModelId) : null,
                capability,
                DetailProject?.ProjectKey is { } key ? new OpaqueAttributionKey(key) : null,
                DetailProject is { IsUnassigned: true },
                projectConsent.AllowsLinks ? projectConsent.Epoch : 0,
                detail,
                token).ConfigureAwait(false);
            AttributionConsent published = await _attributionConsent.LoadAsync(capability, token)
                .ConfigureAwait(false);
            if (!published.AcceptsEpoch(epoch) && epoch != 0)
            {
                return Array.Empty<UsageSessionContribution>();
            }

            if (published.ActiveLinkEpoch != epoch)
            {
                return Array.Empty<UsageSessionContribution>();
            }

            return rows;
        }, token);
        if (_disposed || generation != _selectionGeneration || consentGeneration != _consentGeneration)
        {
            return;
        }

        IReadOnlyList<UsageSessionLineageNode> lineage = SessionLineageAccounting.Build(
            contributions,
            providerId == "codex" ? ParentChildAccountingKind.Exclusive : ParentChildAccountingKind.Unknown);
        _sessionContributions = contributions;
        _sessionLineage = lineage;
        _expandedSessions.Clear();
        SessionRows = MapSessionRows(contributions, lineage);
        HasSessions = true;
        _returnToProjects = DetailProject is not null;
        HasProjects = false;
        HasOperations = false;
        DetailSession = null;
        SessionDetailValues = string.Empty;
        OnPropertyChanged(nameof(SessionRows));
        OnPropertyChanged(nameof(HasSessions));
        OnPropertyChanged(nameof(HasProjects));
        OnPropertyChanged(nameof(HasOperations));
        OnPropertyChanged(nameof(CanOpenSessions));
        OnPropertyChanged(nameof(CanOpenProjects));
        OnPropertyChanged(nameof(CanOpenOperations));
        OnPropertyChanged(nameof(HasModelDetail));
        OnPropertyChanged(nameof(HasSessionDetail));
        OnPropertyChanged(nameof(HasProjectDetail));
        OnPropertyChanged(nameof(DetailSession));
        OnPropertyChanged(nameof(SessionDetailValues));
        NotifyExplorerContextPath();
    }

    public void CloseSessions()
    {
        HasSessions = false;
        DetailSession = null;
        SessionRows = [];
        SessionDetailValues = string.Empty;
        OnPropertyChanged(nameof(SessionRows));
        OnPropertyChanged(nameof(HasSessions));
        OnPropertyChanged(nameof(HasSessionDetail));
        OnPropertyChanged(nameof(DetailSession));
        OnPropertyChanged(nameof(SessionDetailValues));
        if (_returnToProjects)
        {
            _returnToProjects = false;
            HasProjects = true;
            OnPropertyChanged(nameof(HasProjects));
            OnPropertyChanged(nameof(HasProjectDetail));
            OnPropertyChanged(nameof(CanOpenSessions));
            OnPropertyChanged(nameof(CanOpenProjects));
            OnPropertyChanged(nameof(CanOpenOperations));
            OnPropertyChanged(nameof(HasModelDetail));
            NotifyExplorerContextPath();
            return;
        }

        OnPropertyChanged(nameof(CanOpenSessions));
        OnPropertyChanged(nameof(CanOpenProjects));
        OnPropertyChanged(nameof(CanOpenOperations));
        OnPropertyChanged(nameof(HasModelDetail));
        NotifyExplorerContextPath();
    }

    public void OpenSessionDetail(string? sessionKey)
    {
        DetailSession = SessionRows.FirstOrDefault(row => row.SessionKey == sessionKey);
        SessionDetailValues = DetailSession is { } session
            ? session.Label + " · " + session.ContributionText + " · " + session.SessionTotalText
            : string.Empty;
        OnPropertyChanged(nameof(DetailSession));
        OnPropertyChanged(nameof(HasSessionDetail));
        OnPropertyChanged(nameof(SessionDetailValues));
        NotifyExplorerContextPath();
    }

    public async Task OpenModelComparisonAsync()
    {
        if (_disposed || IsLoading || DetailModel is not { } row) return;
        _explorerReturn = new(new UsageReportRequest(Scope, SelectedProvider?.ProviderId,
            WindowDays, Metric, Breakdown), ValueMode, _usesResetCycle, SelectedResetCycle, row.Id);
        var selected = ComparisonModels.FirstOrDefault(model =>
            (model.AgentId, model.ModelProviderId, model.ModelId) == (row.ProviderId, row.ModelProviderId, row.ModelId));
        if (selected is null) return;
        _baselineModel = selected;
        _currentModel = selected;
        _scope = UsageReportScope.Compare;
        _compareAxis = UsageReportCompareAxis.Models;
        _compareModelPeriods = false;
        _useReferencePrices = false;
        _usesResetCycle = false;
        _valueMode = UsageReportValueMode.Absolute;
        OnPropertyChanged(nameof(BaselineModel)); OnPropertyChanged(nameof(CurrentModel));
        OnPropertyChanged(nameof(CompareModelPeriods)); OnPropertyChanged(nameof(UseReferencePrices));
        NotifyScopeChanged();
        NotifyExplorerChanged();
        await LoadAsync();
    }

    public async Task ReturnToExplorerAsync()
    {
        if (_disposed || _explorerReturn is not { } saved) return;
        ApplyRequestCore(saved.Request);
        _valueMode = saved.ValueMode;
        _usesResetCycle = saved.UsesResetCycle;
        _selectedResetCycle = saved.Cycle;
        _detailModelId = saved.ModelId;
        _explorerReturn = null;
        NotifyScopeChanged();
        await LoadAsync();
    }

    private void RebuildExplorerDetail()
    {
        if (HasSessions || HasProjects)
        {
            HasSessions = false;
            HasProjects = false;
            DetailSession = null;
            DetailProject = null;
            SessionRows = [];
            ProjectRows = [];
            SessionDetailValues = string.Empty;
            ProjectDetailValues = string.Empty;
            OnPropertyChanged(nameof(SessionRows));
            OnPropertyChanged(nameof(ProjectRows));
            OnPropertyChanged(nameof(HasSessions));
            OnPropertyChanged(nameof(HasProjects));
            OnPropertyChanged(nameof(HasSessionDetail));
            OnPropertyChanged(nameof(HasProjectDetail));
            OnPropertyChanged(nameof(DetailSession));
            OnPropertyChanged(nameof(DetailProject));
            OnPropertyChanged(nameof(SessionDetailValues));
            OnPropertyChanged(nameof(ProjectDetailValues));
            OnPropertyChanged(nameof(CanOpenSessions));
            OnPropertyChanged(nameof(CanOpenProjects));
        }

        DetailModel = ModelRows.FirstOrDefault(row => row.Id == _detailModelId);
        if (DetailModel is { } row)
        {
            UsageReport selected = UsageReportQuery.FilterByModel(_report, new AgentId(row.ProviderId),
                row.ModelProviderId is { } host ? new ModelProviderId(host) : null, new ModelId(row.ModelId));
            ModelDetailValues = string.Format(CultureInfo.CurrentCulture, GetString("UsageExplorerDetailFormat"),
                row.Metrics.Tokens.Total.ToString("N0", CultureInfo.CurrentCulture),
                ExactCost(row.Metrics.ReportedCostUsd), ExactCost(row.Metrics.EstimatedCostUsd),
                row.Metrics.UnpricedTokens.ToString("N0", CultureInfo.CurrentCulture), row.ShareText);
            ModelDetailTrend = CreateReportTrend(row.ProviderId, false, selected);
            ModelDetailConfigurations = selected.HasConfigurationDetails
                ? string.Join(Environment.NewLine, selected.Configurations.Select(configuration => string.Format(
                    CultureInfo.CurrentCulture, GetString("UsageConfigurationCompositionFormat"),
                    configuration.ObservedModel?.Value ?? GetString("UsageConfigurationUnknown"),
                    configuration.Effort ?? GetString("UsageConfigurationUnknown"),
                    configuration.Tier ?? GetString("UsageConfigurationUnknown"), configuration.Metrics.Tokens.Total,
                    ExactCost(configuration.Metrics.ReportedCostUsd), ExactCost(configuration.Metrics.EstimatedCostUsd),
                    configuration.Metrics.UnpricedTokens))) : GetString("UsageConfigurationLoadHint");
        }
        else
        {
            if (_detailModelId is not null)
            {
                ExplorerNotice = GetString("UsageExplorerDetailRemoved");
                OnPropertyChanged(nameof(ExplorerNotice));
            }
            _detailModelId = null;
            ModelDetailValues = string.Empty;
            ModelDetailConfigurations = string.Empty;
            ModelDetailTrend = UsageReportTrendDataset.Empty;
        }
        OnPropertyChanged(nameof(DetailModel)); OnPropertyChanged(nameof(HasModelDetail));
        OnPropertyChanged(nameof(CanOpenSessions));
        OnPropertyChanged(nameof(CanOpenProjects));
        OnPropertyChanged(nameof(CanOpenOperations));
        UpdateOperationsAvailability();
        OnPropertyChanged(nameof(ModelDetailTitle)); OnPropertyChanged(nameof(ModelDetailValues)); OnPropertyChanged(nameof(ModelDetailTrend));
        OnPropertyChanged(nameof(ModelDetailConfigurations));
        if (HasModelDetail)
        {
            _ = RefreshDistributionAsync();
        }
        else
        {
            DistributionSummary = string.Empty;
            OnPropertyChanged(nameof(DistributionSummary));
        }

        NotifyExplorerContextPath();
    }

    private string ExactCost(decimal? cost) => cost is { } value
        ? "$" + value.ToString("0.######", CultureInfo.CurrentCulture) + " USD" : GetString("UsageExplorerNotAvailable");

    private void NotifyExplorerChanged()
    {
        OnPropertyChanged(nameof(HasExplorer)); OnPropertyChanged(nameof(CanFilterExplorer));
        OnPropertyChanged(nameof(HasExplorerReturn));
        OnPropertyChanged(nameof(HasConfigurationOptions)); OnPropertyChanged(nameof(CanLoadConfigurations));
        OnPropertyChanged(nameof(IsLoadingConfigurations));
        OnPropertyChanged(nameof(HasConfigurationEmpty));
        OnPropertyChanged(nameof(ConfigurationCoverageText));
        OnPropertyChanged(nameof(IsExplorerToolVisible)); OnPropertyChanged(nameof(ExplorerValueComponents));
        OnPropertyChanged(nameof(CanOpenDistributionOutlier));
        NotifyExplorerFilterContext();
        _ = RefreshAttributionAvailabilityAsync();
        RebuildExplorerDetail();
        NotifyExplorerContextPath();
    }

    private void NotifyExplorerFilterContext()
    {
        var parts = new List<string>();
        if (IsExplorerToolVisible)
        {
            parts.Add(ExplorerTool?.Name ?? GetString("UsageExplorerAllTools"));
        }

        parts.Add(ExplorerHost?.Name ?? GetString("UsageExplorerAllHosts"));
        parts.Add(ExplorerModel?.Name ?? GetString("UsageExplorerAllModels"));
        if (!string.IsNullOrWhiteSpace(ModelSearch))
        {
            parts.Add(string.Format(
                CultureInfo.CurrentCulture,
                GetString("UsageExplorerSearchContextFormat"),
                ModelSearch.Trim()));
        }

        ExplorerSelectionContext = string.Join(" · ", parts);
        var hidden = new List<string>();
        if (_explorerHost is { IsAll: false })
        {
            hidden.Add(string.Format(
                CultureInfo.CurrentCulture,
                GetString("UsageExplorerHiddenHostFormat"),
                _explorerHost.Name));
        }

        string[] observed = ExplorerObservedModels.Where(option => option.IsSelected).Select(option => option.Name).ToArray();
        if (observed.Length > 0)
        {
            hidden.Add(string.Format(
                CultureInfo.CurrentCulture,
                GetString("UsageExplorerHiddenObservedFormat"),
                string.Join(", ", observed)));
        }

        string[] efforts = ExplorerEfforts.Where(option => option.IsSelected).Select(option => option.Name).ToArray();
        if (efforts.Length > 0)
        {
            hidden.Add(string.Format(
                CultureInfo.CurrentCulture,
                GetString("UsageExplorerHiddenEffortFormat"),
                string.Join(", ", efforts)));
        }

        string[] tiers = ExplorerTiers.Where(option => option.IsSelected).Select(option => option.Name).ToArray();
        if (tiers.Length > 0)
        {
            hidden.Add(string.Format(
                CultureInfo.CurrentCulture,
                GetString("UsageExplorerHiddenTierFormat"),
                string.Join(", ", tiers)));
        }

        HasHiddenExplorerFilters = hidden.Count > 0;
        HiddenExplorerFilterSummary = string.Join(" · ", hidden);
        OnPropertyChanged(nameof(ExplorerSelectionContext));
        OnPropertyChanged(nameof(HasHiddenExplorerFilters));
        OnPropertyChanged(nameof(HiddenExplorerFilterSummary));
        NotifyExplorerContextPath();
    }

    private void NotifyExplorerContextPath()
    {
        var parts = new List<string> { GetString("UsageExplorerContextOverview") };
        if (_projectsFromOverview)
        {
            parts.Add(GetString("UsageExplorerContextProjects"));
            if (DetailProject is { } project)
            {
                parts.Add(project.Label);
            }
        }
        else if (DetailModel is { } model)
        {
            parts.Add(model.ModelName);
        }

        if (HasSessions)
        {
            parts.Add(GetString("UsageExplorerContextSessions"));
            if (DetailSession is { } session)
            {
                parts.Add(session.Label);
            }
        }

        if (HasOperations)
        {
            parts.Add(string.IsNullOrWhiteSpace(OperationsHeading)
                ? GetString("UsageExplorerContextOperations")
                : OperationsHeading);
        }

        ExplorerContextPath = string.Join(" · ", parts);
        OnPropertyChanged(nameof(ExplorerContextPath));
        OnPropertyChanged(nameof(HasExplorerAnalysisBody));
        OnPropertyChanged(nameof(IsStandardReportVisible));
    }

    private void UpdateOperationsAvailability()
    {
        if (DetailModel is not { } model || HasOperations || HasSessions || HasProjects)
        {
            OperationsAvailabilityText = string.Empty;
        }
        else if (!string.Equals(model.ProviderId, "codex", StringComparison.Ordinal))
        {
            OperationsAvailabilityText = GetString("UsageExplorerOperationsUnsupported");
        }
        else if (!_codexMcpAllowed && !_codexSkillsAllowed && !_codexCommandsAllowed && !_codexFilesAllowed)
        {
            OperationsAvailabilityText = GetString("UsageExplorerOperationsDisabled");
        }
        else
        {
            OperationsAvailabilityText = string.Empty;
        }

        OnPropertyChanged(nameof(OperationsAvailabilityText));
        OnPropertyChanged(nameof(HasOperationsAvailabilityNotice));
    }
}
