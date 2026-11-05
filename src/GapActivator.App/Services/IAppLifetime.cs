namespace GapActivator.App.Services;

public interface IAppLifetime
{
#pragma warning disable CA1716 // Name fixed by the UI service contract; consumers are C# only.
    void Exit();
#pragma warning restore CA1716
}
