using System.Threading.Channels;

namespace WOpenUsage.Core.Providers;

public sealed record ProviderBackoffOptions
{
    public ProviderBackoffOptions(
        TimeSpan? initialDelay = null,
        TimeSpan? maximumDelay = null,
        double jitterRatio = 0.2)
    {
        InitialDelay = initialDelay ?? TimeSpan.FromSeconds(15);
        MaximumDelay = maximumDelay ?? TimeSpan.FromMinutes(5);
        if (InitialDelay <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(initialDelay));
        }

        if (MaximumDelay < InitialDelay)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumDelay));
        }

        if (jitterRatio is < 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(jitterRatio));
        }

        JitterRatio = jitterRatio;
    }

    public TimeSpan InitialDelay { get; }

    public TimeSpan MaximumDelay { get; }

    public double JitterRatio { get; }
}

public sealed class ResilientProviderRuntime : IProviderRuntime
{
    private readonly Channel<bool> _executionGate;
    private readonly IProviderRuntime _inner;
    private readonly ProviderBackoffOptions _options;
    private readonly Random _random;
    private int _consecutiveFailures;
    private DateTimeOffset? _retryAtUtc;

    public ResilientProviderRuntime(
        IProviderRuntime inner,
        ProviderBackoffOptions? options = null,
        Random? random = null)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _options = options ?? new ProviderBackoffOptions();
        _random = random ?? Random.Shared;
        _executionGate = Channel.CreateBounded<bool>(new BoundedChannelOptions(1)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = false,
            SingleWriter = false,
        });
        _executionGate.Writer.TryWrite(true);
    }

    public ProviderDescriptor Descriptor => _inner.Descriptor;

    public ValueTask<ProviderDetection> DetectAsync(CancellationToken cancellationToken) =>
        _inner.DetectAsync(cancellationToken);

    public async Task<ProviderOutcome> RefreshAsync(
        RefreshContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        await _executionGate.Reader.ReadAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            DateTimeOffset now = context.Clock.GetUtcNow().ToUniversalTime();
            if (!context.ForceRefresh
                && _retryAtUtc is DateTimeOffset retryAtUtc
                && retryAtUtc > now)
            {
                return new ProviderOutcome.Throttled(retryAtUtc, context.LastGood);
            }

            ProviderOutcome outcome = await _inner
                .RefreshAsync(context, cancellationToken)
                .ConfigureAwait(false);
            UpdateBackoff(outcome, now);
            return AttachRetry(outcome);
        }
        finally
        {
            _executionGate.Writer.TryWrite(true);
        }
    }

    private ProviderOutcome AttachRetry(ProviderOutcome outcome) => outcome switch
    {
        ProviderOutcome.TransientFailure failure when _retryAtUtc is not null =>
            new ProviderOutcome.TransientFailure(
                failure.Error,
                failure.LastGood,
                _retryAtUtc),
        ProviderOutcome.ContractFailure failure when _retryAtUtc is not null =>
            new ProviderOutcome.ContractFailure(
                failure.Error,
                failure.LastGood,
                _retryAtUtc),
        _ => outcome,
    };

    private void UpdateBackoff(ProviderOutcome outcome, DateTimeOffset now)
    {
        switch (outcome)
        {
            case ProviderOutcome.TransientFailure:
            case ProviderOutcome.ContractFailure:
                _consecutiveFailures++;
                _retryAtUtc = now + NextDelay(_consecutiveFailures);
                break;
            case ProviderOutcome.Throttled throttled:
                _consecutiveFailures++;
                _retryAtUtc = throttled.RetryAtUtc > now
                    ? throttled.RetryAtUtc
                    : now + NextDelay(_consecutiveFailures);
                break;
            default:
                _consecutiveFailures = 0;
                _retryAtUtc = null;
                break;
        }
    }

    private TimeSpan NextDelay(int failureCount)
    {
        int exponent = Math.Min(failureCount - 1, 30);
        double milliseconds = Math.Min(
            _options.MaximumDelay.TotalMilliseconds,
            _options.InitialDelay.TotalMilliseconds * Math.Pow(2, exponent));
        if (_options.JitterRatio > 0)
        {
            double offset = ((_random.NextDouble() * 2) - 1) * _options.JitterRatio;
            milliseconds *= 1 + offset;
            milliseconds = Math.Clamp(
                milliseconds,
                1,
                _options.MaximumDelay.TotalMilliseconds);
        }

        return TimeSpan.FromMilliseconds(milliseconds);
    }
}
