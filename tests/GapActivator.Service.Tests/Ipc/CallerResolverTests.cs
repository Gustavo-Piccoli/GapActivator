using System.IO.Pipes;
using System.Security.Principal;
using GapActivator.Application.Messaging;
using GapActivator.Ipc;
using GapActivator.Service.Ipc;
using Microsoft.Extensions.Configuration;

namespace GapActivator.Service.Tests.Ipc;

public sealed class CallerResolverTests
{
    [Theory]
    [InlineData(false, false, false, false)]
    [InlineData(false, false, true, false)]
    [InlineData(false, true, false, false)]
    [InlineData(false, true, true, true)]
    [InlineData(true, false, false, true)]
    [InlineData(true, false, true, true)]
    [InlineData(true, true, false, true)]
    [InlineData(true, true, true, true)]
    public void CanChange_TruthTable(bool isAdministrator, bool isOperator, bool enabled, bool expected) =>
        CallerRules.CanChange(isAdministrator, isOperator, enabled).ShouldBe(expected);

    [Theory]
    [InlineData(OperatorsGroupSetting.Auto, "S-1-5-80-1-2-3-4-5", true)]
    [InlineData(OperatorsGroupSetting.Auto, "S-1-5-18", false)]
    [InlineData(OperatorsGroupSetting.Auto, "S-1-5-21-1-2-3-1001", false)]
    [InlineData(OperatorsGroupSetting.Enabled, "S-1-5-18", true)]
    [InlineData(OperatorsGroupSetting.Disabled, "S-1-5-80-1-2-3-4-5", false)]
    public void IsOperatorsGroupEnabled_AutoFollowsTheVirtualAccountSid(
        OperatorsGroupSetting setting, string sid, bool expected) =>
        CallerRules.IsOperatorsGroupEnabled(setting, new SecurityIdentifier(sid)).ShouldBe(expected);

    [Theory]
    [InlineData(null, OperatorsGroupSetting.Auto)]
    [InlineData("enabled", OperatorsGroupSetting.Enabled)]
    [InlineData("DISABLED", OperatorsGroupSetting.Disabled)]
    public void AuthorizationOptions_FromConfiguration_ParsesIgnoringCaseAndDefaultsToAuto(
        string? value, OperatorsGroupSetting expected)
    {
        var data = new Dictionary<string, string?>();
        if (value is not null)
        {
            data[AuthorizationOptions.OperatorsGroupKey] = value;
        }

        IConfiguration configuration = new ConfigurationBuilder().AddInMemoryCollection(data).Build();

        AuthorizationOptions.FromConfiguration(configuration).OperatorsGroup.ShouldBe(expected);
    }

