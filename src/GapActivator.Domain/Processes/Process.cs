using System.Collections.ObjectModel;
using GapActivator.Domain.Common;
using GapActivator.Domain.Runs;
using GapActivator.Domain.Scheduling;

namespace GapActivator.Domain.Processes;

/// <summary>A registered program: what to launch, when, and whether it is allowed to run.</summary>
public sealed class Process : AggregateRoot<ProcessId>
{
    public const int MaxDescriptionLength = 1_000;

    private readonly List<Schedule> _schedules = [];

    private Process(ProcessId id, DateTimeOffset createdAt)
        : base(id)
    {
        Schedules = new ReadOnlyCollection<Schedule>(_schedules);
        CreatedAt = createdAt;
        Lifecycle = ProcessLifecycle.Enabled;
    }

    public ProcessName Name { get; private set; } = null!;

    public string Description { get; private set; } = string.Empty;

    public ProcessKind Kind { get; private set; }

    public LaunchOptions Launch { get; private set; } = null!;

    public ExecutionLimits Limits { get; private set; } = null!;

    public IReadOnlyList<Schedule> Schedules { get; }

    public ProcessLifecycle Lifecycle { get; private set; }

    public int ConsecutiveFailures { get; private set; }

    public bool RetryPending { get; private set; }

    public DateTimeOffset CreatedAt { get; }

    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>The earliest next occurrence among the schedules that are not exhausted.</summary>
    public DateTimeOffset? NextScheduledAt => _schedules.Min(s => s.NextOccurrenceAt);

    public static Process Register(
        ProcessName name,
        string description,
        ProcessKind kind,
        LaunchOptions launch,
        ExecutionLimits limits,
        IEnumerable<Schedule> schedules,
        DateTimeOffset now)
    {
        var process = new Process(ProcessId.New(), now);
        process.Define(name, description, kind, launch, limits, schedules, now);
        return process;
    }

    public void Update(
        ProcessName name,
        string description,
        ProcessKind kind,
        LaunchOptions launch,
        ExecutionLimits limits,
        IEnumerable<Schedule> schedules,
        DateTimeOffset now)
    {
        var kindChanged = kind != Kind;
        Define(name, description, kind, launch, limits, schedules, now);

        // Failures counted under the old kind say nothing about the new one.
        if (kindChanged)
        {
            ConsecutiveFailures = 0;
            RetryPending = false;
        }
    }

    /// <summary>Enables a disabled process; occurrences that came due while it was disabled are discarded.</summary>
    public void Enable(DateTimeOffset now, IOccurrenceCalculator calculator)
    {
        ArgumentNullException.ThrowIfNull(calculator);
        RequireLifecycle(ProcessLifecycle.Disabled, "enabled");
        Lifecycle = ProcessLifecycle.Enabled;
        UpdatedAt = now;
        ConsumeDue(now, calculator);
    }

    public void Disable(DateTimeOffset now)
    {
        RequireLifecycle(ProcessLifecycle.Enabled, "disabled");
        Lifecycle = ProcessLifecycle.Disabled;
        RetryPending = false;
        UpdatedAt = now;
    }

    /// <summary>Unblocks a blocked process; occurrences that came due while it was blocked are discarded.</summary>
    public void Unblock(DateTimeOffset now, IOccurrenceCalculator calculator)
    {
        ArgumentNullException.ThrowIfNull(calculator);
        RequireLifecycle(ProcessLifecycle.Blocked, "unblocked");
        Lifecycle = ProcessLifecycle.Enabled;
        ConsecutiveFailures = 0;
        RetryPending = false;
        UpdatedAt = now;
        ConsumeDue(now, calculator);
        Raise(new ProcessUnblocked(Id, now));
    }

    public void ApplyRunOutcome(RunOutcome outcome, int failureLimit, DateTimeOffset now)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(failureLimit, 1);

