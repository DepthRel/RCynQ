namespace RCynQ.UI.Enums;

/// <summary>
/// The visual status of a link or chain node.
/// </summary>
public enum LinkStatusKind
{
    /// <summary>The state is not known yet or the link is idle ("ожидание").</summary>
    Waiting = 1,

    /// <summary>The peer is reachable ("онлайн").</summary>
    Online = 2,

    /// <summary>The peer is reachable through a fallback ("деградация").</summary>
    Degraded = 3,

    /// <summary>The peer is not reachable ("нет связи").</summary>
    Offline = 4
}
