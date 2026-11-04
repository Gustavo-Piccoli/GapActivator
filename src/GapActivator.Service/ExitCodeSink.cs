namespace GapActivator.Service;

/// <summary>
/// Where the service's exit code goes. The host writes it to <see cref="Environment.ExitCode"/>, which the service
/// lifetime reports to the Service Control Manager; tests replace it so the test process's exit code is never set.
/// </summary>
public sealed class ExitCodeSink(Action<int> write)
{
    private readonly Lock _sync = new();
    private int? _last;

    /// <summary>The last code set, or null when none was.</summary>
    public int? Last
    {
        get
        {
            lock (_sync)
            {
                return _last;
            }
        }
    }

    public void Set(int code)
    {
        lock (_sync)
        {
            _last = code;
            write(code);
        }
    }
}
