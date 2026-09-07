using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Xml;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Dispatching;
using TokenUsage.App.Services.Updates;
using TokenUsage.Core.Updates;
using TokenUsage.Runtime.Windows.Updates;

namespace TokenUsage.App.ViewModels.Surfaces;

public sealed class UpdateOptionsViewModel : ObservableObject, IDisposable
{
    private static readonly TimeSpan CheckInterval = TimeSpan.FromDays(1);
    private readonly UpdateEnvironment _environment;
    private readonly UpdateSettingsStore _settingsStore;
    private readonly PendingUpdateStore _pendingStore;
    private readonly GitHubUpdateClient _client;
    private readonly Func<string, string> _getString;
    private readonly TimeProvider _clock;
    private readonly string _updateDirectory;
    private readonly DispatcherQueueTimer _automaticTimer;
    private readonly CancellationTokenSource _lifetime = new();
    private CancellationTokenSource? _operationCancellation;
    private Task _operation = Task.CompletedTask;
    private Task _settingsSave = Task.CompletedTask;
    private UpdateSettings _settings = new();
    private UpdateRelease? _release;
    private PendingUpdate? _pending;
    private Activity _activity;
    private bool _initializing = true;
    private bool _automaticOperation;
    private bool _automaticUpdatesEnabled;
    private bool _isBusy;
    private bool _isReady;
    private bool _settingsError;
    private bool _disposed;
    private bool _exiting;
    private string _statusText;
    private double _progressPercent;

    public UpdateOptionsViewModel(
        UpdateEnvironment environment,
        UpdateSettingsStore settingsStore,
        GitHubUpdateClient client,
        string updateDirectory,
        Func<string, string> getString,
        TimeProvider? clock = null)
    {
        _environment = environment ?? throw new ArgumentNullException(nameof(environment));
        _settingsStore = settingsStore ?? throw new ArgumentNullException(nameof(settingsStore));
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _getString = getString ?? throw new ArgumentNullException(nameof(getString));
        _updateDirectory = Path.GetFullPath(updateDirectory);
        _pendingStore = new PendingUpdateStore(_updateDirectory, environment.InstallDirectory);
        _clock = clock ?? TimeProvider.System;
        _statusText = getString(environment.StatusKey);
        _automaticTimer = DispatcherQueue.GetForCurrentThread().CreateTimer();
        _automaticTimer.IsRepeating = false;
        _automaticTimer.Tick += OnAutomaticTimer;
        CheckUpdatesCommand = new AsyncRelayCommand(() => StartOperationAsync(Operation.Check, automatic: false), () => CanCheck);
        DownloadAndInstallCommand = new AsyncRelayCommand(() => StartOperationAsync(Operation.Download, automatic: false), () => CanDownload);
        RetryInstallationCommand = new AsyncRelayCommand(() => StartOperationAsync(Operation.Retry, automatic: false), () => CanRetry);
        RestartAndInstallCommand = new AsyncRelayCommand(ExitReadyAsync, () => IsReady && !IsBusy && !_exiting);
        CancelUpdateCommand = new RelayCommand(() => _operationCancellation?.Cancel(), () => CanCancel);
        Initialization = InitializeAsync();
    }

    public event EventHandler? ExitRequested;
    public Task Initialization { get; }
    public IAsyncRelayCommand CheckUpdatesCommand { get; }
    public IAsyncRelayCommand DownloadAndInstallCommand { get; }
    public IAsyncRelayCommand RetryInstallationCommand { get; }
    public IAsyncRelayCommand RestartAndInstallCommand { get; }
    public IRelayCommand CancelUpdateCommand { get; }

    public bool AutomaticUpdatesEnabled
    {
        get => _automaticUpdatesEnabled;
        set
        {
            if (!SetProperty(ref _automaticUpdatesEnabled, value) || _initializing) return;
            _settings = _settings with { AutomaticUpdatesEnabled = value };
            QueueSettingsSave();
            _automaticTimer.Stop();
            if (!value && _automaticOperation && CanCancel) _operationCancellation?.Cancel();
            if (value) ScheduleAutomatic(TimeSpan.FromSeconds(30));
        }
    }

