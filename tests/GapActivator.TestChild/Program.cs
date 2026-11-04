using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using GapActivator.TestChild;

// The child process of the launcher tests. The first argument selects the behaviour; every line is flushed at once.
// The cancel event is looked up before anything else, so the check proves it existed before the child ran.
bool cancelEventFound = args is ["check-event", ..] && Commands.CancelEventExists();

Console.OutputEncoding = Encoding.UTF8;

switch (args)
{
    case ["exit", var code]:
        return Commands.Parse(code);

    case ["sleep", var milliseconds]:
        Thread.Sleep(Commands.Parse(milliseconds));
        return 0;

    case ["sleep", var milliseconds, var code]:
        Thread.Sleep(Commands.Parse(milliseconds));
        return Commands.Parse(code);

    case ["print-both"]:
        Commands.Out("out-1");
        Commands.Error("err-1");
        Commands.Out("out-2");
        return 0;

    case ["echo-args", .. var values]:
        foreach (string value in values)
        {
            Commands.Out("arg:" + value);
        }

        return 0;

    case ["cwd"]:
        Commands.Out("cwd:" + Environment.CurrentDirectory);
        return 0;

    case ["env", .. var names]:
        foreach (string name in names)
        {
            Commands.Out($"env:{name}={Environment.GetEnvironmentVariable(name) ?? "<unset>"}");
            Commands.Out(string.Create(CultureInfo.InvariantCulture, $"envcount:{name}={Commands.CountVariables(name)}"));
        }

        return 0;

    case ["check-event"]:
        Commands.Out(cancelEventFound ? "event:found" : "event:missing");
        return 0;

    case ["console-window"]:
        nint window = NativeMethods.GetConsoleWindow();
        Commands.Out(window == 0 ? "console-window:none" : "console-window:" + window.ToString(CultureInfo.InvariantCulture));
        return 0;

    case ["honor-cancel"]:
        using (EventWaitHandle cancel = EventWaitHandle.OpenExisting(Commands.CancelEventName))
        {
            Commands.Out("ready");
            bool signalled = cancel.WaitOne(TimeSpan.FromSeconds(60));
            Commands.Out(signalled ? "cancelled" : "not-cancelled");
            return signalled ? 0 : 1;
        }

    case ["ignore-cancel"]:
        Commands.Out("ready");
        Thread.Sleep(TimeSpan.FromMinutes(10));
        return 0;

    case ["spawn-grandchild", .. var options]:
        // No redirection: the grandchild inherits this process's stdout, which is the launcher's output pipe.
        var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, ArgumentList = { "sleep", "600000" } };
        using (Process grandchild = Process.Start(start)!)
        {
            Commands.Out(string.Create(CultureInfo.InvariantCulture, $"grandchild:{grandchild.Id}"));
        }

        if (options is not ["exit"])
        {
            Thread.Sleep(TimeSpan.FromMinutes(10));
        }

        return 0;

    case ["spawn-grandchild-file", var path]:
        var fileStart = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, ArgumentList = { "sleep", "600000" } };
        using (Process grandchild = Process.Start(fileStart)!)
        {
            // Written through a temporary name, so a reader never sees a partial number.
            string temporary = path + ".tmp";
            File.WriteAllText(temporary, grandchild.Id.ToString(CultureInfo.InvariantCulture));
            File.Move(temporary, path, overwrite: true);
        }

        Thread.Sleep(TimeSpan.FromMinutes(10));
        return 0;

    case ["flood", var bytes]:
        Commands.Flood(Commands.Parse(bytes));
        return 0;

    default:
        Commands.Error("Unknown command: " + string.Join(' ', args));
        return 64;
}

namespace GapActivator.TestChild
{
    internal static class Commands
    {
        public static string CancelEventName => @"Local\GapActivator.Cancel." + Environment.ProcessId.ToString(CultureInfo.InvariantCulture);

        public static bool CancelEventExists()
        {
            bool found = EventWaitHandle.TryOpenExisting(CancelEventName, out EventWaitHandle? handle);
            handle?.Dispose();
            return found;
        }

        public static int Parse(string value) => int.Parse(value, CultureInfo.InvariantCulture);

        public static void Out(string line)
        {
            Console.Out.WriteLine(line);
            Console.Out.Flush();
        }

        public static void Error(string line)
        {
            Console.Error.WriteLine(line);
            Console.Error.Flush();
        }

        public static void Flood(int bytes)
        {
            byte[] chunk = [.. Enumerable.Repeat((byte)'x', 4096)];
            using Stream stdout = Console.OpenStandardOutput();
            for (int left = bytes; left > 0; left -= chunk.Length)
            {
                stdout.Write(chunk, 0, Math.Min(left, chunk.Length));
            }

            stdout.Flush();
        }

        /// <summary>Counts the raw environment block entries whose name equals <paramref name="name"/> ignoring case.</summary>
        /// <remarks>The raw block is read because <see cref="Environment.GetEnvironmentVariables()"/> hides duplicates.</remarks>
        public static unsafe int CountVariables(string name)
        {
            char* block = NativeMethods.GetEnvironmentStringsW();
            if (block == null)
            {
                throw new InvalidOperationException("The environment block could not be read.");
            }

            try
            {
                int count = 0;
                for (char* entry = block; *entry != '\0';)
                {
                    var text = new string(entry);
                    int separator = text.IndexOf('=', 1);
                    if (separator > 0 && text[..separator].Equals(name, StringComparison.OrdinalIgnoreCase))
                    {
                        count++;
                    }

                    entry += text.Length + 1;
                }

                return count;
            }
            finally
            {
                NativeMethods.FreeEnvironmentStringsW(block);
            }
        }
    }

    internal static unsafe partial class NativeMethods
    {
        [LibraryImport("kernel32.dll")]
        internal static partial nint GetConsoleWindow();

        [LibraryImport("kernel32.dll")]
        internal static partial char* GetEnvironmentStringsW();

        [LibraryImport("kernel32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool FreeEnvironmentStringsW(char* block);
    }
}
