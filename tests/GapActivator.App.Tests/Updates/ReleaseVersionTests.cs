using GapActivator.App.Updates;

namespace GapActivator.App.Tests.Updates;

public class ReleaseVersionTests
{
    [Theory]
    [InlineData("v1.2.3", true, "1.2.3")]
    [InlineData("1.2", true, "1.2.0")]
    [InlineData("v1.2.3-beta.1", true, "1.2.3")]
    [InlineData("V2.0.1+build5", true, "2.0.1")]
    [InlineData("x", false, null)]
    [InlineData("", false, null)]
    [InlineData("1", false, null)]
    [InlineData("1.2.3.4", false, null)]
    public void TryParse_Tags(string tag, bool expected, string? version)
    {
        ReleaseVersion.TryParse(tag, out var parsed).ShouldBe(expected);

        parsed?.ToString().ShouldBe(version);
    }
}
