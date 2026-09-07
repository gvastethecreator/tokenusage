using Microsoft.UI.Windowing;

namespace TokenUsage.App;

public sealed partial class MainWindow
{
    private Task? _shutdownTask;
    private bool _allowWindowClose;

    private void OnUpdateExitRequested(object? sender, EventArgs args) => RequestGracefulExit();

    private void OnAppWindowClosing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (_allowWindowClose || _disposed) return;
        args.Cancel = true;
        RequestGracefulExit();
    }

    private void RequestGracefulExit()
    {
        if (_shutdownTask is null && !_disposed) _shutdownTask = ShutdownAsync();
    }

    private async Task ShutdownAsync()
    {
        await Task.Yield();
        try
        {
            if (RootPage.ViewModel.Options.Updates is { } updates) await updates.PrepareForExitAsync();
            RootPage.ViewModel.StopSessionForExit();
            await RootPage.SessionHost.DisposeAsync();
            RootPage.Dispose();
            _allowWindowClose = true;
            Dispose();
            Close();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
            or InvalidOperationException or OperationCanceledException)
        {
            _shutdownTask = null;
            RootPage.ViewModel.Options.Updates?.ReportShutdownFailure();
        }
    }
}
