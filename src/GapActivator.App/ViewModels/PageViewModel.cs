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

    /// <summary>
    /// Runs the call and returns false (with the error text set) when the service refused it or was unreachable. The text is not
    /// cleared before the call, so a call that keeps failing (a refresh every second while disconnected) does not blank and re-announce
    /// it each time; it is cleared after a call that worked, unless the call itself set a new one.
    /// </summary>
    protected async Task<bool> TryAsync(Func<Task> action)
    {
        ArgumentNullException.ThrowIfNull(action);
        var before = ErrorText;
        try
        {
            await action().ConfigureAwait(true);
            if (string.Equals(ErrorText, before, StringComparison.Ordinal))
            {
                ErrorText = null;
            }

            return true;
        }
        catch (Exception ex) when (ex is GapActivatorRequestException or TimeoutException)
        {
            ErrorText = ServiceErrors.Describe(ex, L);
            return false;
        }
    }
}
