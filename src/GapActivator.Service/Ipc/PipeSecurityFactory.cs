using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;

namespace GapActivator.Service.Ipc;

internal static class PipeSecurityFactory
{
    public static PipeSecurity Create(SecurityIdentifier serviceIdentity)
    {
        var security = new PipeSecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);

        void Allow(WellKnownSidType sid, PipeAccessRights rights) =>
            security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(sid, null), rights, AccessControlType.Allow));

        Allow(WellKnownSidType.LocalSystemSid, PipeAccessRights.FullControl);
        Allow(WellKnownSidType.BuiltinAdministratorsSid, PipeAccessRights.FullControl);
        security.AddAccessRule(new PipeAccessRule(serviceIdentity, PipeAccessRights.FullControl, AccessControlType.Allow));
        Allow(WellKnownSidType.InteractiveSid, PipeAccessRights.ReadWrite | PipeAccessRights.Synchronize);
        security.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.NetworkSid, null), PipeAccessRights.FullControl, AccessControlType.Deny));
        return security;
    }
}
