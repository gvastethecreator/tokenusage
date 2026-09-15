using System.Globalization;
using TokenUsage.Core.Providers;
using TokenUsage.Core.Usage;

namespace TokenUsage.App.ViewModels.Reports;

public sealed partial class UsageReportViewModel
{
    public string MixedOperationsHeading
    {
        get
        {
            if (_legacyUnreconciled)
            {
                return GetString("UsageExplorerOperationsLegacyUnrecoveredHeading");
            }

            if (_activeOperationScope.Kind == OperationScopeKind.Project)
            {
                return GetString("UsageExplorerOperationsMixedProjectHeading");
            }

            return GetString("UsageExplorerOperationsMixedHeading");
        }
    }
    public string OperationsHeading { get; private set; } = string.Empty;
    public IReadOnlyList<UsageExplorerOperationRow> MixedOperationRows { get; private set; } = [];
    public bool HasMixedOperations => MixedOperationRows.Count > 0;
    public bool HasMixedOperationsVisible => HasMixedOperations && !HasWorkflowEvidence;
    public bool CanOpenUnlinkedOperations => HasOperations
        && _hasUnlinkedOperations
        && !_activeOperationScope.UnlinkedOnly;
    public string LegacyUnreconciledNotice => _legacyUnreconciled
        ? GetString("UsageExplorerOperationsLegacyUnrecoveredNotice")
        : string.Empty;
    public bool HasLegacyUnreconciledNotice => _legacyUnreconciled;

    public async Task OpenOperationsAsync() =>
        await OpenOperationsCoreAsync(CurrentOperationScope()).ConfigureAwait(true);

    public async Task OpenUnlinkedOperationsAsync()
    {
        if (!CanOpenUnlinkedOperations && !HasOperations)
        {
            return;
        }

        await OpenOperationsCoreAsync(CurrentOperationScope(unlinkedOnly: true)).ConfigureAwait(true);
    }

    private async Task OpenOperationsCoreAsync(OperationLoadScope scope)
    {
        if (_attributionConsent is null)
        {
            return;
        }

        if (!HasOperations && !CanOpenOperations && !_operationsFromDashboard && !scope.UnlinkedOnly)
        {
            return;
        }

        long generation = _selectionGeneration;
        long consentGeneration = _consentGeneration;
        CancellationToken token = _selectionCancellation?.Token ?? CancellationToken.None;
        bool returnToSessions = HasSessions;
        bool returnToProjects = !returnToSessions && (HasProjects || (_projectsFromOverview && DetailProject is not null));
        if (HasOperations)
        {
            returnToSessions = _returnToSessions;
            returnToProjects = _returnToProjects;
        }

        if (_operationsFromDashboard)
        {
            returnToSessions = false;
            returnToProjects = false;
        }

        OperationCohort cohort = await RunReportWorkAsync(
            () => LoadOperationCohortAsync(scope, token),
            token);
        if (_disposed || generation != _selectionGeneration || consentGeneration != _consentGeneration)
        {
            return;
        }

        _activeOperationScope = scope;
        _returnToSessions = returnToSessions;
        _returnToProjects = returnToProjects && !returnToSessions;
        ApplyOperationCohort(cohort);
        _allOperationRows = OperationRows;
        _allMixedOperationRows = MixedOperationRows;
        _operationEvidenceFilter = null;
        HasDerivedActivityReturn = false;
        HasWorkflowReturn = false;
        IEnumerable<UsageOperationRankedRow> derivedSource = cohort.Scope.Kind == OperationScopeKind.Project
            ? cohort.Proved
            : cohort.Proved.Concat(cohort.Mixed);
        DerivedActivityRows = MapDerivedActivityRows(UsageDerivedActivity.Summarize(derivedSource));
        DerivedActivityNote = GetString("UsageExplorerDerivedActivityNote");
        DerivedActivityMethodText = string.Format(
            CultureInfo.CurrentCulture,
            GetString("UsageExplorerDerivedActivityMethodFormat"),
            UsageDerivedActivity.MethodVersion,
            UsageDerivedActivity.CountUnit);
        UsageWorkflowSummary workflow = UsageWorkflowIndicators.Evaluate(cohort.Timeline);
        WorkflowRows = MapWorkflowRows(workflow);
        _workflowContributingEvents = workflow.ContributingEvents;
        WorkflowEvidenceRows = [];
        _hasUnlinkedOperations = cohort.HasUnlinkedOperations;
        _legacyUnreconciled = cohort.HasLegacyUnreconciled;
        HasOperations = true;
        HasSessions = false;
        HasProjects = false;
        DetailOperation = null;
        OperationDetailValues = string.Empty;
        SkillsAvailabilityText = GetString("UsageOverviewSkillsUnavailable");
        var notes = new List<string>();
        if (OperationRows.Concat(MixedOperationRows).Any(row => row.Kind == UsageOperationKind.Command))
        {
            notes.Add(GetString("UsageExplorerOperationsDerivedNote"));
        }

        if (OperationRows.Concat(MixedOperationRows).Any(row => row.Kind == UsageOperationKind.File)
            || WorkflowRows.Count > 0)
        {
            notes.Add(GetString("UsageExplorerFilesWorkflowNote"));
        }

        if (_legacyUnreconciled)
        {
            notes.Add(GetString("UsageExplorerOperationsLegacyUnrecoveredNotice"));
        }

        OperationsDerivedNote = string.Join(Environment.NewLine + Environment.NewLine, notes);
        NotifyOperationSurface();
        UpdateOperationsAvailability();
    }

