namespace Zytter.Core.Battle;

/// <summary>
/// 对局阶段（对应 docs/01-combat-system.md §9 UI 状态机）。
/// </summary>
public enum BattlePhase
{
    /// <summary>热身时间（20 秒）。</summary>
    Warmup,

    /// <summary>商店回合（6/13/20/27/32 及加时赛每 5 回合）。</summary>
    Shop,

    /// <summary>励兵秣马：回合开始准备（3 秒，行动受限时 10 秒复苏弹窗）。</summary>
    Prepare,

    /// <summary>运筹帷幄：双方选择行动（30 秒）。</summary>
    Action,

    /// <summary>兵戎相见：按行动力顺序结算（5 秒展示）。</summary>
    Resolving,

    /// <summary>对局结束（胜负已定，等待结算窗口关闭）。</summary>
    Ended,
}
