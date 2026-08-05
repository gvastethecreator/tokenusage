namespace WOpenUsage.Core.Providers;

public enum ProviderWarningCode
{
    PartialCoverage,
    MissingMetric,
    SourceStale,
    SourceDegraded,
}

public sealed record ProviderWarning
{
    public ProviderWarning(ProviderWarningCode code, string message)
    {
        if (!Enum.IsDefined(code))
        {
            throw new ArgumentOutOfRangeException(nameof(code));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        Code = code;
        Message = message;
    }

    public ProviderWarningCode Code { get; }

    public string Message { get; }
}

public enum ProviderErrorCode
{
    TransientSourceFailure,
    ContractViolation,
    Canceled,
    Unknown,
}

public sealed record ProviderError
{
    public ProviderError(ProviderErrorCode code, string message)
    {
        if (!Enum.IsDefined(code))
        {
            throw new ArgumentOutOfRangeException(nameof(code));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        Code = code;
        Message = message;
    }

    public ProviderErrorCode Code { get; }

    public string Message { get; }
}

public abstract class ProviderOutcome
{
    private ProviderOutcome()
    {
    }

    public sealed class Success : ProviderOutcome
    {
        public Success(ProviderSnapshot snapshot)
        {
            Snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
        }

        public ProviderSnapshot Snapshot { get; }
    }

    public sealed class NotConfigured : ProviderOutcome
    {
        public NotConfigured(string reason)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(reason);
            Reason = reason;
        }

        public string Reason { get; }
    }

    public sealed class UnsupportedAccount : ProviderOutcome
    {
        public UnsupportedAccount(string reason)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(reason);
            Reason = reason;
        }

        public string Reason { get; }
    }

    public sealed class PartialSuccess : ProviderOutcome
    {
        public PartialSuccess(ProviderSnapshot snapshot, IEnumerable<ProviderWarning> warnings)
        {
            Snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
            ArgumentNullException.ThrowIfNull(warnings);

            ProviderWarning[] warningArray = warnings.ToArray();
            if (warningArray.Length == 0)
            {
                throw new ArgumentException("A partial outcome requires at least one warning.", nameof(warnings));
            }

            if (warningArray.Any(warning => warning is null))
            {
                throw new ArgumentException("Warnings cannot contain null values.", nameof(warnings));
            }

            Warnings = Array.AsReadOnly(warningArray);
        }

        public ProviderSnapshot Snapshot { get; }

        public IReadOnlyList<ProviderWarning> Warnings { get; }
    }

    public sealed class Throttled : ProviderOutcome
    {
        public Throttled(DateTimeOffset retryAtUtc, ProviderSnapshot? lastGood)
        {
            UtcTimestamp.Require(retryAtUtc, nameof(retryAtUtc));
            RetryAtUtc = retryAtUtc;
            LastGood = lastGood;
        }

        public DateTimeOffset RetryAtUtc { get; }

        public ProviderSnapshot? LastGood { get; }
    }

    public sealed class TransientFailure : ProviderOutcome
    {
        public TransientFailure(
            ProviderError error,
            ProviderSnapshot? lastGood,
            DateTimeOffset? retryAtUtc = null)
        {
            Error = error ?? throw new ArgumentNullException(nameof(error));
            if (retryAtUtc is DateTimeOffset retryAt)
            {
                UtcTimestamp.Require(retryAt, nameof(retryAtUtc));
            }

            LastGood = lastGood;
            RetryAtUtc = retryAtUtc;
        }

        public ProviderError Error { get; }

        public ProviderSnapshot? LastGood { get; }

        public DateTimeOffset? RetryAtUtc { get; }
    }

    public sealed class ContractFailure : ProviderOutcome
    {
        public ContractFailure(
            ProviderError error,
            ProviderSnapshot? lastGood,
            DateTimeOffset? retryAtUtc = null)
        {
            Error = error ?? throw new ArgumentNullException(nameof(error));
            if (retryAtUtc is DateTimeOffset retryAt)
            {
                UtcTimestamp.Require(retryAt, nameof(retryAtUtc));
            }

            LastGood = lastGood;
            RetryAtUtc = retryAtUtc;
        }

        public ProviderError Error { get; }

        public ProviderSnapshot? LastGood { get; }

        public DateTimeOffset? RetryAtUtc { get; }
    }

    public sealed class PolicyBlocked : ProviderOutcome
    {
        public PolicyBlocked(string reason)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(reason);
            Reason = reason;
        }

        public string Reason { get; }
    }
}
