using GapActivator.Domain.Common;

namespace GapActivator.Domain.Processes;

/// <summary>Ordered set of environment variables with unique names (case-insensitive). Equal when the items are equal in the same order.</summary>
public sealed class EnvironmentVariables : IEquatable<EnvironmentVariables>
{
    private EnvironmentVariables(EnvironmentVariable[] items) => Items = Array.AsReadOnly(items);

    public static EnvironmentVariables Empty { get; } = new([]);

    public IReadOnlyList<EnvironmentVariable> Items { get; }

    public static EnvironmentVariables Create(IEnumerable<EnvironmentVariable> variables)
    {
        ArgumentNullException.ThrowIfNull(variables);
        var items = variables.ToArray();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var variable in items)
        {
            if (variable is null)
            {
                throw new ArgumentNullException(nameof(variables), "Environment variables must not contain null.");
            }

            if (!names.Add(variable.Name))
            {
                throw new DomainException($"Duplicate environment variable name '{variable.Name}'.");
            }
        }

        return new EnvironmentVariables(items);
    }

    public bool Equals(EnvironmentVariables? other) =>
        other is not null && Items.SequenceEqual(other.Items);

    public override bool Equals(object? obj) => Equals(obj as EnvironmentVariables);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var item in Items)
        {
            hash.Add(item);
        }

        return hash.ToHashCode();
    }
}
