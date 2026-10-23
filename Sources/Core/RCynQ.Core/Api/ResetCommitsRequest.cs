using RCynQ.Core.Enums;

namespace RCynQ.Core.Api;

/// <summary>
/// The body of <c>POST /commits/reset</c>.
/// </summary>
/// <param name="Target">The commit that becomes <c>HEAD</c>: an unsent commit, or the commit the Origin has to drop them all.</param>
/// <param name="Mode">What happens to the changes of the dropped commits.</param>
/// <param name="ExpectedHead">The commit checked out when the user chose; the rollback is refused when it changed since.</param>
public sealed record ResetCommitsRequest(string Target, CommitResetMode Mode, string ExpectedHead);