    public void CloseOperations()
    {
        HasOperations = false;
        DetailOperation = null;
        OperationRows = [];
        MixedOperationRows = [];
        _allOperationRows = [];
        _allMixedOperationRows = [];
        _operationEvidenceFilter = null;
        DerivedActivityRows = [];
        DerivedActivityNote = string.Empty;
        HasDerivedActivityReturn = false;
        WorkflowRows = [];
        HasWorkflowReturn = false;
        WorkflowEvidenceRows = [];
        DerivedActivityMethodText = string.Empty;
        _workflowContributingEvents = [];
        _hasUnlinkedOperations = false;
        _legacyUnreconciled = false;
        _activeOperationScope = OperationLoadScope.Global;
        OperationsHeading = string.Empty;
        OperationDetailValues = string.Empty;
        SkillsAvailabilityText = string.Empty;
        OperationsDerivedNote = string.Empty;
        bool fromDashboard = _operationsFromDashboard;
        _operationsFromDashboard = false;
        if (fromDashboard)
        {
            _returnToSessions = false;
            _returnToProjects = false;
        }
        else if (_returnToSessions)
        {
            _returnToSessions = false;
            HasSessions = true;
        }
        else if (_returnToProjects)
        {
            _returnToProjects = false;
            HasProjects = true;
        }

        NotifyOperationSurface();
        UpdateOperationsAvailability();
    }

    public void OpenOperationDetail(string id)
    {
        DetailOperation = OperationRows.FirstOrDefault(row => row.Id == id)
            ?? MixedOperationRows.FirstOrDefault(row => row.Id == id);
        OperationDetailValues = DetailOperation is { } operation
            ? operation.Label + " · " + operation.CountText + " · " + operation.OutcomeText
            : string.Empty;
        OnPropertyChanged(nameof(DetailOperation));
        OnPropertyChanged(nameof(HasOperationDetail));
        OnPropertyChanged(nameof(CanOpenOperationSession));
        OnPropertyChanged(nameof(OperationDetailValues));
    }

    public void SelectDerivedActivity(string category)
    {
        if (!UsageDerivedActivity.TryParse(category, out UsageDerivedActivityCategory parsed)
            || !HasOperations)
        {
            return;
        }

        ApplyOperationEvidenceFilter(
            "activity:" + UsageDerivedActivity.ToWire(parsed),
            row => UsageDerivedActivity.Classify(row.Kind, row.Tool) == parsed);
        HasDerivedActivityReturn = true;
        HasWorkflowReturn = false;
        WorkflowEvidenceRows = [];
        NotifyOperationSurface();
    }

    public void SelectWorkflowEvidence(string id)
    {
        if (!string.Equals(id, "same-file", StringComparison.Ordinal) || !HasOperations)
        {
            return;
        }

        _operationEvidenceFilter = "workflow:same-file";
        WorkflowEvidenceRows = MapEvidenceRows(_workflowContributingEvents);
        DetailOperation = null;
        OperationDetailValues = string.Empty;
        HasWorkflowReturn = true;
        HasDerivedActivityReturn = false;
        NotifyOperationSurface();
    }

