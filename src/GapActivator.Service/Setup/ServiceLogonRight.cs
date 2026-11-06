using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Principal;

namespace GapActivator.Service.Setup;

/// <summary>Grants an account <c>SeServiceLogonRight</c> through the LSA. Granting twice is a no-op.</summary>
internal static partial class ServiceLogonRight
{
    private const uint PolicyCreateAccount = 0x00000010;
    private const uint PolicyLookupNames = 0x00000800;
    private const string RightName = "SeServiceLogonRight";

    public static void Grant(SecurityIdentifier account)
    {
        ArgumentNullException.ThrowIfNull(account);

        var sid = new byte[account.BinaryLength];
        account.GetBinaryForm(sid, 0);

        var attributes = new LsaObjectAttributes { Length = (uint)Marshal.SizeOf<LsaObjectAttributes>() };
        int status = LsaOpenPolicy(0, ref attributes, PolicyCreateAccount | PolicyLookupNames, out nint policy);
        ThrowIfFailed(status);
        try
        {
            nint buffer = Marshal.StringToHGlobalUni(RightName);
            try
            {
                var right = new LsaUnicodeString
                {
                    Length = (ushort)(RightName.Length * 2),
                    MaximumLength = (ushort)((RightName.Length + 1) * 2),
                    Buffer = buffer,
                };
                ThrowIfFailed(LsaAddAccountRights(policy, sid, ref right, 1));
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
        finally
        {
            _ = LsaClose(policy);
        }
    }

    private static void ThrowIfFailed(int ntStatus)
    {
        if (ntStatus != 0)
        {
            throw new Win32Exception((int)LsaNtStatusToWinError(ntStatus));
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct LsaUnicodeString
    {
        public ushort Length;
        public ushort MaximumLength;
        public nint Buffer;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct LsaObjectAttributes
    {
        public uint Length;
        public nint RootDirectory;
        public nint ObjectName;
        public uint Attributes;
        public nint SecurityDescriptor;
        public nint SecurityQualityOfService;
    }

    [LibraryImport("advapi32.dll")]
    private static partial int LsaOpenPolicy(nint systemName, ref LsaObjectAttributes attributes, uint desiredAccess, out nint policy);

    [LibraryImport("advapi32.dll")]
    private static partial int LsaAddAccountRights(nint policy, byte[] accountSid, ref LsaUnicodeString rights, uint count);

    [LibraryImport("advapi32.dll")]
    private static partial int LsaClose(nint policy);

    [LibraryImport("advapi32.dll")]
    private static partial uint LsaNtStatusToWinError(int status);
}
