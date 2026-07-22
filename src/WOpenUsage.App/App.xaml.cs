using Microsoft.UI.Xaml;

namespace WOpenUsage.App;

public partial class App : Application
{
    public static Window Window { get; private set; } = null!;

    public static Microsoft.UI.Dispatching.DispatcherQueue DispatcherQueue { get; private set; } = null!;

    public static nint WindowHandle => WinRT.Interop.WindowNative.GetWindowHandle(Window);

    public App()
    {
        InitializeComponent();
#if DEBUG
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
#if DEBUG
        string[] launchArguments = Environment.GetCommandLineArgs()[1..];
        string? claudeConfigForTest = launchArguments.FirstOrDefault(argument =>
            argument.StartsWith("--test-claude-config=", StringComparison.OrdinalIgnoreCase));
        if (claudeConfigForTest is not null)
        {
            Environment.SetEnvironmentVariable(
                "CLAUDE_CONFIG_DIR",
                claudeConfigForTest[(claudeConfigForTest.IndexOf('=') + 1)..]);
        }

        bool showForTest = launchArguments.Contains(
            "--test-show-flyout",
            StringComparer.OrdinalIgnoreCase);
        bool useSampleForTest = launchArguments.Contains(
            "--test-use-sample",
            StringComparer.OrdinalIgnoreCase);
#else
        const bool showForTest = false;
        const bool useSampleForTest = false;
#endif
        Window = new MainWindow(showForTest, useSampleForTest);
        DispatcherQueue = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
    }
}
