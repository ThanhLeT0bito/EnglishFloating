using System.Net;
using System.Text.Json;

namespace PteFloatingSentence.Windows.Tests;

/// <summary>Fake Gemini endpoint for tests: records requests and answers with scripted responses.</summary>
internal sealed class GeminiTestHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, int, Task<HttpResponseMessage>> _respond;

    public GeminiTestHandler(Func<HttpRequestMessage, int, Task<HttpResponseMessage>> respond) => _respond = respond;

    public List<string> RequestUris { get; } = [];
    public List<string> RequestBodies { get; } = [];
    public List<string?> ApiKeys { get; } = [];

    public static GeminiTestHandler Returning(params string[] modelTexts)
    {
        return new GeminiTestHandler((_, attempt) =>
            Task.FromResult(Ok(modelTexts[Math.Min(attempt, modelTexts.Length - 1)])));
    }

    public static HttpResponseMessage Ok(string modelText) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(JsonSerializer.Serialize(new
        {
            candidates = new[] { new { content = new { parts = new[] { new { text = modelText } } } } }
        }))
    };

    public static HttpResponseMessage Status(HttpStatusCode status) => new(status)
    {
        Content = new StringContent("{\"error\":{\"code\":" + (int)status + "}}")
    };

    /// <summary>The user text of the n-th request (the JSON data the service sent).</summary>
    public string UserText(int index)
    {
        using var document = JsonDocument.Parse(RequestBodies[index]);
        return document.RootElement.GetProperty("contents")[0].GetProperty("parts")[0].GetProperty("text").GetString()!;
    }

    public string SystemInstruction(int index)
    {
        using var document = JsonDocument.Parse(RequestBodies[index]);
        return document.RootElement.GetProperty("systemInstruction").GetProperty("parts")[0].GetProperty("text").GetString()!;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var attempt = RequestBodies.Count;
        RequestUris.Add(request.RequestUri!.ToString());
        RequestBodies.Add(request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken));
        ApiKeys.Add(request.Headers.TryGetValues("x-goog-api-key", out var keys) ? keys.Single() : null);
        return await _respond(request, attempt);
    }
}
