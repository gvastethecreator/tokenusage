using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;
using TokenUsage.App.Services;

namespace TokenUsage.App;

internal static class Program
{
    private const string InstanceKey = "TokenUsage";

    [STAThread]
    public static int Main(string[] args)
    {
        WinRT.ComWrappersSupport.InitializeComWrappers();

        using var notifications = new WindowsAlertNotificationService();
        notifications.ActivationRequested += App.OnAlertNotificationActivation;
        notifications.Register();
        App.ConfigureAlertNotifications(notifications);

        var activationArguments = AppInstance.GetCurrent().GetActivatedEventArgs();
        _ = notifications.TryActivate(activationArguments);
        var keyInstance = AppInstance.FindOrRegisterForKey(InstanceKey);
        if (!keyInstance.IsCurrent)
        {
            RedirectActivation(keyInstance, activationArguments);
            return 0;
        }

        keyInstance.Activated += App.OnRedirectedActivation;
        Application.Start(callbackParameters =>
        {
            var dispatcherQueue = DispatcherQueue.GetForCurrentThread();
            SynchronizationContext.SetSynchronizationContext(
                new DispatcherQueueSynchronizationContext(dispatcherQueue));
            _ = new App();
        });

        notifications.ActivationRequested -= App.OnAlertNotificationActivation;

        return 0;
    }

    private static void RedirectActivation(
        AppInstance keyInstance,
        AppActivationArguments activationArguments)
    {
        Task.Run(async () =>
        {
            await keyInstance.RedirectActivationToAsync(activationArguments);
        }).GetAwaiter().GetResult();
    }
}
