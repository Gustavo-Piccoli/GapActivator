using System.Security.Principal;
using GapActivator.Service.Setup;

namespace GapActivator.Service.Tests.Setup;

public sealed class SetupCommandTests
{
    private static readonly SecurityIdentifier FakeSid = new("S-1-5-21-1-2-3-1001");

    private sealed class Recorder
    {
        public List<string> Calls { get; } = [];

        public SetupActions Actions => new(
            account =>
            {
                Calls.Add("resolve:" + account);
                return account == "missing" ? throw new IdentityNotMappedException() : FakeSid;
            },
            (path, sid) => Calls.Add($"prepare:{path}:{sid.Value}"),
            sid => Calls.Add("grant:" + sid.Value),
            () => Calls.Add("register"),
            () => Calls.Add("unregister"),
            sid => Calls.Add("operator:" + sid.Value),
            () => Calls.Add("remove-group"));
    }

    private static async Task<(int Code, string Out, string Err)> RunAsync(Recorder recorder, params string[] args)
    {
        using var stdout = new StringWriter();
        using var stderr = new StringWriter();
        int code = await SetupCommand.RunAsync(args, stdout, stderr, recorder.Actions);
        return (code, stdout.ToString(), stderr.ToString());
    }

    [Theory]
    [InlineData("setup", true)]
    [InlineData("Setup", false)]
    [InlineData("--GapActivator:DataRoot=C:\\x", false)]
    [InlineData("run", false)]
    public void IsSetup_OnlyTheExactFirstArgument(string first, bool expected) =>
        SetupCommand.IsSetup([first, "setup"]).ShouldBe(expected);

    [Fact]
    public void IsSetup_EmptyOrLaterSetup_IsFalse()
    {
        SetupCommand.IsSetup([]).ShouldBeFalse();
        SetupCommand.IsSetup(["--x", "setup"]).ShouldBeFalse();
    }

    [Theory]
    [InlineData]
    [InlineData("frobnicate")]
    [InlineData("prepare-data-root")]
    [InlineData("grant-logon-right")]
    [InlineData("prepare-data-root", "--account")]
    [InlineData("prepare-data-root", "--path", "C:\\x")]
    [InlineData("prepare-data-root", "--account", "a", "--bogus", "b")]
    [InlineData("prepare-data-root", "--account", "a", "--path", "relative")]
    [InlineData("grant-logon-right", "--account", "a", "--path", "C:\\x")]
    [InlineData("register-event-source", "extra")]
    [InlineData("unregister-event-source", "--account", "a")]
    [InlineData("add-operator")]
    [InlineData("add-operator", "--account", "a", "--path", "C:\\x")]
    [InlineData("remove-operators-group", "--account", "a")]
    public async Task Run_UsageError_Returns2WithOneStderrLine(params string[] args)
    {
        var recorder = new Recorder();

        (int code, string stdout, string stderr) = await RunAsync(recorder, args);

        code.ShouldBe(2);
        stdout.ShouldBeEmpty();
        stderr.TrimEnd().Split('\n').Length.ShouldBe(1);
        recorder.Calls.ShouldBeEmpty();
    }

    [Fact]
    public async Task Run_UnresolvableAccount_Returns1NamingTheAccount()
    {
        var recorder = new Recorder();

        (int code, _, string stderr) = await RunAsync(recorder, "grant-logon-right", "--account", "missing");

        code.ShouldBe(1);
        stderr.ShouldContain("missing");
        stderr.TrimEnd().Split('\n').Length.ShouldBe(1);
        recorder.Calls.ShouldBe(["resolve:missing"]);
    }

    [Fact]
    public async Task Run_RealResolver_UnknownAccount_Returns1NamingTheAccount()
    {
        using var stdout = new StringWriter();
        using var stderr = new StringWriter();

        int code = await SetupCommand.RunAsync(
            ["grant-logon-right", "--account", "NoSuchDomain\\NoSuchUser-ga"], stdout, stderr);

        code.ShouldBe(1);
        stderr.ToString().ShouldContain("NoSuchDomain\\NoSuchUser-ga");
    }

    [Fact]
    public async Task Run_PrepareDataRoot_UsesTheGivenPath()
    {
        var recorder = new Recorder();

        (int code, _, _) = await RunAsync(
            recorder, "prepare-data-root", "--account", "someone", "--path", "C:\\Data\\GA");

        code.ShouldBe(0);
        recorder.Calls.ShouldBe(["resolve:someone", "prepare:C:\\Data\\GA:" + FakeSid.Value]);
    }

    [Fact]
    public async Task Run_PrepareDataRoot_DefaultsToTheServiceDataRoot()
    {
        var recorder = new Recorder();

        (int code, _, _) = await RunAsync(recorder, "prepare-data-root", "--account", "someone");

        code.ShouldBe(0);
        recorder.Calls[1].ShouldBe(
            "prepare:" + GapActivator.Infrastructure.GapActivatorPaths.Default.DataRoot + ":" + FakeSid.Value);
    }

    [Fact]
    public async Task Run_GrantAndEventSourceVerbs_Dispatch()
    {
        var recorder = new Recorder();

        (await RunAsync(recorder, "grant-logon-right", "--account", "someone")).Code.ShouldBe(0);
        (await RunAsync(recorder, "register-event-source")).Code.ShouldBe(0);
        (await RunAsync(recorder, "unregister-event-source")).Code.ShouldBe(0);

        (await RunAsync(recorder, "add-operator", "--account", "someone")).Code.ShouldBe(0);
        (await RunAsync(recorder, "remove-operators-group")).Code.ShouldBe(0);

        recorder.Calls.ShouldBe(
            ["resolve:someone", "grant:" + FakeSid.Value, "register", "unregister", "resolve:someone", "operator:" + FakeSid.Value, "remove-group"]);
    }

    [Fact]
    public async Task Run_ActionFailure_Returns1WithTheWin32Error()
    {
        var actions = new SetupActions(
            _ => FakeSid, (_, _) => { }, _ => { },
            () => throw new System.ComponentModel.Win32Exception(5), () => { }, _ => { }, () => { });
        using var stdout = new StringWriter();
        using var stderr = new StringWriter();

        int code = await SetupCommand.RunAsync(["register-event-source"], stdout, stderr, actions);

        code.ShouldBe(1);
        stderr.ToString().ShouldContain("5");
        stderr.ToString().TrimEnd().Split('\n').Length.ShouldBe(1);
    }

    [Fact]
    public void ResolveAccountName_DotPrefix_MeansThisMachine()
    {
        string user = Environment.UserName;
        SecurityIdentifier expected = SetupActions.ResolveAccountName(Environment.MachineName + "\\" + user);

        SetupActions.ResolveAccountName(".\\" + user).ShouldBe(expected);
    }

    [Fact]
    public void ResolveAccountName_UnknownDotAccount_StillThrows() =>
        Should.Throw<IdentityNotMappedException>(() => SetupActions.ResolveAccountName(".\\NoSuchUser-ga"));
}
