using System.Net;
using System.Text;
using System.Text.Json;
using PteFloatingSentence.Windows.Infrastructure;

namespace PteFloatingSentence.Windows.Tests;

[TestClass]
public sealed class GeminiJsonClientTests
{
    private static readonly object Schema = new { type = "OBJECT" };

    [TestMethod]
    public async Task GenerateJsonAsync_SendsKeySystemInstructionSchemaAndReturnsText()
    {
        var handler = GeminiTestHandler.Returning("{\"ok\":true}");
        using var client = new GeminiJsonClient(() => " test-key ", new HttpClient(handler), ["model-a"]);

        var text = await client.GenerateJsonAsync("system rules", "user data", Schema, temperature: 0.9);

        Assert.AreEqual("{\"ok\":true}", text);
        Assert.AreEqual("test-key", handler.ApiKeys[0]);
        StringAssert.Contains(handler.RequestUris[0], "/models/model-a:generateContent");
        Assert.AreEqual("system rules", handler.SystemInstruction(0));
        Assert.AreEqual("user data", handler.UserText(0));
        using var body = JsonDocument.Parse(handler.RequestBodies[0]);
        var config = body.RootElement.GetProperty("generationConfig");
        Assert.AreEqual("application/json", config.GetProperty("responseMimeType").GetString());
        Assert.AreEqual(0.9, config.GetProperty("temperature").GetDouble());
        Assert.AreEqual("OBJECT", config.GetProperty("responseSchema").GetProperty("type").GetString());
    }

    [TestMethod]
    public async Task GenerateJsonAsync_OmitsTemperatureWhenNotGiven()
    {
        var handler = GeminiTestHandler.Returning("{}");
        using var client = new GeminiJsonClient(() => "k", new HttpClient(handler));

        await client.GenerateJsonAsync("s", "u", Schema);

        using var body = JsonDocument.Parse(handler.RequestBodies[0]);
        Assert.IsFalse(body.RootElement.GetProperty("generationConfig").TryGetProperty("temperature", out _));
    }

    [TestMethod]
    public async Task GenerateJsonAsync_MissingKey_DoesNotSendRequest()
    {
        var handler = GeminiTestHandler.Returning("{}");
        using var client = new GeminiJsonClient(() => "  ", new HttpClient(handler));

        var error = await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => client.GenerateJsonAsync("s", "u", Schema));

        Assert.AreEqual(GeminiJsonClient.MissingKeyMessage, error.Message);
        Assert.AreEqual(0, handler.RequestBodies.Count);
        Assert.IsFalse(client.HasKey);
    }

    [TestMethod]
    public async Task GenerateJsonAsync_TransientStatus_FallsBackToNextModel()
    {
        var handler = new GeminiTestHandler((_, attempt) => Task.FromResult(attempt == 0
            ? GeminiTestHandler.Status(HttpStatusCode.ServiceUnavailable)
            : GeminiTestHandler.Ok("{\"second\":true}")));
        using var client = new GeminiJsonClient(() => "k", new HttpClient(handler), ["model-a", "model-b"]);

        var text = await client.GenerateJsonAsync("s", "u", Schema);

        Assert.AreEqual("{\"second\":true}", text);
        StringAssert.Contains(handler.RequestUris[1], "model-b");
    }

    [TestMethod]
    public async Task GenerateJsonAsync_AllModelsTransient_ReportsLastStatus()
    {
        var handler = new GeminiTestHandler((_, _) => Task.FromResult(GeminiTestHandler.Status(HttpStatusCode.TooManyRequests)));
        using var client = new GeminiJsonClient(() => "k", new HttpClient(handler), ["a", "b"]);

        var error = await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => client.GenerateJsonAsync("s", "u", Schema));

        StringAssert.Contains(error.Message, "429");
        Assert.AreEqual(2, handler.RequestBodies.Count);
    }

    [TestMethod]
    public async Task GenerateJsonAsync_NonTransientStatus_StopsImmediately()
    {
        var handler = new GeminiTestHandler((_, _) => Task.FromResult(GeminiTestHandler.Status(HttpStatusCode.BadRequest)));
        using var client = new GeminiJsonClient(() => "k", new HttpClient(handler), ["a", "b"]);

        var error = await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => client.GenerateJsonAsync("s", "u", Schema));

        StringAssert.Contains(error.Message, "400");
        Assert.AreEqual(1, handler.RequestBodies.Count);
    }

    [TestMethod]
    public async Task GenerateJsonAsync_IgnoresThoughtParts()
    {
        var envelope = JsonSerializer.Serialize(new
        {
            candidates = new[]
            {
                new { content = new { parts = new object[] { new { text = "thinking...", thought = true }, new { text = "{\"a\":1}" } } } }
            }
        });
        var handler = new GeminiTestHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(envelope) }));
        using var client = new GeminiJsonClient(() => "k", new HttpClient(handler));

        Assert.AreEqual("{\"a\":1}", await client.GenerateJsonAsync("s", "u", Schema));
    }

    [DataTestMethod]
    [DataRow("not json")]
    [DataRow("{\"candidates\":[]}")]
    [DataRow("{\"candidates\":[{\"content\":{\"parts\":[]}}]}")]
    public async Task GenerateJsonAsync_InvalidEnvelope_ReportsInvalidResponse(string envelope)
    {
        var handler = new GeminiTestHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(envelope) }));
        using var client = new GeminiJsonClient(() => "k", new HttpClient(handler));

        var error = await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => client.GenerateJsonAsync("s", "u", Schema));

        Assert.AreEqual(GeminiJsonClient.InvalidResponseMessage, error.Message);
    }

    [TestMethod]
    public async Task GenerateJsonAsync_OversizedResponse_IsRejected()
    {
        var big = new string('x', GeminiJsonClient.MaxResponseBytes + 10);
        var handler = new GeminiTestHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(big, Encoding.UTF8)
        }));
        using var client = new GeminiJsonClient(() => "k", new HttpClient(handler));

        var error = await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => client.GenerateJsonAsync("s", "u", Schema));

        StringAssert.Contains(error.Message, "oversized");
    }

    [TestMethod]
    public async Task GenerateJsonAsync_Timeout_ReportsTookTooLong()
    {
        using var client = new GeminiJsonClient(() => "k", new HttpClient(new CancellableDelayHandler()), timeout: TimeSpan.FromMilliseconds(50));

        var error = await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => client.GenerateJsonAsync("s", "u", Schema));

        Assert.AreEqual(GeminiJsonClient.TimeoutMessage, error.Message);
    }

    [TestMethod]
    public async Task GenerateJsonAsync_CallerCancellation_StaysCancellation()
    {
        using var cancellation = new CancellationTokenSource();
        using var client = new GeminiJsonClient(() => "k", new HttpClient(new CancellableDelayHandler()));
        var call = client.GenerateJsonAsync("s", "u", Schema, cancellationToken: cancellation.Token);

        cancellation.Cancel();

        await Assert.ThrowsExceptionAsync<TaskCanceledException>(() => call);
    }

    [TestMethod]
    public async Task GenerateJsonAsync_ConnectionFailure_ReportsConnectionMessage()
    {
        var handler = new GeminiTestHandler((_, _) => throw new HttpRequestException("down"));
        using var client = new GeminiJsonClient(() => "k", new HttpClient(handler));

        var error = await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => client.GenerateJsonAsync("s", "u", Schema));

        Assert.AreEqual(GeminiJsonClient.ConnectionMessage, error.Message);
    }

    private sealed class CancellableDelayHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return GeminiTestHandler.Ok("{}");
        }
    }
}
