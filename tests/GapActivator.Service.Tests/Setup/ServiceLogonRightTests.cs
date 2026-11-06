using System.Diagnostics;
using System.Security.Principal;
using GapActivator.Service.Setup;
using GapActivator.Service.Tests.Support;

namespace GapActivator.Service.Tests.Setup;

public sealed class ServiceLogonRightTests
{
    [Fact]
    [Trait("Category", "Integration")]
    public void Grant_ThrowawayLocalUser_SucceedsAndIsIdempotent()
    {
        Elevation.SkipUnlessElevated();
        string user = "gatest" + Guid.NewGuid().ToString("N")[..8];

        // A throwaway local account with a generated value that is never reused or logged.
        string secret = "Aa1!" + Guid.NewGuid().ToString("N");
        Net("user", user, secret, "/add");
        try
        {
            var sid = (SecurityIdentifier)new NTAccount(Environment.MachineName + "\\" + user)
                .Translate(typeof(SecurityIdentifier));

            Should.NotThrow(() => ServiceLogonRight.Grant(sid));
            Should.NotThrow(() => ServiceLogonRight.Grant(sid));
        }
        finally
        {
            Net("user", user, "/delete");
        }
    }

    private static void Net(params string[] args)
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
        p.StandardOutput.ReadToEnd();
        p.StandardError.ReadToEnd();
        p.WaitForExit();
    }
}
