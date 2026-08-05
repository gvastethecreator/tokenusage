using TokenUsage.Core.Layout;

namespace TokenUsage.App.ViewModels;

public enum DashboardLayoutEditorLoadKind
{
    Empty,
    Loaded,
    Corrupt,
    UnsupportedVersion,
    Unavailable,
}

public enum DashboardLayoutEditorSaveKind
{
    Saved,
    Unchanged,
    RefusedUnsupportedVersion,
    Failed,
    SkippedReadOnly,
    SkippedBusy,
}

/// <summary>
/// Dashboard layout load/mutate/persist session with undo history.
/// Sole product path for layout mutation when wired from FlyoutViewModel.
/// </summary>
public sealed class DashboardLayoutEditor
{
    private readonly DashboardLayoutStore _store;
    private readonly DashboardLayoutSessionHistory _history = new();
    private bool _isReadOnly;

    public DashboardLayoutEditor(DashboardLayoutStore store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        Layout = DashboardLayout.Empty;
    }

    public DashboardLayout Layout { get; private set; }

    public bool IsBusy { get; private set; }

    public bool IsReadOnly => _isReadOnly;

    public bool IsEditable => !_isReadOnly && !IsBusy;

    public bool CanUndo => _history.CanUndo && IsEditable;

    public DashboardLayoutEditorLoadKind LastLoadKind { get; private set; } =
        DashboardLayoutEditorLoadKind.Empty;

    public DashboardLayoutEditorSaveKind LastSaveKind { get; private set; } =
        DashboardLayoutEditorSaveKind.Saved;

    public string? QuarantineFileName { get; private set; }

    public int? UnsupportedSchemaVersion { get; private set; }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        IsBusy = true;
        QuarantineFileName = null;
        UnsupportedSchemaVersion = null;
        try
        {
            DashboardLayoutLoadResult result = await _store
                .LoadAsync(cancellationToken)
                .ConfigureAwait(false);
            switch (result)
            {
                case DashboardLayoutLoadResult.Loaded loaded:
                    Layout = loaded.Layout;
                    _isReadOnly = false;
                    LastLoadKind = DashboardLayoutEditorLoadKind.Loaded;
                    break;
                case DashboardLayoutLoadResult.Empty:
                    Layout = DashboardLayout.Empty;
                    _isReadOnly = false;
                    LastLoadKind = DashboardLayoutEditorLoadKind.Empty;
                    break;
                case DashboardLayoutLoadResult.UnsupportedVersion unsupported:
                    Layout = DashboardLayout.Empty;
                    _isReadOnly = true;
                    UnsupportedSchemaVersion = unsupported.SchemaVersion;
                    LastLoadKind = DashboardLayoutEditorLoadKind.UnsupportedVersion;
                    break;
                case DashboardLayoutLoadResult.Corrupt corrupt:
                    Layout = DashboardLayout.Empty;
                    _isReadOnly = false;
                    QuarantineFileName = corrupt.QuarantineFileName;
                    LastLoadKind = DashboardLayoutEditorLoadKind.Corrupt;
                    break;
                default:
                    Layout = DashboardLayout.Empty;
                    LastLoadKind = DashboardLayoutEditorLoadKind.Empty;
                    break;
            }

            _history.Clear();
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException
            or TimeoutException)
        {
            Layout = DashboardLayout.Empty;
            _isReadOnly = true;
            LastLoadKind = DashboardLayoutEditorLoadKind.Unavailable;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task<DashboardLayoutEditorSaveKind> MutateAsync(
        Func<DashboardLayout, DashboardLayout> mutation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(mutation);
        if (IsBusy)
        {
            LastSaveKind = DashboardLayoutEditorSaveKind.SkippedBusy;
            return LastSaveKind;
        }

        if (_isReadOnly)
        {
            LastSaveKind = DashboardLayoutEditorSaveKind.SkippedReadOnly;
            return LastSaveKind;
        }

        DashboardLayout previous = Layout;
        DashboardLayout next = mutation(previous);
        if (ReferenceEquals(previous, next) || Equals(previous, next))
        {
            LastSaveKind = DashboardLayoutEditorSaveKind.Unchanged;
            return LastSaveKind;
        }

        IsBusy = true;
        try
        {
            DashboardLayoutSaveResult result = await _store
                .SaveAsync(next, cancellationToken)
                .ConfigureAwait(false);
            if (result is DashboardLayoutSaveResult.Saved)
            {
                _history.Record(previous);
                Layout = next;
                LastSaveKind = DashboardLayoutEditorSaveKind.Saved;
                return LastSaveKind;
            }

            if (result is DashboardLayoutSaveResult.RefusedUnsupportedVersion unsupported)
            {
                _isReadOnly = true;
                UnsupportedSchemaVersion = unsupported.SchemaVersion;
                LastSaveKind = DashboardLayoutEditorSaveKind.RefusedUnsupportedVersion;
                return LastSaveKind;
            }

            LastSaveKind = DashboardLayoutEditorSaveKind.Failed;
            return LastSaveKind;
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException
            or InvalidOperationException
            or TimeoutException)
        {
            LastSaveKind = DashboardLayoutEditorSaveKind.Failed;
            return LastSaveKind;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task<DashboardLayoutEditorSaveKind> UndoAsync(
        CancellationToken cancellationToken = default)
    {
        if (IsBusy)
        {
            LastSaveKind = DashboardLayoutEditorSaveKind.SkippedBusy;
            return LastSaveKind;
        }

        if (!CanUndo || !_history.TryPeek(out DashboardLayout? previous) || previous is null)
        {
            LastSaveKind = DashboardLayoutEditorSaveKind.Unchanged;
            return LastSaveKind;
        }

        IsBusy = true;
        try
        {
            DashboardLayoutSaveResult result = await _store
                .SaveAsync(previous, cancellationToken)
                .ConfigureAwait(false);
            if (result is DashboardLayoutSaveResult.Saved)
            {
                if (!_history.CommitUndo(previous))
                {
                    throw new InvalidOperationException(
                        "Dashboard undo history changed unexpectedly.");
                }

                Layout = previous;
                LastSaveKind = DashboardLayoutEditorSaveKind.Saved;
                return LastSaveKind;
            }

            if (result is DashboardLayoutSaveResult.RefusedUnsupportedVersion unsupported)
            {
                _isReadOnly = true;
                UnsupportedSchemaVersion = unsupported.SchemaVersion;
                LastSaveKind = DashboardLayoutEditorSaveKind.RefusedUnsupportedVersion;
                return LastSaveKind;
            }

            LastSaveKind = DashboardLayoutEditorSaveKind.Failed;
            return LastSaveKind;
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException
            or InvalidOperationException
            or TimeoutException)
        {
            LastSaveKind = DashboardLayoutEditorSaveKind.Failed;
            return LastSaveKind;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public Task<DashboardLayoutEditorSaveKind> ResetAsync(
        CancellationToken cancellationToken = default) =>
        MutateAsync(_ => DashboardLayout.Empty, cancellationToken);

    public void MarkReadOnly() => _isReadOnly = true;
}
