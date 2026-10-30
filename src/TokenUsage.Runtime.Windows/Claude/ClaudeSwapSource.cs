using System.Text.Json;
using System.Text.Json.Nodes;
using TokenUsage.Core.Providers;
using TokenUsage.Core.Usage;
using TokenUsage.Providers.Claude;
using TokenUsage.Runtime.Windows.Accounts;

namespace TokenUsage.Runtime.Windows.Claude;

internal sealed class ClaudeSwapAccount : SwapAccount
{
    /// <summary>The account's limits in the shape the Claude account usage endpoint returns.</summary>
    public string? LimitsJson { get; init; }
    public DateTimeOffset? ObservedAtUtc { get; init; }
    public override string ToString() => $"Claude account {Number}";
}

/// <summary>
/// Reads Claude account quotas from claude-swap (<c>cswap list --json</c>, schema 1). claude-swap
/// already queries every account it manages, so TokenUsage reads no credential and calls no
/// endpoint for these accounts. Every profile shares <c>~/.claude/projects</c> through a
/// junction, so local tokens stay counted once by the regular Claude reader.
/// </summary>
internal sealed class ClaudeSwapSource : ISwapAccountSource
{
    public ProviderId Provider { get; } = new("claude");

    public string DisplayName => "Claude";

    public bool KeepsGlobalReadingForActiveAccountOnly => true;