    public bool IsSupported => _environment.IsSupported;
    public bool CanChangePreference => IsSupported && !_initializing && !_exiting;
    public bool IsBusy => _isBusy;
    public bool IsReady => _isReady;
    public bool HasSettingsError => _settingsError;
    public string StatusText => _statusText;
    public double ProgressPercent => _progressPercent;
    public bool IsProgressIndeterminate => _activity != Activity.Downloading;
    public bool CanCancel => IsBusy && _activity is Activity.Checking or Activity.Downloading;
    public bool CanCheck => IsSupported && !_initializing && !IsBusy && _pending is null && !_exiting;
    public bool CanDownload => CanCheck && _release is not null;
    public bool CanRetry => IsSupported && !_initializing && !IsBusy && _pending is not null && !IsReady && !_exiting;
    public bool ShowCheckAction => !IsReady && _pending is null;
    public bool ShowDownloadAction => _release is not null && _pending is null && !IsReady;
    public bool ShowRetryAction => _pending is not null && !IsReady;
    public bool HasLastCheck => _settings.LastCheckUtc is not null;
    public string LastCheckText => _settings.LastCheckUtc is { } time
        ? Format("UpdateLastCheckFormat", time.ToLocalTime().ToString("g", CultureInfo.CurrentCulture))
        : string.Empty;
    public string CurrentVersionText => Format("UpdateCurrentVersionFormat", _environment.Version.ToString(3));
    public string FinishActionText => _getString(_environment.Kind == UpdatePackageKind.Portable
        ? "UpdateRestartInstall" : "UpdateCloseToFinish");

