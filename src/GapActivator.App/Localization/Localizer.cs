using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace GapActivator.App.Localization;

/// <summary>Live UI translation: bindings read the properties; changing the language refreshes them all.</summary>
public sealed partial class Localizer(AppLanguage language) : ObservableObject
{
    private static readonly CultureInfo PortugueseCulture = CultureInfo.GetCultureInfo("pt-BR");
    private static readonly CultureInfo EnglishCulture = CultureInfo.GetCultureInfo("en-US");

    /// <summary>Members whose English and Portuguese texts are equal on purpose.</summary>
    internal static IReadOnlySet<string> SameInBothLanguages { get; } = new HashSet<string>
    {
        nameof(AppName),
    };

    public AppLanguage Language { get; private set; } = language;

    public CultureInfo Culture => Language == AppLanguage.Portuguese ? PortugueseCulture : EnglishCulture;

    public static AppLanguage DefaultLanguage(CultureInfo uiCulture)
    {
        ArgumentNullException.ThrowIfNull(uiCulture);
        return uiCulture.TwoLetterISOLanguageName == "pt" ? AppLanguage.Portuguese : AppLanguage.English;
    }

    public void SetLanguage(AppLanguage language)
    {
        if (language == Language)
        {
            return;
        }

        Language = language;

        // An empty property name refreshes every binding on this object.
        OnPropertyChanged(string.Empty);
    }

    private string T(string en, string pt) => Language == AppLanguage.Portuguese ? pt : en;
}
