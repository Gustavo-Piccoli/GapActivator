using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using GapActivator.Service.Ipc;

namespace GapActivator.Service.Tests.Ipc;

public sealed class PipeSecurityTests
{
    [Fact]
    [Trait("Category", "Integration")]
    public void PipeSecurity_CreatedPipe_ReadsBackTheExpectedRules()
    {
        using WindowsIdentity identity = WindowsIdentity.GetCurrent();
        SecurityIdentifier me = identity.User!;
        string name = $"GapActivator.Tests.{Guid.NewGuid():N}";
        using NamedPipeServerStream pipe = NamedPipeServerStreamAcl.Create(
            name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous, 0, 0,
            PipeSecurityFactory.Create(me));

        PipeSecurity security = pipe.GetAccessControl();
        List<PipeAccessRule> rules = security.GetAccessRules(true, false, typeof(SecurityIdentifier))
            .Cast<PipeAccessRule>().ToList();

        security.AreAccessRulesProtected.ShouldBeTrue();
        security.AreAccessRulesCanonical.ShouldBeTrue();
        rules.Select(r => r.IdentityReference.Value).Distinct().Order().ShouldBe(
            new[] { "S-1-5-18", "S-1-5-32-544", "S-1-5-4", "S-1-5-2", me.Value }.Distinct().Order());

        List<PipeAccessRule> For(string sid) =>
            rules.Where(r => r.IdentityReference.Value == sid).ToList();

        PipeAccessRule network = For("S-1-5-2").ShouldHaveSingleItem();
        network.AccessControlType.ShouldBe(AccessControlType.Deny);
        network.PipeAccessRights.ShouldBe(PipeAccessRights.FullControl);

        PipeAccessRule interactive = For("S-1-5-4").ShouldHaveSingleItem();
        interactive.AccessControlType.ShouldBe(AccessControlType.Allow);
        interactive.PipeAccessRights.ShouldBe(PipeAccessRights.ReadWrite | PipeAccessRights.Synchronize);
        interactive.PipeAccessRights.HasFlag(PipeAccessRights.CreateNewInstance).ShouldBeFalse();
        interactive.PipeAccessRights.HasFlag(PipeAccessRights.ChangePermissions).ShouldBeFalse();

        foreach (string sid in new[] { "S-1-5-18", "S-1-5-32-544", me.Value })
        {
            For(sid).ShouldContain(r =>
                r.AccessControlType == AccessControlType.Allow
                && (r.PipeAccessRights & PipeAccessRights.FullControl) == PipeAccessRights.FullControl);
        }

        foreach (string sid in new[] { "S-1-1-0", "S-1-5-11", "S-1-5-32-545", "S-1-5-7" })
        {
            For(sid).ShouldBeEmpty();
        }
    }
}
