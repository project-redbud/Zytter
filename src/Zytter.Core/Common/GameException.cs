namespace Zytter.Core.Common;

/// <summary>
/// 规则违规异常：客户端发送的意图被权威引擎拒绝时抛出。
/// 旧版中这类校验散落成无数 if/else 直接静默忽略；新版统一抛此异常并附原因码。
/// </summary>
public class RuleViolationException : Exception
{
    /// <summary>规则原因码（稳定字符串，供客户端本地化展示）。</summary>
    public string Reason { get; }

    public RuleViolationException(string reason, string? message = null)
        : base(message ?? reason)
    {
        Reason = reason;
    }
}

/// <summary>游戏数据缺陷异常：数据文件错误、未注册的技能效果等编程/配置错误。</summary>
public class GameDataException : Exception
{
    public GameDataException(string message) : base(message)
    {
    }
}
