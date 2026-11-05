using System.Runtime.InteropServices;
using GapActivator.App.Shell;

namespace GapActivator.App.Tests.Shell;

internal static partial class ShellTestNative
{
    internal const uint WM_ENDSESSION = 0x0016;
    internal const uint WM_QUERYENDSESSION = 0x0011;

    [LibraryImport("user32.dll", EntryPoint = "SendMessageW")]
    internal static partial nint SendMessageW(nint hWnd, uint msg, nint wParam, nint lParam);

    [LibraryImport("user32.dll", EntryPoint = "RegisterWindowMessageW", StringMarshalling = StringMarshalling.Utf16)]
    internal static partial uint RegisterWindowMessageW(string name);

    [LibraryImport("user32.dll", EntryPoint = "FindWindowExW", StringMarshalling = StringMarshalling.Utf16)]
    internal static partial nint FindWindowExW(nint parent, nint after, string? cls, string? title);

    [LibraryImport("user32.dll", EntryPoint = "FindWindowW", StringMarshalling = StringMarshalling.Utf16)]
    internal static partial nint FindWindowW(string? cls, string? title);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool IsWindow(nint hWnd);

    [StructLayout(LayoutKind.Sequential)]
    internal struct MSG
    {
        public nint hwnd;
        public uint message;
        public nint wParam;
        public nint lParam;
        public uint time;
        public int x;
        public int y;
    }

    [LibraryImport("user32.dll", EntryPoint = "PeekMessageW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool PeekMessageW(out MSG msg, nint hWnd, uint min, uint max, uint remove);

    [LibraryImport("user32.dll", EntryPoint = "DispatchMessageW")]
    internal static partial nint DispatchMessageW(in MSG msg);

    /// <summary>Pumps this thread's messages until the condition holds or the timeout passes.</summary>
    internal static bool PumpUntil(Func<bool> condition, TimeSpan timeout)
    {
        var end = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < end)
        {
            while (PeekMessageW(out var msg, 0, 0, 0, 1))
            {
                DispatchMessageW(in msg);
            }

            if (condition())
            {
                return true;
            }

            Thread.Sleep(10);
        }

        return condition();
    }

    internal static bool ClassWindowExists(nint handle)
    {
        nint current = 0;
        while ((current = FindWindowExW(0, current, ShellWindow.ClassName, null)) != 0)
        {
            if (current == handle)
            {
                return true;
            }
        }

        return false;
    }
}
