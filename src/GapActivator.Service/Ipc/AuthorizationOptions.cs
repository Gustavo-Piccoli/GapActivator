using Microsoft.Extensions.Configuration;

namespace GapActivator.Service.Ipc;

/// <summary>Whether membership of the local operators group grants the right to change state.</summary>
public enum OperatorsGroupSetting
{
    Auto,
    Enabled,
    Disabled,
}

/// <summary>Authorization settings read from configuration.</summary>
public sealed record AuthorizationOptions(OperatorsGroupSetting OperatorsGroup)
{
    public const string OperatorsGroupKey = "GapActivator:Authorization:OperatorsGroup";

    public const string OperatorsGroupName = "GapActivator Operators";

    public static AuthorizationOptions FromConfiguration(IConfiguration configuration)
    {
        string? raw = configuration[OperatorsGroupKey];
        if (raw is null)
        {
            return new AuthorizationOptions(OperatorsGroupSetting.Auto);
        }

        // Exact name only (ignoring case): Enum.TryParse would also accept numbers and comma lists.
        string? exact = Enum.GetNames<OperatorsGroupSetting>()
            .FirstOrDefault(n => string.Equals(n, raw, StringComparison.OrdinalIgnoreCase));
        if (exact is null)
        {
            throw new InvalidOperationException(
                $"Invalid value '{raw}' for {OperatorsGroupKey}. Expected one of: Auto, Enabled, Disabled.");
        }

        return new AuthorizationOptions(Enum.Parse<OperatorsGroupSetting>(exact));
    }
}
