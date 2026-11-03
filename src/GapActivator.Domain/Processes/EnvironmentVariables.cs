using GapActivator.Domain.Common;

namespace GapActivator.Domain.Processes;

/// <summary>Ordered set of environment variables with unique names (case-insensitive).</summary>
public sealed class EnvironmentVariables
{
    private EnvironmentVariables(IReadOnlyList<EnvironmentVariable> items) => Items = items;

    public static EnvironmentVariables Empty { get; } = new([]);

    public IReadOnlyList<EnvironmentVariable> Items { get; }

    public static EnvironmentVariables Create(IEnumerable<EnvironmentVariable> variables)
    {
        ArgumentNullException.ThrowIfNull(variables);
        var items = variables.ToArray();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var variable in items)
        {
            if (!names.Add(variable.Name))
            {
                throw new DomainException($"Duplicate environment variable name '{variable.Name}'.");
            }
        }

        return new EnvironmentVariables(items);
    }
}
