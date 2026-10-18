using System.Net;
using PteFloatingSentence.Windows.Infrastructure;

namespace PteFloatingSentence.Windows.Tests;

[TestClass]
public sealed class AiServicesTests
{
    private const string SecretApiKey = "secret-gemini-api-key-999";

    private static (GeminiJsonClient Client, GeminiTestHandler Handler) Gemini(string modelText, Func<string?>? key = null)
    {
        var handler = GeminiTestHandler.Returning(modelText);
        return (new GeminiJsonClient(key ?? (() => SecretApiKey), new HttpClient(handler), ["model-a", "model-b"]), handler);
    }

    // ---- Sentence phrasing ----

    [TestMethod]
    public async Task Phraser_ValidGroups_ReturnsEditableProposal()
    {
        var (client, _) = Gemini("{\"groups\":[\"I usually go to the gym\",\"after work\",\"with my friends.\"]}");
        var phraser = new AiSentencePhraser(client);

        var groups = await phraser.SuggestAsync("I usually go to the gym after work with my freinds.");

        CollectionAssert.AreEqual(new[] { "I usually go to the gym", "after work", "with my friends." }, groups.ToArray());
    }

    [DataTestMethod]
    [DataRow("{\"groups\":[\"First\",\"\",\"second\"]}")]
    [DataRow("{\"groups\":[]}")]
    [DataRow("not json")]
    [DataRow("{\"other\":1}")]
    public async Task Phraser_InvalidProposal_IsRejected(string modelText)
    {
        var (client, _) = Gemini(modelText);
        var phraser = new AiSentencePhraser(client);

        var error = await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => phraser.SuggestAsync("First second."));

        Assert.AreEqual(AiSentencePhraser.InvalidProposalMessage, error.Message);
    }

    [TestMethod]
    public async Task Phraser_MissingKey_DoesNotSendRequest()
    {
        var (client, handler) = Gemini("{}", () => null);
        var phraser = new AiSentencePhraser(client);

        var error = await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => phraser.SuggestAsync("A useful sentence."));

        StringAssert.Contains(error.Message, "key");
        Assert.AreEqual(0, handler.RequestBodies.Count);
    }

    [TestMethod]
    public async Task Phraser_InvalidSentence_IsRejectedBeforeAnyRequest()
    {
        var (client, handler) = Gemini("{}");
        var phraser = new AiSentencePhraser(client);

        await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => phraser.SuggestAsync("..."));

        Assert.AreEqual(0, handler.RequestBodies.Count);
    }

    [TestMethod]
    public async Task Phraser_SendsSentenceAsUserData()
    {
        var (client, handler) = Gemini("{\"groups\":[\"Hello there.\"]}");

        await new AiSentencePhraser(client).SuggestAsync("Hello there.");

        Assert.AreEqual("Hello there.", handler.UserText(0));
        StringAssert.Contains(handler.SystemInstruction(0), "thought groups");
    }

    // ---- Vocabulary explanations ----

    [TestMethod]
    public async Task Explainer_ValidResponse_ReturnsExplanation()
    {
        var (client, handler) = Gemini("""
            {"meaning":" To consider something. ","example":"Take the weather into account.","pronunciationIpa":"/teɪk/","correctedPhrase":"take into account"}
            """);
        var explainer = new AiVocabularyExplainer(client);

        var result = await explainer.ExplainAsync("take into account", "You must take into account all variables.");

        Assert.AreEqual("To consider something.", result.Meaning);
        Assert.AreEqual("Take the weather into account.", result.Example);
        Assert.AreEqual("/teɪk/", result.PronunciationIpa);
        Assert.AreEqual("take into account", result.CorrectedPhrase);
        StringAssert.Contains(handler.UserText(0), "You must take into account all variables.");
    }

    [TestMethod]
    public async Task Explainer_MisspelledPhrase_ReturnsCorrection()
    {
        var (client, _) = Gemini("{\"correctedPhrase\":\"discipline\",\"meaning\":\"Training to obey rules.\",\"example\":\"He lacks self-discipline.\",\"pronunciationIpa\":\"/ˈdɪsəplɪn/\"}");

        var result = await new AiVocabularyExplainer(client).ExplainAsync("displine");

        Assert.AreEqual("discipline", result.CorrectedPhrase);
    }

    [DataTestMethod]
    [DataRow("not json")]
    [DataRow("{\"meaning\":\"\",\"example\":\"x\",\"pronunciationIpa\":\"/x/\"}")]
    [DataRow("{\"meaning\":\"m\",\"example\":\"x\"}")]
    public async Task Explainer_IncompleteResponse_IsRejectedWithoutKey(string modelText)
    {
        var (client, _) = Gemini(modelText);

        var error = await Assert.ThrowsExceptionAsync<InvalidOperationException>(() =>
            new AiVocabularyExplainer(client).ExplainAsync("word", "A sentence with word."));

        Assert.AreEqual(AiVocabularyExplainer.InvalidExplanationMessage, error.Message);
        Assert.IsFalse(error.Message.Contains(SecretApiKey));
    }

    [TestMethod]
    public async Task Explainer_HttpError_ReportsProviderMessageWithoutKey()
    {
        var handler = new GeminiTestHandler((_, _) => Task.FromResult(GeminiTestHandler.Status(HttpStatusCode.TooManyRequests)));
        var client = new GeminiJsonClient(() => SecretApiKey, new HttpClient(handler), ["model-a"]);

        var error = await Assert.ThrowsExceptionAsync<InvalidOperationException>(() =>
            new AiVocabularyExplainer(client).ExplainAsync("word"));

        StringAssert.Contains(error.Message, "429");
        Assert.IsFalse(error.Message.Contains(SecretApiKey));
    }

    [TestMethod]
    public async Task Explainer_NoKey_ThrowsWithoutNetworkCall()
    {
        var (client, handler) = Gemini("{}", () => null);

        var error = await Assert.ThrowsExceptionAsync<InvalidOperationException>(() =>
            new AiVocabularyExplainer(client).ExplainAsync("word"));

        StringAssert.Contains(error.Message, "API key");
        Assert.AreEqual(0, handler.RequestBodies.Count);
    }
}