    public void ClearOperationEvidenceFilter()
    {
        if (!HasOperations)
        {
            return;
        }

        _operationEvidenceFilter = null;
        OperationRows = _allOperationRows;
        MixedOperationRows = _allMixedOperationRows;
        WorkflowEvidenceRows = [];
        HasDerivedActivityReturn = false;
        HasWorkflowReturn = false;
        DetailOperation = null;
        OperationDetailValues = string.Empty;
        NotifyOperationSurface();
    }

    private void ApplyOperationEvidenceFilter(
        string filter,
        Func<UsageExplorerOperationRow, bool> match)
    {
        _operationEvidenceFilter = filter;
        OperationRows = _allOperationRows.Where(match).ToArray();
        MixedOperationRows = _allMixedOperationRows.Where(match).ToArray();
        DetailOperation = null;
        OperationDetailValues = string.Empty;
    }

    private UsageExplorerEvidenceRow[] MapEvidenceRows(
        IReadOnlyList<UsageWorkflowEvidenceEvent> events)
    {
        var ordinals = new Dictionary<OpaqueAttributionKey, int>();
        foreach (UsageWorkflowEvidenceEvent evidence in events)
        {
            if (evidence.SessionKey is { } key && !ordinals.ContainsKey(key))
            {
                ordinals[key] = ordinals.Count + 1;
            }
        }

        return events.Select((evidence, index) => MapEvidenceRow(evidence, index, ordinals)).ToArray();
    }

    private UsageExplorerEvidenceRow MapEvidenceRow(
        UsageWorkflowEvidenceEvent evidence,
        int index,
        Dictionary<OpaqueAttributionKey, int> sessionOrdinals)
    {
        string kind = evidence.Kind.ToString();
        string tool = evidence.Server is { Length: > 0 } server
            ? server + " / " + evidence.Tool
            : evidence.Tool;
        string time = evidence.StartedAtUtc == default
            ? GetString("UsageExplorerEvidenceTimeUnknown")
            : evidence.StartedAtUtc.ToLocalTime().ToString("g", CultureInfo.CurrentCulture);
        string outcome = evidence.Outcome switch
        {
            UsageOperationOutcome.Success => GetString("UsageExplorerEvidenceOutcomeSuccess"),
            UsageOperationOutcome.Error => GetString("UsageExplorerEvidenceOutcomeError"),
            _ => GetString("UsageExplorerEvidenceOutcomeUnknown"),
        };
        string session = evidence.SessionKey is { } key && sessionOrdinals.TryGetValue(key, out int ordinal)
            ? string.Format(
                CultureInfo.CurrentCulture,
                GetString("UsageExplorerEvidenceLinkedSessionFormat"),
                ordinal.ToString("N0", CultureInfo.CurrentCulture))
            : GetString("UsageExplorerEvidenceSessionUnknown");
        string sequence = (index + 1).ToString("N0", CultureInfo.CurrentCulture);
        return new UsageExplorerEvidenceRow(
            sequence,
            time,
            kind,
            tool,
            outcome,
            session,
            sequence + ", " + time + ", " + kind + ", " + tool + ", " + outcome + ", " + session);
    }

    public async Task OpenOperationLinkedSessionAsync()
    {
        if (DetailOperation?.SessionKey is not { Length: > 0 } key)
        {
            return;
        }

        CloseOperations();
        if (!CanOpenSessions)
        {
            return;
        }

        await OpenSessionsAsync().ConfigureAwait(true);
        OpenSessionDetail(key);
        UsageExplorerSessionRow? row = SessionRows.FirstOrDefault(item => item.SessionKey == key);
        if (row is { CanExpand: true, IsExpanded: false })
        {
            ToggleSessionExpand(key);
        }
    }

    private sealed record OperationLoadScope(
        OperationScopeKind Kind,
        UsageReportModelRow? Model,
        OpaqueAttributionKey? ProjectKey,
        OpaqueAttributionKey? SessionKey,
        bool HomogeneousModelJoin,
        bool UnlinkedOnly)
    {
        public static OperationLoadScope Global { get; } = new(
            OperationScopeKind.Global,
            null,
            null,
            null,
            HomogeneousModelJoin: false,
            UnlinkedOnly: false);
    }

    private enum OperationScopeKind
    {
        Global,
        Model,
        Project,
        Session,
        Unlinked,
    }

