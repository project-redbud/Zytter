using Zytter.Core.Common;
using Zytter.Core.Drafting;

namespace Zytter.Core.Tests;

public class DraftSessionTests
{
    private static DraftSession Create() => new(Guid.NewGuid(), Enumerable.Range(1, 12).ToList());

    [Fact]
    public void FullDraftFlowProducesValidRosters()
    {
        var session = Create();
        var events = new List<DraftEvent>();
        session.EventEmitted += events.Add;
        session.Start();
        session.Tick(5); // 准备

        // P1B1=1 P2B1=2 P1B2=3 P2B2=4
        session.Ban("A", 1);
        session.Ban("B", 2);
        session.Ban("A", 3);
        session.Ban("B", 4);
        session.Tick(2); // 过渡

        // P1 选 5/6/7，P2 选 8/9/10
        session.Pick("A", 5);
        session.Pick("B", 8);
        session.Pick("A", 6);
        session.Pick("B", 9);
        session.Pick("A", 7);
        session.Pick("B", 10);

        // 排序
        session.SubmitOrder("A", new[] { 7, 5, 6 });
        session.SubmitOrder("B", new[] { 10, 8, 9 });

        Assert.True(session.IsCompleted);
        Assert.NotNull(session.Result);
        Assert.Equal(new[] { 7, 5, 6 }, session.Result!.Value.RosterA);
        Assert.Equal(new[] { 10, 8, 9 }, session.Result.Value.RosterB);
        Assert.Contains(events, e => e is DraftStartedEvent);
        Assert.Contains(events, e => e is DraftCompletedEvent);
        Assert.Equal(4, events.OfType<HeroBannedEvent>().Count());
        Assert.Equal(6, events.OfType<HeroPickedEvent>().Count());
    }

    [Fact]
    public void BanAndPickRemoveFromPool()
    {
        var session = Create();
        session.Start();
        session.Tick(5);
        session.Ban("A", 1);
        session.Ban("B", 2);
        session.Ban("A", 3);
        session.Ban("B", 4);
        session.Tick(2);
        session.Pick("A", 5);

        Assert.True(session.IsRemoved(1));
        Assert.True(session.IsRemoved(5));
        Assert.False(session.AvailableHeroes.Contains(1));
        Assert.False(session.AvailableHeroes.Contains(5));
    }

    [Fact]
    public void DuplicatePickRejected()
    {
        var session = Create();
        session.Start();
        session.Tick(5);
        session.Ban("A", 1);
        session.Ban("B", 2);
        session.Ban("A", 3);
        session.Ban("B", 4);
        session.Tick(2);

        session.Pick("A", 5);
        session.Pick("B", 8);
        session.Pick("A", 6);
        session.Pick("B", 9);
        session.Pick("A", 7);
        session.Pick("B", 10);
        session.Tick(22); // 排序阶段超时自动提交

        Assert.NotNull(session.Result);
    }

    [Fact]
    public void WrongSideTurnRejected()
    {
        var session = Create();
        session.Start();
        session.Tick(5);
        Assert.Throws<RuleViolationException>(() => session.Ban("B", 1)); // 轮到 A
    }

    [Fact]
    public void BanTimeoutCountsAsGiveUp()
    {
        var session = Create();
        session.Start();
        session.Tick(5);
        session.Tick(10); // A 超时 → 弃权
        Assert.Contains(0, session.BansA);
        Assert.Equal(1, session.StepIndex); // 轮到 B BAN
    }

    [Fact]
    public void OrderTimeoutAutoOrdersByPickSequence()
    {
        var session = Create();
        session.Start();
        session.Tick(5);
        session.Ban("A", 1);
        session.Ban("B", 2);
        session.Ban("A", 3);
        session.Ban("B", 4);
        session.Tick(2);
        session.Pick("A", 5);
        session.Pick("B", 8);
        session.Pick("A", 6);
        session.Pick("B", 9);
        session.Pick("A", 7);
        session.Pick("B", 10);
        session.Tick(22); // 双方超时 → 按选用顺序自动排序

        Assert.NotNull(session.Result);
        Assert.Equal(new[] { 5, 6, 7 }, session.Result!.Value.RosterA);
        Assert.Equal(new[] { 8, 9, 10 }, session.Result.Value.RosterB);
    }

    [Fact]
    public void DraftCompletedEventEmittedExactlyOnce()
    {
        var session = Create();
        var events = new List<DraftEvent>();
        session.EventEmitted += events.Add;
        session.Start();
        session.Tick(5);
        session.Ban("A", 1);
        session.Ban("B", 2);
        session.Ban("A", 3);
        session.Ban("B", 4);
        session.Tick(2);
        session.Pick("A", 5);
        session.Pick("B", 8);
        session.Pick("A", 6);
        session.Pick("B", 9);
        session.Pick("A", 7);
        session.Pick("B", 10);

        // 双方手动提交顺序（最后一次提交触发完成），随后超时路径不得再次触发
        session.SubmitOrder("A", new[] { 5, 6, 7 });
        session.SubmitOrder("B", new[] { 8, 9, 10 });
        session.Tick(22);

        Assert.Equal(1, events.OfType<DraftCompletedEvent>().Count());
    }

    [Fact]
    public void ZeroPickSideVoidsMatch()
    {
        var session = Create();
        session.Start();
        session.Tick(5);
        session.Ban("A", 1);
        session.Ban("B", 2);
        session.Ban("A", 3);
        session.Ban("B", 4);
        session.Tick(2);
        session.Pick("A", 0); // A 弃权
        session.Pick("B", 8);
        session.Pick("A", 0);
        session.Pick("B", 9);
        session.Pick("A", 0);
        session.Pick("B", 10);
        session.Tick(22);

        Assert.True(session.IsCompleted);
        Assert.Null(session.Result); // 对局作废
    }
}
