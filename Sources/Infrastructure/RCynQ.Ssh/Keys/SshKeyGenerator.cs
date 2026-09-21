using RCynQ.Core.Abstractions.Processes;
using RCynQ.Core.Config;
using RCynQ.Core.Exceptions;
using RCynQ.Core.Validation;
using RCynQ.Localization;

namespace RCynQ.Ssh.Keys;

/// <summary>
/// Generates ed25519 SSH key pairs with <c>ssh-keygen</c>.
/// </summary>
/// <remarks>
/// Existing keys are never overwritten. The private key stays on the node; only the public key is exported.
/// </remarks>
public sealed class SshKeyGenerator
{
    /// <summary>
    /// The process runner.
    /// </summary>
    private readonly IProcessRunner _processRunner;

    /// <summary>
    /// The SSH options.
    /// </summary>
    private readonly SshClientOptions _options;

    /// <summary>
    /// Initializes a new instance of the <see cref="SshKeyGenerator"/> class.
    /// </summary>
    /// <param name="processRunner">The process runner.</param>
    /// <param name="options">The SSH options.</param>
    public SshKeyGenerator(IProcessRunner processRunner, SshClientOptions options)
    {
        _processRunner = processRunner;
        _options = options;
    }

    /// <summary>
    /// Generates an unencrypted ed25519 key pair.
    /// </summary>
    /// <param name="privateKeyPath">The private key path; the public key is written to <c>&lt;path&gt;.pub</c>.</param>
    /// <param name="comment">The key comment.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The generated key pair.</returns>
    /// <exception cref="ArgumentException">The comment contains unsafe characters.</exception>
    /// <exception cref="SyncBridgeException">A key already exists at the path.</exception>
    /// <exception cref="SshExecutionException">ssh-keygen failed or produced an unexpected result.</exception>
    public async Task<SshKeyPair> GenerateAsync(string privateKeyPath, string comment, CancellationToken cancellationToken = default)
    {
        var path = Path.GetFullPath(Guard.NotBlank(privateKeyPath, nameof(privateKeyPath)));
        var keyComment = Guard.NotBlank(comment, nameof(comment));
        if (keyComment.Any(c => char.IsControl(c) || c is '"' or '\''))
        {
            throw new ArgumentException("Key comment must not contain quotes or control characters.", nameof(comment));
        }

        var publicPath = path + ".pub";
        if (File.Exists(path) || File.Exists(publicPath))
        {
            throw new SyncBridgeException(Texts.Format("Keys.AlreadyExists", path));
        }

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var generate = await _processRunner.RunAsync(
            new ProcessSpec(_options.SshKeygenExecutable, new[] { "-t", "ed25519", "-N", "", "-C", keyComment, "-f", path, "-q" }, null, null, TimeSpan.FromMinutes(1)),
            cancellationToken);
        if (generate.ExitCode != 0 || !File.Exists(publicPath))
        {
            throw new SshExecutionException(Texts.Format("Keys.KeygenFailed", generate.StandardError.Trim()), generate.ExitCode);
        }

        var publicKey = SshPublicKey.Parse(await File.ReadAllTextAsync(publicPath, cancellationToken));
        var list = await _processRunner.RunAsync(
            new ProcessSpec(_options.SshKeygenExecutable, new[] { "-l", "-E", "sha256", "-f", publicPath }, null, null, TimeSpan.FromMinutes(1)),
            cancellationToken);
        var reported = list.ExitCode == 0
            ? list.StandardOutput.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault(token => token.StartsWith("SHA256:", StringComparison.Ordinal))
            : null;

        if (reported is not null && SshPublicKey.NormalizeFingerprint(reported) != publicKey.Fingerprint)
        {
            throw new SshExecutionException(Texts.Format("Keys.FingerprintMismatch", reported, publicKey.Fingerprint));
        }

        return new SshKeyPair(path, publicPath, publicKey, publicKey.Fingerprint);
    }
}