    public async Task<IReadOnlyList<SwapAccount>> ReadAsync(IOpaqueKeyDeriver keys, CancellationToken token)
    {
        string executable = SwapCli.FindExecutable("cswap.exe", Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "bin", "cswap.exe"))
            ?? throw new IOException("claude-swap is not installed.");
        return Parse(await SwapCli.ListJsonAsync(executable, "claude-swap", token).ConfigureAwait(false), keys);
    }

    public Task<ProviderOutcome> RefreshAsync(SwapAccount account,
        Func<CancellationToken, Task<IReadOnlyList<SwapAccount>>> reread, RefreshContext context, CancellationToken token)
    {
        var profile = (ClaudeSwapAccount)account;
        if (profile.LimitsJson is null || profile.ObservedAtUtc is not DateTimeOffset observed)
            return Task.FromResult<ProviderOutcome>(new ProviderOutcome.TransientFailure(new ProviderError(
                ProviderErrorCode.TransientSourceFailure, "claude-swap has no usage reading for this account."), context.LastGood));
        using JsonDocument limits = JsonDocument.Parse(profile.LimitsJson);
        ProviderSnapshot snapshot = ClaudeUsageSnapshotMapper.Map(limits.RootElement, observed, TimeZoneInfo.Local.Id);
        return Task.FromResult<ProviderOutcome>(new ProviderOutcome.Success(snapshot));
    }

    internal static IReadOnlyList<ClaudeSwapAccount> Parse(byte[] json, IOpaqueKeyDeriver keys)
    {
        try
        {
            if (json.Length > SwapCli.MaximumOutputBytes) throw new JsonException();
            using JsonDocument document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 12 });
            JsonElement root = document.RootElement;
            if (root.GetProperty("schemaVersion").GetInt32() != 1) throw new JsonException();
            JsonElement list = root.GetProperty("accounts");
            if (list.GetArrayLength() > 64) throw new JsonException();
            var accounts = new List<ClaudeSwapAccount>();
            foreach (JsonElement item in list.EnumerateArray())
            {
                int number = item.GetProperty("number").GetInt32();
                if (number < 1) throw new JsonException();
                string email = Text(item, "email", 320) ?? throw new JsonException();
                string organization = Text(item, "organizationUuid", 128) ?? "";
                string? alias = Text(item, "alias", 80);
                if (alias is not null && (alias.Contains('@') || alias.Contains('\\') || alias.Contains('/'))) alias = null;
                bool active = item.GetProperty("active").GetBoolean();
                string usageStatus = Text(item, "usageStatus", 64) ?? throw new JsonException();
                string owner = JsonSerializer.Serialize(new[] { email.ToLowerInvariant(), organization });
                ProviderAccountStatus status = usageStatus switch
                {
                    "ok" or "unavailable" or "keychain_unavailable" => ProviderAccountStatus.Available,
                    "token_expired" or "relogin_required" or "no_credentials" => ProviderAccountStatus.LoginRequired,
                    "foreign_credential" => ProviderAccountStatus.IdentityChanged,
                    _ => ProviderAccountStatus.Unavailable,
                };
                (string? limits, DateTimeOffset? observed) = ReadUsage(item, "usage", "usageFetchedAt");
                if (limits is null) (limits, observed) = ReadUsage(item, "lastGoodUsage", "lastGoodFetchedAt");
                accounts.Add(new ClaudeSwapAccount
                {
                    InstanceKey = new ProviderInstanceKey(new ProviderId("claude"), keys.Derive("quota-account", "claude", owner).Value),
                    Number = number, Alias = alias, IsActive = active, Status = status,
                    Fingerprint = keys.Derive("quota-discovery", "claude",
                        JsonSerializer.Serialize(new { owner, active, usageStatus })).Value,
                    LimitsJson = limits, ObservedAtUtc = observed,
                });
            }

            if (accounts.Select(item => item.Number).Distinct().Count() != accounts.Count
                || accounts.Select(item => item.InstanceKey).Distinct().Count() != accounts.Count
                || accounts.Count(item => item.IsActive) > 1)
                throw new JsonException();
            return accounts;
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException
            or KeyNotFoundException or ArgumentException or OverflowException or FormatException)
        {
            throw new IOException("claude-swap returned invalid, ambiguous or unsupported account metadata.");
        }
    }

    /// <summary>Rewrites claude-swap's usage windows as the endpoint's <c>limits</c> list.</summary>
    private static (string? Limits, DateTimeOffset? ObservedAtUtc) ReadUsage(JsonElement item, string usageName, string fetchedName)
    {
        if (!item.TryGetProperty(usageName, out JsonElement usage) || usage.ValueKind != JsonValueKind.Object
            || !item.TryGetProperty(fetchedName, out JsonElement fetched) || fetched.ValueKind != JsonValueKind.String)
            return (null, null);
        var limits = new JsonArray();
        void Add(JsonElement window, string kind, JsonObject? scope)
        {
            var limit = new JsonObject { ["kind"] = kind, ["percent"] = window.GetProperty("pct").GetDecimal() };
            if (window.TryGetProperty("resetsAt", out JsonElement reset) && reset.ValueKind == JsonValueKind.String)
                limit["resets_at"] = reset.GetString();
            if (scope is not null) limit["scope"] = scope;
            limits.Add(limit);
        }
        if (usage.TryGetProperty("fiveHour", out JsonElement five) && five.ValueKind == JsonValueKind.Object) Add(five, "session", null);
        if (usage.TryGetProperty("sevenDay", out JsonElement week) && week.ValueKind == JsonValueKind.Object) Add(week, "weekly_all", null);
        if (usage.TryGetProperty("scoped", out JsonElement scoped) && scoped.ValueKind == JsonValueKind.Array)
            foreach (JsonElement window in scoped.EnumerateArray())
                Add(window, "weekly_scoped", new JsonObject
                {
                    ["model"] = new JsonObject { ["display_name"] = window.GetProperty("name").GetString() },
                });
        if (limits.Count == 0) return (null, null);
        return (new JsonObject { ["limits"] = limits }.ToJsonString(),
            DateTimeOffset.Parse(fetched.GetString()!, System.Globalization.CultureInfo.InvariantCulture).ToUniversalTime());
    }

    private static string? Text(JsonElement item, string name, int maximum)
    {
        if (!item.TryGetProperty(name, out JsonElement value) || value.ValueKind == JsonValueKind.Null) return null;
        string text = value.GetString() ?? throw new JsonException();
        if (text.Length > maximum || text.Any(char.IsControl)) throw new JsonException();
        return string.IsNullOrWhiteSpace(text) ? null : text;
    }
}
