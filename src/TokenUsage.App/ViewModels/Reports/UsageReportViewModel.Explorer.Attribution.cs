using System.Globalization;
using TokenUsage.Core.Automation;
using TokenUsage.Core.Providers;
using TokenUsage.Core.Usage;

namespace TokenUsage.App.ViewModels.Reports;

public sealed partial class UsageReportViewModel
{
    public async Task OpenProjectsAsync()
    {
        if (DetailModel is not { } row || _attributionConsent is null)
        {
            return;
        }

        if (!HasProjects && !CanOpenProjects)
        {
            return;
        }

        long generation = _selectionGeneration;
        long consentGeneration = _consentGeneration;
        CancellationToken token = _selectionCancellation?.Token ?? CancellationToken.None;
        UsageDetailSelection detail = CurrentDetailSelection();
        IReadOnlyList<UsageProjectContribution> contributions = await RunReportWorkAsync(async () =>
        {
            AttributionConsent consent = await _attributionConsent
                .LoadAsync(AttributionCapability.CodexProject, token)
                .ConfigureAwait(false);
            long epoch = consent.ActiveLinkEpoch;
            UsageRepository repository = await UsageRepository.OpenReadOnlyAsync(_databasePath, token)
                .ConfigureAwait(false);
            IReadOnlyList<UsageProjectContribution> rows = await repository.ReadProjectContributionsAsync(
                StartDate,
                EndDate,
                epoch,
                new AgentId(row.ProviderId),
                row.ModelProviderId is { } host ? new ModelProviderId(host) : null,
                new ModelId(row.ModelId),
                detail,
                token).ConfigureAwait(false);
            AttributionConsent published = await _attributionConsent
                .LoadAsync(AttributionCapability.CodexProject, token)
                .ConfigureAwait(false);
            return published.ActiveLinkEpoch == epoch ? rows : [];
        }, token);
        if (_disposed || generation != _selectionGeneration || consentGeneration != _consentGeneration)
        {
            return;
        }

        ProjectRows = await MapProjectRowsAsync(contributions, token);
        HasProjects = true;
        HasSessions = false;
        HasOperations = false;
        DetailProject = null;
        ProjectDetailValues = string.Empty;
        OnPropertyChanged(nameof(ProjectRows));
        OnPropertyChanged(nameof(HasProjects));
        OnPropertyChanged(nameof(HasSessions));
        OnPropertyChanged(nameof(CanOpenSessions));
        OnPropertyChanged(nameof(CanOpenProjects));
        OnPropertyChanged(nameof(HasModelDetail));
        OnPropertyChanged(nameof(HasProjectDetail));
        OnPropertyChanged(nameof(DetailProject));
        OnPropertyChanged(nameof(ProjectDetailValues));
        OnPropertyChanged(nameof(CanOpenOperations));
    }

    public void CloseProjects()
    {
        _returnToProjects = false;
        HasProjects = false;
        DetailProject = null;
        ProjectRows = [];
        ProjectDetailValues = string.Empty;
        OnPropertyChanged(nameof(ProjectRows));
        OnPropertyChanged(nameof(HasProjects));
        OnPropertyChanged(nameof(HasProjectDetail));
        OnPropertyChanged(nameof(DetailProject));
        OnPropertyChanged(nameof(ProjectDetailValues));
        OnPropertyChanged(nameof(CanOpenSessions));
        OnPropertyChanged(nameof(CanOpenProjects));
        OnPropertyChanged(nameof(HasModelDetail));
        OnPropertyChanged(nameof(CanOpenOperations));
    }

    public void OpenProjectDetail(string? projectKey)
    {
        DetailProject = ProjectRows.FirstOrDefault(row => row.ProjectKey == projectKey);
        ProjectAliasDraft = string.Empty;
        ProjectDetailValues = DetailProject is { } project
            ? project.Label + " · " + project.MappingText + " · " + project.ContributionText
            : string.Empty;
        OnPropertyChanged(nameof(DetailProject));
        OnPropertyChanged(nameof(HasProjectDetail));
        OnPropertyChanged(nameof(ProjectDetailValues));
        OnPropertyChanged(nameof(CanOpenSessions));
        OnPropertyChanged(nameof(ProjectAliasDraft));
        OnPropertyChanged(nameof(CanSaveProjectAlias));
    }

