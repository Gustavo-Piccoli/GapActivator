using CommunityToolkit.Mvvm.ComponentModel;
using GapActivator.App.Localization;
using GapActivator.Ipc.Contracts;

namespace GapActivator.App.Services;

/// <summary>Whether the signed-in user can change things, and the reason when not. Follows the connection and the language.</summary>
public sealed partial class Permissions : ObservableObject
{
    private readonly Localizer _l;
    private readonly IServiceGateway _gateway;

    public Permissions(Localizer l, IServiceGateway gateway)
    {
        _l = l;
        _gateway = gateway;
        gateway.ConnectionChanged += (_, _) => Refresh();
        l.PropertyChanged += (_, _) => OnPropertyChanged(nameof(DeniedReason));
    }

    public bool CanChange => _gateway.Status.CanChange;

    /// <summary>Null when the user can change things.</summary>
    public string? DeniedReason
    {
        get
        {
            GatewayStatus status = _gateway.Status;
            if (status.CanChange)
            {
                return null;
            }

            if (!status.IsConnected)
            {
                return _l.DeniedDisconnected;
            }

            return status.OperatorsMode == OperatorsGroupMode.Enabled ? _l.DeniedOperators : _l.DeniedAdministratorsOnly;
        }
    }

    private void Refresh()
    {
        OnPropertyChanged(nameof(CanChange));
        OnPropertyChanged(nameof(DeniedReason));
    }
}
