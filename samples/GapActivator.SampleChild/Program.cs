using System.Globalization;

int seconds = args.Length > 0 ? int.Parse(args[0], CultureInfo.InvariantCulture) : 5;
string? parent = Environment.GetEnvironmentVariable("GAPACTIVATOR_PARENT_PID");
Console.WriteLine(parent is null ? "Running on its own." : $"Started by GapActivator (PID {parent}).");

// GapActivator creates this event before the child runs; outside GapActivator it does not exist.
EventWaitHandle.TryOpenExisting($@"Local\GapActivator.Cancel.{Environment.ProcessId}", out EventWaitHandle? cancel);
using (cancel)
{
    for (int i = 1; i <= seconds; i++)
    {
        if (cancel?.WaitOne(TimeSpan.FromSeconds(1)) ?? WaitOneSecond())
        {
            Console.WriteLine("Cancellation requested; stopping cleanly.");
            return 0;
        }

        Console.WriteLine($"Working... {i}/{seconds}");
    }
}

Console.WriteLine("Done.");
return 0;

static bool WaitOneSecond()
{
    Thread.Sleep(1000);
    return false;
}
