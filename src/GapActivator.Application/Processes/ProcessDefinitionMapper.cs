using GapActivator.Application.Messaging;
using GapActivator.Application.Ports;
using GapActivator.Domain.Common;
using GapActivator.Domain.Processes;
using GapActivator.Domain.Scheduling;

namespace GapActivator.Application.Processes;

/// <summary>Turns a <see cref="ProcessDefinition"/> into domain values; shared by register and update.</summary>
internal sealed class ProcessDefinitionMapper(
    ISecretProtector protector,
    IOccurrenceCalculator calculator,
    TimeProvider time)
{
    public static ExecutionLimits MapLimits(ProcessDefinition definition) =>
        new(definition.TimeLimit, definition.GracePeriod);

    public LaunchOptions MapLaunch(ProcessDefinition definition, Process? existing)
    {
        var variables = new List<EnvironmentVariable>();
        foreach (EnvironmentVariableInput input in definition.Environment ?? [])
        {
            variables.Add(MapVariable(input, existing));
        }

        return new LaunchOptions(
            ExecutablePath.Create(definition.Executable),
            definition.Arguments,
            definition.WorkingDirectory,
            EnvironmentVariables.Create(variables));
    }

    /// <summary>
    /// Builds the schedules. An input that carries the Id of an existing schedule with an identical definition keeps
    /// that instance; every other input becomes a new schedule (Q-12, Q-13).
    /// </summary>
    public List<Schedule> MapSchedules(ProcessDefinition definition, Process? existing)
    {
        DateTimeOffset now = time.GetUtcNow();
        var used = new HashSet<ScheduleId>();
        var result = new List<Schedule>();

        foreach (ScheduleInput input in definition.Schedules ?? [])
        {
            RecurrenceRule? rule = ParseRule(input);
            Schedule? kept = FindUnchanged(existing, input, rule, used);
            if (kept is not null)
            {
                used.Add(kept.Id);
                result.Add(kept);
                continue;
            }

            result.Add(input.Kind == ScheduleInputKind.OneTime
                ? CreateOneTime(input, now)
                : RecurringSchedule.Create(rule!, input.At, now, calculator));
        }

        return result;
    }

    private static RecurrenceRule? ParseRule(ScheduleInput input)
    {
        switch (input.Kind)
        {
            case ScheduleInputKind.OneTime:
                if (input.Rule is not null)
                {
                    throw new DomainException("A one-time schedule cannot have a recurrence rule.");
                }

                return null;
            case ScheduleInputKind.Recurring:
                if (string.IsNullOrWhiteSpace(input.Rule))
                {
                    throw new DomainException("A recurring schedule requires a recurrence rule.");
                }

                return RecurrenceRule.Parse(input.Rule);
            default:
                throw new DomainException($"Schedule kind '{input.Kind}' is not valid.");
        }
    }

    private static Schedule? FindUnchanged(Process? existing, ScheduleInput input, RecurrenceRule? rule, HashSet<ScheduleId> used)
    {
        if (existing is null || input.Id is not { } id)
        {
            return null;
        }

        Schedule? match = existing.Schedules.FirstOrDefault(s => s.Id.Value == id);
        if (match is null || used.Contains(match.Id))
        {
            return null;
        }

        return match switch
        {
            OneTimeSchedule oneTime when input.Kind == ScheduleInputKind.OneTime && oneTime.At == input.At => match,
            RecurringSchedule recurring when input.Kind == ScheduleInputKind.Recurring
                && recurring.Rule.Value == rule!.Value && recurring.Anchor == input.At => match,
            _ => null,
        };
    }

    private static OneTimeSchedule CreateOneTime(ScheduleInput input, DateTimeOffset now)
    {
        if (input.At <= now)
        {
            throw new CommandRejectedException(
                RejectionCodes.ScheduleInPast, $"A one-time schedule must be in the future ({input.At:O} is not after {now:O}).");
        }

        return OneTimeSchedule.Create(input.At);
    }

    private EnvironmentVariable MapVariable(EnvironmentVariableInput input, Process? existing)
    {
        if (!input.IsSecret)
        {
            return EnvironmentVariable.Plain(input.Name, input.Value ?? string.Empty);
        }

        if (input.Value is not null)
        {
            return EnvironmentVariable.Secret(input.Name, protector.Protect(input.Value));
        }

        EnvironmentVariable? stored = existing?.Launch.Environment.Items.FirstOrDefault(
            v => v.IsSecret && string.Equals(v.Name, input.Name, StringComparison.OrdinalIgnoreCase));
        return stored is null
            ? throw new CommandRejectedException(
                RejectionCodes.SecretValueRequired, $"Secret variable '{input.Name}' needs a value.")
            : EnvironmentVariable.Secret(input.Name, stored.SecretValue!);
    }
}
