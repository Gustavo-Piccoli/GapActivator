using System.Text.Json;
using System.Text.Json.Serialization;
using GapActivator.Domain.Processes;
using GapActivator.Domain.Runs;
using GapActivator.Domain.Scheduling;
using GapActivator.Domain.Settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace GapActivator.Infrastructure.Persistence;

/// <summary>
/// Every value converter of the model, applied as conventions: instants as INTEGER UTC ticks, durations as INTEGER
/// ticks, enums as TEXT names, ids as GUIDs, validated strings through their factories, and two JSON columns.
/// </summary>
internal static class ValueConversions
{
    public static void Apply(ModelConfigurationBuilder builder)
    {
        builder.Properties<DateTimeOffset>().HaveConversion<UtcTicksConverter>();
        builder.Properties<DateTimeOffset?>().HaveConversion<UtcTicksConverter>();
        builder.Properties<TimeSpan>().HaveConversion<TicksConverter>();
        builder.Properties<TimeSpan?>().HaveConversion<TicksConverter>();
        builder.Properties<Enum>().HaveConversion<string>();

        builder.Properties<ProcessId>().HaveConversion<ProcessIdConverter>();
        builder.Properties<RunId>().HaveConversion<RunIdConverter>();
        builder.Properties<ScheduleId>().HaveConversion<ScheduleIdConverter>();

        builder.Properties<ProcessName>().HaveConversion<ProcessNameConverter, ProcessNameComparer>();
        builder.Properties<ExecutablePath>().HaveConversion<ExecutablePathConverter, ExecutablePathComparer>();
        builder.Properties<RecurrenceRule>().HaveConversion<RecurrenceRuleConverter>();

        builder.Properties<EnvironmentVariables>().HaveConversion<EnvironmentVariablesConverter, ImmutableValueComparer<EnvironmentVariables>>();
        builder.Properties<EmailAlertSettings>().HaveConversion<EmailAlertSettingsConverter, ImmutableValueComparer<EmailAlertSettings>>();
    }

    private sealed class UtcTicksConverter() : ValueConverter<DateTimeOffset, long>(
        v => v.UtcTicks,
        v => new DateTimeOffset(v, TimeSpan.Zero));

    private sealed class TicksConverter() : ValueConverter<TimeSpan, long>(v => v.Ticks, v => TimeSpan.FromTicks(v));

    private sealed class ProcessIdConverter() : ValueConverter<ProcessId, Guid>(v => v.Value, v => new ProcessId(v));

    private sealed class RunIdConverter() : ValueConverter<RunId, Guid>(v => v.Value, v => new RunId(v));

    private sealed class ScheduleIdConverter() : ValueConverter<ScheduleId, Guid>(v => v.Value, v => new ScheduleId(v));

    private sealed class ProcessNameConverter() : ValueConverter<ProcessName, string>(v => v.Value, v => ProcessName.Create(v));

    private sealed class ExecutablePathConverter() : ValueConverter<ExecutablePath, string>(
        v => v.Value,
        v => ExecutablePath.Create(v));

    private sealed class RecurrenceRuleConverter() : ValueConverter<RecurrenceRule, string>(
        v => v.Value,
        v => RecurrenceRule.Parse(v));

    // The domain compares names and paths ignoring case; the store must still see a change of case only.
    private sealed class ProcessNameComparer() : ValueComparer<ProcessName>(
        (a, b) => SameText(a == null ? null : a.Value, b == null ? null : b.Value),
        v => StringComparer.Ordinal.GetHashCode(v.Value),
        v => v);

    private sealed class ExecutablePathComparer() : ValueComparer<ExecutablePath>(
        (a, b) => SameText(a == null ? null : a.Value, b == null ? null : b.Value),
        v => StringComparer.Ordinal.GetHashCode(v.Value),
        v => v);

    // The JSON-mapped types are immutable, so a by-reference snapshot is enough.
    private sealed class ImmutableValueComparer<T>() : ValueComparer<T>(
        (a, b) => object.Equals(a, b),
        v => v!.GetHashCode(),
        v => v)
        where T : class;

    private sealed class EnvironmentVariablesConverter() : ValueConverter<EnvironmentVariables, string>(
        v => EnvironmentVariablesJson.Write(v),
        v => EnvironmentVariablesJson.Read(v));

    private sealed class EmailAlertSettingsConverter() : ValueConverter<EmailAlertSettings, string>(
        v => EmailAlertSettingsJson.Write(v),
        v => EmailAlertSettingsJson.Read(v));

    private static bool SameText(string? a, string? b) => string.Equals(a, b, StringComparison.Ordinal);

    /// <summary><c>[{"name":"…","value":"…"} | {"name":"…","secret":"&lt;ciphertext&gt;"}]</c>.</summary>
    private static class EnvironmentVariablesJson
    {
        public static string Write(EnvironmentVariables variables) =>
            JsonSerializer.Serialize(
                variables.Items.Select(v => new Item(v.Name, v.PlainValue, v.SecretValue?.Ciphertext)).ToArray(),
                JsonOptions.Default);

        public static EnvironmentVariables Read(string json) =>
            EnvironmentVariables.Create(
                (JsonSerializer.Deserialize<Item[]>(json, JsonOptions.Default) ?? throw Corrupt()).Select(ToVariable));

        private static EnvironmentVariable ToVariable(Item item) => item switch
        {
            { Secret: { } secret, Value: null } => EnvironmentVariable.Secret(item.Name, new ProtectedSecret(secret)),
            { Value: { } value, Secret: null } => EnvironmentVariable.Plain(item.Name, value),
            _ => throw Corrupt(),
        };

        private static JsonException Corrupt() =>
            new("An environment variable must have exactly one of 'value' and 'secret'.");

        private sealed record Item(
            string Name,
            [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Value = null,
            [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Secret = null);
    }

    /// <summary>Every field of the settings; <c>password</c> is the ciphertext or null.</summary>
    private static class EmailAlertSettingsJson
    {
        public static string Write(EmailAlertSettings email) =>
            JsonSerializer.Serialize(
                new Document(
                    email.Enabled,
                    email.Host,
                    email.Port,
                    email.Tls,
                    email.Username,
                    email.Password?.Ciphertext,
                    email.Sender,
                    [.. email.Recipients],
                    [.. email.Alerts]),
                JsonOptions.Default);

        public static EmailAlertSettings Read(string json)
        {
            var document = JsonSerializer.Deserialize<Document>(json, JsonOptions.Default)
                ?? throw new JsonException("The e-mail settings column is empty.");
            return EmailAlertSettings.Create(
                document.Enabled,
                document.Host,
                document.Port,
                document.Tls,
                document.Username,
                document.Password is { } password ? new ProtectedSecret(password) : null,
                document.Sender,
                document.Recipients,
                document.Alerts);
        }

        private sealed record Document(
            bool Enabled,
            string Host,
            int Port,
            TlsMode Tls,
            string? Username,
            string? Password,
            string Sender,
            string[] Recipients,
            AlertKind[] Alerts);
    }

    private static class JsonOptions
    {
        public static readonly JsonSerializerOptions Default = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            RespectNullableAnnotations = true,
            RespectRequiredConstructorParameters = true,
            Converters = { new JsonStringEnumConverter() },
        };
    }
}
