using System.Collections;
using System.Globalization;
using System.Text;

namespace GapActivator.Infrastructure.Processes;

/// <summary>Builds the Unicode environment block of a child (<c>CREATE_UNICODE_ENVIRONMENT</c>).</summary>
internal static class EnvironmentBlockBuilder
{
    public const string ParentPidVariable = "GAPACTIVATOR_PARENT_PID";
    public const string ParentStartTimeVariable = "GAPACTIVATOR_PARENT_START_TIME";

    /// <summary>
    /// The service's variables, then the registration's, which replace a service variable whose name differs only in
    /// case, then the parent identity. Names are compared and sorted ignoring case, as Windows does. The result is
    /// <c>name=value\0</c> per variable followed by one more <c>\0</c>.
    /// </summary>
    public static char[] Build(
        IDictionary serviceEnvironment,
        IReadOnlyList<KeyValuePair<string, string>> registration,
        ParentIdentity parent)
    {
        ArgumentNullException.ThrowIfNull(serviceEnvironment);
        ArgumentNullException.ThrowIfNull(registration);
        ArgumentNullException.ThrowIfNull(parent);

        var variables = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (DictionaryEntry entry in serviceEnvironment)
        {
            variables[(string)entry.Key] = entry.Value?.ToString() ?? "";
        }

        foreach ((string name, string value) in registration)
        {
            Set(variables, name, value);
        }

        Set(variables, ParentPidVariable, parent.Pid.ToString(CultureInfo.InvariantCulture));
        Set(variables, ParentStartTimeVariable, parent.FormattedStartTime);

        var block = new StringBuilder();
        foreach (KeyValuePair<string, string> variable in variables.OrderBy(v => v.Key, StringComparer.OrdinalIgnoreCase))
        {
            block.Append(variable.Key).Append('=').Append(variable.Value).Append('\0');
        }

        block.Append('\0');
        return block.ToString().ToCharArray();
    }

    /// <summary>Replaces the variable including the case of its name.</summary>
    private static void Set(Dictionary<string, string> variables, string name, string value)
    {
        variables.Remove(name);
        variables[name] = value;
    }
}
