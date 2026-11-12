namespace RCynQ.Core.Api;

/// <summary>
/// The body of <c>PUT /config/retention</c>.
/// </summary>
/// <param name="Days">For how many days finished data is kept; <c>0</c> keeps it until it is cleaned by hand.</param>
public sealed record RetentionRequest(int Days);
