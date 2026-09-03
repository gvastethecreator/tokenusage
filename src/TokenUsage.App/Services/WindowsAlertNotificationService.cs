using Microsoft.Windows.AppLifecycle;
using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;
using TokenUsage.Core.Alerts;

namespace TokenUsage.App.Services;

public sealed class AlertActivationRequestedEventArgs : EventArgs
{
    public AlertActivationRequestedEventArgs(AlertActivationTarget target) =>
        Target = target ?? throw new ArgumentNullException(nameof(target));

    public AlertActivationTarget Target { get; }
}

/// <summary>
/// Sends Windows app notifications and reduces activation payloads to the app's safe route model.
/// </summary>
public sealed class WindowsAlertNotificationService : IAlertNotificationSink, IDisposable
{
    private readonly AppNotificationManager _manager = AppNotificationManager.Default;
    private bool _registered;
    private bool _disposed;

    public event EventHandler<AlertActivationRequestedEventArgs>? ActivationRequested;

    public void Register()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_registered)
        {
            return;
        }

        if (!AppNotificationManager.IsSupported())
        {
            return;
        }

        _manager.NotificationInvoked += OnNotificationInvoked;
        _manager.Register();
        _registered = true;
    }

    public Task ShowAsync(
        AlertNotificationMessage notification,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(notification);
        cancellationToken.ThrowIfCancellationRequested();
        if (!_registered)
        {
            return Task.CompletedTask;
        }

        AppNotificationBuilder builder = new AppNotificationBuilder()
            .AddText(notification.Title)
            .AddText(notification.Body);
        foreach ((string key, string value) in notification.ActivationTarget.ToArguments())
        {
            builder.AddArgument(key, value);
        }

        _manager.Show(builder.BuildNotification());
        return Task.CompletedTask;
    }

    public bool TryActivate(AppActivationArguments activationArguments)
    {
        ArgumentNullException.ThrowIfNull(activationArguments);
        if (!TryParse(activationArguments, out AlertActivationTarget? target))
        {
            return false;
        }

        ActivationRequested?.Invoke(this, new AlertActivationRequestedEventArgs(target!));
        return true;
    }

    public static bool TryParse(
        AppActivationArguments activationArguments,
        out AlertActivationTarget? target)
    {
        ArgumentNullException.ThrowIfNull(activationArguments);
        target = null;
        return activationArguments.Kind == ExtendedActivationKind.AppNotification
            && activationArguments.Data is AppNotificationActivatedEventArgs notificationArgs
            && AlertActivationTarget.TryParse(
                new Dictionary<string, string>(notificationArgs.Arguments, StringComparer.Ordinal),
                out target);
    }

    public bool TryActivate(IReadOnlyDictionary<string, string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        if (!AlertActivationTarget.TryParse(arguments, out AlertActivationTarget? target))
        {
            return false;
        }

        ActivationRequested?.Invoke(this, new AlertActivationRequestedEventArgs(target!));
        return true;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_registered)
        {
            _manager.NotificationInvoked -= OnNotificationInvoked;
            _manager.Unregister();
            _registered = false;
        }

        GC.SuppressFinalize(this);
    }

    private void OnNotificationInvoked(
        AppNotificationManager sender,
        AppNotificationActivatedEventArgs args) => TryActivate(new Dictionary<string, string>(
            args.Arguments,
            StringComparer.Ordinal));
}
