using System.Globalization;
using System.Text.Json;
using TokenUsage.Core.Automation;
using TokenUsage.Core.Providers;
using TokenUsage.Core.Usage;

namespace TokenUsage.Cli;

internal sealed record ParsedReportSelection(
    string Kind,
    DateOnly From,
    DateOnly To,
    string? Agent,
    DateTimeOffset? ExactFromInclusiveUtc,
    DateTimeOffset? ExactToExclusiveUtc,
    UsageReportSelection Filters);

internal static class ReportSelectionFile
{
    internal const int MaxBytes = 64 * 1024;
    internal const string SchemaVersion = "tokenusage.selection.v1";
    private const int MaxFilterValues = 100;
    private const int MaxIdLength = 200;

    private static readonly HashSet<string> AllowedProperties = new(StringComparer.Ordinal)
    {
        "schema",
        "kind",
        "from",
        "to",
        "agent",
        "fromUtc",
        "toExclusiveUtc",
        "tools",
        "hosts",
        "models",
        "observedModels",
        "efforts",
        "tiers",
    };

    internal static bool TryRead(
        string path,
        out DateOnly from,
        out DateOnly to,
        out string? agent,
        out string error)
    {
        from = default;
        to = default;
        agent = null;
        if (!TryRead(path, out ParsedReportSelection? parsed, out error) || parsed is null)
        {
            return false;
        }

        from = parsed.From;
        to = parsed.To;
        agent = parsed.Agent;
        return true;
    }

    internal static bool TryRead(
        string path,
        out ParsedReportSelection? selection,
        out string error)
    {
        selection = null;
        error = string.Empty;
        byte[] bytes;
        try
        {
            bytes = File.ReadAllBytes(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            error = "The selection file could not be read.";
            return false;
        }

        if (bytes.Length is 0 or > MaxBytes)
        {
            error = "The selection file must be a local JSON document of at most 64 KiB.";
            return false;
        }

        if (HasDuplicateRootProperties(bytes))
        {
            error = "The selection file cannot repeat properties.";
            return false;
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(
                bytes,
                new JsonDocumentOptions
                {
                    AllowTrailingCommas = false,
                    CommentHandling = JsonCommentHandling.Disallow,
                });
        }
        catch (JsonException)
        {
            error = "The selection file is not valid JSON.";
            return false;
        }

        using (document)
        {
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                error = "The selection file must be a JSON object.";
                return false;
            }

            foreach (JsonProperty property in document.RootElement.EnumerateObject())
            {
                if (!AllowedProperties.Contains(property.Name))
                {
                    error = "The selection file has an unsupported property.";
                    return false;
                }
            }

            if (!document.RootElement.TryGetProperty("schema", out JsonElement schema)
                || schema.ValueKind != JsonValueKind.String
                || schema.GetString() != SchemaVersion)
            {
                error = "The selection file needs schema tokenusage.selection.v1.";
                return false;
            }

            string kind = "calendar";
            if (document.RootElement.TryGetProperty("kind", out JsonElement kindElement))
            {
                if (kindElement.ValueKind != JsonValueKind.String
                    || kindElement.GetString() is not ("calendar" or "exact"))
                {
                    error = "The selection file kind must be calendar or exact.";
                    return false;
                }

                kind = kindElement.GetString()!;
            }

            if (!TryReadDate(document.RootElement, "from", out DateOnly parsedFrom)
                || !TryReadDate(document.RootElement, "to", out DateOnly parsedTo))
            {
                error = "The selection file needs inclusive from and to dates in YYYY-MM-DD form.";
                return false;
            }

            string? parsedAgent = null;
            if (document.RootElement.TryGetProperty("agent", out JsonElement agentElement))
            {
                if (agentElement.ValueKind == JsonValueKind.Null)
                {
                    parsedAgent = null;
                }
                else if (agentElement.ValueKind == JsonValueKind.String)
                {
                    parsedAgent = agentElement.GetString();
                    if (string.IsNullOrWhiteSpace(parsedAgent) || parsedAgent.Length > MaxIdLength)
                    {
                        error = "The selection file agent must be a short local agent ID.";
                        return false;
                    }
                }
                else
                {
                    error = "The selection file agent must be a string or null.";
                    return false;
                }
            }

            DateTimeOffset? exactFrom = null;
            DateTimeOffset? exactTo = null;
            if (kind == "exact")
            {
                if (!TryReadInstant(document.RootElement, "fromUtc", out exactFrom)
                    || !TryReadInstant(document.RootElement, "toExclusiveUtc", out exactTo)
                    || exactTo <= exactFrom)
                {
                    error = "Exact selections need UTC fromUtc and a later toExclusiveUtc.";
                    return false;
                }
            }

            if (!TryReadIdList(document.RootElement, "tools", allowNull: false, out IReadOnlyList<string?> tools, out error)
                || !TryReadIdList(document.RootElement, "hosts", allowNull: true, out IReadOnlyList<string?> hosts, out error)
                || !TryReadIdList(document.RootElement, "models", allowNull: false, out IReadOnlyList<string?> models, out error)
                || !TryReadIdList(document.RootElement, "observedModels", allowNull: true, out IReadOnlyList<string?> observed, out error)
                || !TryReadIdList(document.RootElement, "efforts", allowNull: true, out IReadOnlyList<string?> efforts, out error)
                || !TryReadIdList(document.RootElement, "tiers", allowNull: true, out IReadOnlyList<string?> tiers, out error))
            {
                return false;
            }

            int rangeDays = parsedTo.DayNumber - parsedFrom.DayNumber + 1;
            if (rangeDays is < 1 or > 3650)
            {
                error = "The selection file range must contain from 1 through 3650 days.";
                return false;
            }

            AgentId[] agents = parsedAgent is null ? [] : [new AgentId(parsedAgent)];
            selection = new ParsedReportSelection(
                kind,
                parsedFrom,
                parsedTo,
                parsedAgent,
                exactFrom,
                exactTo,
                new UsageReportSelection
                {
                    Agents = agents,
                    ModelProviders = hosts
                        .Select(host => host is null ? (ModelProviderId?)null : new ModelProviderId(host))
                        .ToArray(),
                    Models = models.Select(model => new ModelId(model!)).ToArray(),
                    ObservedModels = observed
                        .Select(model => model is null ? (ModelId?)null : new ModelId(model))
                        .ToArray(),
                    ReasoningEfforts = efforts,
                    ServiceTiers = tiers,
                });
            return true;
        }
    }

