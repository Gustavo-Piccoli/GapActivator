using System.IO.Pipes;
using System.Security.Principal;
using GapActivator.Application.Messaging;
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

    [Fact]
    public void AuthorizationOptions_FromConfiguration_InvalidValue_ThrowsNamingTheKey()
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { [AuthorizationOptions.OperatorsGroupKey] = "Sometimes" })
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
        Caller caller = await ResolveOverRealPipeAsync(OperatorsGroupSetting.Disabled);

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

        Caller caller = await ResolveOverRealPipeAsync(OperatorsGroupSetting.Enabled);

        using WindowsIdentity me = WindowsIdentity.GetCurrent();
        caller.CanChange.ShouldBe(new WindowsPrincipal(me)
            .IsInRole(new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null)));
    }

    private static async Task<Caller> ResolveOverRealPipeAsync(OperatorsGroupSetting setting)
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        string name = $"GapActivator.Tests.{Guid.NewGuid():N}";
        await using var server = new NamedPipeServerStream(
            name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        await using var client = new NamedPipeClientStream(
            ".", name, PipeDirection.InOut, PipeOptions.Asynchronous, TokenImpersonationLevel.Identification);

        TimeSpan limit = TimeSpan.FromSeconds(10);
        Task accept = server.WaitForConnectionAsync(ct);
        await client.ConnectAsync(ct).WaitAsync(limit, ct);
        await accept.WaitAsync(limit, ct);

        // Unbuffered pipe: the write completes only once the server reads, so start the read first.
        byte[] buffer = new byte[1];
        ValueTask<int> read = server.ReadAsync(buffer, ct);
        await client.WriteAsync(new byte[] { 1 }, ct).AsTask().WaitAsync(limit, ct);
        (await read.AsTask().WaitAsync(limit, ct)).ShouldBe(1);

        return new WindowsCallerResolver(new AuthorizationOptions(setting)).Resolve(server);
    }
}
