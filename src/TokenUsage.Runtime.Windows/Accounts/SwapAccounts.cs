using System.Diagnostics;
using TokenUsage.Core.Alerts;
using TokenUsage.Core.Cache;
using TokenUsage.Core.Providers;
using TokenUsage.Core.Usage;
using TokenUsage.Platform.Windows.Credentials;
using TokenUsage.Runtime.Windows.Codex;

namespace TokenUsage.Runtime.Windows.Accounts;

/// <summary>
/// One profile listed by an account switcher (code-swap for Codex, claude-swap for Claude).
/// Sensitive discovery fields stay in memory and never reach a snapshot or diagnostic.
/// </summary>
internal abstract class SwapAccount
{
    public required ProviderInstanceKey InstanceKey { get; init; }
    public required int Number { get; init; }
    public string? Alias { get; init; }
    public bool IsActive { get; init; }
    public ProviderAccountStatus Status { get; init; }
    public required string Fingerprint { get; init; }
}

/// <summary>Reads one switcher's inventory and turns one of its profiles into a quota reading.</summary>
internal interface ISwapAccountSource
{
    ProviderId Provider { get; }

    string DisplayName { get; }

    /// <summary>The CLI composition keeps the provider's single global reading instead.</summary>
    bool KeepsGlobalReadingForActiveAccountOnly { get; }

    Task<IReadOnlyList<SwapAccount>> ReadAsync(IOpaqueKeyDeriver keys, CancellationToken token);

    Task<ProviderOutcome> RefreshAsync(SwapAccount account, Func<CancellationToken, Task<IReadOnlyList<SwapAccount>>> reread,
        RefreshContext context, CancellationToken token);
}

/// <summary>
/// Lets the user select switcher profiles and registers one quota reading per selected profile.
/// Local tokens and costs keep their shared accounting; profiles add quota observations only.
/// </summary>
internal sealed class SwapAccountService
{
    private readonly ISwapAccountSource _source;
    private readonly ProviderAccountSelectionStore _selection;
    private readonly TimeProvider _clock;
    private readonly AlertSettingsStore _alertSettings;
    private readonly ProviderOperationGate _gate = new();
    private readonly Dictionary<string, IProviderRuntime> _runtimes = new(StringComparer.Ordinal);
    private readonly Func<bool, IOpaqueKeyDeriver> _getKeys;
    private ProviderAccountInfo[] _accounts = [];

    public SwapAccountService(string localState, TimeProvider clock, ISwapAccountSource source)
    {
        _source = source;
        _clock = clock;
        _selection = new ProviderAccountSelectionStore(localState, source.Provider, clock);
        _alertSettings = new AlertSettingsStore(Path.Combine(localState, AlertSettingsStore.DefaultFileName), clock);
        _getKeys = activated => new WindowsAccountSecretStore(new WindowsCredentialVault()).GetDeriver(activated);
    }

    public ProviderId Provider => _source.Provider;
    public bool WasActivated => _selection.WasActivated;
    public bool DiscoveryUnavailable { get; private set; }
    public IReadOnlyList<ProviderAccountInfo> Accounts => _accounts;

