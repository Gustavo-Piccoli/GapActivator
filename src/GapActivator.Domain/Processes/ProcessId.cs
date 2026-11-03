namespace GapActivator.Domain.Processes;

public readonly record struct ProcessId(Guid Value)
{
    public static ProcessId New() => new(Guid.CreateVersion7());

    public override string ToString() => Value.ToString();
}