    private sealed record OperationCohort(
        IReadOnlyList<UsageOperationRankedRow> Proved,
        IReadOnlyList<UsageOperationRankedRow> Mixed,
        IReadOnlyList<UsageOperationTimelineRow> Timeline,
        OperationLoadScope Scope,
        bool HomogeneousJoin,
        bool HasUnlinkedOperations,
        bool HasLegacyUnreconciled);

    private void ApplyOperationCohort(OperationCohort cohort)
    {
        OperationsHeading = cohort.Scope.Kind switch
        {
            OperationScopeKind.Session => GetString("UsageExplorerOperationsForSessionHeading"),
            OperationScopeKind.Project => GetString("UsageExplorerOperationsForProjectHeading"),
            OperationScopeKind.Unlinked => GetString("UsageExplorerOperationsUnlinkedHeading"),
            OperationScopeKind.Model when cohort.HomogeneousJoin && cohort.Proved.Count > 0 =>
                GetString("UsageExplorerOperationsForModelHeading"),
            _ => GetString("UsageExplorerOperationsUnprovedHeading"),
        };
        OperationRows = cohort.Proved.Select(MapOperationRow).ToArray();
        MixedOperationRows = cohort.Mixed.Select(MapOperationRow).ToArray();
    }

    private OperationLoadScope CurrentOperationScope(bool unlinkedOnly = false)
    {
        if (unlinkedOnly)
        {
            return new OperationLoadScope(
                OperationScopeKind.Unlinked,
                null,
                null,
                null,
                HomogeneousModelJoin: false,
                UnlinkedOnly: true);
        }

        if (DetailSession?.SessionKey is { Length: 64 } session
            && OpaqueAttributionKey.IsHexSha256(session))
        {
            return new OperationLoadScope(
                OperationScopeKind.Session,
                null,
                null,
                new OpaqueAttributionKey(session),
                HomogeneousModelJoin: false,
                UnlinkedOnly: false);
        }

        if (DetailProject is { IsUnassigned: true })
        {
            return new OperationLoadScope(
                OperationScopeKind.Unlinked,
                null,
                null,
                null,
                HomogeneousModelJoin: false,
                UnlinkedOnly: true);
        }

        if (DetailProject?.ProjectKey is { Length: 64 } project
            && OpaqueAttributionKey.IsHexSha256(project))
        {
            return new OperationLoadScope(
                OperationScopeKind.Project,
                null,
                new OpaqueAttributionKey(project),
                null,
                HomogeneousModelJoin: false,
                UnlinkedOnly: false);
        }

        if (_projectsFromOverview)
        {
            return OperationLoadScope.Global;
        }

        if (DetailModel is { ProviderId: "codex" } model)
        {
            return new OperationLoadScope(
                OperationScopeKind.Model,
                model,
                null,
                null,
                HomogeneousModelJoin: true,
                UnlinkedOnly: false);
        }

        return OperationLoadScope.Global;
    }

