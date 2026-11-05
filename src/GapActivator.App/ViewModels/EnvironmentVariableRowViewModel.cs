using CommunityToolkit.Mvvm.ComponentModel;
using GapActivator.App.Localization;
using GapActivator.Ipc.Contracts;

namespace GapActivator.App.ViewModels;

/// <summary>
/// One environment variable of the editor. A secret is write-only: a stored one shows a placeholder and no value, and an empty
/// value on it means "keep what is stored". For a viewer the value is the localized word for hidden.
/// </summary>
public sealed partial class EnvironmentVariableRowViewModel : ViewModelBase
{
    private readonly bool _hidden;
    private readonly bool _storedAtLoad;
    private readonly string _loadedName;

    internal EnvironmentVariableRowViewModel(Localizer l, string name, string value, bool isSecret, bool hasStoredSecret, bool hidden)
        : base(l)
    {
        _hidden = hidden;
        _storedAtLoad = hasStoredSecret;
        _loadedName = name.Trim();
        Name = name;
        Value = value;
        IsSecret = isSecret;
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AutomationName), nameof(SecretAutomationName), nameof(RemoveAutomationName), nameof(HasStoredSecret), nameof(Placeholder))]
    public partial string Name { get; set; }

    [ObservableProperty]
    public partial string Value { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Placeholder), nameof(MaskChar))]
    public partial bool IsSecret { get; set; }

    /// <summary>Set by the editor: the user cannot change the form (a viewer, no connection, or still loading).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsUnlocked))]
    public partial bool IsLocked { get; set; }

    public bool IsUnlocked => !IsLocked;

    /// <summary>
    /// True when the service holds a secret value under this name that the app never received. The service finds a stored value
    /// by name, so a renamed variable no longer has one (the user must type a value); renaming it back restores it.
    /// </summary>
    public bool HasStoredSecret => _storedAtLoad && Name.Trim() == _loadedName;

    /// <summary>True for a viewer: the value is not known and is never editable.</summary>
    public bool IsHidden => _hidden;

    /// <summary>The hint of a stored secret that is still a secret; null otherwise.</summary>
    public string? Placeholder => HasStoredSecret && IsSecret ? L.StoredSecretPlaceholder : null;

    /// <summary>The character a password box shows instead of the text; none for a plain variable or a hidden value.</summary>
    public char MaskChar => IsSecret && !_hidden ? '•' : '\0';

    public string AutomationName => L.VariableValueFor(NameOrPlaceholder());

    public string SecretAutomationName => L.SecretFor(NameOrPlaceholder());

    public string RemoveAutomationName => L.RemoveVariableFor(NameOrPlaceholder());

    /// <summary>True when the variable needs a value it does not have: a new secret, or a stored secret turned plain.</summary>
    internal bool NeedsValue => !_hidden && Value.Length == 0 && ((IsSecret && !HasStoredSecret) || (!IsSecret && HasStoredSecret));

    /// <summary>The input to send. An untouched stored secret is (Name, null, true): the service keeps it.</summary>
    public EnvironmentVariableInputDto ToInput()
    {
        var name = Name.Trim();
        return IsSecret && HasStoredSecret && Value.Length == 0
            ? new EnvironmentVariableInputDto(name, null, true)
            : new EnvironmentVariableInputDto(name, Value, IsSecret);
    }

    /// <summary>Follows a language switch: the placeholder, the accessible names and the word for hidden.</summary>
    internal void Refresh()
    {
        if (_hidden)
        {
            Value = L.ValueHidden;
        }

        OnPropertyChanged(nameof(Placeholder));
        OnPropertyChanged(nameof(AutomationName));
        OnPropertyChanged(nameof(SecretAutomationName));
        OnPropertyChanged(nameof(RemoveAutomationName));
    }

    private string NameOrPlaceholder() => Name.Trim().Length == 0 ? L.NewVariable : Name.Trim();
}
