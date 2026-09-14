using System.Globalization;
using TokenUsage.Core.Usage;

namespace TokenUsage.App.ViewModels.Reports;

public sealed partial class UsageReportViewModel
{
    public async Task OpenOperationsAsync()
    {
        if (DetailModel is not { ProviderId: "codex" } || _attributionConsent is null)
        {
            return;
        }

        if (!HasOperations && !CanOpenOperations)
        {
            return;
        }

        long generation = _selectionGeneration;
        long consentGeneration = _consentGeneration;
        CancellationToken token = _selectionCancellation?.Token ?? CancellationToken.None;
        IReadOnlyList<UsageOperationRankedRow> ranked = await RunReportWorkAsync(async () =>
        {
            UsageRepository repository = await UsageRepository.OpenReadOnlyAsync(_databasePath, token)
                .ConfigureAwait(false);
            DateTimeOffset from = new(StartDate.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
            DateTimeOffset to = new(EndDate.AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
            var rows = new List<UsageOperationRankedRow>();
            await AddCapabilityRankingAsync(
                    repository,
                    AttributionCapability.CodexMcp,
                    from,
                    to,
                    rows,
                    token)
                .ConfigureAwait(false);
            await AddCapabilityRankingAsync(
                    repository,
                    AttributionCapability.CodexSkills,
                    from,
                    to,
                    rows,
                    token)
                .ConfigureAwait(false);
            await AddCapabilityRankingAsync(
                    repository,
                    AttributionCapability.CodexCommands,
                    from,
                    to,
                    rows,
                    token)
                .ConfigureAwait(false);
            await AddCapabilityRankingAsync(
                    repository,
                    AttributionCapability.CodexFiles,
                    from,
                    to,
                    rows,
                    token)
                .ConfigureAwait(false);
            return rows;
        }, token);
        if (_disposed || generation != _selectionGeneration || consentGeneration != _consentGeneration)
        {
            return;
        }

        OperationRows = ranked.Select(MapOperationRow).ToArray();
        HasOperations = true;
        HasSessions = false;
        HasProjects = false;
        DetailOperation = null;
        OperationDetailValues = string.Empty;
        SkillsAvailabilityText = GetString("UsageOverviewSkillsUnavailable");
        OperationsDerivedNote = OperationRows.Any(row => row.Kind == UsageOperationKind.Command)
            ? GetString("UsageExplorerOperationsDerivedNote")
            : string.Empty;
        OnPropertyChanged(nameof(OperationRows));
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
        UpdateOperationsAvailability();
    }

    public void CloseOperations()
    {
        HasOperations = false;
        DetailOperation = null;
        OperationRows = [];
        OperationDetailValues = string.Empty;
        SkillsAvailabilityText = string.Empty;
        OperationsDerivedNote = string.Empty;
        OnPropertyChanged(nameof(OperationRows));
        OnPropertyChanged(nameof(HasOperations));
        OnPropertyChanged(nameof(HasOperationDetail));
        OnPropertyChanged(nameof(CanOpenOperationSession));
        OnPropertyChanged(nameof(DetailOperation));
        OnPropertyChanged(nameof(OperationDetailValues));
        OnPropertyChanged(nameof(SkillsAvailabilityText));
        OnPropertyChanged(nameof(HasSkillsAvailabilityNotice));
        OnPropertyChanged(nameof(OperationsDerivedNote));
        OnPropertyChanged(nameof(HasOperationsDerivedNote));
        OnPropertyChanged(nameof(CanOpenSessions));
        OnPropertyChanged(nameof(CanOpenProjects));
        OnPropertyChanged(nameof(CanOpenOperations));
        OnPropertyChanged(nameof(HasModelDetail));
        UpdateOperationsAvailability();
    }

    public void OpenOperationDetail(string id)
    {
        DetailOperation = OperationRows.FirstOrDefault(row => row.Id == id);
        OperationDetailValues = DetailOperation is { } operation
            ? operation.Label + " · " + operation.CountText + " · " + operation.OutcomeText
            : string.Empty;
        OnPropertyChanged(nameof(DetailOperation));
        OnPropertyChanged(nameof(HasOperationDetail));
        OnPropertyChanged(nameof(CanOpenOperationSession));
        OnPropertyChanged(nameof(OperationDetailValues));
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

    private async Task AddCapabilityRankingAsync(
        UsageRepository repository,
        AttributionCapability capability,
        DateTimeOffset from,
        DateTimeOffset to,
        List<UsageOperationRankedRow> rows,
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

        rows.AddRange(await repository.ReadOperationRankingAsync(
            from,
            to,
            capability,
            consent.Epoch,
            sessionKey: null,
            token).ConfigureAwait(false));
    }

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
