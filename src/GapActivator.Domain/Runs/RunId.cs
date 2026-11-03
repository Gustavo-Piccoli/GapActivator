namespace GapActivator.Domain.Runs;

public readonly record struct RunId(Guid Value)
{
    public static RunId New() => new(Guid.CreateVersion7());

    public override string ToString() => Value.ToString();
}
