using static GapActivator.App.Shell.ShellNativeMethods;

namespace GapActivator.App.Shell;

/// <summary>The notification-area icon, balloons and context menu of the app.</summary>
public sealed unsafe class Win32TrayIcon : ITrayIcon
{
    private const uint IconId = 1;
    private const uint CallbackMessage = WM_APP + 1;
    private const int MenuIdBase = 1000;

    private readonly ShellWindow _window;
    private readonly IReadOnlyDictionary<TrayIconState, byte[]> _icoFiles;
    private readonly Dictionary<TrayIconState, nint> _icons = [];
    private TrayIconState _state;
    private string _tooltip = string.Empty;
    private readonly BalloonTagState _balloon = new();
    private bool _disposed;
    private bool _desired;

    public Win32TrayIcon(ShellWindow window, IReadOnlyDictionary<TrayIconState, byte[]> icoFiles)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(icoFiles);
        _window = window;
        _icoFiles = icoFiles;
        _window.MessageReceived += OnMessage;
    }

    public event EventHandler? OpenRequested;

    public event EventHandler? MenuRequested;

    public event EventHandler<string?>? BalloonClicked;

    internal bool IsAdded { get; private set; }

    internal int ReaddCount { get; private set; }

    /// <summary>Test seam: the dwInfoFlags of the last balloon sent.</summary>
    internal uint LastBalloonFlags { get; private set; }

    /// <summary>Test seam: makes the next add attempt fail as if the taskbar did not exist yet.</summary>
    internal bool FailNextAdd { get; set; }

    public void SetState(TrayIconState state, string tooltip)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _state = state;
        _tooltip = tooltip ?? string.Empty;
        _desired = true;
        if (IsAdded)
        {
            var data = NewData(NIF_ICON | NIF_TIP | NIF_SHOWTIP);
            Fill(ref data);
            Shell_NotifyIconW(NIM_MODIFY, in data);
        }
        else
        {
            Add();
        }
    }

    public bool ShowBalloon(string title, string message, string? tag, BalloonSeverity severity)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!IsAdded)
        {
            return false;
        }

        _balloon.Shown(tag);
        var data = NewData(NIF_INFO);
        data.hWnd = _window.Handle;
        CopyTo(data.szInfoTitle, 64, title);
        CopyTo(data.szInfo, 256, message);
        data.dwInfoFlags = severity switch
        {
            BalloonSeverity.Warning => NIIF_WARNING,
            BalloonSeverity.Error => NIIF_ERROR,
            _ => NIIF_INFO,
        };
        LastBalloonFlags = data.dwInfoFlags;
        Shell_NotifyIconW(NIM_MODIFY, in data);
        return true;
    }

    public string? ShowMenu(IReadOnlyList<TrayMenuItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        ObjectDisposedException.ThrowIf(_disposed, this);
        var ids = new Dictionary<int, string>();
        var next = MenuIdBase;
        var menu = CreatePopupMenu();
        if (menu == 0)
        {
            return null;
        }

        try
        {
            Build(menu, items, ids, ref next);
            GetCursorPos(out var cursor);
            SetForegroundWindow(_window.Handle);
            var command = TrackPopupMenuEx(menu, TPM_RETURNCMD | TPM_RIGHTBUTTON | TPM_NONOTIFY, cursor.X, cursor.Y, _window.Handle, 0);
            PostMessageW(_window.Handle, WM_NULL, 0, 0);
            return ids.TryGetValue(command, out var id) ? id : null;
        }
        finally
        {
            DestroyMenu(menu);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _desired = false;
        _window.MessageReceived -= OnMessage;
        if (IsAdded)
        {
            var data = NewData(0);
            data.hWnd = _window.Handle;
            Shell_NotifyIconW(NIM_DELETE, in data);
            IsAdded = false;
        }

        foreach (var icon in _icons.Values)
        {
            DestroyIcon(icon);
        }

        _icons.Clear();
    }

    private static void Build(nint menu, IReadOnlyList<TrayMenuItem> items, Dictionary<int, string> ids, ref int next)
    {
        foreach (var item in items)
        {
            if (item.Id == TrayMenuItem.Separator.Id)
            {
                AppendMenuW(menu, MF_SEPARATOR, 0, null);
            }
            else if (item.Children is { Count: > 0 } children)
            {
                var sub = CreatePopupMenu();
                Build(sub, children, ids, ref next);
                AppendMenuW(menu, MF_POPUP, (nuint)sub, item.Text);
            }
            else
            {
                ids[next] = item.Id;
                AppendMenuW(menu, MF_STRING, (nuint)next, item.Text);
                next++;
            }
        }
    }

    private static void CopyTo(char* destination, int capacity, string text)
    {
        var length = Math.Min(text.Length, capacity - 1);
        for (var i = 0; i < length; i++)
        {
            destination[i] = text[i];
        }

        destination[length] = '\0';
    }

    private static NOTIFYICONDATAW NewData(uint flags)
    {
        var data = default(NOTIFYICONDATAW);
        data.cbSize = (uint)sizeof(NOTIFYICONDATAW);
        data.uID = IconId;
        data.uFlags = flags;
        return data;
    }

    private bool Add()
    {
        if (FailNextAdd)
        {
            FailNextAdd = false;
            return false;
        }

        var data = NewData(NIF_MESSAGE | NIF_ICON | NIF_TIP | NIF_SHOWTIP);
        data.uCallbackMessage = CallbackMessage;
        Fill(ref data);
        if (!Shell_NotifyIconW(NIM_ADD, in data))
        {
            return false;
        }

        data.uTimeoutOrVersion = NOTIFYICON_VERSION_4;
        Shell_NotifyIconW(NIM_SETVERSION, in data);
        IsAdded = true;
        return true;
    }

    private void Fill(ref NOTIFYICONDATAW data)
    {
        data.hWnd = _window.Handle;
        data.hIcon = GetIcon(_state);
        fixed (char* tip = data.szTip)
        {
            CopyTo(tip, 128, _tooltip);
        }
    }

    private nint GetIcon(TrayIconState state)
    {
        if (_icons.TryGetValue(state, out var existing))
        {
            return existing;
        }

        if (!_icoFiles.TryGetValue(state, out var bytes))
        {
            return 0;
        }

        var size = GetSystemMetrics(SM_CXSMICON);
        var image = IcoImage.SelectImage(bytes, size);
        var icon = CreateIconFromResourceEx(image, (uint)image.Length, true, 0x00030000, size, size, 0);
        if (icon != 0)
        {
            _icons[state] = icon;
        }

        return icon;
    }

    private void OnMessage(uint msg, nint wParam, nint lParam)
    {
        if (msg == ShellWindow.TaskbarCreatedMessage)
        {
            if (_desired)
            {
                // Drop any stale entry first so the add cannot fail as a duplicate.
                var stale = NewData(0);
                stale.hWnd = _window.Handle;
                Shell_NotifyIconW(NIM_DELETE, in stale);
                IsAdded = false;
                if (Add())
                {
                    ReaddCount++;
                }
            }

            return;
        }

        if (msg != CallbackMessage)
        {
            return;
        }

        switch ((uint)((long)lParam & 0xFFFF))
        {
            case WM_CONTEXTMENU:
                MenuRequested?.Invoke(this, EventArgs.Empty);
                break;
            case WM_LBUTTONDBLCLK:
                OpenRequested?.Invoke(this, EventArgs.Empty);
                break;
            case NIN_BALLOONTIMEOUT:
            case NIN_BALLOONHIDE:
                _balloon.Ended();
                break;
            case NIN_BALLOONUSERCLICK:
                BalloonClicked?.Invoke(this, _balloon.Clicked());
                break;
        }
    }
}
