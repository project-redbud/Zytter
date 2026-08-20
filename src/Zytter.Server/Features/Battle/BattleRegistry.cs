using System.Collections.Concurrent;
using Zytter.Core.Battle;
using Zytter.Core.Common;
using Zytter.Core.Data;

namespace Zytter.Server.Features.Battle;

/// <summary>服务器上全部活动对局房间的注册表（内存态，对局结束即清理）。</summary>
public sealed class BattleRegistry
{
    private readonly ConcurrentDictionary<Guid, BattleRoom> _rooms = new();

    public BattleRoom? Get(Guid roomId) => _rooms.GetValueOrDefault(roomId);

    public BattleRoom Create(long accountA, long accountB, IReadOnlyList<int> rosterA, IReadOnlyList<int> rosterB) =>
        CreateWithId(Guid.NewGuid(), accountA, accountB, rosterA, rosterB);

    /// <summary>以既有房间 ID 创建对局（禁选完成后沿用匹配房间 ID）。</summary>
    public BattleRoom CreateWithId(Guid roomId, long accountA, long accountB, IReadOnlyList<int> rosterA, IReadOnlyList<int> rosterB)
    {
        var catalog = GameDataCatalog.LoadDefault();
        var config = new BattleConfig();
        var session = new BattleSession(
            catalog, config, new SystemRng(),
            rosterA.Select(catalog.GetHero).ToList(),
            rosterB.Select(catalog.GetHero).ToList());

        var room = new BattleRoom
        {
            Id = roomId,
            AccountA = accountA,
            AccountB = accountB,
            Session = session,
        };

        session.EventEmitted += room.Capture;
        session.Start();
        _rooms[room.Id] = room;
        return room;
    }

    public void Remove(Guid roomId) => _rooms.TryRemove(roomId, out _);

    public ICollection<BattleRoom> All => _rooms.Values;
}
