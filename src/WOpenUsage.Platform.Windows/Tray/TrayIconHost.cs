using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using WOpenUsage.Platform.Windows.Native;
using WOpenUsage.Platform.Windows.Placement;

namespace WOpenUsage.Platform.Windows.Tray;

public sealed class TrayIconHost : IDisposable
{
    private const uint CallbackMessage = NativeMethods.WmApp + 0x31;
    private const uint IconId = 1;
    private const nuint SubclassId = 0x574F5553;
    private const int MaxTooltipLength = 127;

    private readonly nint _windowHandle;
    private readonly uint _windowThreadId;
    private readonly uint _taskbarCreatedMessage;
    private readonly string _tooltip;
    private readonly TrayMenuLabels _menuLabels;
    private readonly NativeMethods.SubclassProcedure _subclassProcedure;
    private nint _iconHandle;
    private bool _iconAdded;
    private bool _subclassInstalled;
    private bool _disposed;
    private long _lastMouseActivationTick = long.MinValue;

    public TrayIconHost(
        nint windowHandle,
        string iconPath,
        string tooltip,
        TrayMenuLabels menuLabels)
    {
        if (windowHandle == 0)
        {
            throw new ArgumentException("A valid window handle is required.", nameof(windowHandle));
        }

        var windowThreadId = NativeMethods.GetWindowThreadProcessId(windowHandle, out _);
        if (windowThreadId == 0)
        {
            throw CreateWin32Exception("The tray host window handle is invalid.");
        }

        if (windowThreadId != NativeMethods.GetCurrentThreadId())
        {
            throw new InvalidOperationException(
                "The tray host must be created on the window's owning thread.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(iconPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(tooltip);
        ArgumentNullException.ThrowIfNull(menuLabels);
        ValidateLabels(menuLabels);

        if (!Path.IsPathFullyQualified(iconPath))
        {
            throw new ArgumentException("The tray icon path must be fully qualified.", nameof(iconPath));
        }

        if (tooltip.Length > MaxTooltipLength)
        {
            throw new ArgumentOutOfRangeException(
                nameof(tooltip),
                tooltip.Length,
                $"The tooltip cannot exceed {MaxTooltipLength} characters.");
        }

        _windowHandle = windowHandle;
        _windowThreadId = windowThreadId;
        _tooltip = tooltip;
        _menuLabels = menuLabels;
        _subclassProcedure = WindowSubclassProcedure;

        _taskbarCreatedMessage = NativeMethods.RegisterWindowMessage("TaskbarCreated");
        if (_taskbarCreatedMessage == 0)
        {
            throw CreateWin32Exception("The Explorer restart message could not be registered.");
        }

        try
        {
            Install(iconPath, tooltip);
        }
        catch
        {
            ReleaseNativeResources();
            throw;
        }
    }

    public event EventHandler<TrayActivatedEventArgs>? Activated;

    public event EventHandler? UpdateRequested;

    public event EventHandler? SettingsRequested;

    public event EventHandler? ExitRequested;

    public bool TryGetIconBounds(out PlatformRect bounds)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var identifier = new NativeMethods.NotifyIconIdentifier
        {
            Size = checked((uint)Marshal.SizeOf<NativeMethods.NotifyIconIdentifier>()),
            Window = _windowHandle,
            Id = IconId,
            ItemGuid = Guid.Empty,
        };

        if (NativeMethods.Shell_NotifyIconGetRect(ref identifier, out var nativeRect) != 0)
        {
            bounds = default;
            return false;
        }

        bounds = new PlatformRect(
            nativeRect.Left,
            nativeRect.Top,
            nativeRect.Right,
            nativeRect.Bottom);
        return true;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        if (_windowThreadId != NativeMethods.GetCurrentThreadId())
        {
            throw new InvalidOperationException(
                "The tray host must be disposed on the window's owning thread.");
        }

        _disposed = true;
        ReleaseNativeResources();
    }

    private void Install(string iconPath, string tooltip)
    {
        _iconHandle = NativeMethods.LoadImage(
            0,
            iconPath,
            NativeMethods.ImageIcon,
            0,
            0,
            NativeMethods.LrLoadFromFile | NativeMethods.LrDefaultSize);

        if (_iconHandle == 0)
        {
            throw CreateWin32Exception("The tray icon could not be loaded.");
        }

        if (!NativeMethods.SetWindowSubclass(
                _windowHandle,
                _subclassProcedure,
                SubclassId,
                0))
        {
            throw CreateWin32Exception("The tray message handler could not be installed.");
        }

        _subclassInstalled = true;

        AddIcon(tooltip);
    }

    private void AddIcon(string tooltip)
    {
        var data = CreateNotifyIconData(tooltip);
        if (!NativeMethods.Shell_NotifyIcon(NativeMethods.NimAdd, ref data))
        {
            _iconAdded = false;
            throw CreateWin32Exception("The tray icon could not be added.");
        }

        _iconAdded = true;
        data.TimeoutOrVersion = NativeMethods.NotifyIconVersion4;
        if (!NativeMethods.Shell_NotifyIcon(NativeMethods.NimSetVersion, ref data))
        {
            throw CreateWin32Exception("The tray icon version could not be set.");
        }
    }

    private NativeMethods.NotifyIconData CreateNotifyIconData(string tooltip)
    {
        return new NativeMethods.NotifyIconData
        {
            Size = checked((uint)Marshal.SizeOf<NativeMethods.NotifyIconData>()),
            Window = _windowHandle,
            Id = IconId,
            Flags = NativeMethods.NifMessage
                | NativeMethods.NifIcon
                | NativeMethods.NifTip
                | NativeMethods.NifShowTip,
            CallbackMessage = CallbackMessage,
            Icon = _iconHandle,
            Tip = tooltip,
            Info = string.Empty,
            InfoTitle = string.Empty,
        };
    }

    [SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "Exceptions cannot cross the unmanaged window callback boundary.")]
    private nint WindowSubclassProcedure(
        nint window,
        uint message,
        nuint wParam,
        nint lParam,
        nuint subclassId,
        nuint referenceData)
    {
        try
        {
            if (message == CallbackMessage)
            {
                HandleTrayMessage(wParam, lParam);
                return 0;
            }

            if (TrayIconRecoveryPolicy.ShouldRecover(
                    message,
                    _taskbarCreatedMessage,
                    _disposed))
            {
                RecoverIcon();
                return 0;
            }
        }
        catch (Exception)
        {
            // Native callbacks must return control to Windows even if app code fails.
        }

        return NativeMethods.DefSubclassProc(window, message, wParam, lParam);
    }