    private void NotifyOperationSurface()
    {
        OnPropertyChanged(nameof(DashboardOperationBars));
        OnPropertyChanged(nameof(DashboardActivityBars));
        OnPropertyChanged(nameof(DashboardOperationSummary));
        OnPropertyChanged(nameof(DashboardActivitySummary));
        OnPropertyChanged(nameof(OperationRows));
        OnPropertyChanged(nameof(MixedOperationRows));
        OnPropertyChanged(nameof(HasMixedOperations));
        OnPropertyChanged(nameof(HasMixedOperationsVisible));
        OnPropertyChanged(nameof(OperationsHeading));
        OnPropertyChanged(nameof(HasOperations));
        OnPropertyChanged(nameof(HasSessions));
        OnPropertyChanged(nameof(HasProjects));
        OnPropertyChanged(nameof(CanOpenSessions));
        OnPropertyChanged(nameof(CanOpenProjects));
        OnPropertyChanged(nameof(CanOpenOperations));
        OnPropertyChanged(nameof(HasModelDetail));
        OnPropertyChanged(nameof(HasOperationDetail));
        OnPropertyChanged(nameof(CanOpenOperationSession));
        OnPropertyChanged(nameof(DetailOperation));
        OnPropertyChanged(nameof(OperationDetailValues));
        OnPropertyChanged(nameof(SkillsAvailabilityText));
        OnPropertyChanged(nameof(HasSkillsAvailabilityNotice));
        OnPropertyChanged(nameof(OperationsDerivedNote));
        OnPropertyChanged(nameof(HasOperationsDerivedNote));
        OnPropertyChanged(nameof(DerivedActivityRows));
        OnPropertyChanged(nameof(HasDerivedActivity));
        OnPropertyChanged(nameof(DerivedActivityNote));
        OnPropertyChanged(nameof(HasDerivedActivityNote));
        OnPropertyChanged(nameof(HasDerivedActivityReturn));
        OnPropertyChanged(nameof(WorkflowRows));
        OnPropertyChanged(nameof(HasWorkflowIndicators));
        OnPropertyChanged(nameof(HasWorkflowReturn));
        OnPropertyChanged(nameof(CanOpenUnlinkedOperations));
        OnPropertyChanged(nameof(LegacyUnreconciledNotice));
        OnPropertyChanged(nameof(HasLegacyUnreconciledNotice));
        OnPropertyChanged(nameof(MixedOperationsHeading));
        OnPropertyChanged(nameof(HasWorkflowEvidence));
        OnPropertyChanged(nameof(HasRankedOperations));
        OnPropertyChanged(nameof(WorkflowEvidenceRows));
        OnPropertyChanged(nameof(DerivedActivityMethodText));
        NotifyExplorerContextPath();
    }

