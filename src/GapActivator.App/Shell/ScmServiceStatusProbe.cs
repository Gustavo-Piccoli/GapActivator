using System.Runtime.InteropServices;

namespace GapActivator.App.Shell;

/// <summary>Reads the service state from the Service Control Manager. It never throws: a failed query is Unknown.</summary>
public sealed class ScmServiceStatusProbe(string serviceName) : IServiceStatusProbe
{
    public ServiceInstallState Query()
    {
        nint scm = ServiceNativeMethods.OpenSCManager(null, null, ServiceNativeMethods.SC_MANAGER_CONNECT);
        if (scm == 0)
        {
            return ServiceInstallState.Unknown;
        }

        try
        {
            nint service = ServiceNativeMethods.OpenService(scm, serviceName, ServiceNativeMethods.SERVICE_QUERY_STATUS);
            if (service == 0)
            {
                return Marshal.GetLastPInvokeError() == ServiceNativeMethods.ERROR_SERVICE_DOES_NOT_EXIST
                    ? ServiceInstallState.NotInstalled
                    : ServiceInstallState.Unknown;
            }

            try
            {
                bool ok = ServiceNativeMethods.QueryServiceStatusEx(
                    service,
                    ServiceNativeMethods.SC_STATUS_PROCESS_INFO,
                    out ServiceNativeMethods.SERVICE_STATUS_PROCESS status,
                    Marshal.SizeOf<ServiceNativeMethods.SERVICE_STATUS_PROCESS>(),
                    out _);
                if (!ok)
                {
                    return ServiceInstallState.Unknown;
                }

                return status.dwCurrentState switch
                {
                    ServiceNativeMethods.SERVICE_STOPPED => ServiceInstallState.Stopped,
                    ServiceNativeMethods.SERVICE_START_PENDING => ServiceInstallState.StartPending,
                    ServiceNativeMethods.SERVICE_RUNNING => ServiceInstallState.Running,
                    _ => ServiceInstallState.Unknown,
                };
            }
            finally
            {
                ServiceNativeMethods.CloseServiceHandle(service);
            }
        }
        finally
        {
            ServiceNativeMethods.CloseServiceHandle(scm);
        }
    }
}
