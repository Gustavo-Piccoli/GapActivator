using GapActivator.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace GapActivator.Service.Tests;

/// <summary>
/// The tests that write the test process's own exit code. They run alone, after the parallel tests (some of which
/// assert that the exit code is unchanged), and restore the previous value.
/// </summary>
[CollectionDefinition(ProcessExitCodeDefinition.Name, DisableParallelization = true)]
public sealed class ProcessExitCodeDefinition
{
    public const string Name = "ProcessExitCode";
}

[Collection(ProcessExitCodeDefinition.Name)]
[Trait("Category", "Integration")]
public sealed class ProcessExitCodeTests
{
    [Fact]
    public async Task Host_ExitCodeSink_WritesTheProcessExitCode()
    {
        await using var root = new TempDataRoot();
        using IHost host = GapActivatorHost.CreateBuilder(["--GapActivator:DataRoot=" + root.Path]).Build();
        ExitCodeSink sink = host.Services.GetRequiredService<ExitCodeSink>();
        int previous = Environment.ExitCode;

        try
        {
            sink.Set(ServiceExitCodes.Restart);

            Environment.ExitCode.ShouldBe(3);
            sink.Last.ShouldBe(3);
        }
        finally
        {
            Environment.ExitCode = previous;
        }
    }
}
