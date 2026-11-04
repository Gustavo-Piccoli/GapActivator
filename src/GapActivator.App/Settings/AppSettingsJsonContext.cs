using System.Text.Json;
using System.Text.Json.Serialization;

namespace GapActivator.App.Settings;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    ReadCommentHandling = JsonCommentHandling.Skip,
    AllowTrailingCommas = true)]
[JsonSerializable(typeof(AppSettingsDocument))]
internal sealed partial class AppSettingsJsonContext : JsonSerializerContext;
