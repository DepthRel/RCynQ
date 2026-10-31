using RCynQ.MergeTools.Enums;

namespace RCynQ.MergeTools;

/// <summary>
/// The merge tools RCynQ knows: popular tools of well-known vendors, with their usual places and the way Git itself hands
/// them a three-way merge.
/// </summary>
/// <remarks>
/// A tool missing here is still usable as the tool the user describes (<see cref="MergeTool.CUSTOM_ID"/>); adding one
/// here is one more profile.
/// </remarks>
public static class MergeToolCatalog
{
    /// <summary>The JetBrains IDEs whose launcher has a <c>merge</c> command: launcher name and product name.</summary>
    private static readonly (string Launcher, string Name)[] _jetBrains =
    {
        ("idea", "IntelliJ IDEA"),
        ("rider", "JetBrains Rider"),
        ("pycharm", "PyCharm"),
        ("webstorm", "WebStorm"),
        ("clion", "CLion"),
        ("goland", "GoLand"),
        ("phpstorm", "PhpStorm"),
        ("rubymine", "RubyMine"),
        ("rustrover", "RustRover")
    };

    /// <summary>
    /// Gets every known tool, in the order they are offered.
    /// </summary>
    public static IReadOnlyList<MergeToolProfile> Profiles { get; } = Build();

    /// <summary>
    /// Finds a profile by its identifier.
    /// </summary>
    /// <param name="id">The identifier.</param>
    /// <returns>The profile, or <see langword="null"/>.</returns>
    public static MergeToolProfile? Find(string? id)
        => Profiles.FirstOrDefault(profile => string.Equals(profile.Id, id, StringComparison.Ordinal));

