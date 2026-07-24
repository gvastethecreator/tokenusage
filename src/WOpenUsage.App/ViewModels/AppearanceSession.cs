using WOpenUsage.Core.Appearance;

namespace WOpenUsage.App.ViewModels;

public enum AppearanceSessionLoadKind
{
    Defaults,
    Loaded,
    Corrupt,
    UnsupportedVersion,
    Unavailable,
}

public enum AppearanceSessionSaveKind
{
    Saved,
    RefusedUnsupportedVersion,
    Failed,
    SkippedReadOnly,
}

/// <summary>
/// Load/save appearance settings without owning the rest of the flyout surface.
/// </summary>
public sealed class AppearanceSession
{
    private readonly AppearanceSettingsStore _store;
    private bool _isReadOnly;

    public AppearanceSession(AppearanceSettingsStore store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        Settings = AppearanceSettings.Default;
    }

    public AppearanceSettings Settings { get; private set; }

    public bool IsBusy { get; private set; }

    public bool IsReadOnly => _isReadOnly;

    public bool IsEditable => !_isReadOnly && !IsBusy;

    public AppearanceSessionLoadKind LastLoadKind { get; private set; } =
        AppearanceSessionLoadKind.Defaults;

    public AppearanceSessionSaveKind LastSaveKind { get; private set; } =
        AppearanceSessionSaveKind.Saved;

    public string? QuarantineFileName { get; private set; }

    public int? UnsupportedSchemaVersion { get; private set; }

    public bool RequiresMigration { get; private set; }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        IsBusy = true;
        QuarantineFileName = null;
        UnsupportedSchemaVersion = null;
        RequiresMigration = false;
        try
        {
            AppearanceSettingsLoadResult result = await _store
                .LoadAsync(cancellationToken)
                .ConfigureAwait(false);
            switch (result)
            {
                case AppearanceSettingsLoadResult.Loaded loaded:
                    Settings = loaded.Settings;
                    RequiresMigration = loaded.RequiresMigration;
                    _isReadOnly = false;
                    LastLoadKind = AppearanceSessionLoadKind.Loaded;
                    if (RequiresMigration)
                    {
                        AppearanceSessionSaveKind migrate = await SaveCoreAsync(
                            loaded.Settings,
                            cancellationToken).ConfigureAwait(false);
                        if (migrate == AppearanceSessionSaveKind.RefusedUnsupportedVersion)
                        {
                            LastLoadKind = AppearanceSessionLoadKind.UnsupportedVersion;
                        }
                    }

                    break;
                case AppearanceSettingsLoadResult.Defaults:
                    Settings = AppearanceSettings.Default;
                    _isReadOnly = false;
                    LastLoadKind = AppearanceSessionLoadKind.Defaults;
                    break;
                case AppearanceSettingsLoadResult.UnsupportedVersion unsupported:
                    Settings = AppearanceSettings.Default;
                    _isReadOnly = true;
                    UnsupportedSchemaVersion = unsupported.SchemaVersion;
                    LastLoadKind = AppearanceSessionLoadKind.UnsupportedVersion;
                    break;
                case AppearanceSettingsLoadResult.Corrupt corrupt:
                    Settings = AppearanceSettings.Default;
                    _isReadOnly = false;
                    QuarantineFileName = corrupt.QuarantineFileName;
                    LastLoadKind = AppearanceSessionLoadKind.Corrupt;
                    break;
                default:
                    Settings = AppearanceSettings.Default;
                    LastLoadKind = AppearanceSessionLoadKind.Defaults;
                    break;
            }
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException
            or InvalidOperationException
            or TimeoutException)
        {
            Settings = AppearanceSettings.Default;
            _isReadOnly = true;
            LastLoadKind = AppearanceSessionLoadKind.Unavailable;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public Task<AppearanceSessionSaveKind> SaveAsync(
        AppearanceSettings settings,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return SaveCoreAsync(settings, cancellationToken);
    }

    private async Task<AppearanceSessionSaveKind> SaveCoreAsync(
        AppearanceSettings settings,
        CancellationToken cancellationToken)
    {
        if (_isReadOnly)
        {
            LastSaveKind = AppearanceSessionSaveKind.SkippedReadOnly;
            return LastSaveKind;
        }

        IsBusy = true;
        try
        {
            AppearanceSettingsSaveResult result = await _store
                .SaveAsync(settings, cancellationToken)
                .ConfigureAwait(false);
            if (result is AppearanceSettingsSaveResult.Saved)
            {
                Settings = settings;
                LastSaveKind = AppearanceSessionSaveKind.Saved;
                return LastSaveKind;
            }

            if (result is AppearanceSettingsSaveResult.RefusedUnsupportedVersion unsupported)
            {
                _isReadOnly = true;
                UnsupportedSchemaVersion = unsupported.SchemaVersion;
                LastSaveKind = AppearanceSessionSaveKind.RefusedUnsupportedVersion;
                return LastSaveKind;
            }

            LastSaveKind = AppearanceSessionSaveKind.Failed;
            return LastSaveKind;
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException
            or InvalidOperationException
            or TimeoutException)
        {
            LastSaveKind = AppearanceSessionSaveKind.Failed;
            return LastSaveKind;
        }
        finally
        {
            IsBusy = false;
        }
    }
}
