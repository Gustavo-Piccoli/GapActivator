using System.Security.Principal;

namespace GapActivator.Service.Ipc;

internal static class CallerRules
{
    public static bool IsOperatorsGroupEnabled(OperatorsGroupSetting setting, SecurityIdentifier serviceUser) =>
        setting switch
        {
            OperatorsGroupSetting.Enabled => true,
            OperatorsGroupSetting.Disabled => false,
            _ => serviceUser.Value.StartsWith("S-1-5-80-", StringComparison.Ordinal),
        };

    public static bool CanChange(bool isAdministrator, bool isOperator, bool operatorsGroupEnabled) =>
        isAdministrator || (operatorsGroupEnabled && isOperator);
}
