using TokenUsage.Core.Layout;

namespace TokenUsage.Core.Appearance;

/// <summary>
/// A value the tray hover popover can show for one provider.
/// </summary>
public enum TrayPopoverMetric
{
    None,
    SessionQuota,
    PeriodQuota,
    SpendLast30Days,
    TokensLast30Days,
}

/// <summary>
/// What the tray hover popover shows. The popover has room for two values per provider,
/// so the user picks which value sits on each line. A disabled popover keeps every
/// choice so turning it back on restores them.
/// </summary>
public sealed record TrayPopoverSettings
{
    public const int MinProviderCount = 1;
    public const int MaxProviderCount = DashboardLayout.MaxHighlightedProviders;

    /// <summary>
    /// Default popover: enabled, session quota over period quota, four providers,
    /// no provider name.
    /// </summary>
    public static TrayPopoverSettings Default { get; } = new(
        TrayPopoverMetric.SessionQuota,
        TrayPopoverMetric.PeriodQuota,
        MaxProviderCount,
        showProviderName: false);

    public TrayPopoverSettings(
        TrayPopoverMetric primaryMetric,
        TrayPopoverMetric secondaryMetric,
        int providerCount,
        bool showProviderName,
        bool isEnabled = true)
    {
        if (!Enum.IsDefined(primaryMetric))
        {
            throw new ArgumentOutOfRangeException(nameof(primaryMetric), primaryMetric, null);
        }

        if (!Enum.IsDefined(secondaryMetric))
        {
            throw new ArgumentOutOfRangeException(nameof(secondaryMetric), secondaryMetric, null);
        }

        if (primaryMetric == TrayPopoverMetric.None)
        {
            throw new ArgumentOutOfRangeException(
                nameof(primaryMetric),
                primaryMetric,
                "The popover must show at least one value.");
        }

        ArgumentOutOfRangeException.ThrowIfLessThan(providerCount, MinProviderCount);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(providerCount, MaxProviderCount);

        PrimaryMetric = primaryMetric;
        SecondaryMetric = secondaryMetric == primaryMetric
            ? TrayPopoverMetric.None
            : secondaryMetric;
        ProviderCount = providerCount;
        ShowProviderName = showProviderName;
        IsEnabled = isEnabled;
    }

    public TrayPopoverMetric PrimaryMetric { get; }

    public TrayPopoverMetric SecondaryMetric { get; }

    public int ProviderCount { get; }

    public bool ShowProviderName { get; }

    public bool IsEnabled { get; }

    public bool HasSecondaryMetric => SecondaryMetric != TrayPopoverMetric.None;
}
