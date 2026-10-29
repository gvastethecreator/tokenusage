using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace TokenUsage.App.ViewModels.Surfaces;

public sealed partial class GeneralOptionsViewModel
{
    private Func<bool, IProgress<string>, CancellationToken, Task<string>>? _recoverClaudeHistory;
    private Func<Task>? _retryClaudeLimits;
    private Action? _openClaude;
    private CancellationTokenSource? _claudeRecoveryCancellation;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PreviewClaudeRecoveryCommand))]
    [NotifyCanExecuteChangedFor(nameof(RecoverClaudeHistoryCommand))]
    public partial bool IsRecoveringClaudeHistory { get; private set; }

    [ObservableProperty]
    public partial string ClaudeRecoveryStatus { get; private set; } = string.Empty;

    public bool CanRecoverClaudeHistory => _recoverClaudeHistory is not null && !IsRecoveringClaudeHistory;

    public void BindClaudeActions(Action open, Func<Task> retry,
        Func<bool, IProgress<string>, CancellationToken, Task<string>> recover)
    {
        _openClaude = open;
        _retryClaudeLimits = retry;
        _recoverClaudeHistory = recover;
        PreviewClaudeRecoveryCommand.NotifyCanExecuteChanged();
        RecoverClaudeHistoryCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand]
    private void OpenClaude()
    {
        try { _openClaude?.Invoke(); }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or InvalidOperationException)
        { ClaudeLimitsStatus = _getString("ClaudeOpenFailed"); }
    }

    [RelayCommand]
    private async Task RetryClaudeLimitsAsync()
    {
        if (_retryClaudeLimits is not null) await _retryClaudeLimits();
    }

    [RelayCommand(CanExecute = nameof(CanRecoverClaudeHistory))]
    private Task PreviewClaudeRecoveryAsync() => RunClaudeRecoveryAsync(true);

    [RelayCommand(CanExecute = nameof(CanRecoverClaudeHistory))]
    private Task RecoverClaudeHistoryAsync() => RunClaudeRecoveryAsync(false);

    [RelayCommand]
    private void CancelClaudeRecovery() => _claudeRecoveryCancellation?.Cancel();

    private async Task RunClaudeRecoveryAsync(bool dryRun)
    {
        if (_recoverClaudeHistory is null || IsRecoveringClaudeHistory) return;
        using var cancellation = new CancellationTokenSource();
        _claudeRecoveryCancellation = cancellation;
        IsRecoveringClaudeHistory = true;
        ClaudeRecoveryStatus = string.Empty;
        try
        {
            ClaudeRecoveryStatus = await _recoverClaudeHistory(dryRun,
                new Progress<string>(value =>
                {
                    if (ReferenceEquals(_claudeRecoveryCancellation, cancellation))
                        ClaudeRecoveryStatus = value;
                }), cancellation.Token);
        }
        catch (OperationCanceledException) { ClaudeRecoveryStatus = _getString("ClaudeRecoveryIncomplete"); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
            or InvalidOperationException or System.Text.Json.JsonException)
        { ClaudeRecoveryStatus = _getString("ClaudeRecoveryFailed"); }
        finally
        {
            _claudeRecoveryCancellation = null;
            IsRecoveringClaudeHistory = false;
        }
    }
}
