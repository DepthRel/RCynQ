using System.Net;
using System.Text;
using FluentAssertions;
using NSubstitute;
using RCynQ.UI.Enums;
using RCynQ.UI.Services.About;
using RCynQ.UI.Services.Configuration;
using RCynQ.UI.Services.Updates;

namespace RCynQ.UI.Tests.Updates;

/// <summary>
/// Tests of the check for a new version: <see cref="UpdateManifest"/>, <see cref="UpdateChecker"/>,
/// <see cref="HttpUpdateFeed"/> and <see cref="UpdatePreferences"/>. The list of releases comes over the network and is
/// read as untrusted.
/// </summary>
public sealed class UpdateCheckTests
{
    /// <summary>A list of releases as the project page publishes it.</summary>
    private const string LIST = """
        {
          "windows": { "version": "0.3.0", "url": "https://example.invalid/setup.exe" },
          "linux": { "version": "0.2.9", "url": "https://example.invalid/linux.tar.gz" },
          "macos": { "version": "0.3.1" }
        }
        """;

    /// <summary>The list of releases substitute.</summary>
    private readonly IUpdateFeed _feed = Substitute.For<IUpdateFeed>();

    /// <summary>
    /// Verifies that the version of each platform is read from the list.
    /// </summary>
    [Fact]
    public void Version_Of_Each_Platform_Is_Read()
    {
        UpdateManifest.TryReadVersion(LIST, "windows").Should().Be(new Version(0, 3, 0));
        UpdateManifest.TryReadVersion(LIST, "linux").Should().Be(new Version(0, 2, 9));
        UpdateManifest.TryReadVersion(LIST, "macos").Should().Be(new Version(0, 3, 1));
        UpdateManifest.TryReadVersion(LIST, "freebsd").Should().BeNull();
    }