    private void RecoverIcon()
    {
        // Explorer discarded its notification area state before broadcasting
        // TaskbarCreated. Keep the loaded icon and subclass, then rebuild only
        // the missing notification entry.
        _iconAdded = false;
        AddIcon(_tooltip);
    }

    private void HandleTrayMessage(nuint wParam, nint lParam)
    {
        var packedMessage = unchecked((nuint)lParam);
        var eventCode = LowWord(packedMessage);

        if (!TrayMessageRoutingPolicy.IsForIcon(wParam, packedMessage, IconId))
        {
            return;
        }

        switch (TrayMessageClassifier.Classify(eventCode))
        {
            case TrayMessageAction.ActivateWithMouse:
                long currentTick = Environment.TickCount64;
                if (!TrayMessageRoutingPolicy.ShouldDispatchMouseActivation(
                        _lastMouseActivationTick,
                        currentTick))
                {
                    break;
                }

                _lastMouseActivationTick = currentTick;
                Activated?.Invoke(this, new TrayActivatedEventArgs(TrayActivationKind.Mouse));
                break;
            case TrayMessageAction.ActivateWithKeyboard:
                Activated?.Invoke(this, new TrayActivatedEventArgs(TrayActivationKind.Keyboard));
                break;
            case TrayMessageAction.ShowContextMenu:
                ShowContextMenu(GetContextMenuPoint(wParam));
                break;
        }
    }

