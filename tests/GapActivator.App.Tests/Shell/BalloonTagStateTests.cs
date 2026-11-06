using GapActivator.App.Shell;

namespace GapActivator.App.Tests.Shell;

public class BalloonTagStateTests
{
    [Fact]
    public void Clicked_SingleBalloon_ReportsItsTag()
    {
        var state = new BalloonTagState();
        state.Shown("run:a");

        state.Clicked().ShouldBe("run:a");
    }

    [Fact]
    public void Clicked_TwoOverlappingBalloons_ReportsNoTag()
    {
        var state = new BalloonTagState();
        state.Shown("run:a");
        state.Shown("run:b");

        state.Clicked().ShouldBeNull();
    }

    [Fact]
    public void Clicked_BalloonAfterThePreviousTimedOut_ReportsTheNewTag()
    {
        var state = new BalloonTagState();
        state.Shown("run:a");
        state.Ended();
        state.Shown("run:b");

        state.Clicked().ShouldBe("run:b");
    }

    [Fact]
    public void Clicked_AfterTheBalloonEnded_ReportsNoTag()
    {
        var state = new BalloonTagState();
        state.Shown("run:a");
        state.Ended();

        state.Clicked().ShouldBeNull();
    }

    [Fact]
    public void Clicked_Twice_ReportsNoTagTheSecondTime()
    {
        var state = new BalloonTagState();
        state.Shown("run:a");
        state.Clicked().ShouldBe("run:a");

        state.Clicked().ShouldBeNull();
    }

    [Fact]
    public void Clicked_AfterAClickTheNextBalloonStartsFresh()
    {
        var state = new BalloonTagState();
        state.Shown("run:a");
        state.Clicked();
        state.Shown("run:b");

        state.Clicked().ShouldBe("run:b");
    }

    [Fact]
    public void Clicked_AfterAnOverlapEnded_ReportsNoTagUntilANewBalloon()
    {
        var state = new BalloonTagState();
        state.Shown("run:a");
        state.Shown("run:b");
        state.Ended();

        state.Clicked().ShouldBeNull();
    }
}