    /// <summary>
    /// Verifies that anything but a list with a version of three numbers is refused.
    /// </summary>
    /// <param name="json">The content.</param>
    [Theory]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("{ \"windows\": \"0.3.0\" }")]
    [InlineData("{ \"windows\": { \"version\": 3 } }")]
    [InlineData("{ \"windows\": { \"version\": \"0.3\" } }")]
    [InlineData("{ \"windows\": { \"version\": \"0.3.0.1\" } }")]
    [InlineData("{ \"windows\": { \"version\": \"0.3.0-beta\" } }")]
    [InlineData("{ \"windows\": { \"version\": \"-1.0.0\" } }")]
    [InlineData("{ \"windows\": { \"version\": \"999999.0.0\" } }")]
    [InlineData("{ \"windows\": { \"version\": \" 0.3.0\" } }")]
    public void Anything_But_A_Version_Of_Three_Numbers_Is_Refused(string json)
        => UpdateManifest.TryReadVersion(json, "windows").Should().BeNull();

    /// <summary>
    /// Verifies that a newer version is found, and that the same or an older published version means the application is
    /// up to date.
    /// </summary>
    /// <param name="current">The version that runs.</param>
    /// <param name="expected">The expected outcome.</param>
    /// <returns>A task representing the test.</returns>
    [Theory]
    [InlineData("0.2.2", UpdateCheckStatus.Available)]
    [InlineData("0.2.10", UpdateCheckStatus.Available)]
    [InlineData("0.3.0", UpdateCheckStatus.UpToDate)]
    [InlineData("0.4.0", UpdateCheckStatus.UpToDate)]
    public async Task Running_Version_Is_Compared_With_The_Latest(string current, UpdateCheckStatus expected)
    {
        _feed.ReadAsync(Arg.Any<CancellationToken>()).Returns(LIST);

        var result = await Checker(current, "windows").CheckAsync();

        result.Status.Should().Be(expected);
        result.Latest.Should().Be(new Version(0, 3, 0));
        result.Error.Should().BeNull();
    }

    /// <summary>
    /// Verifies that an unreachable page, an answer that is not a list and a platform without packages are failures with
    /// a reason, never exceptions.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Failures_Are_Results_With_A_Reason()
    {
        _feed.ReadAsync(Arg.Any<CancellationToken>()).Returns<string>(_ => throw new HttpRequestException("no route to host"));
        var unreachable = await Checker("0.2.2", "windows").CheckAsync();
        unreachable.Status.Should().Be(UpdateCheckStatus.Failed);
        unreachable.Error.Should().Contain("no route to host");

        _feed.ReadAsync(Arg.Any<CancellationToken>()).Returns("<html>moved</html>");
        (await Checker("0.2.2", "windows").CheckAsync()).Error.Should().Be(TestTexts.Russian.Get("Update.BadList"));

        _feed.ReadAsync(Arg.Any<CancellationToken>()).Returns<string>(_ => throw new InvalidDataException("too large"));
        (await Checker("0.2.2", "windows").CheckAsync()).Error.Should().Be(TestTexts.Russian.Get("Update.BadList"));

        var elsewhere = await Checker("0.2.2", null).CheckAsync();
        elsewhere.Error.Should().Be(TestTexts.Russian.Get("Update.NoPackage"));
    }

    /// <summary>
    /// Verifies that the list is asked at the address of the project, by a plain request that names the program without
    /// its version.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Feed_Asks_The_Project_Page_Plainly()
    {
        using var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(LIST) });
        using var feed = new HttpUpdateFeed(handler);

        (await feed.ReadAsync(CancellationToken.None)).Should().Be(LIST);

        handler.Request!.RequestUri.Should().Be(new Uri(ProjectSite.UPDATES_URL));
        handler.Request.Method.Should().Be(HttpMethod.Get);
        handler.Request.Headers.UserAgent.ToString().Should().Be("RCynQ");
        handler.Request.Headers.Contains("Cookie").Should().BeFalse();
    }

    /// <summary>
    /// Verifies that an error answer fails, and that an answer larger than any list is refused, whether its size is told
    /// in advance or not.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Feed_Refuses_Errors_And_Large_Answers()
    {
        using (var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound)))
        using (var feed = new HttpUpdateFeed(handler))
        {
            await feed.Invoking(item => item.ReadAsync(CancellationToken.None)).Should().ThrowAsync<HttpRequestException>();
        }

        var large = new string('x', HttpUpdateFeed.MAX_BYTES + 1);
        using (var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(large) }))
        using (var feed = new HttpUpdateFeed(handler))
        {
            await feed.Invoking(item => item.ReadAsync(CancellationToken.None)).Should().ThrowAsync<InvalidDataException>();
        }

        using (var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new MemoryStream(Encoding.UTF8.GetBytes(large))) }))
        using (var feed = new HttpUpdateFeed(handler))
        {
            await feed.Invoking(item => item.ReadAsync(CancellationToken.None)).Should().ThrowAsync<InvalidDataException>();
        }
    }

    /// <summary>
    /// Verifies that the check is on until the user turns it off, and that the skipped version is stored as three numbers.
    /// </summary>
    [Fact]
    public void Preferences_Default_To_Checking_And_Store_The_Skipped_Version()
    {
        var settings = Substitute.For<IUiSettingsStore>();
        var preferences = new UpdatePreferences(settings);

        preferences.CheckEnabled.Should().BeTrue();
        preferences.SkippedVersion.Should().BeNull();

        settings.Get(UpdatePreferences.CHECK_SETTING).Returns("False");
        settings.Get(UpdatePreferences.SKIPPED_SETTING).Returns("0.3.0");
        preferences.CheckEnabled.Should().BeFalse();
        preferences.SkippedVersion.Should().Be(new Version(0, 3, 0));

        settings.Get(UpdatePreferences.SKIPPED_SETTING).Returns("garbage");
        preferences.SkippedVersion.Should().BeNull();

        preferences.Skip(new Version(0, 4, 1));
        preferences.SetCheckEnabled(true);
        settings.Received(1).Set(UpdatePreferences.SKIPPED_SETTING, "0.4.1");
        settings.Received(1).Set(UpdatePreferences.CHECK_SETTING, "True");
    }

    /// <summary>
    /// Creates the checker under test.
    /// </summary>
    /// <param name="current">The version that runs.</param>
    /// <param name="platform">The section of the list, or <see langword="null"/>.</param>
    /// <returns>The checker.</returns>
    private UpdateChecker Checker(string current, string? platform) => new(TestTexts.Russian, _feed, Version.Parse(current), platform);

    /// <summary>
    /// An HTTP handler that answers every request the same way and keeps the last request.
    /// </summary>
    private sealed class StubHandler : HttpMessageHandler
    {
        /// <summary>Makes the answer.</summary>
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _answer;

        /// <summary>
        /// Initializes a new instance of the <see cref="StubHandler"/> class.
        /// </summary>
        /// <param name="answer">Makes the answer.</param>
        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> answer)
        {
            _answer = answer;
        }

        /// <summary>Gets the last request.</summary>
        public HttpRequestMessage? Request { get; private set; }

        /// <inheritdoc />
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Request = request;
            return Task.FromResult(_answer(request));
        }
    }
}
