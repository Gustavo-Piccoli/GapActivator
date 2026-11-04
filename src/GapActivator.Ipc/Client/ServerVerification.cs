using System.Runtime.InteropServices;

namespace GapActivator.Ipc;

/// <summary>What the Service Control Manager says about the service the client expects to talk to.</summary>
internal abstract record ServiceLookup
{
    private ServiceLookup()
    {
    }

    public sealed record NotInstalled : ServiceLookup;

    public sealed record Running(uint Pid) : ServiceLookup;

    public sealed record Stopped : ServiceLookup;

    public sealed record Failed(int Win32Error) : ServiceLookup;
}

internal static class ServerVerification
{
    /// <returns>
    /// null when the service is not installed (an unverified server is allowed); true when the pipe server is the
    /// running service; false in every other case (the connection is rejected).
    /// </returns>
    public static bool? Decide(uint serverPid, ServiceLookup lookup)
    {
        ArgumentNullException.ThrowIfNull(lookup);
        return lookup switch
        {
            ServiceLookup.NotInstalled => null,
            ServiceLookup.Running running => running.Pid == serverPid,
            _ => false,
        };
    }
}

/// <summary>The real lookup, through the SCM.</summary>
internal static class ScmServiceLookup
{
    public static ServiceLookup Query(string serviceName)
    {
        nint scm = NativeMethods.OpenSCManager(null, null, NativeMethods.SC_MANAGER_CONNECT);
        if (scm == 0)
        {
            return new ServiceLookup.Failed(Marshal.GetLastPInvokeError());
        }

        try
        {
            nint service = NativeMethods.OpenService(scm, serviceName, NativeMethods.SERVICE_QUERY_STATUS);
            if (service == 0)
            {
                int error = Marshal.GetLastPInvokeError();
                return error == NativeMethods.ERROR_SERVICE_DOES_NOT_EXIST
                    ? new ServiceLookup.NotInstalled()
                    : new ServiceLookup.Failed(error);
            }

            try
            {
                if (!NativeMethods.QueryServiceStatusEx(
                        service,
                        NativeMethods.SC_STATUS_PROCESS_INFO,
                        out NativeMethods.SERVICE_STATUS_PROCESS status,
                        Marshal.SizeOf<NativeMethods.SERVICE_STATUS_PROCESS>(),
                        out _))
                {
                    return new ServiceLookup.Failed(Marshal.GetLastPInvokeError());
                }

                return status.dwCurrentState == NativeMethods.SERVICE_RUNNING
                    ? new ServiceLookup.Running((uint)status.dwProcessId)
                    : new ServiceLookup.Stopped();
            }
            finally
            {
                NativeMethods.CloseServiceHandle(service);
            }
        }
        finally
        {
            NativeMethods.CloseServiceHandle(scm);
        }
    }
}
