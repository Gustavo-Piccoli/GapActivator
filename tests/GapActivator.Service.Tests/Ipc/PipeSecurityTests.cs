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
        SecurityIdentifier me = WindowsIdentity.GetCurrent().User!;
        string name = $"GapActivator.Tests.{Guid.NewGuid():N}";
        using NamedPipeServerStream pipe = NamedPipeServerStreamAcl.Create(
            name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous, 0, 0,
            PipeSecurityFactory.Create(me));

        PipeSecurity security = pipe.GetAccessControl();
        List<PipeAccessRule> rules = security.GetAccessRules(true, false, typeof(SecurityIdentifier))
            .Cast<PipeAccessRule>().ToList();

        security.AreAccessRulesProtected.ShouldBeTrue();

        List<PipeAccessRule> For(string sid) =>
            rules.Where(r => r.IdentityReference.Value == sid).ToList();

        For("S-1-5-2").ShouldHaveSingleItem().AccessControlType.ShouldBe(AccessControlType.Deny);

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
