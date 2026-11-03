using GapActivator.Domain.Common;
using GapActivator.Domain.Settings;

namespace GapActivator.Domain.Processes;

/// <summary>One environment variable passed to a launched process; the value is plain or protected.</summary>
public sealed record EnvironmentVariable
{
    public const string ReservedPrefix = "GAPACTIVATOR_";

    private EnvironmentVariable(string name, string? plainValue, ProtectedSecret? secretValue)
    {
        Name = name;
        PlainValue = plainValue;
        SecretValue = secretValue;
    }

    public string Name { get; }

    public string? PlainValue { get; }

    public ProtectedSecret? SecretValue { get; }

    public bool IsSecret => SecretValue is not null;

    public static EnvironmentVariable Plain(string name, string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return new EnvironmentVariable(ValidateName(name), value, null);
    }

    public static EnvironmentVariable Secret(string name, ProtectedSecret value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return new EnvironmentVariable(ValidateName(name), null, value);
    }

    private static string ValidateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new DomainException("Environment variable name must not be empty.");
        }

        if (name.Contains('=') || name.Contains('\0'))
        {
            throw new DomainException("Environment variable name must not contain '=' or NUL.");
        }

        if (name.StartsWith(ReservedPrefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new DomainException($"Environment variable names starting with '{ReservedPrefix}' are reserved.");
        }

        return name;
    }
}
