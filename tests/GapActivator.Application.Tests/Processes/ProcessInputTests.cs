using GapActivator.Application.Processes;

namespace GapActivator.Application.Tests.Processes;

public class ProcessInputTests
{
    [Fact]
    public void EnvironmentVariableInput_ToString_DoesNotExposeSecretValue()
    {
        var input = new EnvironmentVariableInput("API_TOKEN", "s3cret-value", true);

        string text = input.ToString();

        text.ShouldNotContain("s3cret-value");
        text.ShouldContain("API_TOKEN");
        text.ShouldContain("***");
    }

    [Fact]
    public void EnvironmentVariableInput_ToString_ShowsPlainValue()
    {
        new EnvironmentVariableInput("MODE", "fast", false).ToString().ShouldContain("fast");
    }

    [Fact]
    public void RegisterProcess_ToString_DoesNotExposeSecretValue()
    {
        var command = new RegisterProcess(ProcessDefinitions.Basic(
            environment: [new EnvironmentVariableInput("API_TOKEN", "s3cret-value", true)]));

        command.ToString().ShouldNotContain("s3cret-value");
    }
}
