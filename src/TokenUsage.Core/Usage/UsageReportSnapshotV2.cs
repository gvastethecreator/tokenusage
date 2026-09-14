using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using TokenUsage.Core.Automation;
using TokenUsage.Core.Providers;

namespace TokenUsage.Core.Usage;

public static class UsageReportSnapshotV2
{
    public const string SchemaVersion = "tokenusage.report.v2";

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    public static string Serialize(
        DateTimeOffset generatedAt,
        DateOnly fromInclusive,
        DateOnly toInclusive,
        int days,
        AgentId? agentId,
        UsageReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        return JsonSerializer.Serialize(
            Create(generatedAt, fromInclusive, toInclusive, days, agentId, report),
            SerializerOptions);
    }

    public static Document Create(
        DateTimeOffset generatedAt,
        DateOnly fromInclusive,
        DateOnly toInclusive,
        int days,
        AgentId? agentId,
        UsageReport report,
        UsageReport? comparison = null,
        DateOnly? comparisonFrom = null,
        DateOnly? comparisonTo = null,
        UsageReportSelection? selection = null,
        DateTimeOffset? exactFromInclusiveUtc = null,
        DateTimeOffset? exactToExclusiveUtc = null,
        IReadOnlyList<UsageSessionContribution>? sessions = null,
        IReadOnlyList<UsageProjectContribution>? projects = null,
        UsageReportOverview? overview = null,
        IReadOnlyList<UsageOperationRankedRow>? operations = null)
    {
        ArgumentNullException.ThrowIfNull(report);
        Selection selected = CreateSelection(
            fromInclusive,
            toInclusive,
            days,
            agentId,
            report,
            selection,
            exactFromInclusiveUtc,
            exactToExclusiveUtc);
        ComparisonSide? other = comparison is null
            ? null
            : new ComparisonSide(
                "B",
                CreateSelection(
                    comparisonFrom ?? fromInclusive,
                    comparisonTo ?? toInclusive,
                    comparisonTo is { } to && comparisonFrom is { } from
                        ? to.DayNumber - from.DayNumber + 1
                        : days,
                    agentId,
                    comparison,
                    selection,
                    exactFromInclusiveUtc,
                    exactToExclusiveUtc),
                CreateMetrics(comparison.Totals),
                comparison.Models.Select(item => new ModelRow(
                    item.AgentId.Value,
                    item.ModelProviderId?.Value,
                    item.ModelId.Value,
                    CreateMetrics(item.Metrics))).ToArray());
        return new Document(
            SchemaVersion,
            generatedAt.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture),
            selected,
            report.DataRevision is { } revision
                ? new Revision(Integer(revision.Sequence), revision.DatabaseId)
                : null,
            CreateMetrics(report.Totals),
            report.Agents.Select(item => new AgentRow(item.AgentId.Value, CreateMetrics(item.Metrics))).ToArray(),
            report.Models.Select(item => new ModelRow(
                item.AgentId.Value,
                item.ModelProviderId?.Value,
                item.ModelId.Value,
                CreateMetrics(item.Metrics))).ToArray(),
            report.Days.Select(item => new DayRow(
                item.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                CreateMetrics(item.Metrics))).ToArray(),
            report.PricingVersions.ToArray(),
            report.ParserVersions.ToArray(),
            RequestCount: null,
            RequestCountReason: "finality-not-established",
            Comparison: other,
            Sessions: MapSessions(sessions),
            Projects: MapProjects(projects),
            Overview: MapOverview(overview),
            Operations: operations is { Count: > 0 } ? MapOperations(operations) : null);
    }

    private static Selection CreateSelection(
        DateOnly fromInclusive,
        DateOnly toInclusive,
        int days,
        AgentId? agentId,
        UsageReport report,
        UsageReportSelection? selection,
        DateTimeOffset? exactFromInclusiveUtc,
        DateTimeOffset? exactToExclusiveUtc)
    {
        UsageReportSelection chosen = selection ?? report.ConfigurationSelection ?? new();
        bool exact = exactFromInclusiveUtc is not null && exactToExclusiveUtc is not null;
        return new Selection(
            exact ? "exact" : "calendar",
            exact
                ? exactFromInclusiveUtc!.Value.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture)
                : fromInclusive.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            exact
                ? exactToExclusiveUtc!.Value.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture)
                : toInclusive.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            Integer(days),
            agentId?.Value,
            exact ? exactToExclusiveUtc!.Value.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture) : null,
            chosen.Agents.Select(agent => agent.Value).ToArray(),
            chosen.ModelProviders.Select(host => host?.Value).ToArray(),
            chosen.Models.Select(model => model.Value).ToArray(),
            chosen.ObservedModels.Select(model => model?.Value).ToArray(),
            chosen.ReasoningEfforts.ToArray(),
            chosen.ServiceTiers.ToArray(),
            string.IsNullOrWhiteSpace(chosen.Search) ? null : chosen.Search,
            report.IsExactInterval || exact ? "exact-utc-half-open" : "inclusive-civil-days");
    }

    public static string WriteCsv(Document snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var output = new StringWriter(CultureInfo.InvariantCulture);
        output.WriteLine("section,agent,provider,model,events,tokens,reported_usd,estimated_usd,unpriced");
        WriteCsvRow(output, "totals", null, null, null, snapshot.Totals);
        if (snapshot.Comparison is { } comparison)
        {
            WriteCsvRow(output, "comparison-totals", null, null, null, comparison.Totals);
        }
        foreach (AgentRow agent in snapshot.ByAgent)
        {
            WriteCsvRow(output, "agent", agent.Agent, null, null, agent.Metrics);
        }

        foreach (ModelRow model in snapshot.Models)
        {
            WriteCsvRow(output, "model", model.Agent, model.Provider, model.Model, model.Metrics);
        }

        foreach (AttributionPopulationRow session in snapshot.Sessions ?? [])
        {
            output.Write(Csv("session"));
            output.Write(',');
            output.Write(Csv(session.ExportId));
            output.Write(',');
            output.Write(Csv(session.State));
            output.Write(',');
            output.Write(Csv(session.ParentExportId));
            output.Write(',');
            output.Write(Csv(session.SelectedEvents));
            output.Write(',');
            output.Write(Csv(session.SelectedTokens));
            output.Write(',');
            output.Write(',');
            output.Write(',');
            output.WriteLine(Csv(session.PopulationTokens));
        }

        foreach (AttributionPopulationRow project in snapshot.Projects ?? [])
        {
            output.Write(Csv("project"));
            output.Write(',');
            output.Write(Csv(project.ExportId));
            output.Write(',');
            output.Write(Csv(project.State));
            output.Write(',');
            output.Write(',');
            output.Write(Csv(project.SelectedEvents));
            output.Write(',');
            output.Write(Csv(project.SelectedTokens));
            output.Write(',');
            output.Write(',');
            output.Write(',');
            output.WriteLine(Csv(project.PopulationTokens));
        }

        foreach (OperationPopulationRow operation in snapshot.Operations ?? [])
        {
            output.Write(Csv("operation"));
            output.Write(',');
            output.Write(Csv(operation.ExportId));
            output.Write(',');
            output.Write(Csv(operation.Kind));
            output.Write(',');
            output.Write(Csv(operation.Tool));
            output.Write(',');
            output.Write(Csv(operation.Invocations));
            output.Write(',');
            output.Write(',');
            output.Write(',');
            output.Write(',');
            output.WriteLine();
        }

        return output.ToString();
    }

    public static string WriteHtml(Document snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var output = new StringWriter(CultureInfo.InvariantCulture);
        output.WriteLine("<!DOCTYPE html><html><head><meta charset=\"utf-8\"><title>TokenUsage report</title></head><body>");
        output.WriteLine("<h1>TokenUsage report</h1>");
        output.WriteLine($"<p>Range {Escape(snapshot.Selection.From)} to {Escape(snapshot.Selection.To)} ({Escape(snapshot.Selection.TimeSemantics)})</p>");
        output.WriteLine("<table><thead><tr><th>Section</th><th>Name</th><th>Events</th><th>Tokens</th><th>Reported USD</th></tr></thead><tbody>");
        output.WriteLine($"<tr><td>totals</td><td></td><td>{Escape(snapshot.Totals.Events)}</td><td>{Escape(snapshot.Totals.Tokens.Total)}</td><td>{Escape(snapshot.Totals.ReportedCost?.Amount)}</td></tr>");
        if (snapshot.Comparison is { } comparison)
        {
            output.WriteLine($"<tr><td>comparison</td><td>{Escape(comparison.Label)}</td><td>{Escape(comparison.Totals.Events)}</td><td>{Escape(comparison.Totals.Tokens.Total)}</td><td>{Escape(comparison.Totals.ReportedCost?.Amount)}</td></tr>");
        }
        foreach (ModelRow model in snapshot.Models)
        {
            output.WriteLine($"<tr><td>model</td><td>{Escape(model.Agent)}/{Escape(model.Model)}</td><td>{Escape(model.Metrics.Events)}</td><td>{Escape(model.Metrics.Tokens.Total)}</td><td>{Escape(model.Metrics.ReportedCost?.Amount)}</td></tr>");
        }

        foreach (AttributionPopulationRow session in snapshot.Sessions ?? [])
        {
            output.WriteLine($"<tr><td>session</td><td>{Escape(session.ExportId)} {Escape(session.State)}</td><td>{Escape(session.SelectedEvents)}</td><td>{Escape(session.SelectedTokens)}</td><td></td></tr>");
        }

        foreach (AttributionPopulationRow project in snapshot.Projects ?? [])
        {
            output.WriteLine($"<tr><td>project</td><td>{Escape(project.ExportId)} {Escape(project.State)}</td><td>{Escape(project.SelectedEvents)}</td><td>{Escape(project.SelectedTokens)}</td><td></td></tr>");
        }

        foreach (OperationPopulationRow operation in snapshot.Operations ?? [])
        {
            output.WriteLine($"<tr><td>operation</td><td>{Escape(operation.Kind)} {Escape(operation.Tool)}</td><td>{Escape(operation.Invocations)}</td><td></td><td></td></tr>");
        }

        output.WriteLine("</tbody></table></body></html>");
        return output.ToString();
    }

    public static string Render(Document snapshot, string format)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return format switch
        {
            "csv" => WriteCsv(snapshot),
            "html" => WriteHtml(snapshot),
            _ => JsonSerializer.Serialize(snapshot, SerializerOptions),
        };
    }

    private static void WriteCsvRow(
        TextWriter output,
        string section,
        string? agent,
        string? provider,
        string? model,
        Metrics metrics)
    {
        output.Write(Csv(section));
        output.Write(',');
        output.Write(Csv(agent));
        output.Write(',');
        output.Write(Csv(provider));
        output.Write(',');
        output.Write(Csv(model));
        output.Write(',');
        output.Write(Csv(metrics.Events));
        output.Write(',');
        output.Write(Csv(metrics.Tokens.Total));
        output.Write(',');
        output.Write(Csv(metrics.ReportedCost?.Amount));
        output.Write(',');
        output.Write(Csv(metrics.EstimatedCost?.Amount));
        output.Write(',');
        output.WriteLine(Csv(metrics.Tokens.Unpriced));
    }

    private static string Csv(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        string escaped = value;
        if (escaped[0] is '=' or '+' or '-' or '@')
        {
            escaped = "'" + escaped;
        }

        if (escaped.Contains('"') || escaped.Contains(',') || escaped.Contains('\n'))
        {
            return "\"" + escaped.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
        }

        return escaped;
    }

    private static string Escape(string? value) =>
        (value ?? string.Empty)
            .Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal)
            .Replace("\"", "&quot;", StringComparison.Ordinal);

    private static Metrics CreateMetrics(UsageReportMetrics metrics) =>
        new(
            Integer(metrics.EventCount),
            new Tokens(
                Integer(metrics.Tokens.Input),
                Integer(metrics.Tokens.Output),
                Integer(metrics.Tokens.Reasoning),
                Integer(metrics.Tokens.CacheRead),
                Integer(metrics.Tokens.CacheWrite),
                Integer(metrics.Tokens.Total),
                Integer(metrics.UnpricedTokens)),
            Money(metrics.ReportedCostUsd),
            Money(metrics.EstimatedCostUsd),
            Integer(metrics.UnavailableCostEventCount),
            CoverageName(metrics.Coverage),
            metrics.PriceCoveragePercent.ToString("0.0", CultureInfo.InvariantCulture));

    private static string CoverageName(CoverageKind coverage) => coverage switch
    {
        CoverageKind.Complete => "complete",
        CoverageKind.Partial => "partial",
        CoverageKind.SummaryOnly => "summary-only",
        CoverageKind.Unpriced => "unpriced",
        _ => throw new ArgumentOutOfRangeException(nameof(coverage)),
    };

    private static MoneyAmount? Money(decimal? value) =>
        value is { } amount
            ? new MoneyAmount("USD", amount.ToString(CultureInfo.InvariantCulture))
            : null;

    private static string Integer(long value) => value.ToString(CultureInfo.InvariantCulture);

    public static IReadOnlyList<OperationPopulationRow> MapOperations(
        IReadOnlyList<UsageOperationRankedRow>? rows)
    {
        if (rows is null || rows.Count == 0)
        {
            return [];
        }

        var mapped = new List<OperationPopulationRow>(rows.Count);
        int sequence = 1;
        foreach (UsageOperationRankedRow row in rows)
        {
            mapped.Add(new OperationPopulationRow(
                "o" + sequence.ToString(CultureInfo.InvariantCulture),
                UsageOperationKindCodec.ToWire(row.Kind),
                row.Tool,
                row.Kind == UsageOperationKind.File ? null : row.Server,
                Integer(row.InvocationCount),
                row.OutcomesAvailable ? Integer(row.SuccessCount) : null,
                row.OutcomesAvailable ? Integer(row.ErrorCount) : null));
            sequence++;
        }

        return mapped;
    }

    public static IReadOnlyList<AttributionPopulationRow> MapSessions(
        IReadOnlyList<UsageSessionContribution>? contributions)
    {
        if (contributions is null || contributions.Count == 0)
        {
            return [];
        }

        Dictionary<string, string> exportIds = new(StringComparer.Ordinal);
        int sequence = 1;
        foreach (UsageSessionContribution row in contributions.Where(row => !row.IsUnassigned && row.SessionKey is not null))
        {
            exportIds.TryAdd(row.SessionKey!.Value, "s" + sequence.ToString(CultureInfo.InvariantCulture));
            sequence++;
        }

        var mapped = new List<AttributionPopulationRow>(contributions.Count);
        foreach (UsageSessionContribution row in contributions)
        {
            if (row.IsUnassigned)
            {
                mapped.Add(new AttributionPopulationRow(
                    "u" + (mapped.Count(item => item.State == "unassigned") + 1).ToString(CultureInfo.InvariantCulture),
                    "unassigned",
                    Integer(row.SelectedTokens.Total),
                    Integer(row.SessionTokens.Total),
                    Integer(row.SelectedEventCount),
                    Integer(row.SessionEventCount)));
                continue;
            }

            if (row.SessionKey is null || !exportIds.TryGetValue(row.SessionKey.Value, out string? exportId))
            {
                continue;
            }

            string? parent = row.ParentSessionKey is { } parentKey
                && exportIds.TryGetValue(parentKey.Value, out string? parentId)
                    ? parentId
                    : null;
            mapped.Add(new AttributionPopulationRow(
                exportId,
                "assigned",
                Integer(row.SelectedTokens.Total),
                Integer(row.SessionTokens.Total),
                Integer(row.SelectedEventCount),
                Integer(row.SessionEventCount),
                parent));
        }

        return mapped;
    }

    public static IReadOnlyList<AttributionPopulationRow> MapProjects(
        IReadOnlyList<UsageProjectContribution>? contributions)
    {
        if (contributions is null || contributions.Count == 0)
        {
            return [];
        }

        int sequence = 1;
        var mapped = new List<AttributionPopulationRow>(contributions.Count);
        foreach (UsageProjectContribution row in contributions)
        {
            string state = row.IsUnassigned
                ? "unassigned"
                : row.IsAmbiguous
                    ? "ambiguous"
                    : row.MappingKind == ProjectMappingKind.UserMapped
                        ? "user-mapped"
                        : "observed";
            mapped.Add(new AttributionPopulationRow(
                (row.IsUnassigned ? "u" : "p") + sequence.ToString(CultureInfo.InvariantCulture),
                state,
                Integer(row.SelectedTokens.Total),
                Integer(row.ProjectTokens.Total),
                Integer(row.SelectedEventCount),
                Integer(row.ProjectEventCount)));
            sequence++;
        }

        return mapped;
    }

    public static OverviewSnapshot? MapOverview(UsageReportOverview? overview)
    {
        if (overview is null)
        {
            return null;
        }

        return new OverviewSnapshot(
            Integer(overview.ObservationCount),
            overview.SessionAvailability.ToString().ToLowerInvariant(),
            overview.AttributedSessionCount is { } attributed ? Integer(attributed) : null,
            overview.RootSessionCount is { } root ? Integer(root) : null,
            overview.DelegatedSessionCount is { } delegated ? Integer(delegated) : null,
            Integer(overview.UnassignedTokens),
            Money(overview.AttributedReportedCostPerSession),
            Money(overview.AttributedEstimatedCostPerSession),
            overview.CacheShareMethod,
            overview.CacheShareAvailability.ToString().ToLowerInvariant(),
            overview.CacheSharePercent is { } percent
                ? percent.ToString("0.0", CultureInfo.InvariantCulture)
                : null,
            RequestCount: null,
            overview.RequestCountReason,
            MapOverviewRows(overview.Models, "m"),
            MapOverviewRows(overview.Projects, "p"));
    }

    private static List<OverviewRankedRow> MapOverviewRows(
        IReadOnlyList<UsageOverviewRankedRow> rows,
        string assignedPrefix)
    {
        int sequence = 1;
        var mapped = new List<OverviewRankedRow>(rows.Count);
        foreach (UsageOverviewRankedRow row in rows)
        {
            string exportId = row.IsOther
                ? "other"
                : row.IsUnassigned
                    ? "u" + sequence.ToString(CultureInfo.InvariantCulture)
                    : assignedPrefix + sequence.ToString(CultureInfo.InvariantCulture);
            if (!row.IsOther)
            {
                sequence++;
            }

            mapped.Add(new OverviewRankedRow(
                exportId,
                row.IsOther ? "other" : row.IsUnassigned ? "unassigned" : assignedPrefix == "m" ? "model" : "project",
                row.ModelId?.Value,
                Integer(row.Tokens.Total),
                Money(row.ReportedCostUsd),
                Money(row.EstimatedCostUsd),
                Integer(row.UnpricedTokens),
                Integer(row.ObservationCount),
                Integer(row.AttributedSessionCount)));
        }

        return mapped;
    }

    public sealed record Document(
        [property: JsonPropertyName("schemaVersion")] string Schema,
        string GeneratedAt,
        Selection Selection,
        Revision? DataRevision,
        Metrics Totals,
        IReadOnlyList<AgentRow> ByAgent,
        IReadOnlyList<ModelRow> Models,
        IReadOnlyList<DayRow> Daily,
        IReadOnlyList<string> PricingVersions,
        IReadOnlyList<string> ParserVersions,
        string? RequestCount,
        string RequestCountReason,
        ComparisonSide? Comparison = null,
        IReadOnlyList<AttributionPopulationRow>? Sessions = null,
        IReadOnlyList<AttributionPopulationRow>? Projects = null,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        OverviewSnapshot? Overview = null,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        IReadOnlyList<OperationPopulationRow>? Operations = null);

    public sealed record OverviewSnapshot(
        string ObservationCount,
        string SessionAvailability,
        string? AttributedSessionCount,
        string? RootSessionCount,
        string? DelegatedSessionCount,
        string UnassignedTokens,
        MoneyAmount? AttributedReportedCostPerSession,
        MoneyAmount? AttributedEstimatedCostPerSession,
        string CacheShareMethod,
        string CacheShareAvailability,
        string? CacheSharePercent,
        string? RequestCount,
        string RequestCountReason,
        IReadOnlyList<OverviewRankedRow> Models,
        IReadOnlyList<OverviewRankedRow> Projects);

    public sealed record OverviewRankedRow(
        string ExportId,
        string Kind,
        string? Model,
        string Tokens,
        MoneyAmount? ReportedCost,
        MoneyAmount? EstimatedCost,
        string UnpricedTokens,
        string Observations,
        string AttributedSessions);

    public sealed record OperationPopulationRow(
        string ExportId,
        string Kind,
        string Tool,
        string? Server,
        string Invocations,
        string? Success,
        string? Error);

    public sealed record Selection(
        string Kind,
        string From,
        string To,
        string Days,
        string? Agent,
        string? ToExclusive = null,
        IReadOnlyList<string>? Tools = null,
        IReadOnlyList<string?>? Hosts = null,
        IReadOnlyList<string>? Models = null,
        IReadOnlyList<string?>? ObservedModels = null,
        IReadOnlyList<string?>? Efforts = null,
        IReadOnlyList<string?>? Tiers = null,
        string? Search = null,
        string? TimeSemantics = null);

    public sealed record ComparisonSide(
        string Label,
        Selection Selection,
        Metrics Totals,
        IReadOnlyList<ModelRow> Models);

    public sealed record Revision(string Sequence, string DatabaseId);

    public sealed record AgentRow(string Agent, Metrics Metrics);

    public sealed record ModelRow(string Agent, string? Provider, string Model, Metrics Metrics);

    public sealed record DayRow(string Date, Metrics Metrics);

    public sealed record Metrics(
        string Events,
        Tokens Tokens,
        MoneyAmount? ReportedCost,
        MoneyAmount? EstimatedCost,
        string UnavailableCostEvents,
        string Coverage,
        string PriceCoveragePercent);

    public sealed record Tokens(
        string Input,
        string Output,
        string Reasoning,
        string CacheRead,
        string CacheWrite,
        string Total,
        string Unpriced);

    public sealed record MoneyAmount(string Currency, string Amount);

    public sealed record AttributionPopulationRow(
        string ExportId,
        string State,
        string SelectedTokens,
        string PopulationTokens,
        string SelectedEvents,
        string PopulationEvents,
        string? ParentExportId = null);
}
