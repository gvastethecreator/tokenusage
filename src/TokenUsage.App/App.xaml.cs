using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;
using System.Globalization;
using TokenUsage.App.Localization;
using TokenUsage.App.Services;
using TokenUsage.Core.Alerts;

namespace TokenUsage.App;

public partial class App : Application
{
    private static readonly object ActivationSync = new();
    private static bool _redirectedActivationPending;
    private static AlertActivationTarget? _pendingAlertActivation;
    private static IAlertNotificationSink _alertNotifications = NullAlertNotificationSink.Instance;

    public static Window Window { get; private set; } = null!;

    public static Microsoft.UI.Dispatching.DispatcherQueue DispatcherQueue { get; private set; } = null!;

    public static nint WindowHandle => WinRT.Interop.WindowNative.GetWindowHandle(Window);

    internal static IAlertNotificationSink AlertNotifications => _alertNotifications;

    internal static void ConfigureAlertNotifications(IAlertNotificationSink notifications) =>
        _alertNotifications = notifications ?? throw new ArgumentNullException(nameof(notifications));

    public App()
    {
        AppLanguageRuntime.Initialize();
        InitializeComponent();
#if DEBUG || UI_TEST_FIXTURES
        string[] launchArguments = Environment.GetCommandLineArgs()[1..];
        if (launchArguments.Contains("--theme=light", StringComparer.OrdinalIgnoreCase))
        {
            RequestedTheme = ApplicationTheme.Light;
        }
        else if (launchArguments.Contains("--theme=dark", StringComparer.OrdinalIgnoreCase))
        {
            RequestedTheme = ApplicationTheme.Dark;
        }
#endif
    }

    protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
    {
#if DEBUG || UI_TEST_FIXTURES
        string[] launchArguments = Environment.GetCommandLineArgs()[1..];
        string? claudeConfigForTest = launchArguments.FirstOrDefault(argument =>
            argument.StartsWith("--test-claude-config=", StringComparison.OrdinalIgnoreCase));
        if (claudeConfigForTest is not null)
        {
            Environment.SetEnvironmentVariable(
                "CLAUDE_CONFIG_DIR",
                claudeConfigForTest[(claudeConfigForTest.IndexOf('=') + 1)..]);
        }

        string? grokHomeForTest = launchArguments.FirstOrDefault(argument =>
            argument.StartsWith("--test-grok-home=", StringComparison.OrdinalIgnoreCase));
        if (grokHomeForTest is not null)
        {
            Environment.SetEnvironmentVariable(
                "GROK_HOME",
                grokHomeForTest[(grokHomeForTest.IndexOf('=') + 1)..]);
        }

        string? openCodeDataForTest = launchArguments.FirstOrDefault(argument =>
            argument.StartsWith("--test-opencode-data=", StringComparison.OrdinalIgnoreCase));
        if (openCodeDataForTest is not null)
        {
            Environment.SetEnvironmentVariable(
                "OPENCODE_DATA_DIR",
                openCodeDataForTest[(openCodeDataForTest.IndexOf('=') + 1)..]);
        }

        bool showForTest = launchArguments.Contains(
            "--test-show-flyout",
            StringComparer.OrdinalIgnoreCase);
        bool useSampleForTest = launchArguments.Contains(
            "--test-use-sample",
            StringComparer.OrdinalIgnoreCase);
        bool showTraySummaryForTest = launchArguments.Contains(
            "--test-show-tray-summary",
            StringComparer.OrdinalIgnoreCase);
        double? flyoutWidthForTest = GetFlyoutWidthForTest(launchArguments);
#else
        const bool showForTest = false;
        const bool useSampleForTest = false;
        const bool showTraySummaryForTest = false;
        double? flyoutWidthForTest = null;
#endif
        Window = new MainWindow(
            showForTest,
            useSampleForTest,
            flyoutWidthForTest,
            showTraySummaryForTest);
        DispatcherQueue = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();

        bool showRedirectedActivation;
        AlertActivationTarget? alertActivation;
        lock (ActivationSync)
        {
            showRedirectedActivation = _redirectedActivationPending;
            _redirectedActivationPending = false;
            alertActivation = _pendingAlertActivation;
            _pendingAlertActivation = null;
        }

        if (alertActivation is not null)
        {
            _ = DispatcherQueue.TryEnqueue(
                () => ((MainWindow)Window).ShowAlertActivation(alertActivation));
        }
        else if (showRedirectedActivation)
        {
            _ = DispatcherQueue.TryEnqueue(
                () => ((MainWindow)Window).ShowFromExternalActivation());
        }
    }

    internal static void OnRedirectedActivation(
        object? sender,
        AppActivationArguments args)
    {
        _ = WindowsAlertNotificationService.TryParse(args, out AlertActivationTarget? alertTarget);
        Microsoft.UI.Dispatching.DispatcherQueue? dispatcherQueue;
        lock (ActivationSync)
        {
            if (Window is null || DispatcherQueue is null)
            {
                if (alertTarget is not null)
                {
                    _pendingAlertActivation = alertTarget;
                }
                else
                {
                    _redirectedActivationPending = true;
                }

                return;
            }

            dispatcherQueue = DispatcherQueue;
        }

        _ = dispatcherQueue.TryEnqueue(() =>
        {
            if (alertTarget is not null)
            {
                ((MainWindow)Window).ShowAlertActivation(alertTarget);
            }
            else
            {
                ((MainWindow)Window).ShowFromExternalActivation();
            }
        });
    }

    internal static void OnAlertNotificationActivation(
        object? sender,
        AlertActivationRequestedEventArgs args)
    {
        ArgumentNullException.ThrowIfNull(args);
        Microsoft.UI.Dispatching.DispatcherQueue? dispatcherQueue;
        lock (ActivationSync)
        {
            if (Window is null || DispatcherQueue is null)
            {
                _pendingAlertActivation = args.Target;
                return;
            }

            dispatcherQueue = DispatcherQueue;
        }

        _ = dispatcherQueue.TryEnqueue(
            () => ((MainWindow)Window).ShowAlertActivation(args.Target));
    }

#if DEBUG || UI_TEST_FIXTURES
    private static double? GetFlyoutWidthForTest(IEnumerable<string> launchArguments)
    {
        const string prefix = "--test-flyout-width=";
        string? argument = launchArguments.FirstOrDefault(value =>
            value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
        if (argument is null)
        {
            return null;
        }

        return double.TryParse(
            argument[prefix.Length..],
            NumberStyles.Float,
            CultureInfo.InvariantCulture,
            out double width)
            && double.IsFinite(width)
            && width >= 240d
            && width <= 800d
                ? width
                : null;
    }
#endif
}
