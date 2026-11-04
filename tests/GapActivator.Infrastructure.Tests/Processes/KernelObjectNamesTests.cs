using GapActivator.Infrastructure.Processes;

namespace GapActivator.Infrastructure.Tests.Processes;

public sealed class KernelObjectNamesTests
{
    [Fact]
    public void CancelEvent_Pid_FollowsTheChildContract() =>
        KernelObjectNames.CancelEvent(1234).ShouldBe(@"Local\GapActivator.Cancel.1234");

    [Fact]
    public void Job_PidAndStartTime_NamesBothWithUtcTicks()
    {
        var startTime = new DateTimeOffset(2030, 1, 1, 9, 0, 0, TimeSpan.FromHours(-3)).AddTicks(1234567);

        KernelObjectNames.Job(1234, startTime).ShouldBe(@"Local\GapActivator.Job.1234.640290960001234567");
        KernelObjectNames.Job(1234, startTime.ToUniversalTime()).ShouldBe(KernelObjectNames.Job(1234, startTime));
    }
}
