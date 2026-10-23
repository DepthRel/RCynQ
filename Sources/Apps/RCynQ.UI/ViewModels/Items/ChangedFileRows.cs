namespace RCynQ.UI.ViewModels.Items;

/// <summary>
/// Lays out the changed files as rows: a flat list of paths or a tree of folders, either of them narrowed by a filter.
/// </summary>
public static class ChangedFileRows
{
    /// <summary>The indentation of one level of the tree in device-independent pixels.</summary>
    public const double INDENT_STEP = 18;

    /// <summary>The width of the place of the open/close arrow of a folder, kept empty in a file row to align the names.</summary>
    public const double ARROW_WIDTH = 20;

    /// <summary>The separator of the parts of a path, as the service reports paths.</summary>
    private const char SEPARATOR = '/';

    /// <summary>
    /// The order of the names in a folder: alphabetical ignoring case, names that differ only by case kept apart.
    /// </summary>
    private static readonly IComparer<string> _nameOrder = Comparer<string>.Create((left, right) =>
    {
        var order = StringComparer.OrdinalIgnoreCase.Compare(left, right);
        return order != 0 ? order : string.CompareOrdinal(left, right);
    });

    /// <summary>
    /// Tells whether a path passes the filter: it contains the text of the filter, ignoring case. An empty filter passes
    /// everything; a backslash in the filter stands for the separator of the parts of the path.
    /// </summary>
    /// <param name="path">The relative path of a file.</param>
    /// <param name="filter">The text of the filter, or <see langword="null"/>.</param>
    /// <returns><see langword="true"/> when the file is shown.</returns>
    public static bool Matches(string path, string? filter)
    {
        ArgumentNullException.ThrowIfNull(path);

        var text = Normalize(filter);
        return text.Length == 0 || path.Contains(text, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Brings the text of a filter to the form it is compared in.
    /// </summary>
    /// <param name="filter">The text of the filter, or <see langword="null"/>.</param>
    /// <returns>The trimmed text with the separators of the service; empty when there is no filter.</returns>
    public static string Normalize(string? filter) => (filter ?? string.Empty).Trim().Replace('\\', SEPARATOR);

    /// <summary>
    /// Lays the files out as a flat list of full paths, in the order given.
    /// </summary>
    /// <param name="files">The shown files.</param>
    /// <returns>The rows.</returns>
    public static IReadOnlyList<ChangedRowViewModel> Flat(IEnumerable<ChangedFileItemViewModel> files)
    {
        ArgumentNullException.ThrowIfNull(files);

        var rows = new List<ChangedRowViewModel>();
        foreach (var file in files)
        {
            file.Label = file.Path;
            file.IndentWidth = 0;
            rows.Add(file);
        }

        return rows;
    }

    /// <summary>
    /// Lays the files out as a tree: the folders first, then the files, each by name; a folder that holds only one other
    /// folder is joined with it into one row (<c>src/app</c>), and the contents of a closed folder are left out.
    /// </summary>
    /// <param name="files">The shown files.</param>
    /// <param name="owner">The list that owns the folder rows and knows which of them are open.</param>
    /// <returns>The rows, top to bottom.</returns>
    public static IReadOnlyList<ChangedRowViewModel> Tree(IEnumerable<ChangedFileItemViewModel> files, IChangedFolderOwner owner)
    {
        ArgumentNullException.ThrowIfNull(files);
        ArgumentNullException.ThrowIfNull(owner);

        var root = new Folder();
        foreach (var file in files)
        {
            var parts = file.Path.Split(SEPARATOR, StringSplitOptions.RemoveEmptyEntries);
            var folder = root;
            foreach (var part in parts.SkipLast(1))
            {
                if (!folder.Folders.TryGetValue(part, out var child))
                {
                    child = new Folder();
                    folder.Folders.Add(part, child);
                }

                folder = child;
            }

            folder.Files.Add((parts.Length > 0 ? parts[^1] : file.Path, file));
        }

        var rows = new List<ChangedRowViewModel>();
        AddRows(root, string.Empty, 0, owner, rows);
        return rows;
    }

    /// <summary>
    /// Adds the rows of the contents of a folder: its folders, with the contents of the open ones, then its files.
    /// </summary>
    /// <param name="folder">The folder.</param>
    /// <param name="path">The path of the folder; empty for the root of the working tree.</param>
    /// <param name="depth">The depth of the contents in the tree.</param>
    /// <param name="owner">The list that owns the folder rows.</param>
    /// <param name="rows">The rows filled so far.</param>
    private static void AddRows(Folder folder, string path, int depth, IChangedFolderOwner owner, List<ChangedRowViewModel> rows)
    {
        foreach (var (name, child) in folder.Folders)
        {
            var label = name;
            var childPath = Combine(path, name);
            var contents = child;

            // A folder that holds only one other folder says nothing on its own row.
            while (contents.Files.Count == 0 && contents.Folders.Count == 1)
            {
                var (innerName, inner) = contents.Folders.First();
                label = $"{label}{SEPARATOR}{innerName}";
                childPath = Combine(childPath, innerName);
                contents = inner;
            }

            var row = new ChangedFolderItemViewModel(owner, childPath, contents.AllFiles().ToArray())
            {
                Label = label,
                IndentWidth = depth * INDENT_STEP
            };
            rows.Add(row);

            if (row.IsExpanded)
            {
                AddRows(contents, childPath, depth + 1, owner, rows);
            }
        }

        foreach (var (name, file) in folder.Files.OrderBy(entry => entry.Name, _nameOrder))
        {
            file.Label = name;
            file.IndentWidth = (depth * INDENT_STEP) + ARROW_WIDTH;
            rows.Add(file);
        }
    }

    /// <summary>
    /// Joins the path of a folder and the name of an entry in it.
    /// </summary>
    /// <param name="path">The path of the folder; empty for the root.</param>
    /// <param name="name">The name of the entry.</param>
    /// <returns>The path of the entry.</returns>
    private static string Combine(string path, string name) => path.Length == 0 ? name : $"{path}{SEPARATOR}{name}";

    /// <summary>
    /// A folder of the tree being built.
    /// </summary>
    private sealed class Folder
    {
        /// <summary>Gets the folders inside, by name.</summary>
        public SortedDictionary<string, Folder> Folders { get; } = new(_nameOrder);

        /// <summary>Gets the files directly inside, with their names.</summary>
        public List<(string Name, ChangedFileItemViewModel File)> Files { get; } = new();

        /// <summary>
        /// Lists the files anywhere below the folder.
        /// </summary>
        /// <returns>The files.</returns>
        public IEnumerable<ChangedFileItemViewModel> AllFiles()
            => Folders.Values.SelectMany(folder => folder.AllFiles()).Concat(Files.Select(entry => entry.File));
    }
}
