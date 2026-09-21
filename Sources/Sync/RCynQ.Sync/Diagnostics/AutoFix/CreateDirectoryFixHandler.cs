using RCynQ.Core.Abstractions.Storage;
using RCynQ.Core.Config;
using RCynQ.Core.Diagnostics;
using RCynQ.Localization;

namespace RCynQ.Sync.Diagnostics.AutoFix;

/// <summary>
/// Creates a missing directory below the working tree or the application data root.
/// </summary>
public sealed class CreateDirectoryFixHandler : IAutoFixHandler
{
    /// <summary>The local node.</summary>
    private readonly NodeConfig _node;

    /// <summary>The application data factory.</summary>
    private readonly IAppDataFactory _appData;

    /// <summary>
    /// Initializes a new instance of the <see cref="CreateDirectoryFixHandler"/> class.
    /// </summary>
    /// <param name="node">The local node.</param>
    /// <param name="appData">The application data factory.</param>
    public CreateDirectoryFixHandler(NodeConfig node, IAppDataFactory appData)
    {
        _node = node;
        _appData = appData;
    }

    /// <inheritdoc />
    public DiagnosticFixKind Kind => DiagnosticFixKind.CreateDirectory;

    /// <inheritdoc />
    public Task<AutoFixResult> ExecuteAsync(DiagnosticFix fix, CancellationToken cancellationToken)
    {
        if (!fix.Parameters.TryGetValue("path", out var path) || string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException(Texts.Get("Fix.NoPath"));
        }

        var full = Path.GetFullPath(path);
        var allowedRoots = new[] { _node.EffectiveWorkdir, _appData.Create().Root }.Where(root => root is not null).Select(root => Path.GetFullPath(root!));
        var paths = new Git.Paths.PathUtility();
        if (!allowedRoots.Any(root => paths.IsWithinRoot(root, full)))
        {
            return Task.FromResult(new AutoFixResult(false, $"'{full}' is outside the working tree and application data; create it manually."));
        }

        Directory.CreateDirectory(full);
        return Task.FromResult(new AutoFixResult(true, Texts.Format("Fix.DirectoryCreated", full)));
    }
}
