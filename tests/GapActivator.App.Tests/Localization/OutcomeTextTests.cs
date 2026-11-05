using GapActivator.App.Localization;
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
}
