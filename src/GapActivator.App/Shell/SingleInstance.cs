using static GapActivator.App.Shell.ShellNativeMethods;

namespace GapActivator.App.Shell;

/// <summary>Per-session single-instance guard backed by a named mutex.</summary>
public sealed class SingleInstance : IDisposable
{
    public const string DefaultName = @"Local\GapActivator.App.SingleInstance";

    private Mutex? _mutex;

    private SingleInstance(Mutex mutex) => _mutex = mutex;

    /// <summary>Returns the guard, or null when another holder exists (including one we are not allowed to open, such as an elevated instance).</summary>
    public static SingleInstance? TryAcquire(string name = DefaultName)
    {
        Mutex mutex;
        bool createdNew;
        try
        {
            mutex = new Mutex(true, name, out createdNew);
        }
        catch (UnauthorizedAccessException)
        {
            // The mutex exists and its owner (for example an elevated process) denies us access.
            return null;
        }

        if (!createdNew)
        {
            mutex.Dispose();
            return null;
        }

        return new SingleInstance(mutex);
    }

    /// <summary>Asks the running instance to show its window; false when no shell window was found.</summary>
    public static bool SignalExisting()
    {
        var hwnd = FindWindowW(ShellWindow.ClassName, null);
        if (hwnd == 0)
        {
            return false;
        }

        if (GetWindowThreadProcessId(hwnd, out var pid) != 0)
        {
            AllowSetForegroundWindow(pid);
        }

        PostMessageW(hwnd, ShellWindow.ActivateMessage, 0, 0);
        return true;
    }

    public void Dispose()
    {
        var mutex = Interlocked.Exchange(ref _mutex, null);
        if (mutex is null)
        {
            return;
        }

        try
        {
            mutex.ReleaseMutex();
        }
        catch (ApplicationException)
        {
            // Released from a different thread than the one that acquired it; closing the handle suffices.
        }

        mutex.Dispose();
    }
}
