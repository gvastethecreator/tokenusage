using System.Globalization;
using TokenUsage.App.ViewModels.Dashboard;
using TokenUsage.Core.Providers;
using TokenUsage.Providers.Codex;

namespace TokenUsage.App.ViewModels;

/// <summary>One switcher profile's quota: a code-swap Codex profile or a claude-swap Claude account.</summary>
public sealed record AccountQuota(string ProviderId, string Key, string Title, string Plan, string Status,
    string Observed, IReadOnlyList<QuotaWindow> Windows, bool NeedsAttention)
{
    public string ProviderName => ProviderId == "claude" ? "Claude" : "Codex";
    public string AutomationId => "AccountQuota." + Key;
    public string AutomationName => $"{ProviderName}. {Title}. {Plan}. {Status}. {Observed}. {string.Join(". ", Windows.Select(window => window.AutomationName))}";
    public string StatusDetail => string.IsNullOrEmpty(Observed) ? Status : $"{Status} · {Observed}";
    public bool HasNoWindows => Windows.Count == 0;

    public bool HasSameContent(AccountQuota other) => ProviderId == other.ProviderId && Key == other.Key
        && Title == other.Title && Plan == other.Plan && Status == other.Status && Observed == other.Observed
        && NeedsAttention == other.NeedsAttention && Windows.SequenceEqual(other.Windows);

    public static AccountQuota Create(ProviderAccountInfo account, ProviderSnapshot? snapshot,
        ProviderOutcome? outcome, TimeProvider clock, Func<string, string> text)
    {
        string providerId = account.InstanceKey.ProviderId.Value;
        bool isClaude = providerId == "claude";
        string providerName = isClaude ? "Claude" : "Codex";
        string name = account.Alias ?? string.Format(CultureInfo.CurrentCulture, text("CodexAccountProfileFormat"), account.Number);
        string title = name + (account.IsActive ? " · " + text("CodexAccountActive") : "");
        bool stale = snapshot is not null && (isClaude
            ? SnapshotFreshness.IsStale(snapshot, clock, ClaudeDashboardProjector.StaleAfter)
            : SnapshotFreshness.IsStale(snapshot, clock));
        bool failed = outcome is not null && outcome is not (ProviderOutcome.Success or ProviderOutcome.PartialSuccess);
        bool needsAttention = account.Status != ProviderAccountStatus.Available || failed || snapshot is null || stale;
        string status = text(account.Status switch
        {
            ProviderAccountStatus.LoginRequired => "CodexAccountLoginRequired",
            ProviderAccountStatus.IdentityChanged => "CodexAccountIdentityChanged",
            ProviderAccountStatus.Unavailable => "CodexAccountUnavailable",
            _ when failed => "CodexAccountUpdateFailed",
            _ when snapshot is null => "CodexAccountNoReading",
            _ when stale => "CodexAccountStale",
            _ => "CodexAccountCurrent",
        });
        ProviderCard? card = snapshot is null ? null
            : isClaude ? ClaudeDashboardProjector.Create(snapshot, clock, text)
            : CodexDashboardProjector.Create(snapshot, clock, text).Providers.Single();
        decimal? banked = snapshot?.Metrics.OfType<ScalarMetricSnapshot>()
            .FirstOrDefault(metric => metric.Id.Value == CodexRateLimitsSnapshotMapper.ResetCreditsAvailableMetricId)?.Value;
        string bankedText = banked is null ? "" : banked == 1m ? text("CodexAccountBankedResetOne")
            : string.Format(CultureInfo.CurrentCulture, text("CodexAccountBankedResetsFormat"), banked);
        QuotaWindow[] windows = card is null ? [] : card.Windows.Concat(card.SecondaryWindowItems)
            .Select((window, index) => window with { AutomationName = $"{providerName} · {title}. {window.AutomationName}. {(index == 0 ? bankedText : "")}",
                ProviderId = providerId, ProfileLabel = title, BankedResetsText = index == 0 ? bankedText : "",
                PaceAutomationId = $"Account.{account.InstanceKey.AccountKey}.{window.LayoutMetricId}", UsedText = "" }).ToArray();
        return new(providerId, account.InstanceKey.AccountKey!, title, card?.PlanLabel ?? "", status,
            card?.ObservedValue ?? "", windows, needsAttention);
    }
}
