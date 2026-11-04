using System.Collections;
using GapActivator.Infrastructure.Processes;

namespace GapActivator.Infrastructure.Tests.Processes;

public sealed class EnvironmentBlockBuilderTests
{
    private static readonly ParentIdentity Parent =
        new(4242, new DateTime(2030, 1, 1, 12, 0, 0, DateTimeKind.Utc).AddTicks(1_234_567));

    [Fact]
    public void EnvironmentBlockBuilder_RegistrationOverridesServiceVariableIgnoringCase()
    {
        var service = new Hashtable { ["Path"] = @"C:\Windows", ["TEMP"] = @"C:\Temp" };

        char[] block = EnvironmentBlockBuilder.Build(service, [new("PATH", @"C:\custom")], Parent);

        string[] entries = Entries(block);
        entries.Count(entry => entry.StartsWith("PATH=", StringComparison.OrdinalIgnoreCase)).ShouldBe(1);
        entries.ShouldContain(@"PATH=C:\custom");
        entries.ShouldContain(@"TEMP=C:\Temp");
    }

    [Fact]
    public void EnvironmentBlockBuilder_AddsParentIdentityAndIsSortedAndDoubleNulTerminated()
    {
        var service = new Hashtable { ["b"] = "2", ["C"] = "3", ["A"] = "1" };

        char[] block = EnvironmentBlockBuilder.Build(service, [new("d", "4")], Parent);

        new string(block).ShouldBe(
            "A=1\0b=2\0C=3\0d=4\0GAPACTIVATOR_PARENT_PID=4242\0" +
            "GAPACTIVATOR_PARENT_START_TIME=2030-01-01T12:00:00.1234567Z\0\0");
    }

    private static string[] Entries(char[] block)
    {
        new string(block).ShouldEndWith("\0\0");
        return new string(block)[..^2].Split('\0');
    }
}
