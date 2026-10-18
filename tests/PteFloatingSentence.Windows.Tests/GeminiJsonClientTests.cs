using System.IO;
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
    public async Task GenerateJsonAsync_RequestsLowThinking()
    {
        var handler = GeminiTestHandler.Returning("{}");
        using var client = new GeminiJsonClient(() => "k", new HttpClient(handler));

        await client.GenerateJsonAsync("s", "u", Schema);

        using var body = JsonDocument.Parse(handler.RequestBodies[0]);
        var thinking = body.RootElement.GetProperty("generationConfig").GetProperty("thinkingConfig");
        Assert.AreEqual("low", thinking.GetProperty("thinkingLevel").GetString());
    }

    [TestMethod]
    public async Task GenerateJsonAsync_BadRequestWithThinking_RetriesSameModelWithoutThinking()
    {
        var handler = new GeminiTestHandler((_, attempt) => Task.FromResult(attempt == 0
            ? GeminiTestHandler.Status(HttpStatusCode.BadRequest)
            : GeminiTestHandler.Ok("{\"ok\":true}")));
        using var client = new GeminiJsonClient(() => "k", new HttpClient(handler), ["a", "b"]);

        var text = await client.GenerateJsonAsync("s", "u", Schema);

        Assert.AreEqual("{\"ok\":true}", text);
        StringAssert.Contains(handler.RequestUris[1], "/models/a:");
        using var retry = JsonDocument.Parse(handler.RequestBodies[1]);
        Assert.IsFalse(retry.RootElement.GetProperty("generationConfig").TryGetProperty("thinkingConfig", out _));
    }

    [TestMethod]
    public async Task GenerateJsonAsync_BadRequest_RetriesSimplerThenTriesTheNextModel()
    {
        var handler = new GeminiTestHandler((_, _) => Task.FromResult(GeminiTestHandler.Status(HttpStatusCode.BadRequest)));
        using var client = new GeminiJsonClient(() => "k", new HttpClient(handler), ["a", "b"]);

        var error = await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => client.GenerateJsonAsync("s", "u", Schema));

        StringAssert.Contains(error.Message, "400");
        Assert.AreEqual(4, handler.RequestBodies.Count, "each model once as asked, once simpler");
        Assert.IsTrue(handler.RequestUris.Take(2).All(uri => uri.Contains("/models/a:")));
        Assert.IsTrue(handler.RequestUris.Skip(2).All(uri => uri.Contains("/models/b:")));
    }

    [TestMethod]
    public async Task GenerateJsonAsync_RetiredModel_FallsBackAndShowsNoErrorWhenTheNextWorks()
    {
        var handler = new GeminiTestHandler((request, _) => Task.FromResult(request.RequestUri!.ToString().Contains("/models/old:")
            ? Error(HttpStatusCode.BadRequest, "{\"error\":{\"code\":400,\"message\":\"The model old has been decommissioned.\"}}")
            : GeminiTestHandler.Ok("{\"ok\":true}")));
        using var client = new GeminiJsonClient(() => "k", new HttpClient(handler), ["old", "new"]);

        Assert.AreEqual("{\"ok\":true}", await client.GenerateJsonAsync("s", "u", Schema));
    }

    [TestMethod]
    public async Task GenerateJsonAsync_InvalidKey_SaysSoAtOnceWithoutTryingOtherModels()
    {
        const string body = "{\"error\":{\"code\":400,\"message\":\"API key not valid. Please pass a valid API key.\",\"status\":\"INVALID_ARGUMENT\","
            + "\"details\":[{\"@type\":\"type.googleapis.com/google.rpc.ErrorInfo\",\"reason\":\"API_KEY_INVALID\"}]}}";
        var handler = new GeminiTestHandler((_, _) => Task.FromResult(Error(HttpStatusCode.BadRequest, body)));
        using var client = new GeminiJsonClient(() => "k", new HttpClient(handler), ["a", "b"]);

        var error = await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => client.GenerateJsonAsync("s", "u", Schema));

        Assert.AreEqual("Gemini rejected the API key (HTTP 400). Check it in Settings.", error.Message);
        Assert.AreEqual(1, handler.RequestBodies.Count);
    }

    [TestMethod]
    public async Task GenerateJsonAsync_ShowsTheProvidersReasonWithoutTheKey()
    {
        var handler = new GeminiTestHandler((_, _) => Task.FromResult(Error(HttpStatusCode.UnprocessableEntity,
            "{\"error\":{\"message\":\"Request for key secret-key-123 exceeds the allowed size.\"}}")));
        using var client = new GeminiJsonClient(() => "secret-key-123", new HttpClient(handler), ["a"]);

        var error = await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => client.GenerateJsonAsync("s", "u", Schema));

        Assert.AreEqual("Gemini request failed (HTTP 422): Request for key *** exceeds the allowed size.", error.Message);
    }

    [TestMethod]
    public async Task GenerateJsonAsync_ConnectionDropsWhileReading_SaysCouldNotConnect()
    {
        var handler = new GeminiTestHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StreamContent(new BrokenStream())
        }));
        using var client = new GeminiJsonClient(() => "k", new HttpClient(handler), ["a"]);

        var error = await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => client.GenerateJsonAsync("s", "u", Schema));

        Assert.AreEqual(JsonLlmClientBase.ConnectionText("Gemini"), error.Message);
    }

    [TestMethod]
    public void ErrorDetail_ReadsTheCommonShapesAndShortensLongReasons()
    {
        Assert.AreEqual("Your credit balance is too low to access the Anthropic API.", JsonLlmClientBase.ErrorDetail(
            "{\"type\":\"error\",\"error\":{\"type\":\"invalid_request_error\",\"message\":\"Your credit balance is too low to access the Anthropic API.\"}}", "k"));
        Assert.AreEqual("Rate limit reached", JsonLlmClientBase.ErrorDetail("{\"error\":\"Rate limit reached\"}", "k"));
        Assert.IsNull(JsonLlmClientBase.ErrorDetail("<html>Bad gateway</html>", "k"));
        Assert.IsNull(JsonLlmClientBase.ErrorDetail("{\"error\":{\"code\":400}}", "k"));
        var longReason = JsonLlmClientBase.ErrorDetail("{\"error\":{\"message\":\"" + new string('x', 500) + "\"}}", "k")!;
        Assert.AreEqual(JsonLlmClientBase.MaxErrorDetailLength + 1, longReason.Length);
        Assert.IsTrue(longReason.EndsWith('…'));
    }

    private static HttpResponseMessage Error(HttpStatusCode status, string body) => new(status) { Content = new StringContent(body) };

    /// <summary>A response body whose connection drops mid-read, like HttpIOException.</summary>
    private sealed class BrokenStream : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => 0; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new IOException("The response ended prematurely.");
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    [TestMethod]
    public async Task GenerateJsonAsync_SlowModel_FallsBackToNextModel()
    {
        var handler = new GeminiTestHandler(async (request, attempt) =>
        {
            if (attempt == 0)
                await Task.Delay(TimeSpan.FromSeconds(10));
            return GeminiTestHandler.Ok("{\"fast\":true}");
        });
        using var client = new GeminiJsonClient(() => "k", new HttpClient(new CancelAwareHandler(handler)), ["slow", "fast"],
            timeout: TimeSpan.FromSeconds(5), attemptTimeout: TimeSpan.FromMilliseconds(100));

        var text = await client.GenerateJsonAsync("s", "u", Schema);

        Assert.AreEqual("{\"fast\":true}", text);
        StringAssert.Contains(handler.RequestUris[1], "/models/fast:");
    }

    [TestMethod]
    public async Task GenerateJsonAsync_EveryModelSlow_ReportsTookTooLong()
    {
        using var client = new GeminiJsonClient(() => "k", new HttpClient(new CancellableDelayHandler()), ["a", "b"],
            timeout: TimeSpan.FromSeconds(5), attemptTimeout: TimeSpan.FromMilliseconds(50));

        var error = await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => client.GenerateJsonAsync("s", "u", Schema));

        Assert.AreEqual(GeminiJsonClient.TimeoutMessage, error.Message);
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

    /// <summary>Wraps a scripted handler so a slow scripted response still honours the request's cancellation token.</summary>
    private sealed class CancelAwareHandler(GeminiTestHandler inner) : DelegatingHandler(inner)
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            base.SendAsync(request, cancellationToken).WaitAsync(cancellationToken);
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