    public async Task OpenProjectSessionsAsync(string? projectKey)
    {
        OpenProjectDetail(projectKey);
        if (DetailProject is null)
        {
            return;
        }

        await OpenSessionsAsync().ConfigureAwait(false);
    }

    public void ToggleSessionExpand(string? sessionKey)
    {
        if (string.IsNullOrWhiteSpace(sessionKey))
        {
            return;
        }

        if (!_expandedSessions.Add(sessionKey))
        {
            _expandedSessions.Remove(sessionKey);
        }

        SessionRows = MapSessionRows(_sessionContributions, _sessionLineage);
        if (DetailSession is { } selected)
        {
            DetailSession = SessionRows.FirstOrDefault(row => row.SessionKey == selected.SessionKey);
            SessionDetailValues = DetailSession is { } session
                ? session.Label + " · " + session.ContributionText + " · " + session.SessionTotalText
                : string.Empty;
        }

        OnPropertyChanged(nameof(SessionRows));
        OnPropertyChanged(nameof(DetailSession));
        OnPropertyChanged(nameof(HasSessionDetail));
        OnPropertyChanged(nameof(SessionDetailValues));
    }

    internal async Task RefreshDistributionAsync()
    {
        if (DetailModel is not { } row)
        {
            DistributionSummary = string.Empty;
            _distributionOutlierSessionKey = null;
            OnPropertyChanged(nameof(DistributionSummary));
            OnPropertyChanged(nameof(CanOpenDistributionOutlier));
            return;
        }

        long generation = _selectionGeneration;
        CancellationToken token = _selectionCancellation?.Token ?? CancellationToken.None;
        UsageDetailSelection detail = CurrentDetailSelection();
        var result = await RunReportWorkAsync(async () =>
        {
            UsageRepository repository = await UsageRepository.OpenReadOnlyAsync(
                    _databasePath,
                    token)
                .ConfigureAwait(false);
            AgentId agent = new(row.ProviderId);
            ModelProviderId? host = row.ModelProviderId is { } hostId ? new ModelProviderId(hostId) : null;
            ModelId model = new(row.ModelId);
            UsageDistributionEligibility current = await repository.ReadDistributionEligibilityAsync(
                StartDate,
                EndDate,
                agent,
                host,
                model,
                detail,
                token).ConfigureAwait(false);
            UsageInputSizeShift? shift = null;
            UsageSessionOutlier? outlier = null;
            if (current.Availability is UsageStatisticAvailability.Measured
                or UsageStatisticAvailability.MeasuredZero
                or UsageStatisticAvailability.InsufficientSample)
            {
                if (!IsAllHistoryWindow)
                {
                    int days = RangeDayCount;
                    DateOnly priorEnd = StartDate.AddDays(-1);
                    DateOnly priorStart = priorEnd.AddDays(-(days - 1));
                    UsageDistributionEligibility baseline = await repository.ReadDistributionEligibilityAsync(
                        priorStart,
                        priorEnd,
                        agent,
                        host,
                        model,
                        detail,
                        token).ConfigureAwait(false);
                    (int currentSessions, bool currentAvailable) = await repository
                        .CountAttributedFinalSessionsAsync(StartDate, EndDate, agent, host, model, detail, token)
                        .ConfigureAwait(false);
                    (int baselineSessions, bool baselineAvailable) = await repository
                        .CountAttributedFinalSessionsAsync(priorStart, priorEnd, agent, host, model, detail, token)
                        .ConfigureAwait(false);
                    shift = UsageInputSizeShift.Evaluate(
                        baseline,
                        current,
                        baselineSessions,
                        currentSessions,
                        sessionsAvailable: baselineAvailable || currentAvailable);
                }
            }

            if (_attributionConsent is not null)
            {
                AttributionConsent consent = await _attributionConsent
                    .LoadAsync(SessionCapabilityFor(row.ProviderId), token)
                    .ConfigureAwait(false);
                if (consent.ActiveLinkEpoch > 0)
                {
                    IReadOnlyList<UsageSessionContribution> sessions = await repository
                        .ReadSessionContributionsAsync(
                            StartDate,
                            EndDate,
                            consent.ActiveLinkEpoch,
                            agent,
                            host,
                            model,
                            SessionCapabilityFor(row.ProviderId),
                            detail: detail,
                            cancellationToken: token)
                        .ConfigureAwait(false);
                    outlier = UsageSessionOutlier.Evaluate(sessions
                        .Where(session => !session.IsUnassigned && session.SessionKey is not null)
                        .Select(session => (session.SessionKey!.Value, session.SelectedTokens.Total))
                        .ToArray());
                }
            }

            return (current, shift, outlier);
        }, token);
        if (_disposed || generation != _selectionGeneration)
        {
            return;
        }

        _distributionOutlierSessionKey = result.outlier is { Availability: UsageStatisticAvailability.Measured, SessionKey: { } key }
            ? key
            : null;
        DistributionSummary = FormatDistributionSummary(result.current, result.shift, result.outlier);
        OnPropertyChanged(nameof(DistributionSummary));
        OnPropertyChanged(nameof(CanOpenDistributionOutlier));
    }

