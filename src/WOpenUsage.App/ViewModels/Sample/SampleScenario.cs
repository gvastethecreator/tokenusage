namespace WOpenUsage.App.ViewModels.Sample;

public enum SampleScenario
{
    Normal,
    NearLimit,
    PartialStale,
}

public sealed record SampleScenarioOption(SampleScenario Value, string DisplayName);
