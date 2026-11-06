using System.ComponentModel;
using GapActivator.App.Localization;
using GapActivator.App.Services;
using GapActivator.Ipc;

namespace GapActivator.App.ViewModels;

/// <summary>Base of every page: runs service calls and keeps what a failed call said, shown as <see cref="ErrorText"/>.</summary>
public abstract class PageViewModel : ViewModelBase
{
    // What the error says, not the words: the text is built from the localizer each time it is read, so it follows a language switch.
    private Func<string>? _error;
    private PropertyChangedEventHandler? _follow;

    protected PageViewModel(Localizer l)
        : base(l)
    {
        _follow = Follow(this, l);
    }

    /// <summary>What the last failed call said, in the current language; null after a call that worked.</summary>
    public string? ErrorText => _error?.Invoke();

    /// <summary>The error now shown, to tell later whether it is still the same one.</summary>
    protected Func<string>? CurrentError => _error;

    /// <summary>Shows an error. <paramref name="text"/> is called on every read, so it must build the words from the localizer and the saved arguments.</summary>
    protected void SetError(Func<string>? text)
    {
        var before = ErrorText;
        _error = text;
        if (!string.Equals(before, ErrorText, StringComparison.Ordinal))
        {
            OnPropertyChanged(nameof(ErrorText));
        }
    }

    /// <summary>
    /// Runs the call and returns false (with the error set) when the service refused it or was unreachable. The error is not
    /// cleared before the call, so a call that keeps failing (a refresh every second while disconnected) does not blank and re-announce
    /// it each time; it is cleared after a call that worked, unless the call itself set a new one.
    /// </summary>
    protected async Task<bool> TryAsync(Func<Task> action)
    {
        ArgumentNullException.ThrowIfNull(action);
        var before = _error;
        try
        {
            await action().ConfigureAwait(true);
            if (ReferenceEquals(_error, before))
            {
                SetError(null);
            }

            return true;
        }
        catch (Exception ex) when (ex is GapActivatorRequestException or TimeoutException)
        {
            // The code and the service's message are kept; the words are made when read.
            (string code, string? message) = ex is GapActivatorRequestException request
                ? (request.Code, request.Message)
                : (IpcErrorCodes.Disconnected, null);
            SetError(() => ServiceErrors.Describe(code, message, L));
            return false;
        }
    }

    /// <summary>Stops <see cref="ErrorText"/> following the language; a page that is disposed calls this.</summary>
    protected void StopFollowingLanguage()
    {
        if (_follow is { } handler)
        {
            L.PropertyChanged -= handler;
            _follow = null;
        }
    }

    // The localizer outlives the page (editors come and go), so the subscription holds the page weakly and ends itself when it is gone.
    private static PropertyChangedEventHandler Follow(PageViewModel page, Localizer l)
    {
        var weak = new WeakReference<PageViewModel>(page);
        PropertyChangedEventHandler? handler = null;
        handler = (_, _) =>
        {
            if (weak.TryGetTarget(out PageViewModel? target))
            {
                target.OnPropertyChanged(nameof(ErrorText));
            }
            else
            {
                l.PropertyChanged -= handler;
            }
        };
        l.PropertyChanged += handler;
        return handler;
    }
}
