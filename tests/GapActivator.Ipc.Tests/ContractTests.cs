using System.Reflection;
using GapActivator.Ipc.Contracts;

namespace GapActivator.Ipc.Tests;

public class ContractTests
{
    private static readonly (string Name, Type Request, Type Response)[] Table =
    [
        ("Hello", typeof(HelloRequest), typeof(HelloResponse)),
        ("RegisterProcess", typeof(RegisterProcessRequest), typeof(ProcessSavedDto)),
        ("UpdateProcess", typeof(UpdateProcessRequest), typeof(ProcessSavedDto)),
        ("DeleteProcess", typeof(DeleteProcessRequest), typeof(EmptyResponse)),
        ("EnableProcess", typeof(EnableProcessRequest), typeof(EmptyResponse)),
        ("DisableProcess", typeof(DisableProcessRequest), typeof(EmptyResponse)),
        ("UnblockProcess", typeof(UnblockProcessRequest), typeof(EmptyResponse)),
        ("RunNow", typeof(RunNowRequest), typeof(RunNowResponse)),
        ("CancelRun", typeof(CancelRunRequest), typeof(EmptyResponse)),
        ("RestartRun", typeof(RestartRunRequest), typeof(EmptyResponse)),
        ("PauseInstallation", typeof(PauseInstallationRequest), typeof(EmptyResponse)),
        ("ResumeInstallation", typeof(ResumeInstallationRequest), typeof(EmptyResponse)),
        ("UpdateSettings", typeof(UpdateSettingsRequest), typeof(EmptyResponse)),
        ("SendTestEmail", typeof(SendTestEmailRequest), typeof(EmptyResponse)),
        ("RestartService", typeof(RestartServiceRequest), typeof(EmptyResponse)),
        ("GetDashboard", typeof(GetDashboardRequest), typeof(DashboardDto)),
        ("ListProcesses", typeof(ListProcessesRequest), typeof(ListProcessesResponse)),
        ("GetProcess", typeof(GetProcessRequest), typeof(ProcessDetailsDto)),
        ("ListRuns", typeof(ListRunsRequest), typeof(RunRowPageDto)),
        ("GetRun", typeof(GetRunRequest), typeof(RunDetailsDto)),
        ("GetSettings", typeof(GetSettingsRequest), typeof(SettingsViewDto)),
        ("PreviewSchedule", typeof(PreviewScheduleRequest), typeof(PreviewScheduleResponse)),
    ];

    [Fact]
    public void Messages_Names_AreExactlyTheProtocolList() =>
        IpcMessages.Names.ShouldBe(Table.Select(row => row.Name));

    [Fact]
    public void Messages_EachDeclaredOnce_WithItsRequestAndResponseTypes()
    {
        PropertyInfo[] properties =
        [
            .. typeof(IpcMessages).GetProperties(BindingFlags.Public | BindingFlags.Static)
                .Where(p => p.PropertyType.IsGenericType
                    && p.PropertyType.GetGenericTypeDefinition() == typeof(IpcMessage<,>)),
        ];

        properties.Select(p => p.Name).ShouldBe(Table.Select(row => row.Name), ignoreOrder: true);

        foreach (PropertyInfo property in properties)
        {
            (string name, Type request, Type response) = Table.Single(row => row.Name == property.Name);
            property.PropertyType.GetGenericArguments().ShouldBe([request, response]);
            object message = property.GetValue(null).ShouldNotBeNull();
            message.GetType().GetProperty("Name")!.GetValue(message).ShouldBe(name);
        }
    }

    [Fact]
    public void Events_AreNotificationAndStateChanged()
    {
        IpcEvents.Notification.ShouldBe("Notification");
        IpcEvents.StateChanged.ShouldBe("StateChanged");
    }

    [Fact]
    public void ErrorCodes_AreTheProtocolList()
    {
        string[] codes =
        [
            .. typeof(IpcErrorCodes).GetFields(BindingFlags.Public | BindingFlags.Static)
                .Where(f => f.IsLiteral)
                .Select(f => (string)f.GetRawConstantValue()!),
        ];

        codes.ShouldBe(
            [
                "busy",
                "conflict",
                "disconnected",
                "email-test-failed",
                "has-active-run",
                "installation-paused",
                "internal",
                "invalid",
                "name-taken",
                "not-found",
                "process-blocked",
                "process-disabled",
                "protocol-mismatch",
                "run-already-active",
                "run-not-active",
                "schedule-in-past",
                "secret-value-required",
                "server-not-verified",
                "service-stopping",
                "unauthorized",
                "unknown-message",
                "use-disable",
            ],
            ignoreOrder: true);
    }

    [Fact]
    public void Protocol_Constants_AreTheAgreedOnes()
    {
        IpcProtocol.Version.ShouldBe(1);
        IpcProtocol.DefaultPipeName.ShouldBe("GapActivator");
        IpcProtocol.ServiceName.ShouldBe("GapActivator");
        IpcProtocol.MaxPayloadBytes.ShouldBe(16 * 1024 * 1024);
    }

    // Pre-flight C19: one pure predicate shared by the client and the server.
    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(2, false)]
    public void Protocol_IsSupported_AcceptsOnlyTheCurrentVersion(int version, bool expected) =>
        IpcProtocol.IsSupported(version).ShouldBe(expected);

    [Fact]
    public void Secrets_ToString_MaskTheValues()
    {
        string variable = new EnvironmentVariableInputDto("K", "s3cr3t-value", true).ToString();
        string password = new SecretChangeDto(SecretChangeKind.Set, "p4ssw0rd-x").ToString();

        variable.ShouldNotContain("s3cr3t-value");
        variable.ShouldContain("***");
        password.ShouldNotContain("p4ssw0rd-x");
        password.ShouldContain("***");
    }

    [Fact]
    public void Secrets_ToString_KeepsAPlainValueVisible() =>
        new EnvironmentVariableInputDto("K", "plain-value", false).ToString().ShouldContain("plain-value");
}
