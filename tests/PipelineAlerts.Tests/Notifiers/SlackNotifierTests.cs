using System.Net;
using System.Text.Json;
using PipelineAlerts.Notifiers;

namespace PipelineAlerts.Tests.Notifiers;

/// <summary>
/// Tests for the Slack notifier, the piece that posts a trading signal to a Slack incoming webhook.
/// No network and no real webhook: a fake HTTP handler records each request and answers with a canned
/// response. The webhook URL is a secret, so the error tests also check it never leaks into a message.
/// </summary>
public class SlackNotifierTests
{
    // A made-up webhook on a reserved, never-resolvable domain.
    private static readonly Uri Webhook = new("https://hooks.example.invalid/T000/B000/XXX");

    /// <summary>
    /// Test helper: a fake HTTP handler. Records each request (method, URL, content type, body) and returns
    /// the chosen status and body, or throws the chosen exception to simulate a network failure.
    /// </summary>
    private sealed class FakeHandler(
        HttpStatusCode status = HttpStatusCode.OK, string responseBody = "ok", Exception? throws = null) : HttpMessageHandler
    {
        public List<(HttpMethod Method, Uri? Url, string? ContentType, string Body)> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(ct);
            Requests.Add((request.Method, request.RequestUri, request.Content?.Headers.ContentType?.MediaType, body));
            if (throws != null)
                throw throws;
            return new HttpResponseMessage(status) { Content = new StringContent(responseBody) };
        }
    }

    /// <summary>
    /// Checks the happy path: exactly one POST to the webhook URL, sent as JSON, whose "text" field is
    /// exactly the shared Slack message. That is the payload shape Slack incoming webhooks expect.
    /// </summary>
    [Fact]
    public async Task Posts_message_as_json_to_the_webhook_once()
    {
        var handler = new FakeHandler();
        var signal = TestSignals.Make();

        await new SlackNotifier(new HttpClient(handler), Webhook).SendAsync(signal, CancellationToken.None);

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal(Webhook, request.Url);
        Assert.Equal("application/json", request.ContentType);
        using var json = JsonDocument.Parse(request.Body);
        Assert.Equal(SlackMessage.Build(signal), json.RootElement.GetProperty("text").GetString());
    }

    /// <summary>
    /// Checks that when Slack rejects the message (400 "invalid_payload") the notifier throws, and the error
    /// gives the status and Slack's reason so it can be fixed. The error must not contain the webhook URL or
    /// any part of its path, because whoever has that URL can post to the channel.
    /// </summary>
    [Fact]
    public async Task Slack_error_throws_with_status_and_reason_but_no_webhook()
    {
        var handler = new FakeHandler(HttpStatusCode.BadRequest, "invalid_payload");
        var notifier = new SlackNotifier(new HttpClient(handler), Webhook);

        var ex = await Assert.ThrowsAsync<HttpRequestException>(
            () => notifier.SendAsync(TestSignals.Make(), CancellationToken.None));

        Assert.Contains("400", ex.Message);
        Assert.Contains("invalid_payload", ex.Message);
        Assert.Equal(HttpStatusCode.BadRequest, ex.StatusCode);
        AssertNoWebhook(ex);
    }

    /// <summary>
    /// Checks that a network failure (here one whose own message includes the webhook URL, as some
    /// low-level errors can) comes out as an HttpRequestException that says "Slack webhook" and does not
    /// leak the URL, either in its message or through an attached inner exception.
    /// </summary>
    [Fact]
    public async Task Network_failure_throws_without_leaking_the_webhook()
    {
        var handler = new FakeHandler(throws: new HttpRequestException($"Connection refused ({Webhook.AbsoluteUri})"));
        var notifier = new SlackNotifier(new HttpClient(handler), Webhook);

        var ex = await Assert.ThrowsAsync<HttpRequestException>(
            () => notifier.SendAsync(TestSignals.Make(), CancellationToken.None));

        Assert.Contains("Slack webhook", ex.Message);
        AssertNoWebhook(ex);
    }

    // The URL, its path and its secret segments must not appear anywhere in the exception, inner ones included.
    private static void AssertNoWebhook(Exception ex)
    {
        var all = ex.ToString();
        Assert.DoesNotContain(Webhook.Host, all);
        Assert.DoesNotContain(Webhook.AbsolutePath, all);
        foreach (var part in new[] { "T000", "B000", "XXX" })
            Assert.DoesNotContain(part, all);
    }
}
