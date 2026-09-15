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
        IReadOnlyList<UsageOperationRankedRow>? operations = null,
        UsageDerivedActivitySummary? derivedActivity = null,
        UsageWorkflowSummary? workflow = null)
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
            Operations: operations is { Count: > 0 } ? MapOperations(operations) : null,
            DerivedActivity: MapDerivedActivity(operations, derivedActivity),
            Workflow: MapWorkflow(workflow));
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
        output.WriteLine(
            "section,agent,provider,model,events,tokens,reported_usd,estimated_usd,unpriced,method,unit,category,count,eligible,excluded,unavailable");
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
            output.Write(Csv(session.PopulationTokens));
            WriteCsvExtras(output);
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
            output.Write(Csv(project.PopulationTokens));
            WriteCsvExtras(output);
        }

        foreach (OperationPopulationRow operation in snapshot.Operations ?? [])
        {
            WriteCsvOperation(output, "operation", operation);
        }

        foreach (OperationPopulationRow operation in snapshot.MixedOperations ?? [])
        {
            WriteCsvOperation(output, "operation-mixed-or-incomplete-session", operation);
        }

        if (snapshot.DerivedActivity is { } derived)
        {
            WriteCsvDerived(output, derived, "edit", derived.Edit);
            WriteCsvDerived(output, derived, "read", derived.Read);
            WriteCsvDerived(output, derived, "test", derived.Test);
            WriteCsvDerived(output, derived, "delegate", derived.Delegate);
            WriteCsvDerived(output, derived, "unknown", derived.Unknown);
        }

        if (snapshot.Workflow is { } workflow)
        {
            WriteCsvWorkflow(output, workflow, "same-file-verification-separated", workflow.SameFileVerificationSeparated);
            WriteCsvWorkflow(output, workflow, "excluded-concurrent", workflow.ExcludedConcurrent, excluded: true);
            WriteCsvWorkflow(output, workflow, "eligible-file-edits", workflow.EligibleFileEdits);
            WriteCsvWorkflow(output, workflow, "eligible-verifications", workflow.EligibleVerifications);
            if (workflow.IncompleteUnproved is { Length: > 0 })
            {
                WriteCsvWorkflow(output, workflow, "incomplete-unproved", workflow.IncompleteUnproved);
            }

            WriteCsvWorkflow(
                output,
                workflow,
                "first-edit-latency",
                workflow.FirstEditLatency,
                unavailable: workflow.FirstEditReason);
            WriteCsvWorkflow(output, workflow, "cost", count: null, unavailable: workflow.Cost);
        }

        return output.ToString();
    }

    private static void WriteCsvOperation(
        TextWriter output,
        string section,
        OperationPopulationRow operation)
    {
        output.Write(Csv(section));
        output.Write(',');
        output.Write(Csv(operation.ExportId));
        output.Write(',');
        output.Write(Csv(operation.Kind));
        output.Write(',');
        output.Write(Csv(operation.Tool));
        output.Write(',');
        output.Write(',');
        output.Write(',');
        output.Write(',');
        output.Write(',');
        WriteCsvExtras(
            output,
            unit: UsageDerivedActivity.CountUnit,
            category: operation.Kind,
            count: operation.Invocations);
    }

    private static void WriteCsvDerived(
        TextWriter output,
        DerivedActivitySnapshot derived,
        string category,
        string count)
    {
        output.Write(Csv("derived-activity"));
        output.Write(',');
        output.Write(',');
        output.Write(',');
        output.Write(',');
        output.Write(',');
        output.Write(',');
        output.Write(',');
        output.Write(',');
        WriteCsvExtras(
            output,
            derived.Method,
            derived.Unit,
            category,
            count,
            derived.Eligible,
            unavailable: derived.Cost);
    }

    private static void WriteCsvWorkflow(
        TextWriter output,
        WorkflowSnapshot workflow,
        string category,
        string? count,
        bool excluded = false,
        string? unavailable = null)
    {
        output.Write(Csv("workflow"));
        output.Write(',');
        output.Write(',');
        output.Write(',');
        output.Write(',');
        output.Write(',');
        output.Write(',');
        output.Write(',');
        output.Write(',');
        WriteCsvExtras(
            output,
            workflow.Method,
            workflow.Unit,
            category,
            excluded ? null : count,
            eligible: category.StartsWith("eligible-", StringComparison.Ordinal) ? count : null,
            excluded: excluded ? count : null,
            unavailable);
    }

    public static string WriteHtml(Document snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var output = new StringWriter(CultureInfo.InvariantCulture);
        output.WriteLine("<!DOCTYPE html><html><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width, initial-scale=1\"><title>TokenUsage report</title>");
        output.WriteLine("<style>body{font:16px/1.4 Segoe UI,sans-serif;margin:1.25rem;max-width:72rem}h1,h2,h3{font-weight:600}.table-wrap{overflow-x:auto;margin:0 0 1.25rem;-webkit-overflow-scrolling:touch}table{width:100%;min-width:40rem;border-collapse:collapse}th,td{text-align:left;padding:.45rem .55rem;border-bottom:1px solid #d0d0d0;vertical-align:top}th.num,td.num{text-align:right;font-variant-numeric:tabular-nums}dl.selection{display:grid;grid-template-columns:max-content 1fr;gap:.25rem 1rem;margin:0 0 1.25rem}dl.selection dt{font-weight:600}p.note{max-width:42rem}@media(max-width:420px){.table-wrap{margin:0 0 1.25rem}table{min-width:36rem}}</style></head><body>");
        output.WriteLine("<h1>TokenUsage report</h1>");
        if (snapshot.Comparison is { } compared)
        {
            WriteHtmlSelection(output, snapshot.Selection, "Selection A");
            WriteHtmlSelection(output, compared.Selection, "Selection " + compared.Label);
        }
        else
        {
            WriteHtmlSelection(output, snapshot.Selection, "Selection");
        }

        output.WriteLine("<p class=\"note\">Known cost is reported usage value plus estimated API value, not a subscription invoice. Price coverage is priced tokens over selected tokens, not collection coverage.</p>");
        output.WriteLine("<h2>Totals</h2>");
        WriteHtmlMetricsTableStart(output);
        WriteHtmlMetricsRow(output, "totals", string.Empty, snapshot.Totals);
        if (snapshot.Comparison is { } comparison)
        {
            WriteHtmlMetricsRow(output, "comparison", comparison.Label, comparison.Totals);
        }

        output.WriteLine("</tbody></table></div>");
        output.WriteLine("<h2>Models</h2>");
        WriteHtmlMetricsTableStart(output);
        foreach (ModelRow model in snapshot.Models)
        {
            WriteHtmlMetricsRow(output, "model", HostedModelName(model), model.Metrics);
        }

        output.WriteLine("</tbody></table></div>");
        if (snapshot.Sessions is { Count: > 0 })
        {
            output.WriteLine("<h2>Sessions</h2>");
            output.WriteLine("<div class=\"table-wrap\"><table><thead><tr><th>Section</th><th>Name</th><th class=\"num\">Selected events</th><th class=\"num\">Tokens</th></tr></thead><tbody>");
            foreach (AttributionPopulationRow session in snapshot.Sessions)
            {
                output.WriteLine($"<tr><td>session</td><td>{Escape(session.ExportId)} {Escape(session.State)}</td><td class=\"num\">{Escape(session.SelectedEvents)}</td><td class=\"num\">{Escape(session.SelectedTokens)}</td></tr>");
            }

            output.WriteLine("</tbody></table></div>");
        }

        if (snapshot.Projects is { Count: > 0 })
        {
            output.WriteLine("<h2>Projects</h2>");
            output.WriteLine("<div class=\"table-wrap\"><table><thead><tr><th>Section</th><th>Name</th><th class=\"num\">Selected events</th><th class=\"num\">Tokens</th></tr></thead><tbody>");
            foreach (AttributionPopulationRow project in snapshot.Projects)
            {
                output.WriteLine($"<tr><td>project</td><td>{Escape(project.ExportId)} {Escape(project.State)}</td><td class=\"num\">{Escape(project.SelectedEvents)}</td><td class=\"num\">{Escape(project.SelectedTokens)}</td></tr>");
            }

            output.WriteLine("</tbody></table></div>");
        }

        if (snapshot.Operations is { Count: > 0 } || snapshot.MixedOperations is { Count: > 0 })
        {
            output.WriteLine("<h2>Operations</h2>");
            output.WriteLine("<div class=\"table-wrap\"><table><thead><tr><th>Population</th><th>Name</th><th class=\"num\">Invocations</th></tr></thead><tbody>");
            foreach (OperationPopulationRow operation in snapshot.Operations ?? [])
            {
                output.WriteLine($"<tr><td>operation</td><td>{Escape(operation.Kind)} {Escape(operation.Tool)}</td><td class=\"num\">{Escape(operation.Invocations)}</td></tr>");
            }

            foreach (OperationPopulationRow operation in snapshot.MixedOperations ?? [])
            {
                output.WriteLine($"<tr><td>operation-mixed-or-incomplete-session</td><td>{Escape(operation.Kind)} {Escape(operation.Tool)}</td><td class=\"num\">{Escape(operation.Invocations)}</td></tr>");
            }

            output.WriteLine("</tbody></table></div>");
        }
        if (snapshot.DerivedActivity is { } derivedHtml)
        {
            output.WriteLine("<h2>Derived activity</h2>");
            output.WriteLine($"<p>Method {Escape(derivedHtml.Method)}; unit {Escape(derivedHtml.Unit)}; eligible {Escape(derivedHtml.Eligible)}; cost {Escape(derivedHtml.Cost)}</p>");
            output.WriteLine("<div class=\"table-wrap\"><table><thead><tr><th>Category</th><th>Count</th><th>Unit</th><th>Method</th></tr></thead><tbody>");
            output.WriteLine($"<tr><td>edit</td><td>{Escape(derivedHtml.Edit)}</td><td>{Escape(derivedHtml.Unit)}</td><td>{Escape(derivedHtml.Method)}</td></tr>");
            output.WriteLine($"<tr><td>read</td><td>{Escape(derivedHtml.Read)}</td><td>{Escape(derivedHtml.Unit)}</td><td>{Escape(derivedHtml.Method)}</td></tr>");
            output.WriteLine($"<tr><td>test</td><td>{Escape(derivedHtml.Test)}</td><td>{Escape(derivedHtml.Unit)}</td><td>{Escape(derivedHtml.Method)}</td></tr>");
            output.WriteLine($"<tr><td>delegate</td><td>{Escape(derivedHtml.Delegate)}</td><td>{Escape(derivedHtml.Unit)}</td><td>{Escape(derivedHtml.Method)}</td></tr>");
            output.WriteLine($"<tr><td>unknown</td><td>{Escape(derivedHtml.Unknown)}</td><td>{Escape(derivedHtml.Unit)}</td><td>{Escape(derivedHtml.Method)}</td></tr>");
            output.WriteLine("</tbody></table></div>");
        }

        if (snapshot.Workflow is { } workflowHtml)
        {
            output.WriteLine("<h2>Workflow indicators</h2>");
            output.WriteLine($"<p>Method {Escape(workflowHtml.Method)}; unit {Escape(workflowHtml.Unit)}; cost {Escape(workflowHtml.Cost)}</p>");
            output.WriteLine("<div class=\"table-wrap\"><table><thead><tr><th>Metric</th><th>Count</th><th>Eligible</th><th>Excluded</th><th>Unavailable</th></tr></thead><tbody>");
            output.WriteLine($"<tr><td>same-file-verification-separated</td><td>{Escape(workflowHtml.SameFileVerificationSeparated)}</td><td></td><td></td><td></td></tr>");
            output.WriteLine($"<tr><td>excluded-concurrent</td><td></td><td></td><td>{Escape(workflowHtml.ExcludedConcurrent)}</td><td></td></tr>");
            output.WriteLine($"<tr><td>eligible-file-edits</td><td></td><td>{Escape(workflowHtml.EligibleFileEdits)}</td><td></td><td></td></tr>");
            output.WriteLine($"<tr><td>eligible-verifications</td><td></td><td>{Escape(workflowHtml.EligibleVerifications)}</td><td></td><td></td></tr>");
            output.WriteLine($"<tr><td>incomplete-unproved</td><td>{Escape(workflowHtml.IncompleteUnproved)}</td><td></td><td></td><td></td></tr>");
            output.WriteLine($"<tr><td>first-edit-latency</td><td>{Escape(workflowHtml.FirstEditLatency)}</td><td></td><td></td><td>{Escape(workflowHtml.FirstEditReason)}</td></tr>");
            output.WriteLine("</tbody></table></div>");
        }

        output.WriteLine("</body></html>");
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

    private static void WriteHtmlSelection(TextWriter output, Selection selection, string heading)
    {
        output.Write("<h2>");
        output.Write(Escape(heading));
        output.WriteLine("</h2>");
        output.WriteLine("<dl class=\"selection\">");
        WriteHtmlDefinition(
            output,
            "Dates",
            Escape(selection.From) + " to " + Escape(selection.To) + " (" + Escape(selection.TimeSemantics) + ")");
        if (!string.IsNullOrWhiteSpace(selection.Agent))
        {
            WriteHtmlDefinition(output, "Agent", Escape(selection.Agent));
        }

        bool hasTools = selection.Tools is { Count: > 0 };
        if (hasTools || string.IsNullOrWhiteSpace(selection.Agent))
        {
            WriteHtmlDefinition(output, "Tools", JoinSelection(selection.Tools, "All tools"));
        }

        WriteHtmlDefinition(output, "Hosts", JoinSelection(selection.Hosts, "All hosts"));
        WriteHtmlDefinition(output, "Models", JoinSelection(selection.Models, "All models"));
        if (selection.ObservedModels is { Count: > 0 })
        {
            WriteHtmlDefinition(output, "Observed models", JoinSelection(selection.ObservedModels, "All observed models"));
        }

        if (selection.Efforts is { Count: > 0 })
        {
            WriteHtmlDefinition(output, "Reasoning efforts", JoinSelection(selection.Efforts, "All efforts"));
        }

        if (selection.Tiers is { Count: > 0 })
        {
            WriteHtmlDefinition(output, "Service tiers", JoinSelection(selection.Tiers, "All tiers"));
        }

        if (!string.IsNullOrWhiteSpace(selection.Search))
        {
            WriteHtmlDefinition(output, "Search", Escape(selection.Search));
        }

        output.WriteLine("</dl>");
    }

    private static void WriteHtmlDefinition(TextWriter output, string term, string value)
    {
        output.Write("<dt>");
        output.Write(Escape(term));
        output.Write("</dt><dd>");
        output.Write(value);
        output.WriteLine("</dd>");
    }

    private static void WriteHtmlMetricsTableStart(TextWriter output)
    {
        output.WriteLine("<div class=\"table-wrap\"><table><thead><tr><th>Section</th><th>Name</th><th class=\"num\">Events</th><th class=\"num\">Tokens</th><th class=\"num\">Reported USD</th><th class=\"num\">Estimated USD</th><th class=\"num\">Unpriced tokens</th><th class=\"num\">Priced coverage</th></tr></thead><tbody>");
    }

    private static void WriteHtmlMetricsRow(TextWriter output, string section, string name, Metrics metrics)
    {
        output.WriteLine(
            $"<tr><td>{Escape(section)}</td><td>{Escape(name)}</td><td class=\"num\">{Escape(metrics.Events)}</td><td class=\"num\">{Escape(metrics.Tokens.Total)}</td><td class=\"num\">{HtmlMoney(metrics.ReportedCost?.Amount)}</td><td class=\"num\">{HtmlMoney(metrics.EstimatedCost?.Amount)}</td><td class=\"num\">{Escape(metrics.Tokens.Unpriced)}</td><td class=\"num\">{Escape(metrics.PriceCoveragePercent)}%</td></tr>");
    }

    private static string HostedModelName(ModelRow model)
    {
        string host = string.IsNullOrWhiteSpace(model.Provider) ? "unspecified-host" : model.Provider;
        return model.Agent + "/" + host + "/" + model.Model;
    }

    private static string JoinSelection(IEnumerable<string?>? values, string allLabel)
    {
        if (values is null)
        {
            return Escape(allLabel);
        }

        string[] items = values
            .Select(value => Escape(string.IsNullOrWhiteSpace(value) ? "unspecified" : value))
            .ToArray();
        return items.Length == 0 ? Escape(allLabel) : string.Join(", ", items);
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
        output.Write(Csv(metrics.Tokens.Unpriced));
        WriteCsvExtras(output);
    }

    private static void WriteCsvExtras(
        TextWriter output,
        string? method = null,
        string? unit = null,
        string? category = null,
        string? count = null,
        string? eligible = null,
        string? excluded = null,
        string? unavailable = null)
    {
        output.Write(',');
        output.Write(Csv(method));
        output.Write(',');
        output.Write(Csv(unit));
        output.Write(',');
        output.Write(Csv(category));
        output.Write(',');
        output.Write(Csv(count));
        output.Write(',');
        output.Write(Csv(eligible));
        output.Write(',');
        output.Write(Csv(excluded));
        output.Write(',');
        output.WriteLine(Csv(unavailable));
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

    private static string HtmlMoney(string? amount)
    {
        if (string.IsNullOrWhiteSpace(amount))
        {
            return string.Empty;
        }

        if (decimal.TryParse(amount, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal value)
            && value > 0m
            && value < 0.01m)
        {
            return "&lt;$0.01 <span>(" + Escape(amount) + ")</span>";
        }

        return Escape(amount);
    }

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
        IReadOnlyList<UsageOperationRankedRow>? rows,
        string exportPrefix = "o")
    {
        if (rows is null || rows.Count == 0)
        {
            return [];
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(exportPrefix);
        var mapped = new List<OperationPopulationRow>(rows.Count);
        int sequence = 1;
        foreach (UsageOperationRankedRow row in rows)
        {
            mapped.Add(new OperationPopulationRow(
                exportPrefix + sequence.ToString(CultureInfo.InvariantCulture),
                UsageOperationKindCodec.ToWire(row.Kind),
                row.Tool,
                row.Kind == UsageOperationKind.File ? null : row.Server,
                Integer(row.InvocationCount),
                row.OutcomesAvailable ? Integer(row.SuccessCount) : null,
                row.OutcomesAvailable ? Integer(row.ErrorCount) : null,
                row.Capability?.Value,
                row.ConsentEpoch));
            sequence++;
        }

        return mapped;
    }

    public static DerivedActivitySnapshot? MapDerivedActivity(
        IReadOnlyList<UsageOperationRankedRow>? rows,
        UsageDerivedActivitySummary? summary = null)
    {
        UsageDerivedActivitySummary computed = summary
            ?? (rows is { Count: > 0 } ? UsageDerivedActivity.Summarize(rows) : UsageDerivedActivitySummary.Empty);
        if (computed.Eligible == 0)
        {
            return null;
        }

        return MapDerivedActivity(computed);
    }

    public static DerivedActivitySnapshot? MapDerivedActivity(IReadOnlyList<OperationPopulationRow>? rows)
    {
        if (rows is null || rows.Count == 0)
        {
            return null;
        }

        var classified = new List<(UsageOperationKind Kind, string Tool, int Count)>(rows.Count);
        foreach (OperationPopulationRow row in rows)
        {
            if (!UsageOperationKindCodec.TryParse(row.Kind, out UsageOperationKind kind)
                || !int.TryParse(row.Invocations, NumberStyles.Integer, CultureInfo.InvariantCulture, out int count))
            {
                continue;
            }

            classified.Add((kind, row.Tool, count));
        }

        return MapDerivedActivity(UsageDerivedActivity.Summarize(classified));
    }

    public static DerivedActivitySnapshot? MapDerivedActivity(UsageDerivedActivitySummary summary)
    {
        if (summary.Eligible == 0)
        {
            return null;
        }

        return new DerivedActivitySnapshot(
            summary.MethodVersion,
            Integer(summary.Eligible),
            Integer(summary.Edit),
            Integer(summary.Read),
            Integer(summary.Test),
            Integer(summary.Delegate),
            Integer(summary.Unknown),
            summary.CostAvailability,
            UsageDerivedActivity.CountUnit);
    }

    public static WorkflowSnapshot? MapWorkflow(
        UsageWorkflowSummary? summary,
        long? filesConsentEpoch = null,
        long? commandsConsentEpoch = null)
    {
        if (summary is null)
        {
            return null;
        }

        return new WorkflowSnapshot(
            summary.MethodVersion,
            summary.FirstEditReason,
            Integer(summary.SameFileVerificationSeparated),
            Integer(summary.ExcludedConcurrent),
            Integer(summary.EligibleFileEdits),
            Integer(summary.EligibleVerifications),
            summary.CostAvailability,
            FilesConsentEpoch: filesConsentEpoch,
            CommandsConsentEpoch: commandsConsentEpoch,
            ExcludedFileConcurrent: summary.ExcludedFileConcurrent,
            ExcludedEditTestConcurrent: summary.ExcludedEditTestConcurrent,
            IncompleteUnproved: Integer(summary.IncompleteUnproved),
            Unit: UsageDerivedActivity.CountUnit);
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
                    Integer(row.SessionEventCount),
                    Capability: row.Capability?.Value,
                    ConsentEpoch: row.ConsentEpoch));
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
                parent,
                row.Capability?.Value,
                row.ConsentEpoch));
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
                Integer(row.ProjectEventCount),
                Capability: row.Capability?.Value,
                ConsentEpoch: row.ConsentEpoch));
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
        IReadOnlyList<OperationPopulationRow>? Operations = null,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        IReadOnlyList<OperationPopulationRow>? MixedOperations = null,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        DerivedActivitySnapshot? DerivedActivity = null,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        WorkflowSnapshot? Workflow = null);

    public sealed record DerivedActivitySnapshot(
        string Method,
        string Eligible,
        string Edit,
        string Read,
        string Test,
        string Delegate,
        string Unknown,
        string Cost,
        string Unit = UsageDerivedActivity.CountUnit);

    public sealed record WorkflowSnapshot(
        string Method,
        string FirstEditReason,
        string SameFileVerificationSeparated,
        string ExcludedConcurrent,
        string EligibleFileEdits,
        string EligibleVerifications,
        string Cost,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        string? FirstEditLatency = null,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.Always)]
        long? FilesConsentEpoch = null,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.Always)]
        long? CommandsConsentEpoch = null,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.Always)]
        int? ExcludedFileConcurrent = null,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.Always)]
        int? ExcludedEditTestConcurrent = null,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        string? IncompleteUnproved = null,
        string Unit = UsageDerivedActivity.CountUnit);

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
        string? Error,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.Always)]
        string? Capability = null,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.Always)]
        long? ConsentEpoch = null);

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
        string? ParentExportId = null,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.Always)]
        string? Capability = null,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.Always)]
        long? ConsentEpoch = null);
}
