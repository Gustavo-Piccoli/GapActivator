namespace GapActivator.App.Services;

/// <summary>Marshals work onto the UI thread; the only way timer or pipe threads reach view-model state.</summary>
public interface IUiDispatcher
{
    void Post(Action action);
}
