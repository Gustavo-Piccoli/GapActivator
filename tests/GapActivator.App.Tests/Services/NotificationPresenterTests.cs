using GapActivator.App.Localization;
using GapActivator.App.Services;
using GapActivator.App.Shell;
using GapActivator.App.Tests.Fakes;
using GapActivator.Ipc.Contracts;
using Microsoft.Extensions.Time.Testing;

namespace GapActivator.App.Tests.Services;

public class NotificationPresenterTests : IDisposable
{
    private readonly FakeServiceGateway _gateway = new();
    private readonly FakeTrayIcon _tray = new();
    private readonly FakeAppSettings _settings = new();
    private readonly FakeTimeProvider _time = new(Dtos.Epoch);
    private readonly NotificationPresenter _presenter;

    public NotificationPresenterTests()
    {
        _presenter = new NotificationPresenter(
            _gateway,
            _tray,
            _settings,
            new Localizer(AppLanguage.English),
            _time,
            new ImmediateUiDispatcher());
        _presenter.Start();
    }

    public void Dispose()
    {
        _presenter.Dispose();
        GC.SuppressFinalize(this);
    }

    private static NotificationDto Note(
        Guid? runId,
        string title = "Run failed",
        string message = "It failed.",
        NotificationSeverity severity = NotificationSeverity.Error) =>
        new(severity, title, message, Guid.NewGuid(), runId);

    [Theory]
    [InlineData(NotificationSeverity.Information, BalloonSeverity.Information)]
    [InlineData(NotificationSeverity.Warning, BalloonSeverity.Warning)]
    [InlineData(NotificationSeverity.Error, BalloonSeverity.Error)]
    public void Notification_ShowsABalloonWithItsSeverity(NotificationSeverity severity, BalloonSeverity expected)
    {
        var runId = Guid.NewGuid();

        _gateway.RaiseNotification(Note(runId, "The title", "The message", severity));

        var balloon = _tray.Balloons.ShouldHaveSingleItem();
        balloon.Title.ShouldBe("The title");
        balloon.Message.ShouldBe("The message");
        balloon.Tag.ShouldBe($"run:{runId:D}");
        balloon.Severity.ShouldBe(expected);
    }

    [Fact]
    public void Notification_SameRunTwice_IsShownOnce()
    {
        var runId = Guid.NewGuid();

        _gateway.RaiseNotification(Note(runId));
        _time.Advance(TimeSpan.FromMinutes(1));
        _gateway.RaiseNotification(Note(runId, "Run failed again"));
        _time.Advance(TimeSpan.FromMinutes(1));

        _tray.Balloons.Count.ShouldBe(1);
    }

    [Fact]
    public void Notification_WithoutRunId_IsAlwaysShown()
    {
        _gateway.RaiseNotification(Note(null));
        _time.Advance(TimeSpan.FromMinutes(1));
        _gateway.RaiseNotification(Note(null));

        _tray.Balloons.Count.ShouldBe(2);
        _tray.Balloons.ShouldAllBe(b => b.Tag == null);
    }

    [Fact]
    public void Notification_Disabled_ShowsNothing()
    {
        _settings.Update(s => s with { Notifications = false });

        _gateway.RaiseNotification(Note(Guid.NewGuid()));
        _time.Advance(TimeSpan.FromMinutes(1));

        _tray.Balloons.ShouldBeEmpty();
    }

    [Fact]
    public void Notification_TurnedOffWhileHeld_IsNotShown()
    {
        _gateway.RaiseNotification(Note(Guid.NewGuid()));
        _gateway.RaiseNotification(Note(Guid.NewGuid()));
        _settings.Update(s => s with { Notifications = false });

        _time.Advance(TimeSpan.FromMinutes(1));

        _tray.Balloons.Count.ShouldBe(1);
    }

    [Fact]
    public void NotificationBurst_AtMostOneEvery5Seconds_TheNextCarriesTheCount()
    {
        var tenth = Guid.NewGuid();
        for (var i = 0; i < 10; i++)
        {
            _gateway.RaiseNotification(Note(i == 9 ? tenth : Guid.NewGuid(), message: $"Message {i}"));
            _time.Advance(TimeSpan.FromMilliseconds(100));
        }

        _tray.Balloons.Count.ShouldBe(1);
        _tray.Balloons[0].Message.ShouldBe("Message 0");

        _time.Advance(TimeSpan.FromMilliseconds(3999));
        _tray.Balloons.Count.ShouldBe(1);

        _time.Advance(TimeSpan.FromMilliseconds(1));
        _tray.Balloons.Count.ShouldBe(2);
        _tray.Balloons[1].Message.ShouldBe("Message 9 (+8 more)");
        _tray.Balloons[1].Tag.ShouldBe($"run:{tenth:D}");

        _time.Advance(TimeSpan.FromMinutes(1));
        _tray.Balloons.Count.ShouldBe(2);
    }

    [Fact]
    public void NotificationBurst_TwoInTheWindow_ShowsTheSecondWithoutACount()
    {
        _gateway.RaiseNotification(Note(Guid.NewGuid(), message: "First"));
        _time.Advance(TimeSpan.FromSeconds(1));
        _gateway.RaiseNotification(Note(Guid.NewGuid(), message: "Second"));

        _time.Advance(TimeSpan.FromSeconds(4));

        _tray.Balloons.Select(b => b.Message).ShouldBe(["First", "Second"]);
    }

    [Fact]
    public void Notification_AfterTheIntervalWithNothingHeld_ShowsAtOnce()
    {
        _gateway.RaiseNotification(Note(null, message: "First"));
        _time.Advance(NotificationPresenter.MinInterval);

        _gateway.RaiseNotification(Note(null, message: "Second"));

        _tray.Balloons.Select(b => b.Message).ShouldBe(["First", "Second"]);
    }

    [Fact]
    public void Notification_WhenTheIconIsNotReady_DoesNotStartTheInterval()
    {
        _tray.BalloonResult = false;
        _gateway.RaiseNotification(Note(null, message: "Lost"));
        _tray.BalloonResult = true;

        _gateway.RaiseNotification(Note(null, message: "Shown"));

        _tray.Balloons.Select(b => b.Message).ShouldBe(["Shown"]);
    }

    [Fact]
    public void Dispose_WithAHeldNotification_DropsItSilently()
    {
        _gateway.RaiseNotification(Note(null, message: "First"));
        _gateway.RaiseNotification(Note(null, message: "Held"));

        _presenter.Dispose();
        _time.Advance(TimeSpan.FromMinutes(1));
        _gateway.RaiseNotification(Note(null, message: "After"));

        _tray.Balloons.Select(b => b.Message).ShouldBe(["First"]);
    }

    [Fact]
    public void RememberedRuns_KeepsAtMost512()
    {
        var runs = Enumerable.Range(0, NotificationPresenter.RememberedRuns + 88).Select(_ => Guid.NewGuid()).ToList();
        foreach (var run in runs)
        {
            _gateway.RaiseNotification(Note(run));
            _time.Advance(NotificationPresenter.MinInterval);
        }

        _tray.Balloons.Count.ShouldBe(runs.Count);

        // The oldest 88 were forgotten; the 89th is the oldest one still remembered.
        _gateway.RaiseNotification(Note(runs[88]));
        _tray.Balloons.Count.ShouldBe(runs.Count);

        _time.Advance(NotificationPresenter.MinInterval);
        _gateway.RaiseNotification(Note(runs[87]));
        _tray.Balloons.Count.ShouldBe(runs.Count + 1);
    }
}
