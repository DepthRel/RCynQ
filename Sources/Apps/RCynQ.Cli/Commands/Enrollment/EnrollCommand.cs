using System.Buffers.Text;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using CliFx;
using CliFx.Attributes;
using CliFx.Exceptions;
using CliFx.Infrastructure;
using RCynQ.Core.Api;
using RCynQ.Core.Config;
using RCynQ.Core.Exceptions;
using RCynQ.Localization;
using RCynQ.Sync.Configuration;

namespace RCynQ.Cli.Commands.Enrollment;

/// <summary>
/// <c>enroll</c>: the forced command of a one-time enrollment key. The SSH server runs it with the enrollment the joining
/// node sent as its command (<c>SSH_ORIGINAL_COMMAND</c>); it checks the text, hands it to the running service of this
/// node on loopback and prints the answer. It opens no database and writes nothing itself.
/// </summary>
[Command("enroll", Description = "Forced command of a one-time enrollment key: passes the enrollment the SSH server received to the running service.")]
public sealed class EnrollCommand : ICommand
{
    /// <summary>The variable the SSH server puts the command of the client in.</summary>
    public const string ORIGINAL_COMMAND = "SSH_ORIGINAL_COMMAND";

    /// <summary>The longest enrollment accepted, in characters.</summary>
    private const int MAX_LENGTH = 64 * 1024;

    /// <summary>How long the service may take to answer.</summary>
    private static readonly TimeSpan _timeout = TimeSpan.FromMinutes(1);

    /// <summary>The JSON options of the exchange.</summary>
    private static readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);

    /// <summary>The localizer of the messages.</summary>
    private readonly ILocalizer _localizer;

    /// <summary>Reads an environment variable.</summary>
    private readonly Func<string, string?> _environment;

    /// <summary>The handler of the request to the service.</summary>
    private readonly HttpMessageHandler _handler;

    /// <summary>
    /// Initializes a new instance of the <see cref="EnrollCommand"/> class.
    /// </summary>
    /// <param name="localizer">The localizer of the messages.</param>
    public EnrollCommand(ILocalizer localizer)
        : this(localizer, Environment.GetEnvironmentVariable, new SocketsHttpHandler { UseProxy = false })
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="EnrollCommand"/> class with its environment, for tests.
    /// </summary>
    /// <param name="localizer">The localizer of the messages.</param>
    /// <param name="environment">Reads an environment variable.</param>
    /// <param name="handler">The handler of the request to the service.</param>
    public EnrollCommand(ILocalizer localizer, Func<string, string?> environment, HttpMessageHandler handler)
    {
        _localizer = localizer ?? throw new ArgumentNullException(nameof(localizer));
        _environment = environment ?? throw new ArgumentNullException(nameof(environment));
        _handler = handler ?? throw new ArgumentNullException(nameof(handler));
    }

    /// <summary>Gets or sets the configuration file of this node.</summary>
    [CommandOption("config", 'c', Description = "Path of syncbridge.json (default: RCYNQ_CONFIG or ./syncbridge.json).")]
    public string? ConfigPath { get; set; }

    /// <inheritdoc />
    public async ValueTask ExecuteAsync(IConsole console)
    {
        var cancellationToken = console.RegisterCancellationHandler();

        var request = Read(_environment(ORIGINAL_COMMAND));

        // The process serves this one enrollment, so its messages may take the language of the joining node.
        if (_localizer.Languages.Any(language => language.Code == request.Language))
        {
            _localizer.SetLanguage(request.Language!);
        }

        SyncBridgeOptions options;
        try
        {
            options = await ConfigurationFileEditor.LoadAsync(ConfigurationFileEditor.ResolvePath(ConfigPath), cancellationToken);
        }
        catch (SyncBridgeException ex)
        {
            throw new CommandException(_localizer.Describe(ex), 2);
        }

        using var client = new HttpClient(_handler, disposeHandler: false) { Timeout = _timeout };
        using var message = new HttpRequestMessage(HttpMethod.Post, $"http://127.0.0.1:{options.ListenPort}{ApiRoutes.PEER_ENROLL}")
        {
            Content = JsonContent.Create(request, options: _json)
        };
        if (request.Language is { } code)
        {
            message.Headers.AcceptLanguage.ParseAdd(code);
        }

        try
        {
            using var response = await client.SendAsync(message, cancellationToken);
            await console.Output.WriteAsync(await response.Content.ReadAsStringAsync(cancellationToken));
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new CommandException(_localizer.Format("Enroll.ServiceDown", _localizer.Describe(ex)), 1);
        }
    }

    /// <summary>
    /// Reads the enrollment out of the command the SSH server received: Base64url of the JSON.
    /// </summary>
    /// <param name="text">The command.</param>
    /// <returns>The enrollment.</returns>
    /// <exception cref="CommandException">There is no valid enrollment in it.</exception>
    private EnrollmentRequest Read(string? text)
    {
        var command = text?.Trim() ?? string.Empty;
        if (command.Length == 0 || command.Length > MAX_LENGTH || !command.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_'))
        {
            throw new CommandException(_localizer.Get("Enroll.NoRequest"), 2);
        }

        try
        {
            var request = JsonSerializer.Deserialize<EnrollmentRequest>(Encoding.UTF8.GetString(Base64Url.DecodeFromChars(command)), _json);
            return request?.Card is not null ? request : throw new CommandException(_localizer.Get("Enroll.NoRequest"), 2);
        }
        catch (Exception ex) when (ex is FormatException or JsonException)
        {
            throw new CommandException(_localizer.Get("Enroll.NoRequest"), 2);
        }
    }
}
