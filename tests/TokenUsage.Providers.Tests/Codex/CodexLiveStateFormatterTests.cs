using System.Globalization;
using TokenUsage.App.ViewModels;

namespace TokenUsage.Providers.Tests.Codex;

public sealed class CodexLiveStateFormatterTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 7, 22, 16, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ErrorShowsTheAgeOfTheRetainedSnapshot()
    {
        string text = CodexLiveStateFormatter.Format(
            SampleDataState.Error,
            isSampleMode: false,
            observedAtUtc: Now.AddMinutes(-17),
            retryAtUtc: null,
            Now,
            GetString,
            CultureInfo.InvariantCulture);

        Assert.Equal("error, cache from 17 min ago", text);
    }

    [Fact]
    public void ThrottleShowsRetryAndCacheAgeWithoutNegativeDurations()
    {
        string text = CodexLiveStateFormatter.Format(
            SampleDataState.Throttled,
            isSampleMode: false,
            observedAtUtc: Now.AddHours(-2),
            retryAtUtc: Now.AddSeconds(15),
            Now,
            GetString,
            CultureInfo.InvariantCulture);

        Assert.Equal("retry in less than 1 min, cache from 2 h ago", text);
    }

    [Fact]
    public void ErrorShowsTheScheduledRetryAlongsideCacheAge()
    {
        string text = CodexLiveStateFormatter.Format(
            SampleDataState.Error,
            isSampleMode: false,
            observedAtUtc: Now.AddMinutes(-17),
            retryAtUtc: Now.AddSeconds(15),
            Now,
            GetString,
            CultureInfo.InvariantCulture);

        Assert.Equal("error retry in less than 1 min, cache from 17 min ago", text);
    }

    private static string GetString(string key) => key switch
    {
        "CodexStateErrorWithAgeFormat" => "error, cache from {0} ago",
        "CodexStateErrorRetryWithAgeFormat" => "error retry in {0}, cache from {1} ago",
        "CodexStateThrottledWithAgeFormat" => "retry in {0}, cache from {1} ago",
        "CodexAgeLessThanMinute" => "less than 1 min",
        "CodexAgeMinutesFormat" => "{0} min",
        "CodexAgeHoursFormat" => "{0} h",
        "CodexAgeDaysFormat" => "{0} d",
        _ => key,
    };
}
