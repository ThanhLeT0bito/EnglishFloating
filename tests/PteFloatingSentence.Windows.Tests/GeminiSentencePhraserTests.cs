using System.Net;
using System.Text.Json;
using PteFloatingSentence.Windows.Infrastructure;

namespace PteFloatingSentence.Windows.Tests;

[TestClass]
public sealed class GeminiSentencePhraserTests
{
    [TestMethod]
    public async Task SuggestAsync_ValidGroups_ReturnsEditableProposal()
    {
        using var client = ClientFor("{\"groups\":[\"I usually go to the gym\",\"after work\",\"with my friends.\"]}");
        using var service = new GeminiSentencePhraser(() => "test-key", client);

        var groups = await service.SuggestAsync("I usually go to the gym after work with my freinds.");

        CollectionAssert.AreEqual(new[] { "I usually go to the gym", "after work", "with my friends." }, groups.ToArray());
    }

    [TestMethod]
    public async Task SuggestAsync_EmptyGroup_RejectsResponse()
    {
        using var client = ClientFor("{\"groups\":[\"First\",\"\",\"second\"]}");
        using var service = new GeminiSentencePhraser(() => "test-key", client);
        await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => service.SuggestAsync("First second."));
    }

    [TestMethod]
    public async Task SuggestAsync_MalformedJson_RejectsResponse()
    {
        using var client = ClientFor("not json");
        using var service = new GeminiSentencePhraser(() => "test-key", client);
        await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => service.SuggestAsync("A useful sentence."));
    }

    [TestMethod]
    public async Task SuggestAsync_MissingKey_DoesNotSendRequest()
    {
        using var client = ClientFor("{}");
        using var service = new GeminiSentencePhraser(() => null, client);
        var error = await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => service.SuggestAsync("A useful sentence."));
        StringAssert.Contains(error.Message, "key");
    }

    private static HttpClient ClientFor(string proposal) => new(new ResponseHandler(proposal));

    private sealed class ResponseHandler(string proposal) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(new
                {
                    candidates = new[] { new { content = new { parts = new[] { new { text = proposal } } } } }
                }))
            });
    }
}
