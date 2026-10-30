using System.Text.Json;
using TokenUsage.Core.Providers;
using TokenUsage.Core.Usage;
using TokenUsage.Runtime.Windows.Accounts;

namespace TokenUsage.Runtime.Windows.Codex;

internal sealed class CodexSwapAccount : SwapAccount
{
    public required string Home { get; init; }
    public required string Email { get; init; }
    public override string ToString() => $"Codex profile {Number}";
}

internal sealed class CodexSwapDiscovery(IOpaqueKeyDeriver keys)
{
    internal const int MaximumOutputBytes = SwapCli.MaximumOutputBytes;

    public async Task<IReadOnlyList<CodexSwapAccount>> ReadAsync(CancellationToken token)
    {
        string? registry = Environment.GetEnvironmentVariable("XSWAP_HOME");
        if (registry is not null && (!Path.IsPathFullyQualified(registry) || !Directory.Exists(registry)))
            throw new IOException("The configured code-swap directory is unavailable.");
        string executable = SwapCli.FindExecutable("xswap.exe", Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "codex-swap", "xswap.exe"))
            ?? throw new IOException("code-swap is not installed.");
        return Parse(await SwapCli.ListJsonAsync(executable, "code-swap", token).ConfigureAwait(false), keys);
    }

    internal static IReadOnlyList<CodexSwapAccount> Parse(byte[] json, IOpaqueKeyDeriver keys)
    {
        try
        {
            if (json.Length > MaximumOutputBytes) throw new JsonException();
            using JsonDocument document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 12 });
            JsonElement root = document.RootElement;
            if (root.GetProperty("schemaVersion").GetInt32() != 1) throw new JsonException();
            JsonElement list = root.GetProperty("accounts");
            if (list.GetArrayLength() > 64) throw new JsonException();
            var accounts = new List<CodexSwapAccount>();
            foreach (JsonElement item in list.EnumerateArray())
            {
                int number = item.GetProperty("number").GetInt32();
                string accountId = RequireText(item, "accountId", 512);
                string? userId = OptionalText(item, "userId", 512);
                string email = RequireText(item, "email", 320);
                string home = NormalizeHome(RequireText(item, "home", 32767));
                string? alias = OptionalText(item, "alias", 80);
                if (alias is not null && (alias.Contains('@') || alias.Contains('\\') || alias.Contains('/')
                    || alias.Contains(accountId, StringComparison.OrdinalIgnoreCase)
                    || (userId is not null && alias.Contains(userId, StringComparison.OrdinalIgnoreCase)))) alias = null;
                string login = RequireText(item, "loginStatus", 64);
                bool active = item.GetProperty("isDefault").GetBoolean();
                bool enabled = item.GetProperty("enabled").GetBoolean();
                string owner = JsonSerializer.Serialize(new[] { accountId, userId });
                string key = keys.Derive("quota-account", "codex", owner).Value;
                if (number < 1) throw new JsonException();
                ProviderAccountStatus status = login switch
                {
                    "present" => ProviderAccountStatus.Available,
                    "login_required" or "invalid_credentials" => ProviderAccountStatus.LoginRequired,
                    "identity_changed" => ProviderAccountStatus.IdentityChanged,
                    _ => throw new JsonException(),
                };
                if (!enabled || !Directory.Exists(home)) status = ProviderAccountStatus.Unavailable;
                accounts.Add(new CodexSwapAccount
                {
                    InstanceKey = new ProviderInstanceKey(new ProviderId("codex"), key),
                    Number = number, Alias = alias, Home = home, Email = email, IsActive = active, Status = status,
                    Fingerprint = keys.Derive("quota-discovery", "codex",
                        JsonSerializer.Serialize(new { owner, home, email, active, login, enabled })).Value,
                });
            }

            if (accounts.Select(item => item.Number).Distinct().Count() != accounts.Count
                || accounts.Select(item => item.InstanceKey).Distinct().Count() != accounts.Count
                || accounts.Select(item => item.Home).Distinct(StringComparer.OrdinalIgnoreCase).Count() != accounts.Count
                || accounts.Select(item => item.Email).Distinct(StringComparer.OrdinalIgnoreCase).Count() != accounts.Count
                || accounts.Count(item => item.IsActive) > 1)
                throw new JsonException();
            return accounts;
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException
            or KeyNotFoundException or ArgumentException or OverflowException)
        {
            throw new IOException("code-swap returned invalid, ambiguous or unsupported account metadata.");
        }
    }

    private static string RequireText(JsonElement item, string name, int maximum) =>
        OptionalText(item, name, maximum) ?? throw new JsonException();

    private static string? OptionalText(JsonElement item, string name, int maximum)
    {
        JsonElement value = item.GetProperty(name);
        if (value.ValueKind == JsonValueKind.Null) return null;
        string text = value.GetString() ?? throw new JsonException();
        if (string.IsNullOrWhiteSpace(text) || text.Length > maximum || text.Any(char.IsControl)) throw new JsonException();
        return text;
    }

    private static string NormalizeHome(string home)
    {
        if (home.StartsWith(@"\\?\", StringComparison.Ordinal)) home = home[4..];
        if (!Path.IsPathFullyQualified(home) || home.StartsWith(@"\\", StringComparison.Ordinal)) throw new JsonException();
        return Path.GetFullPath(home).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }
}
