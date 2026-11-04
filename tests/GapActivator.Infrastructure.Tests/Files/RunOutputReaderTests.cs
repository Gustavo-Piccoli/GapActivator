using System.Text;
using GapActivator.Domain.Runs;
using GapActivator.Infrastructure.Files;
using GapActivator.Testing;

namespace GapActivator.Infrastructure.Tests.Files;

[Trait("Category", "Integration")]
public sealed class RunOutputReaderTests : IAsyncLifetime
{
    private readonly TempDataRoot _root = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private GapActivatorPaths Paths => new(_root.Path);

    public ValueTask InitializeAsync()
    {
        Paths.EnsureDirectories();
        return ValueTask.CompletedTask;
    }

    public ValueTask DisposeAsync() => _root.DisposeAsync();

    [Fact]
    public async Task ReadTail_MissingFile_ReturnsNotExists()
    {
        var output = await new RunOutputReader(Paths).ReadTailAsync(RunId.New(), 1024, Ct);

        Assert.Equal(new GapActivator.Application.Ports.RunOutput(false, string.Empty, false), output);
    }

    [Fact]
    public async Task ReadTail_SmallFile_ReturnsAllNotTruncated()
    {
        var runId = RunId.New();
        await File.WriteAllTextAsync(Paths.RunOutputFile(runId), "hello\r\nçã\r\n", Ct);

        var output = await new RunOutputReader(Paths).ReadTailAsync(runId, 1024, Ct);

        Assert.True(output.Exists);
        Assert.Equal("hello\r\nçã\r\n", output.Text);
        Assert.False(output.Truncated);
    }

    [Fact]
    public async Task ReadTail_LargerThanMax_ReturnsTailWithoutABrokenCharacter()
    {
        var runId = RunId.New();
        // "aç" + "bcd": bytes 61 C3 A7 62 63 64. Keeping the last 4 bytes starts on the continuation byte A7.
        await File.WriteAllBytesAsync(Paths.RunOutputFile(runId), Encoding.UTF8.GetBytes("a\u00e7bcd"), Ct);

        var output = await new RunOutputReader(Paths).ReadTailAsync(runId, 4, Ct);

        Assert.True(output.Truncated);
        Assert.Equal("bcd", output.Text);
        Assert.DoesNotContain('\uFFFD', output.Text);
    }

    [Fact]
    public async Task ReadTail_LargerThanMaxCutOnACharacterStart_KeepsTheCharacter()
    {
        var runId = RunId.New();
        await File.WriteAllBytesAsync(Paths.RunOutputFile(runId), Encoding.UTF8.GetBytes("a\u00e7bcd"), Ct);

        var output = await new RunOutputReader(Paths).ReadTailAsync(runId, 5, Ct);

        Assert.True(output.Truncated);
        Assert.Equal("\u00e7bcd", output.Text);
    }

    [Fact]
    public async Task ReadTail_FileOpenByWriter_StillReads()
    {
        var runId = RunId.New();
        await using var writer = new FileStream(
            Paths.RunOutputFile(runId), FileMode.Create, FileAccess.Write, FileShare.Read);
        await writer.WriteAsync(Encoding.UTF8.GetBytes("still running\r\n"), Ct);
        await writer.FlushAsync(Ct);

        var output = await new RunOutputReader(Paths).ReadTailAsync(runId, 1024, Ct);

        Assert.True(output.Exists);
        Assert.Equal("still running\r\n", output.Text);
    }
}
