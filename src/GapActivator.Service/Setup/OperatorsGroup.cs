using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Principal;

namespace GapActivator.Service.Setup;

/// <summary>
/// The local group <c>GapActivator Operators</c>, managed through the NetLocalGroup APIs so no console output has to be
/// parsed. Both operations are idempotent.
/// </summary>
internal static partial class OperatorsGroup
{
    public const string Name = "GapActivator Operators";
    public const string Comment = "Members can change GapActivator processes and settings.";

    private const uint ErrorNoSuchAlias = 1376;
    private const uint ErrorMemberInAlias = 1378;
    private const uint ErrorAliasExists = 1379;
    private const uint NerrGroupNotFound = 2220;
    private const uint NerrGroupExists = 2223;

    /// <summary>
    /// Creates the group if it is missing, then adds the account when one is given. An existing group or membership is success.
    /// </summary>
    public static void AddOperator(SecurityIdentifier? account)
    {
        EnsureGroup();
        if (account is null)
        {
            return;
        }

        var sid = new byte[account.BinaryLength];
        account.GetBinaryForm(sid, 0);
        unsafe
        {
            fixed (byte* sidPointer = sid)
            {
                var member = new LocalGroupMembersInfo0 { Sid = (nint)sidPointer };
                uint status = NetLocalGroupAddMembers(null, Name, 0, ref member, 1);
                if (status != 0 && status != ErrorMemberInAlias)
                {
                    throw new Win32Exception((int)status);
                }
            }
        }
    }

    /// <summary>Deletes the group. A missing group is success.</summary>
    public static void Remove()
    {
        uint status = NetLocalGroupDel(null, Name);
        if (status != 0 && status != NerrGroupNotFound && status != ErrorNoSuchAlias)
        {
            throw new Win32Exception((int)status);
        }
    }

    private static void EnsureGroup()
    {
        nint name = Marshal.StringToHGlobalUni(Name);
        nint comment = Marshal.StringToHGlobalUni(Comment);
        try
        {
            var info = new LocalGroupInfo1 { Name = name, Comment = comment };
            uint status = NetLocalGroupAdd(null, 1, ref info, out _);
            if (status != 0 && status != NerrGroupExists && status != ErrorAliasExists)
            {
                throw new Win32Exception((int)status);
            }
        }
        finally
        {
            Marshal.FreeHGlobal(name);
            Marshal.FreeHGlobal(comment);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct LocalGroupInfo1
    {
        public nint Name;
        public nint Comment;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct LocalGroupMembersInfo0
    {
        public nint Sid;
    }

    [LibraryImport("netapi32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial uint NetLocalGroupAdd(string? server, uint level, ref LocalGroupInfo1 buffer, out uint parameterError);

    [LibraryImport("netapi32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial uint NetLocalGroupAddMembers(string? server, string group, uint level, ref LocalGroupMembersInfo0 buffer, uint count);

    [LibraryImport("netapi32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial uint NetLocalGroupDel(string? server, string group);
}
