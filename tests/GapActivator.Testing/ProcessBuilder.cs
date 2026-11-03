using GapActivator.Domain.Processes;
using GapActivator.Domain.Scheduling;

namespace GapActivator.Testing;

/// <summary>Registers a <see cref="Process"/> with fixed synthetic defaults that each test overrides as needed.</summary>
public sealed class ProcessBuilder
{
    public static readonly DateTimeOffset BaseTime = new(2027, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private readonly List<Schedule> _schedules = [];
    private string _name = "Sample job";
    private string _description = string.Empty;
    private ProcessKind _kind = ProcessKind.RunToCompletion;
    private string _executable = @"C:\Tools\sample.exe";
    private ExecutionLimits _limits = new(null, null);
    private DateTimeOffset _createdAt = BaseTime;

    public ProcessBuilder Named(string name)
    {
        _name = name;
        return this;
    }

    public ProcessBuilder WithDescription(string description)
    {
        _description = description;
        return this;
    }

    public ProcessBuilder OfKind(ProcessKind kind)
    {
        _kind = kind;
        return this;
    }

    public ProcessBuilder WithExecutable(string executable)
    {
        _executable = executable;
        return this;
    }

    public ProcessBuilder WithLimits(ExecutionLimits limits)
    {
        _limits = limits;
        return this;
    }

    public ProcessBuilder WithSchedule(Schedule schedule)
    {
        _schedules.Add(schedule);
        return this;
    }

    public ProcessBuilder CreatedAt(DateTimeOffset createdAt)
    {
        _createdAt = createdAt;
        return this;
    }

    public Process Build() => Process.Register(
        ProcessName.Create(_name),
        _description,
        _kind,
        new LaunchOptions(ExecutablePath.Create(_executable), string.Empty, null, EnvironmentVariables.Empty),
        _limits,
        _schedules,
        _createdAt);
}
