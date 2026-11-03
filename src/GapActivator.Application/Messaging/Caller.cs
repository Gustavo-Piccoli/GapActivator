namespace GapActivator.Application.Messaging;

/// <summary>Who sends a message and what that caller may do.</summary>
public sealed record Caller(string Name, bool CanChange, bool IsSystem)
{
    public static Caller System { get; } = new("GapActivator", true, true);

    public static Caller Viewer(string name) => new(name, false, false);

    public static Caller Operator(string name) => new(name, true, false);
}
