using System.Text.Json.Serialization;

namespace GapActivator.App.Updates;

internal sealed record GitHubReleaseDto(
    [property: JsonPropertyName("tag_name")] string? TagName,
    [property: JsonPropertyName("html_url")] string? HtmlUrl);

[JsonSerializable(typeof(GitHubReleaseDto))]
internal sealed partial class GitHubReleaseJsonContext : JsonSerializerContext;
