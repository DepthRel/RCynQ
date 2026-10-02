using RCynQ.Core.Abstractions.Storage;
using RCynQ.Core.Config;
using RCynQ.Core.Diagnostics;
using RCynQ.Core.Enums;
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

    /// <summary>The localizer of the messages.</summary>
    private readonly ILocalizer _localizer;

    /// <summary>Tells which repository the node works with now.</summary>
    private readonly IActiveRepository _repository;

    /// <summary>
    /// Initializes a new instance of the <see cref="CreateDirectoryFixHandler"/> class.
    /// </summary>
    /// <param name="localizer">The localizer of the messages.</param>
    /// <param name="node">The local node.</param>
    /// <param name="appData">The application data factory.</param>
    /// <param name="repository">Tells which repository the node works with now; <see langword="null"/> for the one the node started with.</param>
    public CreateDirectoryFixHandler(ILocalizer localizer, NodeConfig node, IAppDataFactory appData, IActiveRepository? repository = null)
    {
        _repository = repository ?? FixedRepository.FromNode(node);
        _localizer = localizer ?? throw new ArgumentNullException(nameof(localizer));
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
            throw new ArgumentException(_localizer.Get("Fix.NoPath"));
        }

        var full = Path.GetFullPath(path);
        var allowedRoots = new[] { _repository.Current?.Path, _appData.Create().Root }.Where(root => root is not null).Select(root => Path.GetFullPath(root!));
        var paths = new Git.Paths.PathUtility(_localizer);
        if (!allowedRoots.Any(root => paths.IsWithinRoot(root, full)))
        {
            return Task.FromResult(new AutoFixResult(false, $"'{full}' is outside the working tree and application data; create it manually."));
        }

        Directory.CreateDirectory(full);
        return Task.FromResult(new AutoFixResult(true, _localizer.Format("Fix.DirectoryCreated", full)));
    }
}
