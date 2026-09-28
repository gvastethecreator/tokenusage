namespace TokenUsage.Core.Credentials;

/// <summary>
/// What happened to a provider's cached reading after its saved key changed. A cached reading
/// belongs to the key that produced it, so a new or removed key must not keep showing it.
/// </summary>
public enum ManualCredentialChangeResult
{
    /// <summary>The provider has no live source in this app, so nothing was cached.</summary>
    NoLiveSource,

    /// <summary>The cached reading was removed, or there was none.</summary>
    CacheCleared,

    /// <summary>The cached reading could not be removed.</summary>
    CacheCleanupFailed,
}
