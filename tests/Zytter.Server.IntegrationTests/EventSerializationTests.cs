using System.Text.Json;
using Zytter.Core.Battle;
using Zytter.Core.Buffs;

namespace Zytter.Server.IntegrationTests;

/// <summary>事件流 JSON 多态序列化往返验证（SignalR 传输的基础）。</summary>
public class EventSerializationTests
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    [Fact]
    public void AllEventTypesRoundTripThroughPolymorphicJson()
    {
        BattleEvent[] samples =
        {
            new BattleStartedEvent(1, 35),
            new PhaseChangedEvent(2, BattlePhase.Action, 30),
            new RoundStartedEvent(3, 1, 35),
            new DamageDealtEvent(4, BattleSide.A, 1, 5, DamageType.Physical),
            new SkillCastEvent(5, BattleSide.B, 6, "魔王怒", 4),
            new BattleEndedEvent(6, BattleSide.A, VictoryReason.Annihilation),
        };

        foreach (var sample in samples)
        {
            string json = JsonSerializer.Serialize<BattleEvent>(sample, Options);
            var roundTripped = JsonSerializer.Deserialize<BattleEvent>(json, Options);

            Assert.NotNull(roundTripped);
            Assert.Equal(sample.GetType(), roundTripped!.GetType());
            Assert.Equal(sample, roundTripped);
        }
    }

    [Fact]
    public void ArrayPayloadRoundTrips()
    {
        BattleEvent[] batch =
        {
            new RoundStartedEvent(1, 1, 35),
            new DamageDealtEvent(2, BattleSide.B, 2, 7, DamageType.Magical),
        };

        string json = JsonSerializer.Serialize(batch, Options);
        var result = JsonSerializer.Deserialize<BattleEvent[]>(json, Options);

        Assert.Equal(2, result!.Length);
        Assert.IsType<RoundStartedEvent>(result[0]);
        Assert.IsType<DamageDealtEvent>(result[1]);
    }

    [Fact]
    public void BuffSyncEventRoundTrips()
    {
        var sample = new BuffSyncEvent(7, BattleSide.A, 1, new Dictionary<string, int>
        {
            ["anthem_atk"] = 2,
            ["princess_order"] = -1,
            ["wind_barrier_stun"] = 1,
        });

        string json = JsonSerializer.Serialize<BattleEvent>(sample, Options);
        var roundTripped = JsonSerializer.Deserialize<BattleEvent>(json, Options);

        Assert.IsType<BuffSyncEvent>(roundTripped);
        var synced = (BuffSyncEvent)roundTripped!;
        Assert.Equal(7, synced.Seq);
        Assert.Equal(BattleSide.A, synced.Side);
        Assert.Equal(1, synced.CombatantId);
        Assert.Equal(3, synced.Rounds.Count);
        Assert.Equal(2, synced.Rounds["anthem_atk"]);
        Assert.Equal(-1, synced.Rounds["princess_order"]);
        Assert.Equal(1, synced.Rounds["wind_barrier_stun"]);
    }
}