    private async Task<OperationCohort> LoadOperationCohortAsync(
        OperationLoadScope scope,
        CancellationToken token)
    {
        if (_attributionConsent is null || !File.Exists(_databasePath))
        {
            return EmptyCohort(scope);
        }

        if (scope.Kind == OperationScopeKind.Model && !IncludesCodexOperations(scope.Model))
        {
            return EmptyCohort(scope);
        }

        UsageRepository repository = await UsageRepository.OpenReadOnlyAsync(_databasePath, token)
            .ConfigureAwait(false);
        (DateTimeOffset from, DateTimeOffset to) = UsageCivilDay.UtcBounds(StartDate, EndDate, TimeZoneInfo.Local);
        IReadOnlyList<string> sessionKeys = [];
        string[] mixedSessionKeys = [];
        bool restrict = false;
        bool includeMixed = false;
        if (scope.Kind == OperationScopeKind.Session && scope.SessionKey is { } session)
        {
            sessionKeys = [session.Value];
            restrict = true;
        }
        else if (scope.Kind == OperationScopeKind.Project && scope.ProjectKey is { } project)
        {
            AttributionConsent sessionConsent = await _attributionConsent
                .LoadAsync(AttributionCapability.CodexSession, token)
                .ConfigureAwait(false);
            AttributionConsent projectConsent = await _attributionConsent
                .LoadAsync(AttributionCapability.CodexProject, token)
                .ConfigureAwait(false);
            if (sessionConsent.AllowsLinks && projectConsent.AllowsLinks)
            {
                IReadOnlyList<string> overlapping = await repository.ReadSessionKeysForProjectAsync(
                    project,
                    projectConsent.Epoch,
                    sessionConsent.Epoch,
                    from,
                    to,
                    token).ConfigureAwait(false);
                sessionKeys = await repository.ReadHomogeneousSessionKeysForProjectAsync(
                    project,
                    projectConsent.Epoch,
                    sessionConsent.Epoch,
                    from,
                    to,
                    token).ConfigureAwait(false);
                HashSet<string> exact = sessionKeys.ToHashSet(StringComparer.Ordinal);
                mixedSessionKeys = overlapping
                    .Where(key => !exact.Contains(key))
                    .ToArray();
            }

            restrict = true;
        }
        else if (scope.HomogeneousModelJoin && scope.Model is { ProviderId: "codex" } model)
        {
            AttributionConsent sessionConsent = await _attributionConsent
                .LoadAsync(AttributionCapability.CodexSession, token)
                .ConfigureAwait(false);
            sessionKeys = await repository.ReadHomogeneousSessionKeysAsync(
                from,
                to,
                new AgentId("codex"),
                AttributionCapability.CodexSession,
                sessionConsent.ActiveLinkEpoch,
                new ModelId(model.ModelId),
                model.ModelProviderId is { } host ? new ModelProviderId(host) : null,
                CurrentDetailSelection(),
                token).ConfigureAwait(false);
            restrict = sessionKeys.Count > 0;
            includeMixed = sessionKeys.Count > 0;
        }

        IReadOnlyList<string> sourceKeys = mixedSessionKeys.Length == 0
            ? sessionKeys
            : sessionKeys.Concat(mixedSessionKeys).Distinct(StringComparer.Ordinal).ToArray();
        bool? sourceFilter = await ResolveNamedSourceFilterAsync(
                repository,
                from,
                to,
                sourceKeys,
                restrict,
                scope.UnlinkedOnly,
                token)
            .ConfigureAwait(false);
        bool hasLegacy = sourceFilter is true;
        var proved = new List<UsageOperationRankedRow>();
        var mixed = new List<UsageOperationRankedRow>();
        var timeline = new List<UsageOperationTimelineRow>();
        AttributionCapability[] capabilities =
        [
            AttributionCapability.CodexMcp,
            AttributionCapability.CodexSkills,
            AttributionCapability.CodexCommands,
            AttributionCapability.CodexFiles,
        ];
        foreach (AttributionCapability capability in capabilities)
        {
            await AddCapabilityRankingAsync(
                    repository,
                    capability,
                    from,
                    to,
                    proved,
                    mixed,
                    sessionKeys,
                    restrict,
                    includeMixed,
                    scope.UnlinkedOnly,
                    sourceFilter,
                    token)
                .ConfigureAwait(false);
            await AddCapabilityTimelineAsync(
                    repository,
                    capability,
                    from,
                    to,
                    timeline,
                    sessionKeys,
                    restrict,
                    includeMixed,
                    scope.UnlinkedOnly,
                    sourceFilter,
                    token)
                .ConfigureAwait(false);
        }

        if (mixedSessionKeys.Length > 0)
        {
            var mixedScoped = new List<UsageOperationRankedRow>();
            foreach (AttributionCapability capability in capabilities)
            {
                await AddCapabilityRankingAsync(
                        repository,
                        capability,
                        from,
                        to,
                        mixedScoped,
                        mixedScoped,
                        mixedSessionKeys,
                        restrict: true,
                        includeMixed: false,
                        unlinkedOnly: false,
                        sourceFilter,
                        token)
                    .ConfigureAwait(false);
            }

            for (int index = 0; index < mixedScoped.Count; index++)
            {
                UsageOperationRankedRow row = mixedScoped[index];
                mixed.Add(row with { Id = "mixed:" + row.Id });
            }
        }

        bool hasUnlinked = false;
        if (!scope.UnlinkedOnly && scope.Kind is OperationScopeKind.Project or OperationScopeKind.Session)
        {
            foreach (AttributionCapability capability in capabilities)
            {
                AttributionConsent consent = await _attributionConsent.LoadAsync(capability, token)
                    .ConfigureAwait(false);
                if (!consent.AllowsLinks)
                {
                    continue;
                }

                IReadOnlyList<UsageOperationRankedRow> unlinked = await repository.ReadOperationRankingAsync(
                    from,
                    to,
                    capability,
                    consent.Epoch,
                    unlinkedOnly: true,
                    sourceInstancePresent: sourceFilter,
                    cancellationToken: token).ConfigureAwait(false);
                if (unlinked.Count > 0)
                {
                    hasUnlinked = true;
                    break;
                }
            }
        }

        if (scope.Kind == OperationScopeKind.Model && scope.HomogeneousModelJoin && sessionKeys.Count == 0)
        {
            return new OperationCohort(
                [],
                proved.Concat(mixed).ToArray(),
                timeline,
                scope,
                HomogeneousJoin: false,
                hasUnlinked,
                hasLegacy);
        }

        IReadOnlyList<UsageOperationRankedRow> primary = restrict || scope.UnlinkedOnly
            ? proved
            : proved.Concat(mixed).ToArray();
        IReadOnlyList<UsageOperationRankedRow> secondary = restrict && mixed.Count > 0 ? mixed : [];
        return new OperationCohort(
            primary,
            secondary,
            timeline,
            scope,
            HomogeneousJoin: includeMixed,
            hasUnlinked,
            hasLegacy);
    }

    private static OperationCohort EmptyCohort(OperationLoadScope scope) =>
        new([], [], [], scope, HomogeneousJoin: false, HasUnlinkedOperations: false, HasLegacyUnreconciled: false);

