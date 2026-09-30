using System.Globalization;
using TokenUsage.App.ViewModels.Dashboard;
using TokenUsage.Core.Providers;
using TokenUsage.Providers.Codex;

namespace TokenUsage.App.ViewModels;

public sealed record CodexAccountQuota(string Key, string Title, string Plan, string Status,
    string Observed, IReadOnlyList<QuotaWindow> Windows, bool NeedsAttention)
{
    public string AutomationId => "CodexAccountQuota." + Key;
    public string AutomationName => $"Codex. {Title}. {Plan}. {Status}. {Observed}. {string.Join(". ", Windows.Select(window => window.AutomationName))}";
    public string StatusDetail => string.IsNullOrEmpty(Observed) ? Status : $"{Status} · {Observed}";
    public bool HasNoWindows => Windows.Count == 0;

    public bool HasSameContent(CodexAccountQuota other) => Key == other.Key && Title == other.Title
        && Plan == other.Plan && Status == other.Status && Observed == other.Observed
        && NeedsAttention == other.NeedsAttention && Windows.SequenceEqual(other.Windows);

    public static CodexAccountQuota Create(ProviderAccountInfo account, ProviderSnapshot? snapshot,
        ProviderOutcome? outcome, TimeProvider clock, Func<string, string> text)
    {
        string name = account.Alias ?? string.Format(CultureInfo.CurrentCulture, text("CodexAccountProfileFormat"), account.Number);
        string title = name + (account.IsActive ? " · " + text("CodexAccountActive") : "");
        bool failed = outcome is not null && outcome is not (ProviderOutcome.Success or ProviderOutcome.PartialSuccess);
        bool needsAttention = account.Status != ProviderAccountStatus.Available || failed
            || snapshot is null || SnapshotFreshness.IsStale(snapshot, clock);
        string status = text(account.Status switch
        {
            ProviderAccountStatus.LoginRequired => "CodexAccountLoginRequired",
            ProviderAccountStatus.IdentityChanged => "CodexAccountIdentityChanged",
            ProviderAccountStatus.Unavailable => "CodexAccountUnavailable",
            _ when failed => "CodexAccountUpdateFailed",
            _ when snapshot is null => "CodexAccountNoReading",
            _ when SnapshotFreshness.IsStale(snapshot, clock) => "CodexAccountStale",
            _ => "CodexAccountCurrent",
        });
        ProviderCard? card = snapshot is null ? null : CodexDashboardProjector.Create(snapshot, clock, text).Providers.Single();
        decimal? banked = snapshot?.Metrics.OfType<ScalarMetricSnapshot>()
            .FirstOrDefault(metric => metric.Id.Value == CodexRateLimitsSnapshotMapper.ResetCreditsAvailableMetricId)?.Value;
        string bankedText = banked is null ? "" : banked == 1m ? text("CodexAccountBankedResetOne")
            : string.Format(CultureInfo.CurrentCulture, text("CodexAccountBankedResetsFormat"), banked);
        QuotaWindow[] windows = card is null ? [] : card.Windows.Concat(card.SecondaryWindowItems)
            .Select((window, index) => window with { AutomationName = $"Codex · {title}. {window.AutomationName}. {(index == 0 ? bankedText : "")}",
                ProviderId = "codex", ProfileLabel = title, BankedResetsText = index == 0 ? bankedText : "",
                PaceAutomationId = $"CodexAccount.{account.InstanceKey.AccountKey}.{window.LayoutMetricId}", UsedText = "" }).ToArray();
        return new(account.InstanceKey.AccountKey!, title, card?.PlanLabel ?? "", status,
            card?.ObservedValue ?? "", windows, needsAttention);
    }
}