    public async Task OpenDistributionOutlierAsync()
    {
        if (_distributionOutlierSessionKey is not { } key)
        {
            return;
        }

        if (!HasSessions)
        {
            await OpenSessionsAsync().ConfigureAwait(true);
        }

        OpenSessionDetail(key);
    }

    public UsageReportSnapshotV2.Document CreateCanonicalSnapshot()
    {
        DateTimeOffset generatedAt = _clock.GetUtcNow().ToUniversalTime();
        generatedAt = generatedAt.AddTicks(-(generatedAt.Ticks % TimeSpan.TicksPerSecond));
        UsageReportSelection selection = ExplorerSelection();
        AgentId? agentId = IsProviderScope && SelectedProvider is { } provider
            ? new AgentId(provider.ProviderId)
            : selection.Agents.Count == 1 ? selection.Agents[0] : null;
        return UsageReportSnapshotV2.Create(
            generatedAt,
            IsCompareScope ? _compareLeftStart : StartDate,
            IsCompareScope ? _compareLeftEnd : EndDate,
            IsCompareScope
                ? _compareLeftEnd.DayNumber - _compareLeftStart.DayNumber + 1
                : RangeDayCount,
            agentId,
            _report,
            comparison: IsCompareScope ? _compareRightReport : null,
            comparisonFrom: IsCompareScope ? _compareRightStart : null,
            comparisonTo: IsCompareScope ? _compareRightEnd : null,
            selection: selection,
            overview: Overview);
    }

