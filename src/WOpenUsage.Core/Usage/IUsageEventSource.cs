using WOpenUsage.Core.Providers;

namespace WOpenUsage.Core.Usage;

public interface IUsageEventSource
{
    SourceKind SourceKind { get; }

    Task<IReadOnlyList<UsageEvent>> ReadAsync(
        CancellationToken cancellationToken = default);
}
