using GapActivator.Domain.Common;

namespace GapActivator.Domain.Processes;

/// <summary>Fully qualified path of the program to launch, compared ignoring case.</summary>
public sealed record ExecutablePath
{
    public const int MaxLength = 32_767;

    private ExecutablePath(string value) => Value = value;

    public string Value { get; }

    public static ExecutablePath Create(string value)
    {
        var trimmed = value?.Trim() ?? string.Empty;
        if (trimmed.Length is 0 or > MaxLength)
        {
            throw new DomainException($"Executable path must be between 1 and {MaxLength} characters.");
        }

        if (trimmed.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
        {
            throw new DomainException("Executable path contains invalid characters.");
        }

        if (!Path.IsPathFullyQualified(trimmed))
        {
            throw new DomainException("Executable path must be fully qualified.");
        }

        return new ExecutablePath(trimmed);
    }

    public bool Equals(ExecutablePath? other) =>
        other is not null && string.Equals(Value, other.Value, StringComparison.OrdinalIgnoreCase);

    public override int GetHashCode() => StringComparer.OrdinalIgnoreCase.GetHashCode(Value);

    public override string ToString() => Value;
}
