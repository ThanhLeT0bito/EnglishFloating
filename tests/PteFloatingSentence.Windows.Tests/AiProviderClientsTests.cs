using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using PteFloatingSentence.Core;
using PteFloatingSentence.Windows.Infrastructure;

namespace PteFloatingSentence.Windows.Tests;

[TestClass]
public sealed class AiProviderClientsTests
{
    private static readonly object Schema = new
    {
        type = "OBJECT",
        properties = new
        {
            groups = new { type = "ARRAY", items = new { type = "STRING" } },
            tips = new { type = "OBJECT", properties = new { starter = new { type = "STRING" } } }
        },
        required = new[] { "groups" }
    };

    // ---- Schema conversion ----

    [TestMethod]
    public void JsonSchemaConverter_MakesStrictStandardSchema()
    {
        var strict = JsonSchemaConverter.ToStrict(Schema);

        Assert.AreEqual("object", strict["type"]!.GetValue<string>());
        Assert.IsFalse(strict["additionalProperties"]!.GetValue<bool>());
        CollectionAssert.AreEqual(new[] { "groups", "tips" }, strict["required"]!.AsArray().Select(n => n!.GetValue<string>()).ToArray());
        Assert.AreEqual("array", strict["properties"]!["groups"]!["type"]!.GetValue<string>());
        Assert.AreEqual("string", strict["properties"]!["groups"]!["items"]!["type"]!.GetValue<string>());
        var tips = strict["properties"]!["tips"]!;
        Assert.IsFalse(tips["additionalProperties"]!.GetValue<bool>());
        CollectionAssert.AreEqual(new[] { "starter" }, tips["required"]!.AsArray().Select(n => n!.GetValue<string>()).ToArray());
    }

    [TestMethod]
    public void JsonSchemaConverter_DoesNotChangeTheSource()
    {
        var source = new JsonObject { ["type"] = "OBJECT", ["properties"] = new JsonObject() };

        JsonSchemaConverter.ToStrict(source);

        Assert.AreEqual("OBJECT", source["type"]!.GetValue<string>());
    }

    // ---- Groq (OpenAI-compatible) ----

