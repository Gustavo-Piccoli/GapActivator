namespace GapActivator.App.Tests;

public class StartupOptionsTests
{
    [Theory]
    [InlineData("--minimized", true)]
    [InlineData("--MINIMIZED", true)]
    [InlineData(null, false)]
    public void Parse_Minimized_IgnoresCase(string? arg, bool expected)
    {
        string[] args = arg is null ? [] : [arg];

        StartupOptions.Parse(args).Minimized.ShouldBe(expected);
    }
}
