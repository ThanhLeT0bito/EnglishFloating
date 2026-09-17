using System.Net;
using System.Text;
using PteFloatingSentence.Windows.Infrastructure;

namespace PteFloatingSentence.Windows.Tests;

[TestClass]
public sealed class GeminiVocabularyExplainerTests
{
    private const string SecretApiKey = "secret-gemini-api-key-999";

    [TestMethod]
    public async Task ExplainAsync_ValidJsonResponse_ReturnsVocabularyExplanation()
    {
        var jsonResponse = """
            {
              "candidates": [
                {
                  "content": {
                    "parts": [
                      {
                        "text": "{\n  \"meaning\": \"To consider or remember something when making a decision.\",\n  \"example\": \"You should take into account the weather before traveling.\",\n  \"pronunciationIpa\": \"/teɪk ˈɪntuː əˈkaʊnt/\"\n}"
                      }
                    ]
                  }
                }
              ]
            }
            """;

        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(jsonResponse, Encoding.UTF8, "application/json")
        });

        var client = new HttpClient(handler);
        var explainer = new GeminiVocabularyExplainer(() => SecretApiKey, client);

        var result = await explainer.ExplainAsync("take into account", "You must take into account all variables.");

        Assert.AreEqual("To consider or remember something when making a decision.", result.Meaning);
        Assert.AreEqual("You should take into account the weather before traveling.", result.Example);
        Assert.AreEqual("/teɪk ˈɪntuː əˈkaʊnt/", result.PronunciationIpa);
    }

    [TestMethod]
    public async Task ExplainAsync_MalformedJson_ThrowsConciseExceptionWithoutApiKey()
    {
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("not valid json at all", Encoding.UTF8, "application/json")
        });

        var client = new HttpClient(handler);
        var explainer = new GeminiVocabularyExplainer(() => SecretApiKey, client);

        var ex = await Assert.ThrowsExceptionAsync<InvalidOperationException>(async () =>
            await explainer.ExplainAsync("word", "A sentence with word."));

        StringAssert.DoesNotMatch(ex.Message, new System.Text.RegularExpressions.Regex(SecretApiKey));
    }

    [TestMethod]
    public async Task ExplainAsync_HttpError_ThrowsConciseExceptionWithoutApiKey()
    {
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.TooManyRequests)
        {
            Content = new StringContent("Quota exceeded for quota metric", Encoding.UTF8, "text/plain")
        });

        var client = new HttpClient(handler);
        var explainer = new GeminiVocabularyExplainer(() => SecretApiKey, client);

        var ex = await Assert.ThrowsExceptionAsync<HttpRequestException>(async () =>
            await explainer.ExplainAsync("word", "A sentence with word."));

        StringAssert.DoesNotMatch(ex.Message, new System.Text.RegularExpressions.Regex(SecretApiKey));
    }

    [TestMethod]
    public async Task ExplainAsync_HttpErrorWithJsonPayload_IncludesSanitizedErrorMessage()
    {
        var errorJson = $$"""
            {
              "error": {
                "code": 404,
                "message": "Model not found with key {{SecretApiKey}}",
                "status": "NOT_FOUND"
              }
            }
            """;

        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound)
        {
            Content = new StringContent(errorJson, Encoding.UTF8, "application/json")
        });

        var client = new HttpClient(handler);
        var explainer = new GeminiVocabularyExplainer(() => SecretApiKey, client);

        var ex = await Assert.ThrowsExceptionAsync<HttpRequestException>(async () =>
            await explainer.ExplainAsync("word", "A sentence with word."));

        Assert.IsTrue(ex.Message.Contains("Model not found with key [REDACTED]"));
        StringAssert.DoesNotMatch(ex.Message, new System.Text.RegularExpressions.Regex(SecretApiKey));
    }

    [TestMethod]
    public async Task ExplainAsync_UsesGemini36FlashModel()
    {
        HttpRequestMessage? capturedRequest = null;
        var jsonResponse = """
            {
              "candidates": [
                {
                  "content": {
                    "parts": [
                      {
                        "text": "{\n  \"meaning\": \"test\",\n  \"example\": \"test\",\n  \"pronunciationIpa\": \"/test/\"\n}"
                      }
                    ]
                  }
                }
              ]
            }
            """;

        var handler = new FakeHttpMessageHandler(req =>
        {
            capturedRequest = req;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(jsonResponse, Encoding.UTF8, "application/json")
            };
        });

        var client = new HttpClient(handler);
        var explainer = new GeminiVocabularyExplainer(() => SecretApiKey, client);

        await explainer.ExplainAsync("word", "A sentence with word.");

        Assert.IsNotNull(capturedRequest);
        Assert.IsTrue(capturedRequest.RequestUri!.ToString().Contains("models/gemini-3.6-flash:generateContent"));
    }

    [TestMethod]
    public async Task ExplainAsync_NoApiKeyConfigured_ThrowsWithoutNetworkCall()
    {
        var handler = new FakeHttpMessageHandler(_ => throw new Exception("Should not be called"));
        var client = new HttpClient(handler);
        var explainer = new GeminiVocabularyExplainer(() => null, client);

        var ex = await Assert.ThrowsExceptionAsync<InvalidOperationException>(async () =>
            await explainer.ExplainAsync("word", "A sentence with word."));

        Assert.IsTrue(ex.Message.Contains("API key"));
    }

    private sealed class FakeHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _handler;

        public FakeHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> handler)
        {
            _handler = handler;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(_handler(request));
        }
    }
}
