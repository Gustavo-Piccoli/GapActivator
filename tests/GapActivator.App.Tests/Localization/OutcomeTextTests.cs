using GapActivator.App.Localization;
using GapActivator.App.Services;
using GapActivator.App.Tests.Fakes;
using GapActivator.App.ViewModels;
using GapActivator.Ipc.Contracts;

namespace GapActivator.App.Tests.Localization;

public class OutcomeTextTests
{
    [Fact]
    public void OutcomeText_Succeeded_IsFixed()
    {
        new Localizer(AppLanguage.English).OutcomeText(RunOutcome.Succeeded).ShouldBe("Succeeded");
        new Localizer(AppLanguage.Portuguese).OutcomeText(RunOutcome.Succeeded).ShouldBe("Concluída com sucesso");
    }

    [Theory]
    [InlineData(AppLanguage.English, "Reason")]
    [InlineData(AppLanguage.Portuguese, "Motivo")]
    public void OutcomeText_Cancelled_IsNeutralAndNeverAFailure(AppLanguage language, string label)
    {
        var l = new Localizer(language);

        l.RunReason.ShouldBe(label);
        string[] banned = ["fail", "error", "falh", "erro"];
        foreach (var text in new[] { l.OutcomeText(RunOutcome.Cancelled), l.RunReason })
        {
            banned.ShouldAllBe(word => !text.Contains(word, StringComparison.OrdinalIgnoreCase));
        }
    }

    [Fact]
    public async Task Detail_Failed_ShowsTheErrorDetailUnderError()
    {
        var l = new Localizer(AppLanguage.English);
        var gateway = new FakeServiceGateway();
        var run = Dtos.RunRow(outcome: RunOutcome.Failed, exitCode: 2);
        gateway.Runs[run.Id] = Dtos.RunDetails(run, error: "exit code 2");
        using var vm = new RunDetailViewModel(l, gateway, TimeProvider.System, new TimeDisplay(l, TimeZoneInfo.Utc), new FakeClipboardService(), new ImmediateUiDispatcher());

        await vm.LoadAsync(run.Id);

        vm.ErrorLabel.ShouldBe(l.RunError);
        vm.RunErrorText.ShouldBe("exit code 2");
        vm.ErrorText.ShouldBeNull();
    }
}
