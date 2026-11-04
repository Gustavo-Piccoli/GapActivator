using GapActivator.Service.Tests.Support;
using GapActivator.Testing;
using Microsoft.Extensions.Hosting;

namespace GapActivator.Service.Tests;

public sealed class EntryTests
{
    [Theory]
    [InlineData("relative\\data")]
    [InlineData(" ")]
    [InlineData("")]
    public async Task Run_InvalidDataRoot_ReportsTheFailureAndReturns1(string dataRoot)
    {
        int exitCodeBefore = Environment.ExitCode;
        var reported = new List<Exception>();

        int code = await ServiceEntry.RunAsync(
            ["--GapActivator:DataRoot=" + dataRoot], reported.Add, TestContext.Current.CancellationToken);

        code.ShouldBe(ServiceExitCodes.Fatal);
        reported.ShouldHaveSingleItem().ShouldBeOfType<ArgumentException>();
        Environment.ExitCode.ShouldBe(exitCodeBefore);
    }

    [Fact]
    public async Task Run_BuildThrows_ReportsTheFailureAndReturns1()
    {
        var reported = new List<Exception>();

        int code = await ServiceEntry.RunAsync(
            () => throw new InvalidOperationException("build failed"), reported.Add, TestContext.Current.CancellationToken);

        code.ShouldBe(ServiceExitCodes.Fatal);
        reported.ShouldHaveSingleItem().Message.ShouldBe("build failed");
    }

    [Fact]
    public async Task Run_BuildThrows_WritesOneLineToStandardError()
    {
        var lines = new List<string>();

        await ServiceEntry.RunAsync(
            () => throw new InvalidOperationException("build failed"), _ => { }, lines.Add, TestContext.Current.CancellationToken);

        lines.ShouldBe(["GapActivator failed to start: build failed"]);
    }

    [Fact]
    public async Task Run_StandardErrorWriterThrows_StillReturns1()
    {
        int code = await ServiceEntry.RunAsync(
            () => throw new InvalidOperationException("build failed"),
            _ => { },
            _ => throw new IOException("stderr closed"),
            TestContext.Current.CancellationToken);

        code.ShouldBe(ServiceExitCodes.Fatal);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Run_WorkerFailsAfterStart_ReturnsTheWorkerExitCodeWithoutReporting()
    {
        int exitCodeBefore = Environment.ExitCode;
        await using var root = new TempDataRoot();
        string file = Path.Combine(root.Path, "not-a-folder");
        await File.WriteAllTextAsync(file, "x", TestContext.Current.CancellationToken);
        var exitCodes = new List<int>();
        var reported = new List<Exception>();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));

        int code = await ServiceEntry.RunAsync(
            () => ServiceHostFactory.CreateBuilder(file, exitCodes).Build(), reported.Add, timeout.Token);

        code.ShouldBe(ServiceExitCodes.Fatal);
        exitCodes.ShouldBe([ServiceExitCodes.Fatal]);
        reported.ShouldBeEmpty();
        Environment.ExitCode.ShouldBe(exitCodeBefore);
    }

    [Fact]
    public void FatalEventLog_Write_NeverThrows() =>
        Should.NotThrow(() => FatalEventLog.Write(new InvalidOperationException("probe"), source: "GapActivator.Tests.Missing." + Guid.NewGuid().ToString("N")));
}
