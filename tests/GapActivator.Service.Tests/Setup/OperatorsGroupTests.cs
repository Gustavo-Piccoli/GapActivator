using System.Diagnostics;
using System.Security.Principal;
using GapActivator.Service.Setup;
using GapActivator.Service.Tests.Support;

namespace GapActivator.Service.Tests.Setup;

public sealed class OperatorsGroupTests
{
    [Fact]
    [Trait("Category", "Integration")]
    public void AddOperator_ThenRemove_IsIdempotentAndCleansUp()
    {
        Elevation.SkipUnlessElevated();
        Assert.SkipWhen(GroupExists(), "A real GapActivator Operators group exists; the test would delete it.");
        string user = "gatest" + Guid.NewGuid().ToString("N")[..8];

        // A throwaway local account with a generated value that is never reused or logged. net.exe asks for
        // confirmation (and fails without a console) when the value is longer than 14 characters, so keep it at 14.
        Run("user", user, "Aa1!" + Guid.NewGuid().ToString("N")[..10], "/add").Code.ShouldBe(0);
        try
        {
            var sid = (SecurityIdentifier)new NTAccount(Environment.MachineName + "\\" + user)
                .Translate(typeof(SecurityIdentifier));

            Should.NotThrow(() => OperatorsGroup.AddOperator(sid));
            Should.NotThrow(() => OperatorsGroup.AddOperator(sid));
            GroupExists().ShouldBeTrue();
            Net("localgroup", OperatorsGroup.Name).ShouldContain(user);
        }
        finally
        {
            OperatorsGroup.Remove();
            Net("user", user, "/delete");
        }

        GroupExists().ShouldBeFalse();
        Should.NotThrow(OperatorsGroup.Remove);
    }

    private static bool GroupExists() => Run("localgroup", OperatorsGroup.Name).Code == 0;

    private static string Net(params string[] args) => Run(args).Output;

    private static (int Code, string Output) Run(params string[] args)
    {
        var info = new ProcessStartInfo("net.exe")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (string a in args)
        {
            info.ArgumentList.Add(a);
        }

        using Process p = Process.Start(info)!;
        string output = p.StandardOutput.ReadToEnd();
        p.StandardError.ReadToEnd();
        p.WaitForExit();
        return (p.ExitCode, output);
    }
}
