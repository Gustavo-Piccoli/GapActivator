using CommunityToolkit.Mvvm.ComponentModel;
using GapActivator.App.Localization;
using GapActivator.App.Services;
using GapActivator.Ipc;

namespace GapActivator.App.ViewModels;

/// <summary>Base of every page: runs service calls and keeps the text of a failed call in <see cref="ErrorText"/>.</summary>
public abstract partial class PageViewModel(Localizer l) : ViewModelBase(l)
{
    /// <summary>What the last failed call said; null after a call that worked.</summary>
    [ObservableProperty]
    public partial string? ErrorText { get; protected set; }

    /// <summary>Clears <see cref="ErrorText"/>, runs the call, and returns false (with the error text set) when the service refused it or was unreachable.</summary>
    protected async Task<bool> TryAsync(Func<Task> action)
    {
        ArgumentNullException.ThrowIfNull(action);
        ErrorText = null;
        try
        {
            await action().ConfigureAwait(true);
            return true;
        }
        catch (Exception ex) when (ex is GapActivatorRequestException or TimeoutException)
        {
            ErrorText = ServiceErrors.Describe(ex, L);
            return false;
        }
    }
}
