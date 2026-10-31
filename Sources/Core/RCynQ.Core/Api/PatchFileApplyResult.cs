namespace RCynQ.Core.Api;

/// <summary>
/// How applying or removing a patch file ended.
/// </summary>
/// <param name="Succeeded">Whether the working tree changed; otherwise it stayed exactly as it was.</param>
/// <param name="Message">What happened, in the language of the user.</param>
/// <param name="Output">What Git printed.</param>
public sealed record PatchFileApplyResult(bool Succeeded, string Message, string Output);
