namespace TokenUsage.App.ViewModels;

public enum FlyoutSurfaceState
{
    Loading,
    Empty,
    Options,
    Sample,
    SampleUnavailable,
}

public enum SampleDataState
{
    Idle,
    CacheRefreshing,
    StaleCacheRefreshing,
    Fresh,
    Partial,
    Stale,
    Error,
    Throttled,
    NotSaved,
    Unavailable,
}
