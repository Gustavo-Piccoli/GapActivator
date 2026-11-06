using System.Net;
using System.Text.Json;

namespace GapActivator.App.Updates;

/// <summary>Reads the latest release from the GitHub REST API. Sends nothing but the product version, and never downloads anything.</summary>
public sealed class GitHubReleaseSource(HttpClient http, string productVersion) : IReleaseSource
{
    private static readonly Uri LatestUrl = new("https://api.github.com/repos/Gustavo-Piccoli/GapActivator/releases/latest");

    private const string ReleasesPath = "/Gustavo-Piccoli/GapActivator/releases/";

    public async Task<ReleaseInfo?> GetLatestAsync(CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, LatestUrl);
        request.Headers.TryAddWithoutValidation("User-Agent", $"GapActivator/{productVersion}");
        request.Headers.TryAddWithoutValidation("Accept", "application/vnd.github+json");
        request.Headers.TryAddWithoutValidation("X-GitHub-Api-Version", "2022-11-28");

        using var response = await http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();

        var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await using (stream.ConfigureAwait(false))
        {
            var dto = await JsonSerializer.DeserializeAsync(stream, GitHubReleaseJsonContext.Default.GitHubReleaseDto, cancellationToken).ConfigureAwait(false);
            if (dto is null || string.IsNullOrWhiteSpace(dto.TagName) || string.IsNullOrWhiteSpace(dto.HtmlUrl))
            {
                throw new InvalidDataException("Release payload is missing tag_name or html_url.");
            }

            // A prerelease or a draft is never offered as an update: to the user it is as if nothing was published.
            if (dto.Prerelease || dto.Draft)
            {
                return null;
            }

            if (!Uri.TryCreate(dto.HtmlUrl, UriKind.Absolute, out var page)
                || page.Scheme != Uri.UriSchemeHttps
                || !string.Equals(page.Host, "github.com", StringComparison.OrdinalIgnoreCase)
                || !page.AbsolutePath.StartsWith(ReleasesPath, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("Release html_url is not a release page of the GapActivator repository on github.com.");
            }

            return new ReleaseInfo(dto.TagName, page);
        }
    }
}
