using GapActivator.App.Shell;

namespace GapActivator.App.Tests.Shell;

[Collection("ShellWin32")]
[Trait("Category", "Integration")]
public sealed class Win32TrayIconTests
{
    private static Dictionary<TrayIconState, byte[]> Icons() =>
        TrayIconImages.Colors.ToDictionary(c => c.Key, c => TrayIconImages.Create(c.Value));

    [Fact]
    public void SetState_AddsIconThenUpdates()
    {
        using var window = new ShellWindow();
        using var tray = new Win32TrayIcon(window, Icons());
        tray.IsAdded.ShouldBeFalse();

        tray.SetState(TrayIconState.Healthy, "GapActivator test");
        tray.IsAdded.ShouldBeTrue();
        tray.SetState(TrayIconState.Attention, "GapActivator test 2");

        tray.IsAdded.ShouldBeTrue();
    }

    [Fact]
    public void ShowBalloon_BeforeAdd_ReturnsFalse()
    {
        using var window = new ShellWindow();
        using var tray = new Win32TrayIcon(window, Icons());

        tray.ShowBalloon("t", "m", null, BalloonSeverity.Information).ShouldBeFalse();
    }

    [Fact]
    public void ShowBalloon_ErrorSeverity_UsesTheErrorFlag()
    {
        using var window = new ShellWindow();
        using var tray = new Win32TrayIcon(window, Icons());
        tray.SetState(TrayIconState.Healthy, "GapActivator test");

        tray.ShowBalloon("t", "m", "tag", BalloonSeverity.Error).ShouldBeTrue();

        (tray.LastBalloonFlags & 0xF).ShouldBe(3u);
    }

    [Theory]
    [InlineData(BalloonSeverity.Information, 1u)]
    [InlineData(BalloonSeverity.Warning, 2u)]
    public void ShowBalloon_OtherSeverities_UseTheirFlags(BalloonSeverity severity, uint expected)
    {
        using var window = new ShellWindow();
        using var tray = new Win32TrayIcon(window, Icons());
        tray.SetState(TrayIconState.Healthy, "GapActivator test");

        tray.ShowBalloon("t", "m", null, severity).ShouldBeTrue();

        (tray.LastBalloonFlags & 0xF).ShouldBe(expected);
    }

    [Fact]
    public void TaskbarCreated_ReaddsIcon()
    {
        using var window = new ShellWindow();
        using var tray = new Win32TrayIcon(window, Icons());
        tray.SetState(TrayIconState.Busy, "GapActivator test");

        ShellTestNative.SendMessageW(window.Handle, ShellTestNative.RegisterWindowMessageW("TaskbarCreated"), 0, 0);

        tray.ReaddCount.ShouldBe(1);
        tray.IsAdded.ShouldBeTrue();
    }

    [Fact]
    public void Dispose_RemovesIcon()
    {
        using var window = new ShellWindow();
        var tray = new Win32TrayIcon(window, Icons());
        try
        {
            tray.SetState(TrayIconState.Unreachable, "GapActivator test");

            tray.Dispose();

            tray.IsAdded.ShouldBeFalse();
        }
        finally
        {
            tray.Dispose();
        }
    }
}
