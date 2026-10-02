using System.Text.Json.Serialization;

namespace RCynQ.Core.Enums;

/// <summary>
/// What is known of whether a route answers.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<RouteReachability>))]
public enum RouteReachability
{
    /// <summary>The route was not checked yet.</summary>
    Unknown = 0,

    /// <summary>The peer answered on the route at the last check.</summary>
    Reachable = 1,

    /// <summary>The peer did not answer on the route at the last check.</summary>
    Unreachable = 2
}