    private async Task<bool?> ResolveNamedSourceFilterAsync(
        UsageRepository repository,
        DateTimeOffset from,
        DateTimeOffset to,
        IReadOnlyList<string> sessionKeys,
        bool restrict,
        bool unlinkedOnly,
        CancellationToken token)
    {
        int named = 0;
        int legacy = 0;
        foreach (AttributionCapability capability in new[]
                 {
                     AttributionCapability.CodexMcp,
                     AttributionCapability.CodexSkills,
                     AttributionCapability.CodexCommands,
                     AttributionCapability.CodexFiles,
                 })
        {
            AttributionConsent consent = await _attributionConsent!.LoadAsync(capability, token)
                .ConfigureAwait(false);
            if (!consent.AllowsLinks)
            {
                continue;
            }

            (int namedCount, int legacyCount) = await repository.CountOperationSourcePopulationsAsync(
                from,
                to,
                capability,
                consent.Epoch,
                sessionKeys.Count == 0 ? null : sessionKeys,
                restrict,
                unlinkedOnly,
                token).ConfigureAwait(false);
            named += namedCount;
            legacy += legacyCount;
        }

        return named > 0 && legacy > 0 ? true : null;
    }

    private static bool IncludesCodexOperations(UsageReportModelRow? model) =>
        model is null || string.Equals(model.ProviderId, "codex", StringComparison.Ordinal);

    private bool IncludesCodexOperationsForExport(AgentId? agentId) =>
        (agentId is null || string.Equals(agentId.Value, "codex", StringComparison.Ordinal))
        && DetailModel is not { ProviderId: "cursor" };

    private async Task AddCapabilityRankingAsync(
        UsageRepository repository,
        AttributionCapability capability,
        DateTimeOffset from,
        DateTimeOffset to,
        List<UsageOperationRankedRow> proved,
        List<UsageOperationRankedRow> mixed,
        IReadOnlyList<string> sessionKeys,
        bool restrict,
        bool includeMixed,
        bool unlinkedOnly,
        bool? sourceInstancePresent,
        CancellationToken token)
    {
        if (_attributionConsent is null)
        {
            return;
        }

        AttributionConsent consent = await _attributionConsent.LoadAsync(capability, token).ConfigureAwait(false);
        if (!consent.AllowsLinks)
        {
            return;
        }

        proved.AddRange(await repository.ReadOperationRankingAsync(
            from,
            to,
            capability,
            consent.Epoch,
            sessionKeys: sessionKeys.Count == 0 ? null : sessionKeys,
            restrictToSessionKeys: restrict && !unlinkedOnly,
            unlinkedOnly: unlinkedOnly,
            sourceInstancePresent: sourceInstancePresent,
            cancellationToken: token).ConfigureAwait(false));
        if (includeMixed && !unlinkedOnly)
        {
            mixed.AddRange(await repository.ReadOperationRankingAsync(
                from,
                to,
                capability,
                consent.Epoch,
                sessionKeys: sessionKeys,
                restrictToSessionKeys: false,
                sourceInstancePresent: sourceInstancePresent,
                cancellationToken: token).ConfigureAwait(false));
        }
    }

    private async Task AddCapabilityTimelineAsync(
        UsageRepository repository,
        AttributionCapability capability,
        DateTimeOffset from,
        DateTimeOffset to,
        List<UsageOperationTimelineRow> timeline,
        IReadOnlyList<string> sessionKeys,
        bool restrict,
        bool includeMixed,
        bool unlinkedOnly,
        bool? sourceInstancePresent,
        CancellationToken token)
    {
        if (_attributionConsent is null)
        {
            return;
        }

        AttributionConsent consent = await _attributionConsent.LoadAsync(capability, token).ConfigureAwait(false);
        if (!consent.AllowsLinks)
        {
            return;
        }

        timeline.AddRange(await repository.ReadOperationTimelineAsync(
            from,
            to,
            capability,
            consent.Epoch,
            sessionKeys: sessionKeys.Count == 0 ? null : sessionKeys,
            restrictToSessionKeys: restrict && !unlinkedOnly,
            unlinkedOnly: unlinkedOnly,
            sourceInstancePresent: sourceInstancePresent,
            cancellationToken: token).ConfigureAwait(false));
        if (includeMixed && !unlinkedOnly)
        {
            timeline.AddRange(await repository.ReadOperationTimelineAsync(
                from,
                to,
                capability,
                consent.Epoch,
                sessionKeys: sessionKeys,
                restrictToSessionKeys: false,
                sourceInstancePresent: sourceInstancePresent,
                cancellationToken: token).ConfigureAwait(false));
        }
    }

