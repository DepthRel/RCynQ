using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text;

namespace RCynQ.UI.Services.Shell;

/// <summary>
/// Keeps one copy of the application per node: a second start finds the first one, asks it to show its window and ends.
/// </summary>
/// <remarks>
/// <para>
/// The key is made from the configuration file, so nodes with different configuration files (a developer running a few
/// of them on one computer) do not stand in each other's way. The first copy owns a named mutex of the key and listens on
/// a named pipe of the same key.
/// </para>
/// <para>
/// The pipe is open to the current user only, and nothing is read from it: a connection itself is the request to show
/// the window, so a stranger can neither reach it nor make it do anything else.
/// </para>
/// </remarks>
public sealed class SingleInstanceGuard : IDisposable
{
    /// <summary>
    /// The start of the key; the hash of the configuration file follows.
    /// </summary>
    private const string KEY_PREFIX = "rcynq-ui-";

    /// <summary>
    /// The number of hexadecimal characters of the hash in the key: short, because on Unix the pipe is a socket in the
    /// temporary folder and its path is limited.
    /// </summary>
    private const int HASH_LENGTH = 16;

    /// <summary>
    /// The mutex this copy owns.
    /// </summary>
    private readonly Mutex _mutex;

    /// <summary>
    /// Stops the listening loop.
    /// </summary>
    private readonly CancellationTokenSource _stop = new();

    /// <summary>
    /// The listening loop, or <see langword="null"/> before <see cref="Listen"/>.
    /// </summary>
    private Task? _listening;

    /// <summary>
    /// Initializes a new instance of the <see cref="SingleInstanceGuard"/> class for a mutex this copy owns.
    /// </summary>
    /// <param name="key">The key.</param>
    /// <param name="mutex">The owned mutex.</param>
    private SingleInstanceGuard(string key, Mutex mutex)
    {
        Key = key;
        _mutex = mutex;
    }

    /// <summary>
    /// Gets the key the guard owns.
    /// </summary>
    public string Key { get; }

    /// <summary>
    /// Makes the key of a configuration file.
    /// </summary>
    /// <param name="configPath">The configuration file.</param>
    /// <param name="ignoreCase">
    /// Whether the file system ignores the case of names; <see langword="null"/> assumes so everywhere but on Linux.
    /// </param>
    /// <returns>The key: the same for every spelling of the same file.</returns>
    public static string KeyFor(string configPath, bool? ignoreCase = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(configPath);

        var path = Path.TrimEndingDirectorySeparator(Path.GetFullPath(configPath));
        if (ignoreCase ?? !OperatingSystem.IsLinux())
        {
            path = path.ToUpperInvariant();
        }

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(path));
        return KEY_PREFIX + Convert.ToHexStringLower(hash)[..HASH_LENGTH];
    }

    /// <summary>
    /// Becomes the copy of the application for a key, unless another copy already is.
    /// </summary>
    /// <param name="key">The key from <see cref="KeyFor"/>.</param>
    /// <returns>The guard, or <see langword="null"/> when another copy owns the key.</returns>
    public static SingleInstanceGuard? TryAcquire(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        var mutex = new Mutex(initiallyOwned: true, key, out var createdNew);
        if (!createdNew)
        {
            mutex.Dispose();
            return null;
        }

        return new SingleInstanceGuard(key, mutex);
    }

    /// <summary>
    /// Asks the copy that owns a key to show its window.
    /// </summary>
    /// <param name="key">The key.</param>
    /// <param name="timeout">How long to wait for the other copy to answer.</param>
    /// <returns><see langword="true"/> when the other copy took the request.</returns>
    public static bool SignalExisting(string key, TimeSpan timeout)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        try
        {
            using var client = new NamedPipeClientStream(".", key, PipeDirection.Out, PipeOptions.CurrentUserOnly);
            client.Connect(timeout);
            return true;
        }
        catch (Exception ex) when (ex is TimeoutException or IOException or UnauthorizedAccessException)
        {
            // The other copy is still starting or is closing: the request is lost, and this copy ends anyway.
            return false;
        }
    }

    /// <summary>
    /// Starts taking the requests of later copies.
    /// </summary>
    /// <param name="onActivation">Called on a background thread for every request.</param>
    /// <exception cref="InvalidOperationException">The guard already listens.</exception>
    public void Listen(Action onActivation)
    {
        ArgumentNullException.ThrowIfNull(onActivation);
        if (_listening is not null)
        {
            throw new InvalidOperationException("The guard already listens.");
        }

        _listening = Task.Run(() => ListenAsync(onActivation, _stop.Token));
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _stop.Cancel();
        try
        {
            _listening?.Wait(TimeSpan.FromSeconds(2));
        }
        catch (AggregateException)
        {
            // The loop ends with the cancellation; what it threw on the way out does not matter any more.
        }

        try
        {
            _mutex.ReleaseMutex();
        }
        catch (ApplicationException)
        {
            // Released from another thread than the one that took it: closing the handle below frees it all the same.
        }

        _mutex.Dispose();
        _stop.Dispose();
    }

    /// <summary>
    /// Waits for the connections of later copies, one after another, until the guard is disposed.
    /// </summary>
    /// <param name="onActivation">Called for every connection.</param>
    /// <param name="cancellationToken">Ends the loop.</param>
    /// <returns>A task that completes when the loop ends.</returns>
    private async Task ListenAsync(Action onActivation, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await using var server = new NamedPipeServerStream(
                    Key,
                    PipeDirection.In,
                    1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await server.WaitForConnectionAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (IOException)
            {
                // A client that went away half way: wait for the next one.
                continue;
            }

            onActivation();
        }
    }
}
