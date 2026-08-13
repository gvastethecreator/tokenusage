using System.Globalization;

namespace TokenUsage.App.ViewModels;

public static class CodexLiveStateFormatter
{
    public static string Format(
        SampleDataState state,
        bool isSampleMode,
        DateTimeOffset? observedAtUtc,
        DateTimeOffset? retryAtUtc,
        DateTimeOffset nowUtc,
        Func<string, string> getString,
        CultureInfo? culture = null)
    {
        ArgumentNullException.ThrowIfNull(getString);
        culture ??= CultureInfo.CurrentCulture;
        nowUtc = nowUtc.ToUniversalTime();
        if (isSampleMode || observedAtUtc is null)
        {
            return getString(StateResourceKey(state));
        }

        string age = FormatRelativeDuration(nowUtc - observedAtUtc.Value, getString, culture);
        string? formatKey = state switch
        {
            SampleDataState.CacheRefreshing => "CodexStateCacheRefreshingWithAgeFormat",
            SampleDataState.StaleCacheRefreshing => "CodexStateStaleCacheRefreshingWithAgeFormat",
            SampleDataState.Stale => "CodexStateStaleWithAgeFormat",
            SampleDataState.Error when retryAtUtc is not null =>
                "CodexStateErrorRetryWithAgeFormat",
            SampleDataState.Error => "CodexStateErrorWithAgeFormat",
            SampleDataState.Throttled when retryAtUtc is not null =>
                "CodexStateThrottledWithAgeFormat",
            _ => null,
        };
        if (formatKey is null)
        {
            return getString(StateResourceKey(state));
        }

        if (state is SampleDataState.Throttled or SampleDataState.Error
            && retryAtUtc is not null)
        {
            string retry = FormatRelativeDuration(
                retryAtUtc!.Value - nowUtc,
                getString,
                culture);
            return string.Format(culture, getString(formatKey), retry, age);
        }

        return string.Format(culture, getString(formatKey), age);
    }

    private static string FormatRelativeDuration(
        TimeSpan duration,
        Func<string, string> getString,
        CultureInfo culture)
    {
        if (duration <= TimeSpan.Zero || duration < TimeSpan.FromMinutes(1))
        {
            return getString("CodexAgeLessThanMinute");
        }

        if (duration < TimeSpan.FromHours(1))
        {
            return string.Format(
                culture,
                getString("CodexAgeMinutesFormat"),
                (int)Math.Floor(duration.TotalMinutes));
        }

        if (duration < TimeSpan.FromDays(1))
        {
            return string.Format(
                culture,
                getString("CodexAgeHoursFormat"),
                (int)Math.Floor(duration.TotalHours));
        }

        return string.Format(
            culture,
            getString("CodexAgeDaysFormat"),
            (int)Math.Floor(duration.TotalDays));
    }

    private static string StateResourceKey(SampleDataState state) => state switch
    {
        SampleDataState.CacheRefreshing => "CodexStateCacheRefreshing",
        SampleDataState.StaleCacheRefreshing => "CodexStateStaleCacheRefreshing",
        SampleDataState.Fresh => "CodexStateFresh",
        SampleDataState.Partial => "CodexStatePartial",
        SampleDataState.Stale => "CodexStateStale",
        SampleDataState.Error => "CodexStateError",
        SampleDataState.Throttled => "CodexStateThrottled",
        SampleDataState.NotSaved => "CodexStateNotSaved",
        SampleDataState.Unavailable => "CodexStateUnavailable",
        _ => "CodexQuotaPeriod",
    };
}