    /// <summary>
    /// Puts the catalog together.
    /// </summary>
    /// <returns>The profiles.</returns>
    private static IReadOnlyList<MergeToolProfile> Build()
    {
        var profiles = new List<MergeToolProfile>
        {
            // code --wait --merge <incoming> <local> <base> <result>: the merge editor shows Incoming and Current above the result.
            new(
                "vscode",
                "Visual Studio Code",
                MergeToolDiscovery.Paths,
                new[] { "code" },
                new[]
                {
                    Windows("%LOCALAPPDATA%/Programs/Microsoft VS Code/Code.exe"),
                    Windows("%ProgramFiles%/Microsoft VS Code/Code.exe"),
                    Linux("/usr/share/code/bin/code"),
                    Linux("/snap/bin/code"),
                    Mac("/Applications/Visual Studio Code.app/Contents/Resources/app/bin/code"),
                    Mac("~/Applications/Visual Studio Code.app/Contents/Resources/app/bin/code")
                },
                Template("--wait", "--merge", "{incoming}", "{local}", "{base}", "{result}"),
                TrustExitCode: false,
                new[] { "vscode", "code" }),

            // vsDiffMerge.exe <incoming> <local> <base> <result> /m, as Visual Studio registers itself for Git.
            new(
                "vsdiffmerge",
                "Visual Studio",
                MergeToolDiscovery.VisualStudio,
                Array.Empty<string>(),
                Array.Empty<MergeToolCandidate>(),
                Template("{incoming}", "{local}", "{base}", "{result}", "/m"),
                TrustExitCode: true,
                new[] { "vsdiffmerge", "vs", "visualstudio" }),

            // kdiff3 --L1 <base title> --L2 <local title> --L3 <incoming title> -o <result> <base> <local> <incoming>.
            new(
                "kdiff3",
                "KDiff3",
                MergeToolDiscovery.Paths,
                new[] { "kdiff3" },
                new[]
                {
                    Windows("%ProgramFiles%/KDiff3/kdiff3.exe"),
                    Windows("%ProgramFiles%/KDiff3/bin/kdiff3.exe"),
                    Windows("%ProgramFiles(x86)%/KDiff3/kdiff3.exe"),
                    Windows("%ProgramFiles%/GitExtensions/KDiff3/kdiff3.exe"),
                    Windows("%ProgramFiles(x86)%/GitExtensions/KDiff3/kdiff3.exe"),
                    Mac("/Applications/kdiff3.app/Contents/MacOS/kdiff3")
                },
                Template("--L1", "{baseLabel}", "--L2", "{localLabel}", "--L3", "{incomingLabel}", "-o", "{result}", "{base}", "{local}", "{incoming}"),
                TrustExitCode: true,
                new[] { "kdiff3" }),

            // meld --auto-merge -L … -o <result> <local> <base> <incoming>: the middle pane is written to the result.
            new(
                "meld",
                "Meld",
                MergeToolDiscovery.Paths,
                new[] { "meld" },
                new[]
                {
                    Windows("%ProgramFiles%/Meld/Meld.exe"),
                    Windows("%ProgramFiles(x86)%/Meld/Meld.exe"),
                    Windows("%LOCALAPPDATA%/Programs/Meld/Meld.exe"),
                    Mac("/Applications/Meld.app/Contents/MacOS/Meld")
                },
                Template("--auto-merge", "-L", "{localLabel}", "-L", "{baseLabel}", "-L", "{incomingLabel}", "--output", "{result}", "{local}", "{base}", "{incoming}"),
                TrustExitCode: false,
                new[] { "meld" }),

            // WinMergeU /e /u /fm /wl /wr /dl … /dm … /dr … /o <result> <local> <base> <incoming>: three panes, the middle one is saved.
            new(
                "winmerge",
                "WinMerge",
                MergeToolDiscovery.Paths,
                Array.Empty<string>(),
                new[]
                {
                    Windows("%ProgramFiles%/WinMerge/WinMergeU.exe"),
                    Windows("%ProgramFiles(x86)%/WinMerge/WinMergeU.exe"),
                    Windows("%LOCALAPPDATA%/Programs/WinMerge/WinMergeU.exe")
                },
                Template("/e", "/u", "/fm", "/wl", "/wr", "/dl", "{localLabel}", "/dm", "{baseLabel}", "/dr", "{incomingLabel}", "/o", "{result}", "{local}", "{base}", "{incoming}"),
                TrustExitCode: false,
                new[] { "winmerge" }),

            // bcomp <local> <incoming> <base> -mergeoutput=<result>; a three-way merge needs Beyond Compare Pro.
            new(
                "bc",
                "Beyond Compare",
                MergeToolDiscovery.Paths,
                new[] { "bcomp", "bcompare" },
                new[]
                {
                    Windows("%ProgramFiles%/Beyond Compare 5/BComp.exe"),
                    Windows("%ProgramFiles%/Beyond Compare 4/BComp.exe"),
                    Windows("%ProgramFiles(x86)%/Beyond Compare 4/BComp.exe"),
                    Windows("%LOCALAPPDATA%/Programs/Beyond Compare 5/BComp.exe"),
                    Windows("%LOCALAPPDATA%/Programs/Beyond Compare 4/BComp.exe"),
                    Mac("/Applications/Beyond Compare.app/Contents/MacOS/bcomp")
                },
                Template("{local}", "{incoming}", "{base}", "-mergeoutput={result}", "-lefttitle={localLabel}", "-righttitle={incomingLabel}", "-centertitle={baseLabel}", "-outputtitle={resultLabel}"),
                TrustExitCode: false,
                new[] { "bc", "bc3", "bc4", "bc5", "beyondcompare" }),

            // p4merge -nb … -nl … -nr … -nm … <base> <incoming> <local> <result> (base, theirs, yours, result).
            new(
                "p4merge",
                "P4Merge",
                MergeToolDiscovery.Paths,
                new[] { "p4merge" },
                new[]
                {
                    Windows("%ProgramFiles%/Perforce/p4merge.exe"),
                    Windows("%ProgramFiles(x86)%/Perforce/p4merge.exe"),
                    Mac("/Applications/p4merge.app/Contents/Resources/launchp4merge")
                },
                Template("-nb", "{baseLabel}", "-nl", "{incomingLabel}", "-nr", "{localLabel}", "-nm", "{resultLabel}", "{base}", "{incoming}", "{local}", "{result}"),
                TrustExitCode: false,
                new[] { "p4merge" }),

            // TortoiseGitMerge /base: /theirs: /mine: /merged: with the titles of the panes.
            new(
                "tortoisegitmerge",
                "TortoiseGitMerge",
                MergeToolDiscovery.Paths,
                Array.Empty<string>(),
                new[]
                {
                    Windows("%ProgramFiles%/TortoiseGit/bin/TortoiseGitMerge.exe")
                },
                Template("/base:{base}", "/theirs:{incoming}", "/mine:{local}", "/merged:{result}", "/basename:{baseLabel}", "/theirsname:{incomingLabel}", "/minename:{localLabel}", "/mergedname:{resultLabel}"),
                TrustExitCode: false,
                new[] { "tortoisemerge", "tortoisegitmerge" })
        };

        // <ide> merge <local> <incoming> <base> <result>: the merge dialog of the IDE, also when the IDE is open already.
        foreach (var (launcher, name) in _jetBrains)
        {
            profiles.Add(new MergeToolProfile(
                launcher,
                name,
                MergeToolDiscovery.Paths,
                new[] { launcher },
                new[]
                {
                    Windows($"%ProgramFiles%/JetBrains/*/bin/{launcher}64.exe"),
                    Windows($"%LOCALAPPDATA%/Programs/*/bin/{launcher}64.exe"),
                    Linux($"/opt/*/bin/{launcher}.sh"),
                    Linux($"~/.local/share/JetBrains/Toolbox/apps/*/bin/{launcher}.sh"),
                    Mac($"/Applications/*.app/Contents/MacOS/{launcher}"),
                    Mac($"~/Applications/*.app/Contents/MacOS/{launcher}")
                },
                Template("merge", "{local}", "{incoming}", "{base}", "{result}"),
                TrustExitCode: false,
                new[] { launcher }));
        }

        return profiles;
    }

    /// <summary>
    /// Creates a place on Windows.
    /// </summary>
    /// <param name="path">The path.</param>
    /// <returns>The place.</returns>
    private static MergeToolCandidate Windows(string path) => new(MergeToolPlatform.Windows, path);

    /// <summary>
    /// Creates a place on Linux.
    /// </summary>
    /// <param name="path">The path.</param>
    /// <returns>The place.</returns>
    private static MergeToolCandidate Linux(string path) => new(MergeToolPlatform.Linux, path);

    /// <summary>
    /// Creates a place on macOS.
    /// </summary>
    /// <param name="path">The path.</param>
    /// <returns>The place.</returns>
    private static MergeToolCandidate Mac(string path) => new(MergeToolPlatform.MacOS, path);

    /// <summary>
    /// Creates a template of arguments.
    /// </summary>
    /// <param name="arguments">The arguments.</param>
    /// <returns>The template.</returns>
    private static IReadOnlyList<string> Template(params string[] arguments) => arguments;
}
