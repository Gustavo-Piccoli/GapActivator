using Avalonia.Threading;

namespace GapActivator.App.Services;

public sealed class AvaloniaUiDispatcher : IUiDispatcher
{
    public void Post(Action action) => Dispatcher.UIThread.Post(action);
}
