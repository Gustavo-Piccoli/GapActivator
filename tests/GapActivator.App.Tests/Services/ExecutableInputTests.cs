using GapActivator.App.Services;

namespace GapActivator.App.Tests.Services;

public class ExecutableInputTests
{
    [Theory]
    [InlineData("  \"C:\\Tools\\a b.exe\"  ", "C:\\Tools\\a b.exe")]
    [InlineData("\"\"C:\\x.exe\"\"", "\"C:\\x.exe\"")]
    [InlineData("\"C:\\x.exe", "\"C:\\x.exe")]
    [InlineData("C:\\%TOOLS%\\x.exe", "C:\\%TOOLS%\\x.exe")]
    [InlineData("   ", "")]
    [InlineData(null, "")]
    [InlineData("\"", "\"")]
    public void Normalize_TrimsAndStripsOnePairOfQuotes(string? text, string expected) =>
        ExecutableInput.Normalize(text).ShouldBe(expected);

    [Theory]
    [InlineData("C:\\%TOOLS%\\x.exe", true)]
    [InlineData("C:\\100% done\\x.exe", false)]
    [InlineData("C:\\a\\x.exe", false)]
    [InlineData("%A b%", false)]
    [InlineData(null, false)]
    public void HasVariablePattern_MatchesOnlyAPercentName(string? text, bool expected) =>
        ExecutableInput.HasVariablePattern(text).ShouldBe(expected);
}