    private static bool TryReadInstant(JsonElement root, string name, out DateTimeOffset? value)
    {
        value = null;
        if (!root.TryGetProperty(name, out JsonElement element) || element.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        if (!DateTimeOffset.TryParse(
                element.GetString(),
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out DateTimeOffset parsed))
        {
            return false;
        }

        value = parsed.ToUniversalTime();
        return true;
    }

    private static bool TryReadIdList(
        JsonElement root,
        string name,
        bool allowNull,
        out IReadOnlyList<string?> values,
        out string error)
    {
        values = [];
        error = string.Empty;
        if (!root.TryGetProperty(name, out JsonElement element))
        {
            return true;
        }

        if (element.ValueKind != JsonValueKind.Array)
        {
            error = "Selection file filters must be JSON arrays.";
            return false;
        }

        var items = new List<string?>();
        foreach (JsonElement item in element.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.Null)
            {
                if (!allowNull)
                {
                    error = "Selection file filter values cannot be null.";
                    return false;
                }

                items.Add(null);
                continue;
            }

            if (item.ValueKind != JsonValueKind.String)
            {
                error = "Selection file filter values must be strings.";
                return false;
            }

            string? text = item.GetString();
            if (string.IsNullOrWhiteSpace(text) || text.Length > MaxIdLength)
            {
                error = "Selection file filter values must be short local identifiers.";
                return false;
            }

            items.Add(text);
        }

        if (items.Count > MaxFilterValues)
        {
            error = "Selection file filters may contain at most 100 values.";
            return false;
        }

        values = items;
        return true;
    }

    private static bool TryReadDate(JsonElement root, string name, out DateOnly date)
    {
        date = default;
        return root.TryGetProperty(name, out JsonElement value)
            && value.ValueKind == JsonValueKind.String
            && DateOnly.TryParseExact(
                value.GetString(),
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out date);
    }

    private static bool HasDuplicateRootProperties(byte[] bytes)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        var reader = new Utf8JsonReader(bytes, new JsonReaderOptions
        {
            AllowTrailingCommas = false,
            CommentHandling = JsonCommentHandling.Disallow,
        });
        try
        {
            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.PropertyName && reader.CurrentDepth == 1)
                {
                    string name = reader.GetString() ?? string.Empty;
                    if (!names.Add(name))
                    {
                        return true;
                    }
                }
            }
        }
        catch (JsonException)
        {
            return false;
        }

        return false;
    }
}