    private static HttpResponseMessage OpenAiOk(string content) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(JsonSerializer.Serialize(new { choices = new[] { new { message = new { role = "assistant", content } } } }))
    };

    [TestMethod]
    public async Task Groq_SendsStrictSchemaBearerKeyAndLowReasoning()
    {
        var handler = new GeminiTestHandler((_, _) => Task.FromResult(OpenAiOk("{\"groups\":[\"Hi.\"]}")));
        var authorization = new List<string?>();
        using var client = OpenAiCompatibleJsonClient.ForGroq(() => "gsk-test", new HttpClient(new RecordingHandler(handler, authorization)));

        var text = await client.GenerateJsonAsync("system rules", "user data", Schema, temperature: 0.4);

        Assert.AreEqual("{\"groups\":[\"Hi.\"]}", text);
        Assert.AreEqual("Bearer gsk-test", authorization[0]);
        Assert.AreEqual(OpenAiCompatibleJsonClient.GroqEndpoint, handler.RequestUris[0]);
        using var body = JsonDocument.Parse(handler.RequestBodies[0]);
        var root = body.RootElement;
        Assert.AreEqual("openai/gpt-oss-120b", root.GetProperty("model").GetString());
        Assert.AreEqual("system", root.GetProperty("messages")[0].GetProperty("role").GetString());
        Assert.AreEqual("system rules", root.GetProperty("messages")[0].GetProperty("content").GetString());
        Assert.AreEqual("user data", root.GetProperty("messages")[1].GetProperty("content").GetString());
        Assert.AreEqual("json_schema", root.GetProperty("response_format").GetProperty("type").GetString());
        Assert.IsTrue(root.GetProperty("response_format").GetProperty("json_schema").GetProperty("strict").GetBoolean());
        Assert.AreEqual("object", root.GetProperty("response_format").GetProperty("json_schema").GetProperty("schema").GetProperty("type").GetString());
        Assert.AreEqual("low", root.GetProperty("reasoning_effort").GetString());
        Assert.AreEqual(0.4, root.GetProperty("temperature").GetDouble());
        Assert.AreEqual("Groq", client.ProviderName);
    }

    [TestMethod]
    public async Task Groq_BadRequest_RetriesWithJsonObjectModeAndSchemaInPrompt()
    {
        var handler = new GeminiTestHandler((_, attempt) => Task.FromResult(attempt == 0
            ? GeminiTestHandler.Status(HttpStatusCode.BadRequest)
            : OpenAiOk("```json\n{\"groups\":[\"Hi.\"]}\n```")));
        using var client = OpenAiCompatibleJsonClient.ForGroq(() => "k", new HttpClient(handler));

        var text = await client.GenerateJsonAsync("system rules", "u", Schema);

        Assert.AreEqual("{\"groups\":[\"Hi.\"]}", text, "a markdown fence is removed");
        using var retry = JsonDocument.Parse(handler.RequestBodies[1]);
        Assert.AreEqual("json_object", retry.RootElement.GetProperty("response_format").GetProperty("type").GetString());
        Assert.IsFalse(retry.RootElement.TryGetProperty("reasoning_effort", out _));
        StringAssert.Contains(retry.RootElement.GetProperty("messages")[0].GetProperty("content").GetString()!, "JSON schema");
    }

    [TestMethod]
    public async Task Groq_FallbackModelUsesJsonObjectMode()
    {
        var handler = new GeminiTestHandler((_, attempt) => Task.FromResult(attempt == 0
            ? GeminiTestHandler.Status(HttpStatusCode.ServiceUnavailable)
            : OpenAiOk("{}")));
        using var client = OpenAiCompatibleJsonClient.ForGroq(() => "k", new HttpClient(handler));

        await client.GenerateJsonAsync("s", "u", Schema);

        using var second = JsonDocument.Parse(handler.RequestBodies[1]);
        Assert.AreEqual("llama-3.3-70b-versatile", second.RootElement.GetProperty("model").GetString());
        Assert.AreEqual("json_object", second.RootElement.GetProperty("response_format").GetProperty("type").GetString());
    }

    [TestMethod]
    public async Task Groq_MessagesNameTheProvider()
    {
        var unauthorized = new GeminiTestHandler((_, _) => Task.FromResult(GeminiTestHandler.Status(HttpStatusCode.Unauthorized)));
        using var rejected = OpenAiCompatibleJsonClient.ForGroq(() => "k", new HttpClient(unauthorized));
        using var missing = OpenAiCompatibleJsonClient.ForGroq(() => null);

        var keyError = await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => rejected.GenerateJsonAsync("s", "u", Schema));
        var missingError = await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => missing.GenerateJsonAsync("s", "u", Schema));

        Assert.AreEqual("Groq rejected the API key (HTTP 401). Check it in Settings.", keyError.Message);
        Assert.AreEqual("Configure your Groq API key in Settings first.", missingError.Message);
        Assert.IsFalse(missing.HasKey);
    }

    // ---- Claude (Anthropic) ----

    private static HttpResponseMessage AnthropicOk(string text, string stopReason = "end_turn") => new(HttpStatusCode.OK)
    {
        Content = new StringContent(JsonSerializer.Serialize(new
        {
            content = new[] { new { type = "text", text } },
            stop_reason = stopReason
        }))
    };

    [TestMethod]
    public async Task Claude_SendsHeadersSystemAndStructuredOutput()
    {
        var handler = new GeminiTestHandler((_, _) => Task.FromResult(AnthropicOk("{\"groups\":[\"Hi.\"]}")));
        var headers = new List<HttpRequestMessage>();
        using var client = new AnthropicJsonClient(() => "sk-ant-test", new HttpClient(new CapturingHandler(handler, headers)));

        var text = await client.GenerateJsonAsync("system rules", "user data", Schema, temperature: 1.5);

        Assert.AreEqual("{\"groups\":[\"Hi.\"]}", text);
        Assert.AreEqual(AnthropicJsonClient.Endpoint, handler.RequestUris[0]);
        Assert.AreEqual("sk-ant-test", headers[0].Headers.GetValues("x-api-key").Single());
        Assert.AreEqual(AnthropicJsonClient.ApiVersion, headers[0].Headers.GetValues("anthropic-version").Single());
        using var body = JsonDocument.Parse(handler.RequestBodies[0]);
        var root = body.RootElement;
        Assert.AreEqual("claude-haiku-5-5", root.GetProperty("model").GetString());
        Assert.AreEqual("system rules", root.GetProperty("system").GetString());
        Assert.AreEqual("user data", root.GetProperty("messages")[0].GetProperty("content").GetString());
        Assert.AreEqual(AnthropicJsonClient.MaxTokens, root.GetProperty("max_tokens").GetInt32());
        Assert.AreEqual(1.0, root.GetProperty("temperature").GetDouble(), "temperature is clamped to Claude's 0-1 range");
        var format = root.GetProperty("output_config").GetProperty("format");
        Assert.AreEqual("json_schema", format.GetProperty("type").GetString());
        Assert.IsFalse(format.GetProperty("schema").GetProperty("additionalProperties").GetBoolean());
    }

    [TestMethod]
    public async Task Claude_BadRequest_RetriesWithoutStructuredOutput()
    {
        var handler = new GeminiTestHandler((_, attempt) => Task.FromResult(attempt == 0
            ? GeminiTestHandler.Status(HttpStatusCode.BadRequest)
            : AnthropicOk("{}")));
        using var client = new AnthropicJsonClient(() => "k", new HttpClient(handler));

        await client.GenerateJsonAsync("system rules", "u", Schema);

        using var retry = JsonDocument.Parse(handler.RequestBodies[1]);
        Assert.IsFalse(retry.RootElement.TryGetProperty("output_config", out _));
        StringAssert.Contains(retry.RootElement.GetProperty("system").GetString()!, "JSON schema");
    }

    [TestMethod]
    public async Task Claude_OverloadedModel_FallsBackToNextModel()
    {
        var handler = new GeminiTestHandler((_, attempt) => Task.FromResult(attempt == 0
            ? GeminiTestHandler.Status((HttpStatusCode)529)
            : AnthropicOk("{\"ok\":true}")));
        using var client = new AnthropicJsonClient(() => "k", new HttpClient(handler));

        Assert.AreEqual("{\"ok\":true}", await client.GenerateJsonAsync("s", "u", Schema));
        using var second = JsonDocument.Parse(handler.RequestBodies[1]);
        Assert.AreEqual("claude-haiku-4-5", second.RootElement.GetProperty("model").GetString());
    }

    [TestMethod]
    public async Task Claude_Refusal_IsInvalidResponse()
    {
        var handler = new GeminiTestHandler((_, _) => Task.FromResult(AnthropicOk("{}", "refusal")));
        using var client = new AnthropicJsonClient(() => "k", new HttpClient(handler));

        var error = await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => client.GenerateJsonAsync("s", "u", Schema));

        Assert.AreEqual("Claude returned an invalid response. Please try again.", error.Message);
    }

    // ---- Switching ----

    [TestMethod]
    public async Task SwitchingClient_UsesTheCurrentlySelectedProvider()
    {
        var provider = AiProvider.Gemini;
        var gemini = new StubClient("Gemini");
        var groq = new StubClient("Groq");
        var switching = new SwitchingJsonClient(() => provider, new Dictionary<AiProvider, IJsonLlmClient>
        {
            [AiProvider.Gemini] = gemini,
            [AiProvider.Groq] = groq
        });

        await switching.GenerateJsonAsync("s", "u", Schema);
        provider = AiProvider.Groq;
        await switching.GenerateJsonAsync("s", "u", Schema);
        provider = AiProvider.Claude;
        await switching.GenerateJsonAsync("s", "u", Schema);

        Assert.AreEqual(2, gemini.Calls, "a provider without a client falls back to Gemini");
        Assert.AreEqual(1, groq.Calls);
    }

    [TestMethod]
    public async Task WithInfo_ReportsModelThatAnsweredAndAttempts()
    {
        var handler = new GeminiTestHandler((_, attempt) => Task.FromResult(attempt == 0
            ? GeminiTestHandler.Status(HttpStatusCode.ServiceUnavailable)
            : GeminiTestHandler.Ok("{}")));
        using var client = new GeminiJsonClient(() => "k", new HttpClient(handler), ["model-a", "model-b"]);

        var result = await client.GenerateJsonWithInfoAsync("s", "u", Schema);

        Assert.AreEqual("{}", result.Text);
        Assert.AreEqual("Gemini", result.Info.Provider);
        Assert.AreEqual("model-b", result.Info.Model);
        Assert.AreEqual(2, result.Info.Attempts);
        Assert.IsTrue(result.Info.Elapsed >= TimeSpan.Zero);
    }

    [TestMethod]
    public async Task Groq_GptOss_ExcludesReasoningFromResponse()
    {
        var handler = new GeminiTestHandler((_, _) => Task.FromResult(OpenAiOk("{}")));
        using var client = OpenAiCompatibleJsonClient.ForGroq(() => "k", new HttpClient(handler));

        await client.GenerateJsonAsync("s", "u", Schema);

        using var body = JsonDocument.Parse(handler.RequestBodies[0]);
        Assert.IsFalse(body.RootElement.GetProperty("include_reasoning").GetBoolean());
    }

    [TestMethod]
    public async Task InterfaceDefault_WithInfoWrapsPlainClients()
    {
        IJsonLlmClient stub = new StubClient("Stub");

        var result = await stub.GenerateJsonWithInfoAsync("s", "u", Schema);

        Assert.AreEqual("{}", result.Text);
        Assert.AreEqual("Stub", result.Info.Provider);
    }

    private sealed class StubClient(string name) : IJsonLlmClient
    {
        public int Calls { get; private set; }
        public string ProviderName => name;
        public bool HasKey => true;

        public Task<string> GenerateJsonAsync(string systemInstruction, string userText, object responseSchema, double? temperature = null, CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult("{}");
        }
    }

    private sealed class RecordingHandler(HttpMessageHandler inner, List<string?> authorization) : DelegatingHandler(inner)
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            authorization.Add(request.Headers.Authorization?.ToString());
            return base.SendAsync(request, cancellationToken);
        }
    }

    private sealed class CapturingHandler(HttpMessageHandler inner, List<HttpRequestMessage> requests) : DelegatingHandler(inner)
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            requests.Add(request);
            return base.SendAsync(request, cancellationToken);
        }
    }
}
