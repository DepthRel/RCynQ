using System.Net;
using System.Text;
using FluentAssertions;
using RCynQ.UI.Services.Api;
using RCynQ.UI.Services.Connection;

namespace RCynQ.UI.Tests.Connection;

/// <summary>
/// Tests of how <see cref="ServiceApiClient"/> turns an error response into a <see cref="ServiceApiException"/>.
/// </summary>
public sealed class ServiceApiClientErrorTests
{
    /// <summary>
    /// Verifies that the message is taken from the error body of the service, and that any other body, also one that is
    /// JSON of another shape, is reported as it came instead of failing with another exception.
    /// </summary>
    /// <param name="body">The body of the error response.</param>
    /// <param name="expected">The expected message.</param>
    /// <returns>A task representing the test.</returns>
    [Theory]
    [InlineData("{\"error\":\"No sync base yet.\"}", "No sync base yet.")]
    [InlineData("{\"reason\":\"Replay\",\"error\":\"Seen before.\"}", "Seen before.")]
    [InlineData("\"just a JSON string\"", "\"just a JSON string\"")]
    [InlineData("[1,2,3]", "[1,2,3]")]
    [InlineData("{\"error\":{\"code\":7}}", "{\"error\":{\"code\":7}}")]
    [InlineData("plain text", "plain text")]
    [InlineData("", "Bad Request")]
    public async Task Error_Response_Becomes_A_Service_Exception(string body, string expected)
    {
        using var client = new ServiceApiClient(
            TestTexts.Russian,
            new ServiceConnectionSettings(new Uri("http://127.0.0.1:1/"), "token", ApiTokenProvider.ArgumentSource, "syncbridge.json"),
            handler: new FixedResponseHandler(HttpStatusCode.BadRequest, body));

        var call = () => client.GetStateAsync();

        var error = await call.Should().ThrowAsync<ServiceApiException>();
        error.Which.Message.Should().Be(expected);
        error.Which.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    /// <summary>
    /// Answers every request with the same status and body.
    /// </summary>
    private sealed class FixedResponseHandler : HttpMessageHandler
    {
        /// <summary>The status code.</summary>
        private readonly HttpStatusCode _status;

        /// <summary>The body.</summary>
        private readonly string _body;

        /// <summary>
        /// Initializes a new instance of the <see cref="FixedResponseHandler"/> class.
        /// </summary>
        /// <param name="status">The status code.</param>
        /// <param name="body">The body.</param>
        public FixedResponseHandler(HttpStatusCode status, string body)
        {
            _status = status;
            _body = body;
        }

        /// <inheritdoc />
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(_status) { Content = new StringContent(_body, Encoding.UTF8, "application/json") });
    }
}
