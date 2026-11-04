using System.ComponentModel;
using System.IO.Pipes;
using System.Security;
using System.Security.Principal;
using GapActivator.Application.Messaging;

namespace GapActivator.Service.Ipc;

internal sealed class WindowsCallerResolver(AuthorizationOptions options) : ICallerResolver
{
    // Not lazy on purpose: this also preloads the assemblies that cannot be loaded later,
    // while a thread impersonates an Identification-level client token.
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

        (SecurityIdentifier user, bool isAdmin, bool isOperator) = ReadClient(pipe, group);

        string name;
        try
        {
            name = user.Translate(typeof(NTAccount)).Value;
        }
        catch (IdentityNotMappedException)
        {
            name = user.Value;
        }

        return CallerRules.CanChange(isAdmin, isOperator, OperatorsGroupEnabled)
            ? Caller.Operator(name)
            : Caller.Viewer(name);
    }

    /// <summary>
    /// Impersonates the client on a dedicated thread that does not inherit the caller's ExecutionContext,
    /// so no impersonation state can reach the caller's async flow.
    /// </summary>
    private static (SecurityIdentifier User, bool IsAdmin, bool IsOperator) ReadClient(
        NamedPipeServerStream pipe, SecurityIdentifier? group)
    {
        (SecurityIdentifier, bool, bool) result = default;
        Exception? failure = null;

        var thread = new Thread(() =>
        {
            try
            {
                pipe.RunAsClient(() =>
                {
                    using WindowsIdentity id = WindowsIdentity.GetCurrent(TokenAccessLevels.Query);

                    // Touch only the SID and IsInRole(SecurityIdentifier) (CheckTokenMembership, so the
                    // deny-only Administrators SID of a UAC-filtered token is not admin). Never Name or
                    // Groups: Name impersonates through an AsyncLocal that outlives RunAsClient.
                    var principal = new WindowsPrincipal(id);
                    result = (
                        id.User ?? throw new UnauthorizedAccessException("The client has no user SID."),
                        principal.IsInRole(new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null)),
                        group is not null && principal.IsInRole(group));
                });
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        })
        {
            IsBackground = true,
        };
        thread.UnsafeStart();
        thread.Join();

        return failure switch
        {
            null => result,
            UnauthorizedAccessException => throw failure,
            IOException or SecurityException or Win32Exception =>
                throw new UnauthorizedAccessException("The client could not be identified.", failure),
            _ => throw new InvalidOperationException("Resolving the pipe client failed.", failure),
        };
    }

    private static bool ComputeEnabled(AuthorizationOptions options)
    {
        using WindowsIdentity current = WindowsIdentity.GetCurrent();
        return CallerRules.IsOperatorsGroupEnabled(options.OperatorsGroup, current.User!);
    }
}