    public async Task<UsageReportSnapshotV2.Document> FreezeCanonicalSnapshotAsync(
        CancellationToken token = default)
    {
        UsageReportSnapshotV2.Document snapshot = CreateCanonicalSnapshot();
        if (_attributionConsent is null || !File.Exists(_databasePath))
        {
            return snapshot;
        }

        (IReadOnlyList<UsageSessionContribution> sessions, IReadOnlyList<UsageProjectContribution> projects) =
            await RunReportWorkAsync(() => LoadExportPopulationsAsync(token), token).ConfigureAwait(true);
        IReadOnlyList<UsageOperationRankedRow> operations = await RunReportWorkAsync(async () =>
        {
            var rows = new List<UsageOperationRankedRow>();
            if (!File.Exists(_databasePath))
            {
                return rows;
            }

            UsageRepository repository = await UsageRepository.OpenReadOnlyAsync(_databasePath, token)
                .ConfigureAwait(false);
            DateTimeOffset from = new(StartDate.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
            DateTimeOffset to = new(EndDate.AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
            await AddCapabilityRankingAsync(repository, AttributionCapability.CodexMcp, from, to, rows, token)
                .ConfigureAwait(false);
            await AddCapabilityRankingAsync(repository, AttributionCapability.CodexSkills, from, to, rows, token)
                .ConfigureAwait(false);
            await AddCapabilityRankingAsync(repository, AttributionCapability.CodexCommands, from, to, rows, token)
                .ConfigureAwait(false);
            await AddCapabilityRankingAsync(repository, AttributionCapability.CodexFiles, from, to, rows, token)
                .ConfigureAwait(false);
            return rows;
        }, token).ConfigureAwait(true);
        return snapshot with
        {
            Sessions = UsageReportSnapshotV2.MapSessions(sessions),
            Projects = UsageReportSnapshotV2.MapProjects(projects),
            Operations = operations is { Count: > 0 } ? UsageReportSnapshotV2.MapOperations(operations) : null,
        };
    }

    public Task<UsageReportSnapshotV2.Document> FinalizeExportSnapshotAsync(
        UsageReportSnapshotV2.Document frozen,
        CancellationToken token = default) =>
        UsageAttributionExport.RecheckConsentAsync(frozen, _attributionConsent, token);

    public string RenderCanonicalExport(string format) =>
        UsageReportSnapshotV2.Render(CreateCanonicalSnapshot(), format);

    private string FormatDistributionSummary(
        UsageDistributionEligibility eligibility,
        UsageInputSizeShift? shift,
        UsageSessionOutlier? outlier)
    {
        string summary = eligibility.Availability switch
        {
            UsageStatisticAvailability.InsufficientSample => string.Format(
                CultureInfo.CurrentCulture,
                GetString("UsageDistributionInsufficientFormat"),
                eligibility.EligibleFinalCount),
            UsageStatisticAvailability.MeasuredZero => GetString("UsageDistributionMeasuredZero"),
            UsageStatisticAvailability.Measured => string.Format(
                CultureInfo.CurrentCulture,
                GetString("UsageDistributionMeasuredFormat"),
                eligibility.MedianInputTokens!.Value.ToString("N0", CultureInfo.CurrentCulture),
                eligibility.Percentile95InputTokens is { } percentile
                    ? percentile.ToString("N0", CultureInfo.CurrentCulture)
                    : GetString("UsageExplorerNotAvailable")),
            _ => GetString("UsageDistributionUnavailable"),
        };
        if (shift is { Availability: UsageStatisticAvailability.Measured })
        {
            summary += Environment.NewLine + string.Format(
                CultureInfo.CurrentCulture,
                GetString("UsageDistributionShiftFormat"),
                shift.BaselineMedianInputTokens!.Value.ToString("N0", CultureInfo.CurrentCulture),
                shift.CurrentMedianInputTokens!.Value.ToString("N0", CultureInfo.CurrentCulture),
                shift.PercentChange);
        }

        if (outlier is { Availability: UsageStatisticAvailability.InsufficientSample })
        {
            summary += Environment.NewLine + GetString("UsageDistributionOutlierInsufficient");
        }

        if (outlier is { Availability: UsageStatisticAvailability.Measured, SessionKey: not null })
        {
            summary += Environment.NewLine + string.Format(
                CultureInfo.CurrentCulture,
                GetString("UsageDistributionOutlierFormat"),
                new OpaqueAttributionKey(outlier.SessionKey).ShortLabel,
                outlier.SessionTokens!.Value.ToString("N0", CultureInfo.CurrentCulture));
        }

        return summary;
    }

    private List<UsageExplorerSessionRow> MapSessionRows(
        IReadOnlyList<UsageSessionContribution> contributions,
        IReadOnlyList<UsageSessionLineageNode> lineage)
    {
        Dictionary<string, UsageSessionLineageNode> byKey = lineage.ToDictionary(
            node => node.SessionKey.Value,
            StringComparer.Ordinal);
        Dictionary<string, UsageSessionContribution> contributionByKey = contributions
            .Where(row => !row.IsUnassigned && row.SessionKey is not null)
            .ToDictionary(row => row.SessionKey!.Value, StringComparer.Ordinal);
        var rows = new List<UsageExplorerSessionRow>();
        foreach (UsageSessionLineageNode node in lineage.Where(node =>
                     node.ParentSessionKey is null
                     || !byKey.ContainsKey(node.ParentSessionKey.Value)))
        {
            AppendSessionTree(node, 0, byKey, contributionByKey, rows);
        }

        foreach (UsageSessionContribution unassigned in contributions.Where(row => row.IsUnassigned))
        {
            rows.Add(MapSessionContribution(unassigned, canExpand: false, isExpanded: false, depth: 0, inclusive: ""));
        }

        return rows;
    }

    private void AppendSessionTree(
        UsageSessionLineageNode node,
        int depth,
        Dictionary<string, UsageSessionLineageNode> byKey,
        Dictionary<string, UsageSessionContribution> contributionByKey,
        List<UsageExplorerSessionRow> rows)
    {
        if (!contributionByKey.TryGetValue(node.SessionKey.Value, out UsageSessionContribution? contribution))
        {
            return;
        }

        bool canExpand = node.Children.Count > 0;
        bool isExpanded = canExpand && _expandedSessions.Contains(node.SessionKey.Value);
        string inclusive = node.InclusiveTokens is { } tokens
            ? string.Format(
                CultureInfo.CurrentCulture,
                GetString("UsageExplorerSessionInclusiveFormat"),
                tokens.Total.ToString("N0", CultureInfo.CurrentCulture))
            : string.Empty;
        rows.Add(MapSessionContribution(contribution, canExpand, isExpanded, depth, inclusive));
        if (!isExpanded)
        {
            return;
        }

        foreach (OpaqueAttributionKey child in node.Children)
        {
            if (byKey.TryGetValue(child.Value, out UsageSessionLineageNode? childNode))
            {
                AppendSessionTree(childNode, depth + 1, byKey, contributionByKey, rows);
            }
        }
    }

    private UsageExplorerSessionRow MapSessionContribution(
        UsageSessionContribution row,
        bool canExpand,
        bool isExpanded,
        int depth,
        string inclusive)
    {
        string label = row.IsUnassigned
            ? GetString("UsageExplorerUnassigned")
            : string.Format(
                CultureInfo.CurrentCulture,
                GetString("UsageExplorerSessionLabelFormat"),
                row.SessionKey!.ShortLabel);
        if (depth > 0)
        {
            label = new string('·', depth) + " " + label;
        }

        return new UsageExplorerSessionRow(
            row.SessionKey?.Value,
            label,
            string.Format(
                CultureInfo.CurrentCulture,
                GetString("UsageExplorerSessionContributionFormat"),
                row.SelectedTokens.Total.ToString("N0", CultureInfo.CurrentCulture)),
            string.Format(
                CultureInfo.CurrentCulture,
                GetString("UsageExplorerSessionTotalFormat"),
                row.SessionTokens.Total.ToString("N0", CultureInfo.CurrentCulture)),
            row.IsUnassigned,
            canExpand,
            isExpanded,
            depth,
            inclusive,
            row.ParentSessionKey?.Value,
            canExpand ? (isExpanded ? "−" : "+") : "");
    }

    private async Task<List<UsageExplorerProjectRow>> MapProjectRowsAsync(
        IReadOnlyList<UsageProjectContribution> contributions,
        CancellationToken token)
    {
        var rows = new List<UsageExplorerProjectRow>(contributions.Count);
        foreach (UsageProjectContribution row in contributions)
        {
            string label = row.IsUnassigned
                ? GetString("UsageExplorerUnassigned")
                : row.ProjectKey is { } key
                    ? await ResolveProjectLabelAsync(key, token).ConfigureAwait(false)
                    : GetString("UsageExplorerProjectAmbiguous");
            string mapping = row.IsUnassigned
                ? GetString("UsageExplorerUnassigned")
                : row.IsAmbiguous
                    ? GetString("UsageExplorerProjectAmbiguous")
                    : row.MappingKind == ProjectMappingKind.UserMapped
                        ? GetString("UsageExplorerProjectUserMapped")
                        : GetString("UsageExplorerProjectObserved");
            rows.Add(new UsageExplorerProjectRow(
                row.ProjectKey?.Value,
                label,
                mapping,
                string.Format(
                    CultureInfo.CurrentCulture,
                    GetString("UsageExplorerProjectContributionFormat"),
                    row.SelectedTokens.Total.ToString("N0", CultureInfo.CurrentCulture)),
                string.Format(
                    CultureInfo.CurrentCulture,
                    GetString("UsageExplorerProjectTotalFormat"),
                    row.ProjectTokens.Total.ToString("N0", CultureInfo.CurrentCulture)),
                row.IsUnassigned,
                row.IsAmbiguous));
        }

        return rows;
    }

    private async Task<string> ResolveProjectLabelAsync(OpaqueAttributionKey key, CancellationToken token)
    {
        if (_attributionAliases is not null)
        {
            string? alias = await _attributionAliases.LoadAsync(key, token).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(alias))
            {
                return alias;
            }
        }

        return string.Format(
            CultureInfo.CurrentCulture,
            GetString("UsageExplorerProjectLabelFormat"),
            key.ShortLabel);
    }

    public void ObserveAttribution(TokenUsage.App.ViewModels.Surfaces.GeneralOptionsViewModel options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.CodexSessionAttributionChanged += (_, _) => _ = HandleAttributionChangedAsync();
        options.CodexProjectAttributionChanged += (_, _) => _ = HandleAttributionChangedAsync();
        options.CursorSessionAttributionChanged += (_, _) => _ = HandleAttributionChangedAsync();
        options.CodexMcpAttributionChanged += (_, _) => _ = HandleAttributionChangedAsync();
        options.CodexSkillsAttributionChanged += (_, _) => _ = HandleAttributionChangedAsync();
        options.CodexCommandsAttributionChanged += (_, _) => _ = HandleAttributionChangedAsync();
        options.CodexFilesAttributionChanged += (_, _) => _ = HandleAttributionChangedAsync();
        _ = RefreshAttributionAvailabilityAsync();
    }

    public async Task HandleAttributionChangedAsync()
    {
        _consentGeneration++;
        _selectionGeneration++;
        HasSessions = false;
        HasProjects = false;
        HasOperations = false;
        DetailSession = null;
        DetailProject = null;
        DetailOperation = null;
        SessionRows = [];
        ProjectRows = [];
        OperationRows = [];
        _distributionOutlierSessionKey = null;
        SessionDetailValues = string.Empty;
        ProjectDetailValues = string.Empty;
        OperationDetailValues = string.Empty;
        SkillsAvailabilityText = string.Empty;
        OperationsDerivedNote = string.Empty;
        OnPropertyChanged(nameof(HasSessions));
        OnPropertyChanged(nameof(HasProjects));
        OnPropertyChanged(nameof(HasOperations));
        OnPropertyChanged(nameof(SessionRows));
        OnPropertyChanged(nameof(ProjectRows));
        OnPropertyChanged(nameof(OperationRows));
        OnPropertyChanged(nameof(HasSessionDetail));
        OnPropertyChanged(nameof(HasProjectDetail));
        OnPropertyChanged(nameof(HasOperationDetail));
        OnPropertyChanged(nameof(CanOpenOperationSession));
        OnPropertyChanged(nameof(SkillsAvailabilityText));
        OnPropertyChanged(nameof(HasSkillsAvailabilityNotice));
        OnPropertyChanged(nameof(OperationsDerivedNote));
        OnPropertyChanged(nameof(HasOperationsDerivedNote));
        OnPropertyChanged(nameof(DetailSession));
        OnPropertyChanged(nameof(DetailProject));
        OnPropertyChanged(nameof(DetailOperation));
        OnPropertyChanged(nameof(CanOpenDistributionOutlier));
        OnPropertyChanged(nameof(CanOpenOperations));
        await RefreshAttributionAvailabilityAsync().ConfigureAwait(true);
        RebuildExplorerDetail();
    }

    public async Task SaveProjectAliasAsync()
    {
        if (!CanSaveProjectAlias || _attributionAliases is null || DetailProject?.ProjectKey is not { } key)
        {
            return;
        }

        await _attributionAliases.SetAsync(new OpaqueAttributionKey(key), ProjectAliasDraft.Trim())
            .ConfigureAwait(true);
        if (HasProjects)
        {
            await OpenProjectsAsync().ConfigureAwait(true);
            OpenProjectDetail(key);
        }
    }

    public async Task RemapProjectToCurrentSelectionAsync()
    {
        if (DetailProject?.ProjectKey is not { } key || _attributionConsent is null)
        {
            return;
        }

        AttributionConsent consent = await _attributionConsent
            .LoadAsync(AttributionCapability.CodexProject)
            .ConfigureAwait(true);
        if (!consent.AllowsLinks)
        {
            return;
        }

        UsageRepository repository = await UsageRepository.OpenAsync(_databasePath).ConfigureAwait(true);
        IReadOnlyList<UsageEventKey> unassigned = await repository.ReadUnassignedProjectEventKeysAsync(
            StartDate,
            EndDate,
            consent.Epoch,
            DetailModel is { } model ? new AgentId(model.ProviderId) : null,
            DetailModel?.ModelProviderId is { } host ? new ModelProviderId(host) : null,
            DetailModel is { } selected ? new ModelId(selected.ModelId) : null,
            CurrentDetailSelection()).ConfigureAwait(true);
        await repository.MapEventsToProjectAsync(
            unassigned,
            new OpaqueAttributionKey(key),
            consent.Epoch).ConfigureAwait(true);
        await OpenProjectsAsync().ConfigureAwait(true);
    }

    private async Task RefreshAttributionAvailabilityAsync()
    {
        if (_attributionConsent is null)
        {
            _codexSessionsAllowed = false;
            _codexProjectsAllowed = false;
            _cursorSessionsAllowed = false;
            _codexMcpAllowed = false;
            _codexSkillsAllowed = false;
            _codexCommandsAllowed = false;
            _codexFilesAllowed = false;
            OnPropertyChanged(nameof(CanOpenSessions));
            OnPropertyChanged(nameof(CanOpenProjects));
            OnPropertyChanged(nameof(CanOpenOperations));
            UpdateOperationsAvailability();
            return;
        }

        AttributionConsent sessions = await _attributionConsent
            .LoadAsync(AttributionCapability.CodexSession)
            .ConfigureAwait(true);
        AttributionConsent projects = await _attributionConsent
            .LoadAsync(AttributionCapability.CodexProject)
            .ConfigureAwait(true);
        AttributionConsent cursor = await _attributionConsent
            .LoadAsync(AttributionCapability.CursorSession)
            .ConfigureAwait(true);
        AttributionConsent mcp = await _attributionConsent
            .LoadAsync(AttributionCapability.CodexMcp)
            .ConfigureAwait(true);
        AttributionConsent skills = await _attributionConsent
            .LoadAsync(AttributionCapability.CodexSkills)
            .ConfigureAwait(true);
        AttributionConsent commands = await _attributionConsent
            .LoadAsync(AttributionCapability.CodexCommands)
            .ConfigureAwait(true);
        AttributionConsent files = await _attributionConsent
            .LoadAsync(AttributionCapability.CodexFiles)
            .ConfigureAwait(true);
        _codexSessionsAllowed = sessions.AllowsLinks;
        _codexProjectsAllowed = projects.AllowsLinks;
        _cursorSessionsAllowed = cursor.AllowsLinks;
        _codexMcpAllowed = mcp.AllowsLinks;
        _codexSkillsAllowed = skills.AllowsLinks;
        _codexCommandsAllowed = commands.AllowsLinks;
        _codexFilesAllowed = files.AllowsLinks;
        OnPropertyChanged(nameof(CanOpenSessions));
        OnPropertyChanged(nameof(CanOpenProjects));
        OnPropertyChanged(nameof(CanOpenOperations));
        UpdateOperationsAvailability();
    }

    private static AttributionCapability SessionCapabilityFor(string providerId) =>
        string.Equals(providerId, "cursor", StringComparison.Ordinal)
            ? AttributionCapability.CursorSession
            : AttributionCapability.CodexSession;

    private async Task<(IReadOnlyList<UsageSessionContribution> Sessions, IReadOnlyList<UsageProjectContribution> Projects)>
        LoadExportPopulationsAsync(CancellationToken token)
    {
        UsageReportSelection selection = ExplorerSelection();
        UsageDetailSelection detail = CurrentDetailSelection();
        DateOnly from = StartDate;
        DateOnly to = EndDate;
        if (IsCompareScope)
        {
            from = _compareLeftStart < _compareRightStart ? _compareLeftStart : _compareRightStart;
            to = _compareLeftEnd > _compareRightEnd ? _compareLeftEnd : _compareRightEnd;
        }
        AgentId[] agents = selection.Agents.Count > 0
            ? [.. selection.Agents]
            : IsProviderScope && SelectedProvider is { } provider
                ? [new AgentId(provider.ProviderId)]
                : [.. _report.Agents.Select(row => row.AgentId)];
        ModelProviderId? host = selection.ModelProviders.Count == 1 ? selection.ModelProviders[0] : null;
        ModelId? model = selection.Models.Count == 1 ? selection.Models[0] : null;
        UsageRepository repository = await UsageRepository.OpenReadOnlyAsync(_databasePath, token)
            .ConfigureAwait(false);
        var sessions = new List<UsageSessionContribution>();
        foreach (AgentId agent in agents.Distinct())
        {
            AttributionCapability capability = SessionCapabilityFor(agent.Value);
            AttributionConsent consent = await _attributionConsent!
                .LoadAsync(capability, token)
                .ConfigureAwait(false);
            IReadOnlyList<UsageSessionContribution> rows = await repository
                .ReadSessionContributionsAsync(
                    from,
                    to,
                    consent.ActiveLinkEpoch,
                    agent,
                    host,
                    model,
                    capability,
                    detail: detail,
                    cancellationToken: token)
                .ConfigureAwait(false);
            AttributionConsent published = await _attributionConsent
                .LoadAsync(capability, token)
                .ConfigureAwait(false);
            if (published.ActiveLinkEpoch != consent.ActiveLinkEpoch)
            {
                continue;
            }

            sessions.AddRange(rows);
        }

        AttributionConsent projectConsent = await _attributionConsent!
            .LoadAsync(AttributionCapability.CodexProject, token)
            .ConfigureAwait(false);
        IReadOnlyList<UsageProjectContribution> projectRows = await repository
            .ReadProjectContributionsAsync(
                from,
                to,
                projectConsent.ActiveLinkEpoch,
                agents.Length == 1 ? agents[0] : null,
                host,
                model,
                detail,
                token)
            .ConfigureAwait(false);
        AttributionConsent publishedProjects = await _attributionConsent
            .LoadAsync(AttributionCapability.CodexProject, token)
            .ConfigureAwait(false);
        if (publishedProjects.ActiveLinkEpoch != projectConsent.ActiveLinkEpoch)
        {
            projectRows = [];
        }

        return (sessions, projectRows);
    }
}
