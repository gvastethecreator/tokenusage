using TokenUsage.Runtime.Windows.Cursor;
using TokenUsage.Runtime.Windows.Grok;
using TokenUsage.Runtime.Windows.Zcode;

namespace TokenUsage.Runtime.Windows;

/// <summary>
/// Registers the refresh-trigger Stop hooks for every detected local provider
/// when the app opens, so usage updates after each task without manual setup.
/// Installation is idempotent and best effort: a provider that is missing or
/// fails to configure is skipped and never blocks startup.
/// </summary>
public static class RefreshHookAutoSetup
{
    public static void EnsureInstalled(
        ZcodeHookInstaller? zcode = null,
        GrokHookInstaller? grok = null,
        CursorHookInstaller? cursor = null,
        bool backgroundCollection = true)
    {
        Try(() =>
        {
            ZcodeHookInstaller installer = zcode ?? new ZcodeHookInstaller();
            if (!installer.IsProviderDetected)
            {
                return;
            }

            if (backgroundCollection)
            {
                installer.Install();
            }
            else
            {
                installer.Uninstall();
            }
        });
        Try(() =>
        {
            GrokHookInstaller installer = grok ?? new GrokHookInstaller();
            if (!installer.IsProviderDetected)
            {
                return;
            }

            if (backgroundCollection)
            {
                installer.Install();
            }
            else
            {
                installer.Uninstall();
            }
        });
        Try(() =>
        {
            CursorHookInstaller installer = cursor ?? new CursorHookInstaller();
            if (!installer.IsProviderDetected)
            {
                return;
            }

            if (backgroundCollection)
            {
                installer.InstallRefreshHook();
            }
            else
            {
                installer.Uninstall();
            }
        });
    }

    private static void Try(Action action)
    {
        try
        {
            action();
        }
        catch (Exception)
        {
            // Hook setup is a convenience; a failed provider never blocks the app.
        }
    }
}
