namespace RCynQ.Core.Api;

/// <summary>
/// The body of <c>POST /commits/send</c>.
/// </summary>
/// <param name="UpTo">
/// The last commit to send: every unsent commit from the oldest up to this one is sent. <see langword="null"/> sends every
/// unsent commit.
/// </param>
public sealed record SendCommitsRequest(string? UpTo);
