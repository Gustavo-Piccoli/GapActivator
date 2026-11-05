using GapActivator.App.Localization;
using GapActivator.App.Services;
using GapActivator.App.Tests.Fakes;
using GapActivator.Ipc.Contracts;

namespace GapActivator.App.Tests.Services;

public class PermissionsTests : IAsyncDisposable
{
    private readonly Localizer _l = new(AppLanguage.English);
    private readonly FakeServiceGateway _gateway = new();

    public ValueTask DisposeAsync()
    {
        GC.SuppressFinalize(this);
        return _gateway.DisposeAsync();
    }

    [Fact]
    public void Permissions_Operator_CanChange()
    {
        var permissions = new Permissions(_l, _gateway);

        permissions.CanChange.ShouldBeTrue();
        permissions.DeniedReason.ShouldBeNull();
    }

    [Fact]
    public void Permissions_ViewerOperatorsEnabled_DeniedReasonIsAdministratorsOrOperators()
    {
        _gateway.SetStatus(FakeServiceGateway.Viewer(OperatorsGroupMode.Enabled));
        var permissions = new Permissions(_l, _gateway);

        permissions.CanChange.ShouldBeFalse();
        permissions.DeniedReason.ShouldBe("Requires Administrators or GapActivator Operators");
    }

    [Fact]
    public void Permissions_ViewerOperatorsDisabled_DeniedReasonIsAdministratorsOnly()
    {
        _gateway.SetStatus(FakeServiceGateway.Viewer(OperatorsGroupMode.Disabled));
        var permissions = new Permissions(_l, _gateway);

        permissions.DeniedReason.ShouldBe("Administrators only");
    }

    [Fact]
    public void Permissions_Disconnected_CannotChangeAndSaysNotConnected()
    {
        _gateway.SetStatus(FakeServiceGateway.Stopped);
        var permissions = new Permissions(_l, _gateway);

        permissions.CanChange.ShouldBeFalse();
        permissions.DeniedReason.ShouldBe("Not connected to the service.");
    }

    [Fact]
    public void Permissions_StatusChange_RaisesPropertyChanged()
    {
        var permissions = new Permissions(_l, _gateway);
        var raised = new List<string?>();
        permissions.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        _gateway.SetStatus(FakeServiceGateway.Viewer());

        raised.ShouldContain(nameof(Permissions.CanChange));
        raised.ShouldContain(nameof(Permissions.DeniedReason));
        permissions.CanChange.ShouldBeFalse();
    }

    [Fact]
    public void Permissions_LanguageSwitched_RaisesDeniedReason()
    {
        _gateway.SetStatus(FakeServiceGateway.Viewer(OperatorsGroupMode.Disabled));
        var permissions = new Permissions(_l, _gateway);
        var raised = new List<string?>();
        permissions.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        _l.SetLanguage(AppLanguage.Portuguese);

        raised.ShouldContain(nameof(Permissions.DeniedReason));
        permissions.DeniedReason.ShouldBe("Somente Administradores");
    }
}
