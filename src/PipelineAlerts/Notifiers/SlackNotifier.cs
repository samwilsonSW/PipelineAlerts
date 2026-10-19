using System.Net.Http.Json;
using PipelineAlerts.Interfaces;
using PipelineAlerts.Models;

namespace PipelineAlerts.Notifiers;

/// <summary>
/// Sends one trading signal to Slack through an incoming webhook: POST {"text": ...} as JSON.
/// The webhook URL is a secret, so it never appears in any exception message; errors say "Slack webhook".
/// No retries. The caller owns the HttpClient.
/// </summary>
public sealed class SlackNotifier : INotifier
{
    // Slack's error bodies are short ("invalid_payload", "no_service"); anything longer is cut off.
    private const int MaxBodyLength = 200;

    private readonly HttpClient _http;
    private readonly Uri _webhookUrl;

    public SlackNotifier(HttpClient http, Uri webhookUrl)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(webhookUrl);
        _http = http;
        _webhookUrl = webhookUrl;
    }

    public async Task SendAsync(SignalResult signalResult, CancellationToken ct)
    {
        var payload = new Dictionary<string, string> { ["text"] = SlackMessage.Build(signalResult) };

        HttpResponseMessage response;
        try
        {
            response = await _http.PostAsync(_webhookUrl, JsonContent.Create(payload), ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw; // the caller cancelled; not an error to report
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            // The inner exception is not attached: its message could include the webhook URL.
            var why = ex is TaskCanceledException ? "timed out" : $"network error ({((HttpRequestException)ex).HttpRequestError})";
            throw new HttpRequestException($"Slack webhook request failed: {why}.");
        }

        using (response)
        {
            if (response.IsSuccessStatusCode)
                return;

            var body = await response.Content.ReadAsStringAsync(ct);
            throw new HttpRequestException(
                $"Slack webhook returned {(int)response.StatusCode} ({response.StatusCode}): {Clean(body)}",
                inner: null,
                statusCode: response.StatusCode);
        }
    }

    // Trims the response body and makes sure it can't echo the webhook URL back into the message.
    private string Clean(string body)
    {
        body = body.Replace(_webhookUrl.AbsoluteUri, "[Slack webhook]", StringComparison.OrdinalIgnoreCase);
        if (_webhookUrl.AbsolutePath.Length > 1)
            body = body.Replace(_webhookUrl.AbsolutePath, "[Slack webhook]", StringComparison.OrdinalIgnoreCase);
        body = body.Trim();
        if (body.Length > MaxBodyLength)
            body = body[..MaxBodyLength] + "...";
        return body.Length == 0 ? "(empty response)" : body;
    }
}
