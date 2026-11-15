namespace RCynQ.Core.Api;

/// <summary>
/// The body of <c>POST /changes/compare</c>: prepares the files of a diff in an external tool.
/// </summary>
/// <param name="Path">The path of the file relative to the working tree.</param>
/// <param name="ForChoice">
/// <see langword="true"/> to choose a part of the changes (the tool gets an editable copy of the version that goes), or
/// <see langword="false"/> only to look (the tool gets the working file itself).
/// </param>
public sealed record CompareFilesRequest(string Path, bool ForChoice);
