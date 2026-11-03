namespace GapActivator.Application.Messaging;

/// <summary>Result of a command that returns nothing.</summary>
public readonly record struct Unit
{
    public static Unit Value => default;
}
