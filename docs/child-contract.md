# Child process contract

GapActivator starts each registered process as a child. Any executable works without knowing this contract. The contract only adds graceful cancellation and a way to know who started you. Everything below is what GapActivator guarantees and what it expects.

## Exit code

`0` means success. Any other exit code means failure. GapActivator decides the outcomes Cancelled and TimedOut itself, because it knows what it requested and when, so the exit code after a cancel request does not matter.

## Launch

- The child runs in session 0, with no window, under the service account.
- Standard input is `NUL`: reading from it returns end of file at once.
- The arguments are passed verbatim, as configured in the registration.
- The working directory defaults to the folder of the executable, unless the registration sets one.
- `.cmd` and `.bat` files run through `cmd.exe /d /s /c`.

## Environment

The child gets the service's environment first. The registration's variables are applied next and override it, ignoring case (`path` replaces `PATH`). GapActivator then sets two variables of its own. The prefix `GAPACTIVATOR_` is reserved for GapActivator.

## Parent identity

| Variable | Value |
|---|---|
| `GAPACTIVATOR_PARENT_PID` | The service's process ID, in decimal. |
| `GAPACTIVATOR_PARENT_START_TIME` | The service's start time as UTC, formatted with `DateTime.ToString("O", CultureInfo.InvariantCulture)`, for example `2030-01-01T12:00:00.1234567Z`. |

A process ID can be reused by another program after the service exits. To check that the parent is still the same service, read the process with the PID and compare its start time with the variable:

```csharp
DateTime expected = DateTime.ParseExact(
    Environment.GetEnvironmentVariable("GAPACTIVATOR_PARENT_START_TIME")!,
    "O", CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
int pid = int.Parse(Environment.GetEnvironmentVariable("GAPACTIVATOR_PARENT_PID")!, CultureInfo.InvariantCulture);
bool parentIsAlive;
try
{
    parentIsAlive = Process.GetProcessById(pid).StartTime.ToUniversalTime() == expected;
}
catch (ArgumentException)
{
    parentIsAlive = false;
}
```

The comparison is exact: the two values are equal when the process is the same one.

A child may stop when GapActivator is gone, but it does not need to. A restarted GapActivator re-adopts live children (see "After a GapActivator crash").

## Cancellation

GapActivator creates a manual-reset named event, `Local\GapActivator.Cancel.{childPid}`, where `{childPid}` is the child's own process ID in decimal. The event exists before the child executes its first instruction, so a child can always open it. To request a graceful stop, GapActivator sets the event. The event stays set.

A child that wants to stop gracefully opens the event with `EventWaitHandle.TryOpenExisting` and waits on it, as the sample below does. When the event is set, the child should finish what it is doing and exit.

GapActivator then waits for the grace period. It is configured per process and defaults to 30 seconds. If the child has not exited when the grace period ends, GapActivator kills the whole process tree. It does this through a job object, `Local\GapActivator.Job.{childPid}.{startTimeUtcTicks}`, that holds the child and the processes it starts. The job object does not kill anything when GapActivator closes it.

## Output

Standard output and standard error go to one file, in the order the child wrote them. Write UTF-8. A run's output file is capped at 50 MiB: after the cap, GapActivator stops writing to the file and appends the line `[GapActivator] Output truncated after 52428800 bytes.` once. It keeps reading the pipe, so the child never blocks. The output is visible to everyone who can use the GapActivator app, so never print secrets.

## After a GapActivator crash

If the service crashes or loses power while a child runs, the child is not stopped. When the service starts again it re-adopts every live child by process ID and start time and keeps supervising it. The output the child writes after the crash is not captured: the pipe to the old service is broken, writes to it fail, and .NET ignores those failures. A child that outlives the service must tolerate failed writes to standard output and standard error. This also happens on a normal stop or restart of the service, because the pipe closes then too.

A child that started between its creation and the first commit of its run record is not seen by the restarted service. It keeps running unsupervised. This is the "crash window". Its run ends as Abandoned.

Known limitation: after a crash, GapActivator stops a re-adopted child by walking parent links to find its process tree. Descendants whose parent has already exited are not reached and keep running. Durable job ownership across service crashes is listed as future work.

## Children never touch GapActivator's database

The database belongs to the service. A child never opens it, reads it or writes to it. Use the cancel event to learn that a stop is requested.

## Sample

`samples/GapActivator.SampleChild` is a complete child. `GapActivator.SampleChild.exe [seconds]` works for `seconds` (default 5), prints its progress, and stops cleanly when the cancel event is set. Its whole program:

```csharp
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
```
