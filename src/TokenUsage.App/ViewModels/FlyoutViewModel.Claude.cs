using TokenUsage.Core.Session;
using TokenUsage.Core.Usage;
using TokenUsage.Providers.Claude;
using TokenUsage.Runtime.Windows.Claude;

namespace TokenUsage.App.ViewModels;

public partial class FlyoutViewModel
{
    private void ConfigureClaude(ClaudeRateLimitStore? claudeRateLimits, ClaudeSettingsInstaller? claudeSettings,
        string? dataDirectory, AppSessionHost appSessionHost)
    {
        if (claudeRateLimits is not null && dataDirectory is not null)
        {
            ClaudeSettingsInstaller claude = claudeSettings ?? new ClaudeSettingsInstaller();
            var consent = new ClaudeLimitsConsentStore(ClaudeLimitsConsentStore.DefaultPath(dataDirectory));
            GeneralOptions.BindClaudeLimits(true,
                () => consent.ReadOrMigrateAsync(claude.GetStatusLineStatus() == ClaudeIntegrationStatus.Installed)
                    .GetAwaiter().GetResult() == ClaudeLimitsConsentStatus.Enabled,
                enabled =>
                {
                    if (enabled) consent.EnableAsync().GetAwaiter().GetResult();
                    else
                    {
                        consent.DisableAsync().GetAwaiter().GetResult();
                        claudeRateLimits.Delete();
                    }
                });
            GeneralOptions.ClaudeLimitsChanged += OnClaudeLimitsChanged;
            GeneralOptions.BindClaudeActions(
                () => System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("claude")
                    { UseShellExecute = true }),
                async () => { await appSessionHost.RefreshAsync(AppSessionRefreshReason.Manual, forceRefresh: true); },
                async (dryRun, progress, token) =>
                {
                    string database = Path.Combine(dataDirectory, "scanner", "usage.v1.db");
                    var repository = dryRun
                        ? await UsageRepository.OpenReadOnlyAsync(database, token)
                        : await UsageRepository.OpenAsync(database, token);
                    var source = new ClaudeUsageEventSource(TimeZoneInfo.Local.Id,
                        checkpointPath: Path.Combine(dataDirectory, "scanner", "claude-usage.v1.json"));
                    var service = new ClaudeHistoryRecoveryService(repository, source);
                    var numericProgress = new Progress<ClaudeHistoryRecoveryProgress>(value => progress.Report(
                        string.Format(System.Globalization.CultureInfo.CurrentCulture, GetString("ClaudeRecoveryProgressFormat"),
                            value.Added, value.Existing, value.Excluded, value.BytesRead / (1024 * 1024), value.PendingFiles)));
                    var result = await service.RecoverAsync(dryRun, numericProgress, token);
                    var reasons = result.Exclusions;
                    string exclusions = reasons.Total == 0 ? string.Empty : string.Format(System.Globalization.CultureInfo.CurrentCulture,
                        GetString("ClaudeRecoveryExclusionsFormat"), reasons.OutsideRetention, reasons.RetiredDetails,
                        reasons.Tombstones, reasons.ConflictingIdentity, reasons.ConflictingPriceEvidence,
                        reasons.Future, reasons.UnreadableRecords);
                    return string.Format(System.Globalization.CultureInfo.CurrentCulture, GetString("ClaudeRecoveryResultFormat"),
                        GetString(dryRun ? "ClaudeRecoveryPreviewLabel" : result.Completed ? "ClaudeRecoveryComplete" : "ClaudeRecoveryIncomplete"),
                        result.Added, result.Existing, result.Excluded, result.BytesRead / (1024 * 1024),
                        exclusions + " " + (result.Blocker ?? string.Empty));
                });
        }

    }
}
