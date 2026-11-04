using System.Globalization;
using System.Security;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace GapActivator.App.Settings;

/// <summary>Reads and writes the app-local settings file. Neither method throws for file or JSON problems.</summary>
public sealed partial class AppSettingsStore(string path, ILogger<AppSettingsStore> logger, CultureInfo uiCulture)
{
    private const int SchemaVersion = 1;

    public AppSettings Load()
    {
        var defaults = AppSettings.Defaults(uiCulture);
        if (!File.Exists(path))
        {
            return defaults;
        }

        AppSettingsDocument? document;
        try
        {
            document = JsonSerializer.Deserialize(File.ReadAllText(path), AppSettingsJsonContext.Default.AppSettingsDocument);
        }
        catch (JsonException)
        {
            return Corrupt(defaults);
        }
        catch (Exception ex) when (IsFileSystemFailure(ex))
        {
            LogReadFailed(logger, ex);
            return defaults;
        }

        if (document is null)
        {
            return Corrupt(defaults);
        }

        return new AppSettings(
            ReadEnum(document.Theme, "theme", defaults.Theme),
            ReadEnum(document.Language, "language", defaults.Language),
            document.Notifications ?? defaults.Notifications,
            document.CheckForUpdates ?? defaults.CheckForUpdates,
            document.LastUpdateCheck,
            document.Window);
    }

    public bool Save(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var document = new AppSettingsDocument
        {
            SchemaVersion = SchemaVersion,
            Theme = settings.Theme.ToString(),
            Language = settings.Language.ToString(),
            Notifications = settings.Notifications,
            CheckForUpdates = settings.CheckForUpdates,
            LastUpdateCheck = settings.LastUpdateCheck,
            Window = settings.Window,
        };

        try
        {
            AtomicFile.WriteAllText(path, JsonSerializer.Serialize(document, AppSettingsJsonContext.Default.AppSettingsDocument));
            return true;
        }
        catch (Exception ex) when (IsFileSystemFailure(ex))
        {
            LogSaveFailed(logger, ex);
            return false;
        }
    }

    private static bool IsFileSystemFailure(Exception ex) =>
        ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or SecurityException;

    [LoggerMessage(Level = LogLevel.Warning, Message = "The app settings file is corrupt; defaults are used.")]
    private static partial void LogCorrupt(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The app settings file could not be moved aside.")]
    private static partial void LogCorruptNotMoved(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The app settings file could not be read; defaults are used.")]
    private static partial void LogReadFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The app settings file could not be saved; the change stays in memory only.")]
    private static partial void LogSaveFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The app setting '{Field}' has the unknown value '{Value}'; its default is used.")]
    private static partial void LogUnknownValue(ILogger logger, string field, string value);

    private AppSettings Corrupt(AppSettings defaults)
    {
        try
        {
            File.Move(path, path + ".corrupt", overwrite: true);
        }
        catch (Exception ex) when (IsFileSystemFailure(ex))
        {
            LogCorruptNotMoved(logger, ex);
        }

        LogCorrupt(logger);
        return defaults;
    }

    private T ReadEnum<T>(string? value, string field, T fallback)
        where T : struct, Enum
    {
        if (value is null)
        {
            return fallback;
        }

        if (Enum.TryParse(value, ignoreCase: true, out T parsed) && Enum.IsDefined(parsed))
        {
            return parsed;
        }

        LogUnknownValue(logger, field, value);
        return fallback;
    }
}
