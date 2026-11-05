using GapActivator.App.Shell;

namespace GapActivator.App.Tests.Shell;

[Collection("ShellWin32")]
[Trait("Category", "Integration")]
public sealed class ShellWindowTests
{
    private static readonly uint Activate = ShellTestNative.RegisterWindowMessageW("GapActivator.Activate");

    [Fact]
    public void Create_HasHandleAndIsFindableByClass()
    {
        using var window = new ShellWindow();

        window.Handle.ShouldNotBe(0);
        ShellTestNative.ClassWindowExists(window.Handle).ShouldBeTrue();
        ShellTestNative.FindWindowW("GapActivator.ShellWindow", null).ShouldNotBe(0);
    }

    [Fact]
    public void ActivateMessage_RaisesActivationRequested()
    {
        using var window = new ShellWindow();
        var raised = 0;
        window.ActivationRequested += (_, _) => raised++;

        ShellTestNative.SendMessageW(window.Handle, Activate, 0, 0);

        raised.ShouldBe(1);
    }

    [Fact]
    public void SignalExisting_WithAShellWindow_ReturnsTrueAndActivationArrives()
    {
        using var window = new ShellWindow();
        var raised = 0;
        window.ActivationRequested += (_, _) => raised++;

        SingleInstance.SignalExisting().ShouldBeTrue();

        ShellTestNative.PumpUntil(() => raised > 0, TimeSpan.FromSeconds(2)).ShouldBeTrue();
        raised.ShouldBe(1);
    }

    [Fact]
    public void EndSession_RaisesSessionEnding()
    {
        using var window = new ShellWindow();
        var raised = 0;
        window.SessionEnding += (_, _) => raised++;

        ShellTestNative.SendMessageW(window.Handle, ShellTestNative.WM_ENDSESSION, 0, 0);
        raised.ShouldBe(0);
        ShellTestNative.SendMessageW(window.Handle, ShellTestNative.WM_ENDSESSION, 1, 0);

        raised.ShouldBe(1);
    }

    [Fact]
    public void ThrowingHandler_RaisesHandlerFailedAndQueryEndSessionStillReturnsOne()
    {
        using var window = new ShellWindow();
        Exception? failure = null;
        window.HandlerFailed += e => failure = e;
        window.ActivationRequested += (_, _) => throw new InvalidOperationException("boom");

        ShellTestNative.SendMessageW(window.Handle, Activate, 0, 0);
        failure.ShouldBeOfType<InvalidOperationException>();

        failure = null;
        window.HandlerFailed += _ => throw new InvalidOperationException("reporter fails too");
        ShellTestNative.SendMessageW(window.Handle, Activate, 0, 0);
        failure.ShouldNotBeNull();

        // A throwing observer during WM_QUERYENDSESSION must still let Windows proceed.
        failure = null;
        window.MessageReceived += (msg, _, _) =>
        {
            if (msg == ShellTestNative.WM_QUERYENDSESSION)
            {
                throw new InvalidOperationException("observer fails");
            }
        };
        ShellTestNative.SendMessageW(window.Handle, ShellTestNative.WM_QUERYENDSESSION, 0, 0).ShouldBe(1);
        failure.ShouldBeOfType<InvalidOperationException>();
    }

    [Fact]
    public void Dispose_DestroysWindow()
    {
        var window = new ShellWindow();
        var handle = window.Handle;

        window.Dispose();
        window.Dispose();

        ShellTestNative.IsWindow(handle).ShouldBeFalse();
        window.Handle.ShouldBe(0);
    }
}