    private async Task InitializeAsync()
    {
        try
        {
            _settings = await _settingsStore.LoadAsync(_lifetime.Token);
            AutomaticUpdatesEnabled = _settings.AutomaticUpdatesEnabled;
            if (IsSupported)
            {
                _pending = await _pendingStore.LoadAsync(_lifetime.Token);
                if (_pending is not null && _pending.Release.Kind != _environment.Kind)
                    throw new InvalidDataException("The pending update package type has changed.");
                if (_pending is not null)
                {
                    PortableUpdateResult? result = _pending.PlanPath is { } plan
                        ? await PortableUpdateInstaller.ReadResultAsync(plan, _lifetime.Token) : null;
                    bool installed = Normalize(_environment.Version) >= Normalize(_pending.Release.Version)
                        && (_pending.PlanPath is null || result?.Status == "installed");
                    if (installed)
                    {
                        bool cleaned = true;
                        if (_pending.PlanPath is { } completedPlan)
                        {
                            try { await PortableUpdateInstaller.CleanupAfterSuccessfulInstallAsync(completedPlan, _lifetime.Token); }
                            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { cleaned = false; }
                        }
                        if (cleaned) await _pendingStore.ClearAsync(_lifetime.Token);
                        _pending = null;
                        SetStatus(Activity.Idle, "UpdateInstalled");
                    }
                    else
                    {
                        SetStatus(Activity.Idle, result?.Status == "rollback-required" ? "UpdateRecoveryRequired" : "UpdateIncomplete");
                    }
                }
            }
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (Exception exception) when (IsUpdateFailure(exception))
        {
            SetStatus(Activity.Idle, "UpdateInitializationFailed");
        }
        finally
        {
            _initializing = false;
            NotifyState();
            ScheduleAutomatic(TimeSpan.FromSeconds(30));
        }
    }

    private Task StartOperationAsync(Operation operation, bool automatic)
    {
        if (_disposed || _exiting || IsBusy || _initializing || !IsSupported) return Task.CompletedTask;
        _isBusy = true;
        _automaticOperation = automatic;
        _operationCancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        _operation = RunOperationAsync(operation, _operationCancellation.Token);
        NotifyState();
        return _operation;
    }

    private async Task RunOperationAsync(Operation operation, CancellationToken cancellationToken)
    {
        // Let the owner record the task before a synchronous fake or cached result completes.
        await Task.Yield();
        try
        {
            if (operation == Operation.Check)
            {
                _operationCancellation!.CancelAfter(TimeSpan.FromMinutes(2));
                SetStatus(Activity.Checking, "UpdateChecking");
                try
                {
                    _release = await _client.FindUpdateAsync(_environment.Version, _environment.Architecture,
                        _environment.Kind!.Value, cancellationToken);
                }
                finally
                {
                    _settings = _settings with { LastCheckUtc = _clock.GetUtcNow() };
                    QueueSettingsSave();
                }
                SetStatus(Activity.Idle, _release is null ? "UpdateUpToDate" : "UpdateAvailable",
                    _release?.Version.ToString(3));
                if (_automaticOperation && AutomaticUpdatesEnabled && _release is not null)
                    await DownloadAndPrepareAsync(cancellationToken);
            }
            else if (operation == Operation.Retry && _pending is not null)
            {
                SetStatus(Activity.Preparing, "UpdatePreparing");
                _operationCancellation!.CancelAfter(Timeout.InfiniteTimeSpan);
                if (_pending.PlanPath is { } planPath)
                {
                    PortableUpdatePlan plan = await PortableUpdateInstaller.RearmAsync(
                        planPath, _environment.InstallDirectory, CancellationToken.None);
                    await _pendingStore.SaveAsync(_pending, CancellationToken.None);
                    await ArmPortableWorkerAsync(plan);
                    MarkReady();
                }
                else
                {
                    _release = _pending.Release;
                    await DownloadAndPrepareAsync(cancellationToken);
                }
            }
            else if (_release is not null)
            {
                await DownloadAndPrepareAsync(cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            if (!_disposed) SetStatus(Activity.Idle, "UpdateCancelled");
        }
        catch (Exception exception) when (IsUpdateFailure(exception))
        {
            if (!_disposed) SetStatus(Activity.Idle, _pending is null ? "UpdateFailed" : "UpdateIncomplete");
        }
        finally
        {
            _operationCancellation?.Dispose();
            _operationCancellation = null;
            _automaticOperation = false;
            _isBusy = false;
            NotifyState();
            ScheduleAutomatic(CheckInterval);
        }
    }

    private async Task DownloadAndPrepareAsync(CancellationToken cancellationToken)
    {
        UpdateRelease release = _release ?? throw new InvalidOperationException("No update is selected.");
        _operationCancellation!.CancelAfter(TimeSpan.FromMinutes(30));
        _progressPercent = 0;
        SetStatus(Activity.Downloading, "UpdateDownloading", release.Version.ToString(3));
        string? download = null;
        try
        {
            var progress = new Progress<double>(value =>
            {
                if (!_disposed && _activity == Activity.Downloading)
                {
                    SetProperty(ref _progressPercent, Math.Clamp(value * 100, 0, 100), nameof(ProgressPercent));
                }
            });
            download = await _client.DownloadAsync(release, Path.Combine(_updateDirectory, "downloads"), progress, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            _operationCancellation.CancelAfter(Timeout.InfiniteTimeSpan);
            SetStatus(Activity.Preparing, "UpdatePreparing");
            if (release.Kind == UpdatePackageKind.Portable)
            {
                PortableUpdatePlan plan = await Task.Run(() => PortableUpdateInstaller.PrepareAsync(download, release.Version,
                    _environment.InstallDirectory, Path.Combine(_updateDirectory, "portable"), CancellationToken.None), CancellationToken.None);
                _pending = new PendingUpdate(release, _environment.InstallDirectory, plan.PlanPath);
                await _pendingStore.SaveAsync(_pending, CancellationToken.None);
                await ArmPortableWorkerAsync(plan);
            }
            else
            {
                await Task.Run(() => MsixUpdateInstaller.StageAsync(download, _environment, release.Version, CancellationToken.None), CancellationToken.None);
                _pending = new PendingUpdate(release, _environment.InstallDirectory);
                await _pendingStore.SaveAsync(_pending, CancellationToken.None);
            }
            MarkReady();
        }
        finally
        {
            if (download is not null)
            {
                try { File.Delete(download); }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
            }
        }
    }

    private static async Task ArmPortableWorkerAsync(PortableUpdatePlan plan)
    {
        var start = new ProcessStartInfo(plan.WorkerExecutablePath)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
            WorkingDirectory = Path.GetDirectoryName(plan.WorkerExecutablePath)!,
        };
        start.ArgumentList.Add(PortableUpdateInstaller.WorkerArgument);
        start.ArgumentList.Add(plan.PlanPath);
        start.ArgumentList.Add(plan.ParentProcessId.ToString(CultureInfo.InvariantCulture));
        using Process worker = Process.Start(start) ?? throw new IOException("The update worker could not start.");
        await Task.Delay(300);
        if (worker.HasExited)
            throw new IOException("The update worker stopped before the application exited.");
    }

    private void MarkReady()
    {
        _isReady = true;
        SetStatus(Activity.Idle, _environment.Kind == UpdatePackageKind.Portable ? "UpdateReadyPortable" : "UpdateReadyMsix",
            _pending!.Release.Version.ToString(3));
    }

    private async Task ExitReadyAsync()
    {
        if (!IsReady || IsBusy || _pending is null || _exiting) return;
        try
        {
            if (_pending.PlanPath is { } plan)
                await PortableUpdateInstaller.SetRelaunchAsync(plan, true, _lifetime.Token);
            _exiting = true;
            NotifyState();
            ExitRequested?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception exception) when (IsUpdateFailure(exception))
        {
            SetStatus(Activity.Idle, "UpdateRestartFailed");
        }
    }

    public async Task PrepareForExitAsync()
    {
        _exiting = true;
        _automaticTimer.Stop();
        if (CanCancel) _operationCancellation?.Cancel();
        await Initialization;
        await _operation;
        await _settingsSave;
    }

    public void ReportShutdownFailure()
    {
        _exiting = false;
        SetStatus(Activity.Idle, "UpdateRestartFailed");
    }

    private async void OnAutomaticTimer(DispatcherQueueTimer sender, object args)
    {
        if (_disposed || !AutomaticUpdatesEnabled || _exiting) return;
        TimeSpan? elapsed = _settings.LastCheckUtc is { } time ? _clock.GetUtcNow() - time : null;
        if (CanCheck && (elapsed is null || elapsed < TimeSpan.Zero || elapsed >= CheckInterval))
            await StartOperationAsync(Operation.Check, automatic: true);
        else if (CanDownload)
            await StartOperationAsync(Operation.Download, automatic: true);
        else
            ScheduleAutomatic(elapsed is { } duration && duration >= TimeSpan.Zero && duration < CheckInterval
                ? CheckInterval - duration : CheckInterval);
    }

    private void ScheduleAutomatic(TimeSpan delay)
    {
        if (_disposed || _exiting || _initializing || !AutomaticUpdatesEnabled || !IsSupported || _pending is not null) return;
        _automaticTimer.Stop();
        _automaticTimer.Interval = delay;
        _automaticTimer.Start();
    }

    private void QueueSettingsSave() => _settingsSave = SaveSettingsAsync(_settingsSave, _settings);

    private async Task SaveSettingsAsync(Task previous, UpdateSettings settings)
    {
        await previous;
        try
        {
            await _settingsStore.SaveAsync(settings);
            _settingsError = false;
        }
        catch (Exception exception) when (IsUpdateFailure(exception)) { _settingsError = true; }
        if (!_disposed)
        {
            OnPropertyChanged(nameof(HasSettingsError));
            OnPropertyChanged(nameof(LastCheckText));
            OnPropertyChanged(nameof(HasLastCheck));
        }
    }

    private void SetStatus(Activity activity, string key, string? version = null)
    {
        _activity = activity;
        SetProperty(ref _statusText, version is null ? _getString(key) : Format(key, version), nameof(StatusText));
        NotifyState();
    }

    private void NotifyState()
    {
        if (_disposed) return;
        foreach (string property in new[] { nameof(IsBusy), nameof(IsReady), nameof(CanCancel), nameof(CanCheck),
            nameof(CanDownload), nameof(CanRetry), nameof(CanChangePreference), nameof(ShowCheckAction),
            nameof(ShowDownloadAction), nameof(ShowRetryAction), nameof(IsProgressIndeterminate), nameof(ProgressPercent),
            nameof(LastCheckText), nameof(HasLastCheck) }) OnPropertyChanged(property);
        CheckUpdatesCommand.NotifyCanExecuteChanged();
        DownloadAndInstallCommand.NotifyCanExecuteChanged();
        RetryInstallationCommand.NotifyCanExecuteChanged();
        RestartAndInstallCommand.NotifyCanExecuteChanged();
        CancelUpdateCommand.NotifyCanExecuteChanged();
    }

    private string Format(string key, string value) => string.Format(CultureInfo.CurrentCulture, _getString(key), value);
    private static Version Normalize(Version version) => new(version.Major, version.Minor, Math.Max(0, version.Build));
    private static bool IsUpdateFailure(Exception exception) => exception is IOException or InvalidDataException or UnauthorizedAccessException
        or TimeoutException or HttpRequestException or JsonException or XmlException or COMException
        or InvalidOperationException or ArgumentException or Win32Exception;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _automaticTimer.Stop();
        _automaticTimer.Tick -= OnAutomaticTimer;
        _lifetime.Cancel();
        _lifetime.Dispose();
        GC.SuppressFinalize(this);
    }

    private enum Activity { Idle, Checking, Downloading, Preparing }
    private enum Operation { Check, Download, Retry }
}
