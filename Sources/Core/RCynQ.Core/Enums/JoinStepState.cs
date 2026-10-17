using System.Text.Json.Serialization;

namespace RCynQ.Core.Enums;

/// <summary>
/// Where a stage of joining stands while it runs.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<JoinStepState>))]
public enum JoinStepState
{
    /// <summary>
    /// The stage has started.
    /// </summary>
    Running = 0,

    /// <summary>
    /// The stage ended well.
    /// </summary>
    Succeeded = 1,

    /// <summary>
    /// The stage failed; joining stops here.
    /// </summary>
    Failed = 2
}
