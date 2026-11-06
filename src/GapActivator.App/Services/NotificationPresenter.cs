using GapActivator.App.Localization;
using GapActivator.App.Settings;
using GapActivator.App.Shell;
using GapActivator.Ipc.Contracts;

namespace GapActivator.App.Services;

/// <summary>
/// Shows the service's notifications as tray balloons: dropped when the user turned them off, once per run, and at most one
/// balloon every <see cref="MinInterval"/>. The text is the service's own (it is not localized).
/// </summary>
public sealed class NotificationPresenter(
    IServiceGateway gateway,
    ITrayIcon tray,
    IAppSettingsService settings,
    Localizer l,
    TimeProvider time,
    IUiDispatcher ui) : IDisposable
{
    public const int RememberedRuns = 512;

    private readonly Queue<Guid> _rememberedOrder = new();
    private readonly HashSet<Guid> _remembered = [];
    private DateTimeOffset? _lastShown;
    private NotificationDto? _held;
    private int _heldCount;
    private ITimer? _holdTimer;
    private bool _started;
    private bool _disposed;

    public static TimeSpan MinInterval { get; } = TimeSpan.FromSeconds(5);

    public void Start()
    {
        if (_started || _disposed)
        {
            return;
        }

        _started = true;
        gateway.NotificationReceived += OnNotification;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_started)
        {
            gateway.NotificationReceived -= OnNotification;
        }

        // A held notification is dropped: nothing may be shown after shutdown.
        _holdTimer?.Dispose();
        _holdTimer = null;
        _held = null;
    }

    private void OnNotification(object? sender, NotificationDto notification) => ui.Post(() => Handle(notification));

    private void Handle(NotificationDto notification)
    {
        if (_disposed || !settings.Current.Notifications)
        {
            return;
        }

        if (notification.RunId is { } runId && !Remember(runId))
        {
            return;
        }

        var now = time.GetUtcNow();
        if (_held is null && (_lastShown is not { } last || now - last >= MinInterval))
        {
            Show(notification, now, more: 0);
            return;
        }

        // Within the interval: keep only the latest and count the rest.
        _held = notification;
        _heldCount++;
        if (_holdTimer is null)
        {
            var due = _lastShown is { } shownAt ? shownAt + MinInterval - now : TimeSpan.Zero;
            _holdTimer = time.CreateTimer(_ => ui.Post(ReleaseHeld), null, due < TimeSpan.Zero ? TimeSpan.Zero : due, Timeout.InfiniteTimeSpan);
        }
    }

    private void ReleaseHeld()
    {
        if (_disposed)
        {
            return;
        }

        _holdTimer?.Dispose();
        _holdTimer = null;
        var held = _held;
        var count = _heldCount;
        _held = null;
        _heldCount = 0;
        if (held is null || !settings.Current.Notifications)
        {
            return;
        }

        Show(held, time.GetUtcNow(), count - 1);
    }

    private void Show(NotificationDto notification, DateTimeOffset now, int more)
    {
        var message = more > 0 ? notification.Message + " " + l.MoreNotifications(more) : notification.Message;
        var tag = notification.RunId is { } runId ? $"run:{runId:D}" : null;
        if (tray.ShowBalloon(notification.Title, message, tag, Map(notification.Severity)))
        {
            _lastShown = now;
        }
    }

    // True when the run is new (and now remembered); false when it was already shown.
    private bool Remember(Guid runId)
    {
        if (!_remembered.Add(runId))
        {
            return false;
        }

        _rememberedOrder.Enqueue(runId);
        if (_rememberedOrder.Count > RememberedRuns)
        {
            _remembered.Remove(_rememberedOrder.Dequeue());
        }

        return true;
    }

    private static BalloonSeverity Map(NotificationSeverity severity) => severity switch
    {
        NotificationSeverity.Warning => BalloonSeverity.Warning,
        NotificationSeverity.Error => BalloonSeverity.Error,
        _ => BalloonSeverity.Information,
    };
}
