using TokenUsage.Core.Alerts;
using TokenUsage.Core.Cache;
using TokenUsage.Core.Providers;
using TokenUsage.Core.Usage;
using TokenUsage.Platform.Windows.Credentials;
using TokenUsage.Providers.Codex;

namespace TokenUsage.Runtime.Windows.Codex;

public sealed class CodexAccountService : IProviderAccountService
{
    private readonly ProviderAccountSelectionStore _selection;
    private readonly TimeProvider _clock;
    private readonly AlertSettingsStore _alertSettings;
    private readonly ProviderOperationGate _gate = new();
    private readonly Dictionary<string, IProviderRuntime> _runtimes = new(StringComparer.Ordinal);
    private ProviderAccountInfo[] _accounts = [];
    private readonly Func<bool, IOpaqueKeyDeriver> _getKeys;

    public CodexAccountService(string localState, TimeProvider clock)
    {
        _clock = clock;
        _selection = new ProviderAccountSelectionStore(localState, new ProviderId("codex"), clock);
        _alertSettings = new AlertSettingsStore(Path.Combine(localState, AlertSettingsStore.DefaultFileName), clock);
        _getKeys = activated => new WindowsAccountSecretStore(new WindowsCredentialVault()).GetDeriver(activated);
    }

    public bool WasActivated => _selection.WasActivated;
    public bool DiscoveryUnavailable { get; private set; }
    public IReadOnlyList<ProviderAccountInfo> Accounts => _accounts;

    public async Task<IReadOnlyList<ProviderAccountInfo>> DiscoverAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyList<ProviderAccountSelection> selected = await _selection.LoadAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            IReadOnlyList<CodexSwapAccount> discovered = await ReadInventoryAsync(cancellationToken).ConfigureAwait(false);
            _accounts = discovered.Select(account => new ProviderAccountInfo(account.InstanceKey,
                account.Number, account.Alias, account.IsActive,
                selected.Any(item => item.AccountKey == account.InstanceKey.AccountKey), account.Status))
                .Concat(selected.Where(item => discovered.All(account => account.InstanceKey.AccountKey != item.AccountKey))
                    .Select(item => Missing(item))).OrderBy(item => item.Number).ToArray();
            DiscoveryUnavailable = false;
        }
        catch (Exception exception) when (IsSourceFailure(exception))
        {
            _accounts = selected.Select(item => Missing(item)).ToArray();
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
        if (!WasActivated) return registrations;
        var resolved = registrations.Where(item => item.InstanceKey.ProviderId.Value != "codex").ToList();
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

    private Task<IReadOnlyList<CodexSwapAccount>> ReadInventoryAsync(CancellationToken token) =>
        new CodexSwapDiscovery(_getKeys(WasActivated)).ReadAsync(token);

    private static ProviderAccountInfo Missing(ProviderAccountSelection item) => new(
        new ProviderInstanceKey(new ProviderId("codex"), item.AccountKey), item.Number, item.Alias,
        false, true, ProviderAccountStatus.Unavailable);

    private static bool IsSourceFailure(Exception exception) => exception is IOException
        or UnauthorizedAccessException or InvalidOperationException or ArgumentException or TimeoutException
        or System.Runtime.InteropServices.COMException or System.Security.SecurityException;

    private sealed class AccountRuntime(CodexAccountService service, ProviderInstanceKey identity, bool requireSelection) : IProviderRuntime
    {
        public ProviderDescriptor Descriptor { get; } = new(new ProviderId("codex"), "Codex");

        public async Task<ProviderOutcome> RefreshAsync(RefreshContext context, CancellationToken cancellationToken)
        {
            try
            {
                if (requireSelection && !(await service._selection.LoadAsync(cancellationToken).ConfigureAwait(false))
                    .Any(item => item.AccountKey == identity.AccountKey)) return Failure(context);
                IReadOnlyList<CodexSwapAccount> before = await service.ReadInventoryAsync(cancellationToken).ConfigureAwait(false);
                CodexSwapAccount? account = before.SingleOrDefault(item => item.InstanceKey == identity);
                if (account is null || account.Status != ProviderAccountStatus.Available) return Failure(context);

                var options = new CodexClientOptions("tokenusage", "0.1.0", "TokenUsage",
                    expectedHome: account.Home, expectedEmail: account.Email);
                var runtime = new CodexProviderRuntime(new CodexAppServerQuotaClientFactory(service._clock, options, account.Home),
                    includeUsage: false);
                ProviderOutcome outcome = await runtime.RefreshAsync(context, cancellationToken).ConfigureAwait(false);
                if (outcome is not ProviderOutcome.Success success) return outcome;
                IReadOnlyList<CodexSwapAccount> after = await service.ReadInventoryAsync(cancellationToken).ConfigureAwait(false);
                CodexSwapAccount? verified = after.SingleOrDefault(item => item.InstanceKey == identity);
                if (verified?.Fingerprint != account.Fingerprint) return Failure(context);
                cancellationToken.ThrowIfCancellationRequested();
                return new ProviderOutcome.Success(success.Snapshot.ForAccount(identity.AccountKey!));
            }
            catch (Exception exception) when (IsSourceFailure(exception)) { return Failure(context); }
        }

        private static ProviderOutcome.TransientFailure Failure(RefreshContext context) => new(
            new ProviderError(ProviderErrorCode.TransientSourceFailure,
                "The account is unavailable or its identity could not be verified."), context.LastGood);
    }
}
