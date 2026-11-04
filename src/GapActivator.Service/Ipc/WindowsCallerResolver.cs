using System.IO.Pipes;
using System.Security.Principal;
using GapActivator.Application.Messaging;

namespace GapActivator.Service.Ipc;

internal sealed class WindowsCallerResolver(AuthorizationOptions options) : ICallerResolver
{
    public bool OperatorsGroupEnabled { get; } = ComputeEnabled(options);

    public Caller Resolve(NamedPipeServerStream pipe)
    {
        // Translated on every call: the group can be created or removed while the service runs.
        SecurityIdentifier? group = null;
        try
        {
            group = (SecurityIdentifier)new NTAccount($"{Environment.MachineName}\\{AuthorizationOptions.OperatorsGroupName}")
                .Translate(typeof(SecurityIdentifier));
        }
        catch (IdentityNotMappedException)
        {
        }

        string name = "";
        bool isAdmin = false;
        bool isOperator = false;
        pipe.RunAsClient(() =>
        {
            using WindowsIdentity id = WindowsIdentity.GetCurrent(TokenAccessLevels.Query);
            var principal = new WindowsPrincipal(id);
            name = id.Name;

            // IsInRole(SecurityIdentifier) uses CheckTokenMembership, so the deny-only
            // Administrators SID of a UAC-filtered token does not count. Never use id.Groups.
            isAdmin = principal.IsInRole(new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null));
            isOperator = group is not null && principal.IsInRole(group);
        });

        return CallerRules.CanChange(isAdmin, isOperator, OperatorsGroupEnabled)
            ? Caller.Operator(name)
            : Caller.Viewer(name);
    }

    private static bool ComputeEnabled(AuthorizationOptions options)
    {
        using WindowsIdentity current = WindowsIdentity.GetCurrent();
        return CallerRules.IsOperatorsGroupEnabled(options.OperatorsGroup, current.User!);
    }
}
