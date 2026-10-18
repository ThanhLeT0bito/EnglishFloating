using PteFloatingSentence.Core;
using PteFloatingSentence.Windows.Infrastructure;

namespace PteFloatingSentence.Windows.Tests;

/// <summary>The AI side of the vocabulary check: translating meanings in batches and grading a typed meaning.</summary>
[TestClass]
public sealed class VocabularyCheckerTests
{
    private static readonly MeaningLanguage Vietnamese = MeaningLanguages.Find("vi");

    [TestMethod]
    public void TranslateMeanings_SendsTheItemsAsDataAndReturnsOnePerItem()
    {
        var client = new QueueClient("{\"meanings\":[\"Danh mục tài liệu\",\"Trường đại học\"]}");
        var checker = new AiVocabularyChecker(client);

        var result = checker.TranslateMeaningsAsync(
            [new("bibliography", "A list of sources."), new("university", "A place for higher education.")], Vietnamese).GetAwaiter().GetResult();

        CollectionAssert.AreEqual(new[] { "Danh mục tài liệu", "Trường đại học" }, result.ToArray());
        StringAssert.Contains(client.UserTexts.Single(), "bibliography");
        StringAssert.Contains(client.UserTexts.Single(), "Tiếng Việt");
    }

    [TestMethod]
    public void TranslateMeanings_SplitsLargeDecksIntoBatches()
    {
        var items = Enumerable.Range(0, AiVocabularyChecker.MaxBatch + 2).Select(i => new MeaningToTranslate($"w{i}", "m")).ToList();
        string Answer(int count) => "{\"meanings\":[" + string.Join(',', Enumerable.Repeat("\"x\"", count)) + "]}";
        var client = new QueueClient(Answer(AiVocabularyChecker.MaxBatch), Answer(2));

        var result = new AiVocabularyChecker(client).TranslateMeaningsAsync(items, Vietnamese).GetAwaiter().GetResult();

        Assert.AreEqual(items.Count, result.Count);
        Assert.AreEqual(2, client.UserTexts.Count);
    }

    [TestMethod]
    public void Parse_RejectsAnswersThatDoNotFit()
    {
        Assert.ThrowsException<InvalidOperationException>(() => AiVocabularyChecker.ParseTranslations("{\"meanings\":[\"a\"]}", 2));
        Assert.ThrowsException<InvalidOperationException>(() => AiVocabularyChecker.ParseTranslations("nope", 1));
        Assert.ThrowsException<InvalidOperationException>(() => AiVocabularyChecker.ParseGrade("{\"correct\":\"yes\"}"));
        Assert.AreEqual(new MeaningGrade(true, "Đúng."), AiVocabularyChecker.ParseGrade("{\"correct\":true,\"feedback\":\" Đúng. \"}"));
        Assert.AreEqual(new MeaningGrade(false, string.Empty), AiVocabularyChecker.ParseGrade("{\"correct\":false}"));
    }

    [TestMethod]
    public void GradeMeaning_SendsTheWordReferenceAndAnswer()
    {
        var client = new QueueClient("{\"correct\":true,\"feedback\":\"Right.\"}");

        var grade = new AiVocabularyChecker(client).GradeMeaningAsync("bibliography", "A list of sources.", "danh sách tài liệu", Vietnamese).GetAwaiter().GetResult();

        Assert.IsTrue(grade.IsCorrect);
        StringAssert.Contains(client.UserTexts.Single(), "danh sách tài liệu");
        StringAssert.Contains(client.UserTexts.Single(), "A list of sources.");
    }

    private sealed class QueueClient(params string[] answers) : IJsonLlmClient
    {
        private readonly Queue<string> _answers = new(answers);
        public List<string> UserTexts { get; } = [];
        public string ProviderName => "Fake";
        public bool HasKey => true;

        public Task<string> GenerateJsonAsync(string systemInstruction, string userText, object responseSchema, double? temperature = null, CancellationToken cancellationToken = default)
        {
            UserTexts.Add(userText);
            return Task.FromResult(_answers.Dequeue());
        }
    }
}
