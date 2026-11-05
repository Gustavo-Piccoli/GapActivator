using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using GapActivator.App.Services;
using GapActivator.App.ViewModels;
using GapActivator.App.Views;

namespace GapActivator.App.Tests.Views;

public class DialogWindowTests(HeadlessFixture fixture)
{
    [Fact]
    public async Task Dialog_Opened_FocusesTheSecondaryButtonAndEscapeCancels()
    {
        (string? focused, bool primaryIsDefault, bool closed, DialogChoice result) = await fixture.Session.Dispatch(
            () =>
            {
                var window = new DialogWindow { DataContext = new DialogViewModel("Delete", "Sure?", "Delete", "Keep it", "Cancel") };
                var isClosed = false;
                window.Closed += (_, _) => isClosed = true;
                window.Show();
                Dispatcher.UIThread.RunJobs();
                var focusedName = (window.FocusManager!.GetFocusedElement() as Button)?.Name;
                var isDefault = window.FindControl<Button>("PrimaryButton")!.IsDefault;
                window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
                return (focusedName, isDefault, isClosed, window.Result);
            },
            TestContext.Current.CancellationToken);

        focused.ShouldBe("SecondaryButton");
        primaryIsDefault.ShouldBeFalse();
        closed.ShouldBeTrue();
        result.ShouldBe(DialogChoice.Cancel);
    }

    [Fact]
    public async Task Dialog_WithoutSecondary_FocusesCancel()
    {
        string? focused = await fixture.Session.Dispatch(
            () =>
            {
                var window = new DialogWindow { DataContext = new DialogViewModel("Stop", "Sure?", "Stop", null, "Cancel") };
                window.Show();
                Dispatcher.UIThread.RunJobs();
                var name = (window.FocusManager!.GetFocusedElement() as Button)?.Name;
                window.Close();
                return name;
            },
            TestContext.Current.CancellationToken);

        focused.ShouldBe("CancelButton");
    }
}