    [Theory]
    [InlineData("Sometimes")]
    [InlineData("1")]
    [InlineData("Auto, Enabled")]
    [InlineData("")]
    public void AuthorizationOptions_FromConfiguration_InvalidValue_ThrowsNamingTheKey(string value)
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { [AuthorizationOptions.OperatorsGroupKey] = value })
            .Build();

        InvalidOperationException ex =
            Should.Throw<InvalidOperationException>(() => AuthorizationOptions.FromConfiguration(configuration));

        ex.Message.ShouldContain(AuthorizationOptions.OperatorsGroupKey);
        ex.Message.ShouldContain("Auto, Enabled, Disabled");
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Resolve_RealCurrentUserOverARealPipe_ReturnsTheUserAndTheirAdminStatus()
    {
        await using ConnectedPipe pipe = await ConnectedPipe.OpenAsync(TokenImpersonationLevel.Identification);
        await pipe.ClientSendsOneFrameAsync();

        Caller caller = new WindowsCallerResolver(new AuthorizationOptions(OperatorsGroupSetting.Disabled)).Resolve(pipe.Server);

        using WindowsIdentity me = WindowsIdentity.GetCurrent();
        caller.Name.ShouldBe(me.Name);
        caller.IsSystem.ShouldBeFalse();
        caller.CanChange.ShouldBe(new WindowsPrincipal(me)
            .IsInRole(new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null)));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Resolve_OperatorsGroupMissing_NobodyQualifiesThroughIt()
    {
        bool groupExists = true;
        try
        {
            _ = new NTAccount($"{Environment.MachineName}\\{AuthorizationOptions.OperatorsGroupName}")
                .Translate(typeof(SecurityIdentifier));
        }
        catch (IdentityNotMappedException)
        {
            groupExists = false;
        }

        Assert.SkipWhen(groupExists, "This machine has a 'GapActivator Operators' group.");

        await using ConnectedPipe pipe = await ConnectedPipe.OpenAsync(TokenImpersonationLevel.Identification);
        await pipe.ClientSendsOneFrameAsync();

        Caller caller = new WindowsCallerResolver(new AuthorizationOptions(OperatorsGroupSetting.Enabled)).Resolve(pipe.Server);

        using WindowsIdentity me = WindowsIdentity.GetCurrent();
        caller.CanChange.ShouldBe(new WindowsPrincipal(me)
            .IsInRole(new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null)));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Resolve_AfterReturning_LeavesNoImpersonationInTheAsyncFlow()
    {
        await using ConnectedPipe pipe = await ConnectedPipe.OpenAsync(TokenImpersonationLevel.Identification);
        await pipe.ClientSendsOneFrameAsync();

        _ = new WindowsCallerResolver(new AuthorizationOptions(OperatorsGroupSetting.Disabled)).Resolve(pipe.Server);

        WindowsIdentity.GetCurrent(ifImpersonating: true).ShouldBeNull();
        await Task.Yield();
        WindowsIdentity.GetCurrent(ifImpersonating: true).ShouldBeNull();
        await Task.Run(() =>
        {
            WindowsIdentity.GetCurrent(ifImpersonating: true).ShouldBeNull();
            using var another = new NamedPipeServerStream(
                $"GapActivator.Tests.{Guid.NewGuid():N}", PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        }, TestContext.Current.CancellationToken);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Resolve_AnonymousClient_ThrowsUnauthorizedAccess()
    {
        await using ConnectedPipe pipe = await ConnectedPipe.OpenAsync(TokenImpersonationLevel.Anonymous);
        await pipe.ClientSendsOneFrameAsync();

        var resolver = new WindowsCallerResolver(new AuthorizationOptions(OperatorsGroupSetting.Disabled));

        Should.Throw<UnauthorizedAccessException>(() => resolver.Resolve(pipe.Server));
    }

    private sealed class ConnectedPipe : IAsyncDisposable
    {
        private ConnectedPipe(NamedPipeServerStream server, NamedPipeClientStream client)
        {
            Server = server;
            Client = client;
        }

        public NamedPipeServerStream Server { get; }

        public NamedPipeClientStream Client { get; }

        public static async Task<ConnectedPipe> OpenAsync(TokenImpersonationLevel level)
        {
            CancellationToken ct = TestContext.Current.CancellationToken;
            string name = $"GapActivator.Tests.{Guid.NewGuid():N}";
            var server = new NamedPipeServerStream(
                name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
            var client = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous, level);
            Task accept = server.WaitForConnectionAsync(ct);
            await client.ConnectAsync(ct).WaitAsync(TimeSpan.FromSeconds(10), ct);
            await accept.WaitAsync(TimeSpan.FromSeconds(10), ct);
            return new ConnectedPipe(server, client);
        }

        public async Task ClientSendsOneFrameAsync()
        {
            CancellationToken ct = TestContext.Current.CancellationToken;
            TimeSpan limit = TimeSpan.FromSeconds(10);

            // Unbuffered pipe: the write completes only once the server reads, so start the read first.
            ValueTask<byte[]?> read = IpcFrame.ReadAsync(Server, ct);
            await IpcFrame.WriteAsync(Client, "{}"u8.ToArray(), ct).AsTask().WaitAsync(limit, ct);
            (await read.AsTask().WaitAsync(limit, ct)).ShouldNotBeNull();
        }

        public async ValueTask DisposeAsync()
        {
            await Client.DisposeAsync();
            await Server.DisposeAsync();
        }
    }
}
