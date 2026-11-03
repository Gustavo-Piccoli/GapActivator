using GapActivator.Domain.Common;

namespace GapActivator.Domain.Processes;

/// <summary>Display name of a process: trimmed, 1-100 characters, compared ignoring case.</summary>
public sealed record ProcessName
{
    public const int MaxLength = 100;

    private ProcessName(string value) => Value = value;

    public string Value { get; }

    public static ProcessName Create(string value)
    {
        var trimmed = value?.Trim() ?? string.Empty;
        if (trimmed.Length is 0 or > MaxLength)
        {
            throw new DomainException($"Process name must be between 1 and {MaxLength} characters.");
        }

        if (trimmed.Any(char.IsControl))
        {
            throw new DomainException("Process name must not contain control characters.");
        }

        return new ProcessName(trimmed);
    }

    public bool Equals(ProcessName? other) =>
        other is not null && string.Equals(Value, other.Value, StringComparison.OrdinalIgnoreCase);

    public override int GetHashCode() => StringComparer.OrdinalIgnoreCase.GetHashCode(Value);

    public override string ToString() => Value;
}
