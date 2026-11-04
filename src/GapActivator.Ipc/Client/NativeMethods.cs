using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace GapActivator.Ipc;

/// <summary>The Win32 calls of the client (pipe server PID and the SCM query), declared by hand.</summary>
internal static partial class NativeMethods
{
    internal const uint SC_MANAGER_CONNECT = 0x0001;
    internal const uint SERVICE_QUERY_STATUS = 0x0004;
    internal const int SC_STATUS_PROCESS_INFO = 0;
    internal const int SERVICE_RUNNING = 4;
    internal const int ERROR_SERVICE_DOES_NOT_EXIST = 1060;

    [StructLayout(LayoutKind.Sequential)]
    internal struct SERVICE_STATUS_PROCESS
    {
        public int dwServiceType, dwCurrentState, dwControlsAccepted, dwWin32ExitCode, dwServiceSpecificExitCode,
                   dwCheckPoint, dwWaitHint, dwProcessId, dwServiceFlags;
    }

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool GetNamedPipeServerProcessId(SafePipeHandle pipe, out uint serverProcessId);

    [LibraryImport("advapi32.dll", EntryPoint = "OpenSCManagerW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    internal static partial nint OpenSCManager(string? machineName, string? databaseName, uint access);

    [LibraryImport("advapi32.dll", EntryPoint = "OpenServiceW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    internal static partial nint OpenService(nint scm, string serviceName, uint access);

    [LibraryImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool QueryServiceStatusEx(nint service, int infoLevel, out SERVICE_STATUS_PROCESS status, int size, out int needed);

    [LibraryImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool CloseServiceHandle(nint handle);
}