        switch (RunOutcomeRules.EffectOf(outcome))
        {
            case FailureEffect.Reset:
                ConsecutiveFailures = 0;
                RetryPending = false;
                UpdatedAt = now;
                break;
            case FailureEffect.Increment:
                ConsecutiveFailures++;

                // Only an Enabled process can be blocked or retried; a Disabled or Blocked one
                // just counts (EnforceFailureLimit blocks a Disabled one once it is enabled again).
                if (Lifecycle == ProcessLifecycle.Enabled)
                {
                    if (ConsecutiveFailures >= failureLimit)
                    {
                        Block(now);
                    }
                    else if (Kind == ProcessKind.RunToCompletion)
                    {
                        RetryPending = true;
                    }
                }

                UpdatedAt = now;
                break;
            case FailureEffect.Unchanged:
                break;
        }
    }

    /// <summary>A Service-kind process found up during a cycle has its failure counter reset.</summary>
    public void MarkServiceFoundRunning(DateTimeOffset now)
    {
        if (Kind != ProcessKind.Service)
        {
            throw new DomainException("Only a Service-kind process can be found running.");
        }

        if (ConsecutiveFailures == 0)
        {
            return;
        }

        ConsecutiveFailures = 0;
        UpdatedAt = now;
    }

    /// <summary>Blocks an enabled process whose counter already meets the limit (for example after the limit was lowered).</summary>
    public void EnforceFailureLimit(int failureLimit, DateTimeOffset now)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(failureLimit, 1);

        if (Lifecycle == ProcessLifecycle.Enabled && ConsecutiveFailures >= failureLimit)
        {
            Block(now);
            UpdatedAt = now;
        }
    }

    /// <summary>
    /// Decides whether a RunToCompletion process should start a run now, consuming every due occurrence.
    /// A due schedule wins over a pending retry.
    /// </summary>
    public DueDecision EvaluateDue(DateTimeOffset now, TimeSpan lateThreshold, IOccurrenceCalculator calculator)
    {
        ArgumentNullException.ThrowIfNull(calculator);

        if (Kind != ProcessKind.RunToCompletion || Lifecycle != ProcessLifecycle.Enabled)
        {
            return DueDecision.NotDue;
        }

        if (ConsumeDue(now, calculator) is { } scheduledFor)
        {
            return new DueDecision(true, RunTrigger.Schedule, scheduledFor, now - scheduledFor > lateThreshold);
        }

        return RetryPending ? new DueDecision(true, RunTrigger.Retry, null, false) : DueDecision.NotDue;
    }

    /// <summary>Advances every schedule whose next occurrence is at or before <paramref name="now"/> (used by manual runs too).</summary>
    public void ConsumeDueOccurrences(DateTimeOffset now, IOccurrenceCalculator calculator)
    {
        ArgumentNullException.ThrowIfNull(calculator);
        ConsumeDue(now, calculator);
    }

    public TimeSpan? EffectiveTimeLimit(TimeSpan? installationDefault) =>
        Kind == ProcessKind.Service ? null : Limits.EffectiveTimeLimit(installationDefault);

    public TimeSpan EffectiveGracePeriod(TimeSpan installationDefault) => Limits.EffectiveGracePeriod(installationDefault);

    /// <summary>Advances the due schedules and returns the earliest occurrence consumed, or null when none was due.</summary>
    private DateTimeOffset? ConsumeDue(DateTimeOffset now, IOccurrenceCalculator calculator)
    {
        DateTimeOffset? earliest = null;
        foreach (var schedule in _schedules)
        {
            if (schedule.NextOccurrenceAt is { } occurrence && occurrence <= now)
            {
                if (earliest is null || occurrence < earliest)
                {
                    earliest = occurrence;
                }

                schedule.Advance(now, calculator);
            }
        }

        if (earliest is not null)
        {
            UpdatedAt = now;
        }

        return earliest;
    }

    private void Define(
        ProcessName name,
        string description,
        ProcessKind kind,
        LaunchOptions launch,
        ExecutionLimits limits,
        IEnumerable<Schedule> schedules,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(description);
        ArgumentNullException.ThrowIfNull(launch);
        ArgumentNullException.ThrowIfNull(limits);
        ArgumentNullException.ThrowIfNull(schedules);

        var trimmedDescription = description.Trim();
        if (trimmedDescription.Length > MaxDescriptionLength)
        {
            throw new DomainException($"Description must not exceed {MaxDescriptionLength} characters.");
        }

        if (!Enum.IsDefined(kind))
        {
            throw new DomainException($"Process kind '{kind}' is not valid.");
        }

        var newSchedules = schedules.ToArray();
        if (newSchedules.Any(s => s is null))
        {
            throw new ArgumentException("Schedules must not contain null.", nameof(schedules));
        }

        if (kind == ProcessKind.Service && newSchedules.Length > 0)
        {
            throw new DomainException("A Service-kind process cannot have schedules.");
        }

        if (kind == ProcessKind.Service && limits.TimeLimit is not null)
        {
            throw new DomainException("A Service-kind process cannot have a time limit.");
        }

        Name = name;
        Description = trimmedDescription;
        Kind = kind;
        Launch = launch;
        Limits = limits;
        _schedules.Clear();
        _schedules.AddRange(newSchedules);
        UpdatedAt = now;
    }

    private void Block(DateTimeOffset now)
    {
        var failuresReached = ConsecutiveFailures;
        Lifecycle = ProcessLifecycle.Blocked;
        ConsecutiveFailures = 0;
        RetryPending = false;
        Raise(new ProcessBlocked(Id, failuresReached, now));
    }

    private void RequireLifecycle(ProcessLifecycle expected, string action)
    {
        if (Lifecycle != expected)
        {
            throw new DomainException($"A {Lifecycle} process cannot be {action}; it must be {expected}.");
        }
    }
}
