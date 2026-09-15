using System.Text;
using System.Text.RegularExpressions;
using RCynQ.Core.Config;
using RCynQ.Core.Enums;

namespace RCynQ.Ssh.Keys.AuthorizedKeys;

/// <summary>
/// The result of reading an <c>authorized_keys</c> file.
/// </summary>
/// <param name="Entries">The valid entries.</param>
/// <param name="InvalidLines">The 1-based numbers of lines that could not be parsed.</param>
public sealed record AuthorizedKeysFile(IReadOnlyList<AuthorizedKeyEntry> Entries, IReadOnlyList<int> InvalidLines);
