namespace WOpenUsage.App.ViewModels.Sample;

public enum SampleScenario
{
    Normal,
    NearLimit,
    Partial,
    Stale,
    Error,
}

public sealed record SampleScenarioOption(SampleScenario Value, string DisplayName);