    private IReadOnlyList<UsageExplorerDerivedActivityRow> MapDerivedActivityRows(
        UsageDerivedActivitySummary summary)
    {
        return
        [
            DerivedRow(UsageDerivedActivityCategory.Edit, "UsageExplorerDerivedActivityEdit", summary.Edit),
            DerivedRow(UsageDerivedActivityCategory.Read, "UsageExplorerDerivedActivityRead", summary.Read),
            DerivedRow(UsageDerivedActivityCategory.Test, "UsageExplorerDerivedActivityTest", summary.Test),
            DerivedRow(UsageDerivedActivityCategory.Delegate, "UsageExplorerDerivedActivityDelegate", summary.Delegate),
            DerivedRow(UsageDerivedActivityCategory.Unknown, "UsageExplorerDerivedActivityUnknown", summary.Unknown),
        ];
    }

    private UsageExplorerDerivedActivityRow DerivedRow(
        UsageDerivedActivityCategory category,
        string labelKey,
        int count) =>
        new(
            UsageDerivedActivity.ToWire(category),
            GetString(labelKey),
            string.Format(
                CultureInfo.CurrentCulture,
                GetString("UsageExplorerDerivedActivityCountFormat"),
                count),
            count);

    private IReadOnlyList<UsageExplorerWorkflowRow> MapWorkflowRows(UsageWorkflowSummary summary) =>
    [
        new(
            "first-edit",
            GetString("UsageExplorerWorkflowFirstEdit"),
            GetString("UsageExplorerWorkflowFirstEditUnavailable"),
            CanOpenEvidence: false),
        new(
            "same-file",
            GetString("UsageExplorerWorkflowSameFile"),
            string.Format(
                CultureInfo.CurrentCulture,
                GetString("UsageExplorerWorkflowCountFormat"),
                summary.SameFileVerificationSeparated),
            summary.SameFileVerificationSeparated > 0),
        new(
            "excluded",
            GetString("UsageExplorerWorkflowExcluded"),
            string.Format(
                CultureInfo.CurrentCulture,
                GetString("UsageExplorerWorkflowCountFormat"),
                summary.ExcludedConcurrent),
            CanOpenEvidence: false),
        new(
            "incomplete",
            GetString("UsageExplorerWorkflowIncomplete"),
            string.Format(
                CultureInfo.CurrentCulture,
                GetString("UsageExplorerWorkflowCountFormat"),
                summary.IncompleteUnproved),
            CanOpenEvidence: false),
    ];

    private UsageExplorerOperationRow MapOperationRow(UsageOperationRankedRow row)
    {
        string kind = row.Kind switch
        {
            UsageOperationKind.Tool => "Tool",
            UsageOperationKind.Mcp => "MCP",
            UsageOperationKind.Spawn => "Spawn",
            UsageOperationKind.Command => "Command",
            UsageOperationKind.File => "File",
            UsageOperationKind.Skill => "Skill",
            _ => row.Kind.ToString(),
        };
        string label = row.Kind == UsageOperationKind.File && row.Server is { Length: 64 } hmac
            ? kind + " · " + hmac[^4..]
            : row.Server is { Length: > 0 } server
                ? kind + " · " + server + " / " + row.Tool
                : kind + " · " + row.Tool;
        string outcomes = row.OutcomesAvailable
            ? string.Format(
                CultureInfo.CurrentCulture,
                GetString("UsageExplorerOperationOutcomeFormat"),
                row.SuccessCount,
                row.ErrorCount,
                row.UnknownCount)
            : GetString("UsageExplorerOperationOutcomeUnavailable");
        return new UsageExplorerOperationRow(
            row.Id,
            label,
            kind,
            string.Format(
                CultureInfo.CurrentCulture,
                GetString("UsageExplorerOperationCountFormat"),
                row.InvocationCount),
            outcomes,
            row.InvocationCount,
            row.Kind,
            row.Tool,
            row.Server,
            row.SessionKey?.Value);
    }
}
