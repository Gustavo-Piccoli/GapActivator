using GapActivator.Infrastructure.Processes;

namespace GapActivator.Infrastructure.Tests.Processes;

public sealed class CommandLineBuilderTests
{
    [Fact]
    public void CommandLineBuilder_PlainExecutable_QuotesPathAndAppendsArgumentsVerbatim()
    {
        const string executable = @"C:\Tools\My App\run.exe";

        (string application, string commandLine) = CommandLineBuilder.Build(executable, "\"a b\" c  --x=\"y z\" ");
        (string _, string withoutArguments) = CommandLineBuilder.Build(executable, "");

        application.ShouldBe(executable);
        commandLine.ShouldBe("\"C:\\Tools\\My App\\run.exe\" \"a b\" c  --x=\"y z\" ");
        withoutArguments.ShouldBe("\"C:\\Tools\\My App\\run.exe\"");
    }

    [Theory]
    [InlineData(".cmd")]
    [InlineData(".BAT")]
    public void CommandLineBuilder_CmdOrBat_UsesCmdExeWithDSC(string extension)
    {
        string script = @"C:\Scripts\hello world" + extension;
        string cmd = Path.Combine(Environment.SystemDirectory, "cmd.exe");

        (string application, string commandLine) = CommandLineBuilder.Build(script, "one \"two three\"");
        (string _, string withoutArguments) = CommandLineBuilder.Build(script, "");

        application.ShouldBe(cmd);
        commandLine.ShouldBe($"\"{cmd}\" /d /s /c \"\"{script}\" one \"two three\"\"");
        withoutArguments.ShouldBe($"\"{cmd}\" /d /s /c \"\"{script}\"\"");
    }
}
