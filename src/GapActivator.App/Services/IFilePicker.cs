namespace GapActivator.App.Services;

/// <summary>Lets the user choose an executable file; filter <c>*.exe;*.cmd;*.bat</c>.</summary>
public interface IFilePicker
{
    /// <summary>The chosen local path, or null when the user cancelled.</summary>
    Task<string?> PickExecutableAsync();
}