    public async Task<IReadOnlyList<ProviderAccountInfo>> DiscoverAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyList<ProviderAccountSelection> selected = await _selection.LoadAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            IReadOnlyList<SwapAccount> discovered = await ReadInventoryAsync(cancellationToken).ConfigureAwait(false);
            _accounts = discovered.Select(account => new ProviderAccountInfo(account.InstanceKey,
                account.Number, account.Alias, account.IsActive,
                selected.Any(item => item.AccountKey == account.InstanceKey.AccountKey), account.Status))
                .Concat(selected.Where(item => discovered.All(account => account.InstanceKey.AccountKey != item.AccountKey))
                    .Select(Missing)).OrderBy(item => item.Number).ToArray();
            DiscoveryUnavailable = false;
        }
        catch (Exception exception) when (IsSourceFailure(exception))
        {
            _accounts = selected.Select(Missing).ToArray();
            DiscoveryUnavailable = true;
        }
        return _accounts;
    }

    public async Task SaveSelectionAsync(IReadOnlyCollection<string> accountKeys, CancellationToken cancellationToken = default)
    {
        await using IAsyncDisposable lease = await _gate.EnterAsync(cancellationToken).ConfigureAwait(false);
        IReadOnlyList<ProviderAccountSelection> previous = await _selection.LoadAsync(cancellationToken).ConfigureAwait(false);
        IReadOnlyList<ProviderAccountInfo> discovered = await DiscoverAsync(cancellationToken).ConfigureAwait(false);
        ProviderAccountSelection[] selection = accountKeys.Select(key =>
        {
            ProviderAccountInfo? account = discovered.FirstOrDefault(item => item.InstanceKey.AccountKey == key);
            if (account is null || (account.Status != ProviderAccountStatus.Available
                && previous.All(item => item.AccountKey != key)))
                throw new IOException("The account selection contains an unavailable or changed account.");
            return new ProviderAccountSelection(key, account.Number, account.Alias);
        }).ToArray();
        await _selection.SaveAsync(selection, cancellationToken).ConfigureAwait(false);
        _accounts = _accounts.Select(item => item with { IsSelected = accountKeys.Contains(item.InstanceKey.AccountKey!) }).ToArray();
    }

    public async Task<IReadOnlyList<ProviderRefreshRegistration>> ResolveAsync(
        IReadOnlyList<ProviderRefreshRegistration> registrations, CancellationToken token, bool activeAccountOnly = false)
    {
        if (!WasActivated || (activeAccountOnly && _source.KeepsGlobalReadingForActiveAccountOnly)) return registrations;
        var resolved = registrations.Where(item => item.InstanceKey.ProviderId != Provider).ToList();
        try { await DiscoverAsync(token).ConfigureAwait(false); }
        catch (Exception exception) when (IsSourceFailure(exception)) { DiscoveryUnavailable = true; }
        foreach (ProviderAccountInfo account in _accounts.Where(item => activeAccountOnly ? item.IsActive : item.IsSelected))
        {
            string key = account.InstanceKey.AccountKey!;
            string directory = _selection.AccountDirectory(key);
            if (!_runtimes.TryGetValue(key, out IProviderRuntime? runtime))
            {
                runtime = new ResilientProviderRuntime(new AccountRuntime(this, account.InstanceKey, !activeAccountOnly));
                _runtimes.Add(key, runtime);
            }
            var history = new QuotaResetHistoryStore(Path.Combine(directory, QuotaResetHistoryStore.DefaultFileName),
                _clock, account.InstanceKey);
            resolved.Add(new ProviderRefreshRegistration(runtime,
                new SnapshotStore(Path.Combine(directory, SnapshotStore.DefaultFileName), _clock, account.InstanceKey),
                _gate, new AlertHost(new AlertDecisionStore(Path.Combine(directory, AlertDecisionStore.DefaultFileName),
                    _clock, account.InstanceKey), _alertSettings),
                async (snapshot, cancellation) =>
                {
                    try { await history.ObserveAsync(snapshot, cancellation).ConfigureAwait(false); }
                    catch (Microsoft.Data.Sqlite.SqliteException)
                    { throw new IOException("The account quota history could not be saved."); }
                }, account));
        }
        return resolved;
    }

    public IReadOnlyList<ProviderAccountHistory> SelectedHistories() => WasActivated
        ? _accounts.Where(item => item.IsSelected).Select(item => new ProviderAccountHistory(item, Path.Combine(
            _selection.AccountDirectory(item.InstanceKey.AccountKey!), QuotaResetHistoryStore.DefaultFileName))).ToArray()
        : [];

    private Task<IReadOnlyList<SwapAccount>> ReadInventoryAsync(CancellationToken token) =>
        _source.ReadAsync(_getKeys(WasActivated), token);

    private ProviderAccountInfo Missing(ProviderAccountSelection item) => new(
        new ProviderInstanceKey(Provider, item.AccountKey), item.Number, item.Alias,
        false, true, ProviderAccountStatus.Unavailable);

    internal static bool IsSourceFailure(Exception exception) => exception is IOException
        or UnauthorizedAccessException or InvalidOperationException or ArgumentException or TimeoutException
        or System.Runtime.InteropServices.COMException or System.Security.SecurityException;

    private sealed class AccountRuntime(SwapAccountService service, ProviderInstanceKey identity, bool requireSelection) : IProviderRuntime
    {
        public ProviderDescriptor Descriptor { get; } = new(identity.ProviderId, service._source.DisplayName);

        public async Task<ProviderOutcome> RefreshAsync(RefreshContext context, CancellationToken cancellationToken)
        {
            try
            {
                if (requireSelection && !(await service._selection.LoadAsync(cancellationToken).ConfigureAwait(false))
                    .Any(item => item.AccountKey == identity.AccountKey)) return Failure(context);
                IReadOnlyList<SwapAccount> before = await service.ReadInventoryAsync(cancellationToken).ConfigureAwait(false);
                SwapAccount? account = before.SingleOrDefault(item => item.InstanceKey == identity);
                if (account is null || account.Status != ProviderAccountStatus.Available) return Failure(context);
                ProviderOutcome outcome = await service._source.RefreshAsync(account, service.ReadInventoryAsync,
                    context, cancellationToken).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                return outcome is ProviderOutcome.Success success
                    ? new ProviderOutcome.Success(success.Snapshot.ForAccount(identity.AccountKey!))
                    : outcome;
            }
            catch (Exception exception) when (IsSourceFailure(exception)) { return Failure(context); }
        }

        private static ProviderOutcome.TransientFailure Failure(RefreshContext context) => new(
            new ProviderError(ProviderErrorCode.TransientSourceFailure,
                "The account is unavailable or its identity could not be verified."), context.LastGood);
    }
}

