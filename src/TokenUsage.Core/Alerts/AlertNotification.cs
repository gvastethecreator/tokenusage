namespace TokenUsage.Core.Alerts;

public enum AlertActivationArea
{
    QuotaReport,
    ProviderStatus,
}

public sealed record AlertActivationTarget
{
    public AlertActivationTarget(
        AlertActivationArea area,
        string providerId,
        string? metricId = null)
    {
        if (!Enum.IsDefined(area))
        {
            throw new ArgumentOutOfRangeException(nameof(area));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(providerId);
        if (providerId.Length > 64 || providerId.Any(char.IsControl))
        {
            throw new ArgumentException("Provider IDs must be readable and no longer than 64 characters.", nameof(providerId));
        }

        if (area == AlertActivationArea.QuotaReport && string.IsNullOrWhiteSpace(metricId))
        {
            throw new ArgumentException("Quota report activations require a metric ID.", nameof(metricId));
        }

        if (area == AlertActivationArea.ProviderStatus && metricId is not null)
        {
            throw new ArgumentException("Provider status activations cannot carry a metric ID.", nameof(metricId));
        }

        if (metricId is not null && (metricId.Length > 128 || metricId.Any(char.IsControl)))
        {
            throw new ArgumentException("Metric IDs must be readable and no longer than 128 characters.", nameof(metricId));
        }

        Area = area;
        ProviderId = providerId;
        MetricId = metricId;
    }

    public AlertActivationArea Area { get; }

    public string ProviderId { get; }

    public string? MetricId { get; }

    public IReadOnlyDictionary<string, string> ToArguments() =>
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["area"] = Area == AlertActivationArea.QuotaReport ? "quota" : "status",
            ["provider"] = ProviderId,
            ["metric"] = MetricId ?? string.Empty,
        };

    public static bool TryParse(
        IReadOnlyDictionary<string, string> arguments,
        out AlertActivationTarget? target)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        target = null;
        if (!arguments.TryGetValue("area", out string? areaValue)
            || !arguments.TryGetValue("provider", out string? providerId)
            || string.IsNullOrWhiteSpace(providerId))
        {
            return false;
        }

        AlertActivationArea area = areaValue switch
        {
            "quota" => AlertActivationArea.QuotaReport,
            "status" => AlertActivationArea.ProviderStatus,
            _ => (AlertActivationArea)(-1),
        };
        if (!Enum.IsDefined(area))
        {
            return false;
        }

        arguments.TryGetValue("metric", out string? metricId);
        metricId = string.IsNullOrWhiteSpace(metricId) ? null : metricId;
        try
        {
            target = new AlertActivationTarget(area, providerId, metricId);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }
}

public sealed record AlertNotificationMessage
{
    public AlertNotificationMessage(
        string title,
        string body,
        AlertActivationTarget activationTarget)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentException.ThrowIfNullOrWhiteSpace(body);
        if (title.Length > 96 || title.Any(char.IsControl))
        {
            throw new ArgumentException("Notification titles must be readable and no longer than 96 characters.", nameof(title));
        }

        if (body.Length > 240 || body.Any(char.IsControl))
        {
            throw new ArgumentException("Notification bodies must be readable and no longer than 240 characters.", nameof(body));
        }

        Title = title;
        Body = body;
        ActivationTarget = activationTarget
            ?? throw new ArgumentNullException(nameof(activationTarget));
    }

    public string Title { get; }

    public string Body { get; }

    public AlertActivationTarget ActivationTarget { get; }
}

public interface IAlertNotificationSink
{
    Task ShowAsync(
        AlertNotificationMessage notification,
        CancellationToken cancellationToken = default);
}

public sealed class NullAlertNotificationSink : IAlertNotificationSink
{
    public static NullAlertNotificationSink Instance { get; } = new();

    private NullAlertNotificationSink()
    {
    }

    public Task ShowAsync(
        AlertNotificationMessage notification,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(notification);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }
}
