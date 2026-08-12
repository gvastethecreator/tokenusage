using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;

namespace TokenUsage.App;

internal static class Program
{
    private const string InstanceKey = "TokenUsage";

    [STAThread]
    public static int Main(string[] args)
    {
        RecordPortableStartupStage("main");
        WinRT.ComWrappersSupport.InitializeComWrappers();
        RecordPortableStartupStage("com-wrappers");

        var activationArguments = AppInstance.GetCurrent().GetActivatedEventArgs();
        RecordPortableStartupStage("activation-arguments");
        var keyInstance = AppInstance.FindOrRegisterForKey(InstanceKey);
        RecordPortableStartupStage("instance-registered");
        if (!keyInstance.IsCurrent)
        {
            RedirectActivation(keyInstance, activationArguments);
            return 0;
        }

        keyInstance.Activated += App.OnRedirectedActivation;
        Application.Start(callbackParameters =>
        {
            RecordPortableStartupStage("application-start");
            var dispatcherQueue = DispatcherQueue.GetForCurrentThread();
            SynchronizationContext.SetSynchronizationContext(
                new DispatcherQueueSynchronizationContext(dispatcherQueue));
            _ = new App();
            RecordPortableStartupStage("app-created");
        });

        return 0;
    }

    internal static void RecordPortableStartupStage(string stage)
    {
        string markerPath = Path.Combine(
            AppContext.BaseDirectory,
            TokenUsage.Platform.Windows.Storage.TokenUsageDataDirectory.PortableMarkerFileName);
        if (!File.Exists(markerPath))
        {
            return;
        }

        File.WriteAllText(
            Path.Combine(AppContext.BaseDirectory, "portable-startup-stage.txt"),
            stage);
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