/// <summary>The Codex (code-swap) and Claude (claude-swap) account lists as one selection surface.</summary>
public sealed class ProviderAccountServices : IProviderAccountService
{
    private readonly SwapAccountService[] _services;

    internal ProviderAccountServices(params SwapAccountService[] services) => _services = services;

    public bool WasActivated => _services.Any(service => service.WasActivated);

    public bool IsActivated(string providerId) =>
        _services.Any(service => service.WasActivated && service.Provider.Value == providerId);

    public IReadOnlyList<ProviderAccountInfo> Accounts => _services.SelectMany(service => service.Accounts).ToArray();

    public bool DiscoveryUnavailable => _services.All(service => service.DiscoveryUnavailable);

    public async Task<IReadOnlyList<ProviderAccountInfo>> DiscoverAsync(CancellationToken cancellationToken = default)
    {
        foreach (SwapAccountService service in _services)
            await service.DiscoverAsync(cancellationToken).ConfigureAwait(false);
        return Accounts;
    }

    public async Task SaveSelectionAsync(IReadOnlyCollection<string> accountKeys, CancellationToken cancellationToken = default)
    {
        foreach (SwapAccountService service in _services)
        {
            string[] owned = accountKeys.Where(key => service.Accounts.Any(item => item.InstanceKey.AccountKey == key)).ToArray();
            // A provider the user never selected stays on its single global reading.
            if (owned.Length > 0 || service.WasActivated)
                await service.SaveSelectionAsync(owned, cancellationToken).ConfigureAwait(false);
        }
    }

    public IReadOnlyList<ProviderAccountHistory> SelectedHistories() =>
        _services.SelectMany(service => service.SelectedHistories()).ToArray();

    internal async Task<IReadOnlyList<ProviderRefreshRegistration>> ResolveAsync(
        IReadOnlyList<ProviderRefreshRegistration> registrations, bool activeAccountOnly, CancellationToken token)
    {
        foreach (SwapAccountService service in _services)
            registrations = await service.ResolveAsync(registrations, token, activeAccountOnly).ConfigureAwait(false);
        return registrations;
    }
}

/// <summary>Runs a switcher's <c>list --json</c> with a bounded output and timeout.</summary>
internal static class SwapCli
{
    internal const int MaximumOutputBytes = 1024 * 1024;

    public static string? FindExecutable(string fileName, params string[] fallbacks)
    {
        foreach (string entry in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
        {
            string directory = entry.Trim().Trim('"');
            if (!Path.IsPathFullyQualified(directory)) continue;
            string candidate = Path.Combine(directory, fileName);
            if (File.Exists(candidate)) return candidate;
        }
        return fallbacks.FirstOrDefault(File.Exists);
    }

    public static async Task<byte[]> ListJsonAsync(string executable, string toolName, CancellationToken token)
    {
        var start = new ProcessStartInfo(executable)
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true,
            WorkingDirectory = Path.GetDirectoryName(executable)!,
        };
        start.ArgumentList.Add("list");
        start.ArgumentList.Add("--json");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        using var process = new Process { StartInfo = start };
        bool started = false;
        try
        {
            if (!process.Start()) throw new IOException($"{toolName} could not be started.");
            started = true;
            var budget = new System.Runtime.CompilerServices.StrongBox<int>();
            Task<byte[]> output = ReadBoundedAsync(process.StandardOutput.BaseStream, budget, toolName, timeout.Token);
            Task<byte[]> errors = ReadBoundedAsync(process.StandardError.BaseStream, budget, toolName, timeout.Token);
            await Task.WhenAll(output, errors, process.WaitForExitAsync(timeout.Token)).ConfigureAwait(false);
            if (process.ExitCode != 0) throw new IOException($"{toolName} discovery failed.");
            return await output.ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        {
            throw new IOException($"{toolName} discovery timed out.");
        }
        catch (System.ComponentModel.Win32Exception)
        {
            throw new IOException($"{toolName} could not be started.");
        }
        finally
        {
            if (started && !process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                using var shutdown = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                try { await process.WaitForExitAsync(shutdown.Token).ConfigureAwait(false); }
                catch (OperationCanceledException) { }
            }
        }
    }

    private static async Task<byte[]> ReadBoundedAsync(Stream stream,
        System.Runtime.CompilerServices.StrongBox<int> budget, string toolName, CancellationToken token)
    {
        using var result = new MemoryStream();
        var buffer = new byte[8192];
        int count;
        while ((count = await stream.ReadAsync(buffer, token).ConfigureAwait(false)) > 0)
        {
            if (Interlocked.Add(ref budget.Value, count) > MaximumOutputBytes)
                throw new IOException($"{toolName} output exceeded its limit.");
            result.Write(buffer, 0, count);
        }
        return result.ToArray();
    }
}