    private PlatformPoint GetContextMenuPoint(nuint packedPoint)
    {
        var x = SignedLowWord(packedPoint);
        var y = SignedHighWord(packedPoint);
        if (x != -1 || y != -1)
        {
            return new PlatformPoint(x, y);
        }

        if (NativeMethods.GetCursorPos(out var cursor))
        {
            return new PlatformPoint(cursor.X, cursor.Y);
        }

        if (TryGetIconBounds(out var iconBounds))
        {
            return new PlatformPoint(iconBounds.Right, iconBounds.Top);
        }

        return default;
    }

    private void ShowContextMenu(PlatformPoint point)
    {
        var menu = NativeMethods.CreatePopupMenu();
        if (menu == 0)
        {
            throw CreateWin32Exception("The tray menu could not be created.");
        }

        try
        {
            AppendMenuItem(menu, TrayMenuCommand.Update, _menuLabels.Update);
            AppendMenuItem(menu, TrayMenuCommand.Settings, _menuLabels.Settings);
            if (!NativeMethods.AppendMenu(menu, NativeMethods.MfSeparator, 0, null))
            {
                throw CreateWin32Exception("The tray menu separator could not be added.");
            }

            AppendMenuItem(menu, TrayMenuCommand.Exit, _menuLabels.Exit);
            _ = NativeMethods.SetForegroundWindow(_windowHandle);

            var selected = (TrayMenuCommand)NativeMethods.TrackPopupMenuEx(
                menu,
                NativeMethods.TpmRightButton | NativeMethods.TpmReturnCmd,
                point.X,
                point.Y,
                _windowHandle,
                0);

            DispatchMenuCommand(selected);
            _ = NativeMethods.PostMessage(_windowHandle, NativeMethods.WmNull, 0, 0);
        }
        finally
        {
            _ = NativeMethods.DestroyMenu(menu);
        }
    }

    private static void ValidateLabels(TrayMenuLabels labels)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(labels.Update);
        ArgumentException.ThrowIfNullOrWhiteSpace(labels.Settings);
        ArgumentException.ThrowIfNullOrWhiteSpace(labels.Exit);
    }

    private static void AppendMenuItem(nint menu, TrayMenuCommand command, string label)
    {
        if (!NativeMethods.AppendMenu(menu, NativeMethods.MfString, (nuint)command, label))
        {
            throw CreateWin32Exception($"The '{command}' tray menu item could not be added.");
        }
    }

    private void DispatchMenuCommand(TrayMenuCommand command)
    {
        switch (command)
        {
            case TrayMenuCommand.Update:
                UpdateRequested?.Invoke(this, EventArgs.Empty);
                break;
            case TrayMenuCommand.Settings:
                SettingsRequested?.Invoke(this, EventArgs.Empty);
                break;
            case TrayMenuCommand.Exit:
                ExitRequested?.Invoke(this, EventArgs.Empty);
                break;
        }
    }

    private void ReleaseNativeResources()
    {
        if (_iconAdded)
        {
            var data = CreateNotifyIconData(string.Empty);
            _ = NativeMethods.Shell_NotifyIcon(NativeMethods.NimDelete, ref data);
            _iconAdded = false;
        }

        if (_subclassInstalled)
        {
            _ = NativeMethods.RemoveWindowSubclass(
                _windowHandle,
                _subclassProcedure,
                SubclassId);
            _subclassInstalled = false;
        }

        if (_iconHandle != 0)
        {
            _ = NativeMethods.DestroyIcon(_iconHandle);
            _iconHandle = 0;
        }
    }

    private static Win32Exception CreateWin32Exception(string message)
    {
        return new Win32Exception(Marshal.GetLastPInvokeError(), message);
    }

    private static uint LowWord(nuint value) => unchecked((uint)(value & 0xFFFF));

    private static int SignedLowWord(nuint value) => unchecked((short)(value & 0xFFFF));

    private static int SignedHighWord(nuint value) => unchecked((short)((value >> 16) & 0xFFFF));
}
