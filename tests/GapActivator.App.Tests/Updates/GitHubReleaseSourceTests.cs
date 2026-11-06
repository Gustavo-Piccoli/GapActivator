using System.Net;
using GapActivator.App.Tests.Fakes;
using GapActivator.App.Updates;

namespace GapActivator.App.Tests.Updates;

public class GitHubReleaseSourceTests
{
    private const string Release = """{"tag_name":"v0.2.0","html_url":"https://github.com/Gustavo-Piccoli/GapActivator/releases/tag/v0.2.0"}""";

    private static GitHubReleaseSource Create(StubHttpHandler handler) => new(new HttpClient(handler), "0.1.0");

    [Fact]
    public async Task GetLatest_SendsTheUrlAndHeaders()
    {
        var handler = new StubHttpHandler(HttpStatusCode.OK, Release);

        var release = await Create(handler).GetLatestAsync(TestContext.Current.CancellationToken);

        var request = handler.LastRequest.ShouldNotBeNull();
        request.Method.ShouldBe(HttpMethod.Get);
        request.RequestUri.ShouldBe(new Uri("https://api.github.com/repos/Gustavo-Piccoli/GapActivator/releases/latest"));
        request.Headers.GetValues("User-Agent").Single().ShouldBe("GapActivator/0.1.0");
        request.Headers.GetValues("Accept").Single().ShouldBe("application/vnd.github+json");
        request.Headers.GetValues("X-GitHub-Api-Version").Single().ShouldBe("2022-11-28");
        release.ShouldNotBeNull().TagName.ShouldBe("v0.2.0");
    }

    [Fact]
    public async Task GetLatest_NotFound_ReturnsNull()
    {
        var release = await Create(new StubHttpHandler(HttpStatusCode.NotFound)).GetLatestAsync(TestContext.Current.CancellationToken);

        release.ShouldBeNull();
    }

    [Fact]
    public async Task GetLatest_NonGitHubPage_Throws()
    {
        var body = """{"tag_name":"v0.2.0","html_url":"https://example.com/release"}""";

        await Should.ThrowAsync<InvalidDataException>(
            () => Create(new StubHttpHandler(HttpStatusCode.OK, body)).GetLatestAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task GetLatest_ServerError_Throws() =>
        await Should.ThrowAsync<HttpRequestException>(
            () => Create(new StubHttpHandler(HttpStatusCode.InternalServerError)).GetLatestAsync(TestContext.Current.CancellationToken));
}
