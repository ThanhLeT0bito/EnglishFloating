using System.Globalization;
using System.Text;

namespace PteFloatingSentence.Core;

public enum GrammarCategory
{
    Tenses = 0,
    Verbs = 1,
    Sentences = 2,
    Nouns = 3,
    Connectors = 4,
    Words = 5,
    Custom = 6
}

/// <summary>
/// A grammar point the learner can study. Built-in keys are stable slugs; a topic the learner searched for has a
/// key starting with <see cref="GrammarTopics.CustomKeyPrefix"/>.
/// </summary>
/// <param name="FocusHint">What a practice sentence must make the learner use, for the AI prompt.</param>
/// <param name="Keywords">Extra search words in English and Vietnamese.</param>
public sealed record GrammarTopic(
    string Key,
    string Name,
    string VietnameseName,
    GrammarCategory Category,
    string FocusHint,
    IReadOnlyList<string> Keywords)
{
    public bool IsCustom => GrammarTopics.IsCustomKey(Key);
}

/// <summary>Built-in grammar topics, lookup and search.</summary>
public static class GrammarTopics
{
    public const string CustomKeyPrefix = "custom:";
    public const int MaxCustomLength = 60;

    public static IReadOnlyList<GrammarTopic> All { get; } =
    [
        // Tenses
        new("present-simple", "Present simple", "Thì hiện tại đơn", GrammarCategory.Tenses,
            "the present simple for habits, facts and schedules, including third-person -s/-es and do/does in questions and negatives",
            ["simple present", "hien tai don", "every day", "usually", "always", "does", "s es"]),
        new("present-continuous", "Present continuous", "Thì hiện tại tiếp diễn", GrammarCategory.Tenses,
            "the present continuous (am/is/are + V-ing) for actions happening now or temporary situations",
            ["present progressive", "hien tai tiep dien", "now", "at the moment", "ing"]),
        new("present-perfect", "Present perfect", "Thì hiện tại hoàn thành", GrammarCategory.Tenses,
            "the present perfect (have/has + past participle) for experiences, results and situations up to now, with since/for/already/yet",
            ["hien tai hoan thanh", "have done", "since", "for", "already", "yet", "ever", "never"]),
        new("present-perfect-continuous", "Present perfect continuous", "Thì hiện tại hoàn thành tiếp diễn", GrammarCategory.Tenses,
            "the present perfect continuous (have/has been + V-ing) for an activity that started in the past and is still going on",
            ["hien tai hoan thanh tiep dien", "have been doing", "how long"]),
        new("past-simple", "Past simple", "Thì quá khứ đơn", GrammarCategory.Tenses,
            "the past simple for finished actions at a known past time, including irregular verbs and did in questions and negatives",
            ["simple past", "qua khu don", "yesterday", "ago", "last", "ed", "irregular verbs"]),
        new("past-continuous", "Past continuous", "Thì quá khứ tiếp diễn", GrammarCategory.Tenses,
            "the past continuous (was/were + V-ing) for an action in progress in the past, often interrupted by a past simple action",
            ["past progressive", "qua khu tiep dien", "while", "when", "was doing"]),
        new("past-perfect", "Past perfect", "Thì quá khứ hoàn thành", GrammarCategory.Tenses,
            "the past perfect (had + past participle) for an action that happened before another past action",
            ["qua khu hoan thanh", "had done", "before", "by the time"]),
        new("future", "Future: will / be going to", "Thì tương lai: will / be going to", GrammarCategory.Tenses,
            "future forms: will for decisions and predictions, be going to for plans and evidence, present continuous for arrangements",
            ["future simple", "tuong lai", "will", "going to", "tomorrow", "next"]),

        // Verbs
        new("passive-voice", "Passive voice", "Câu bị động", GrammarCategory.Verbs,
            "the passive voice (be + past participle) in the right tense, with by + agent only when needed",
            ["bi dong", "passive", "is done", "was made", "be + v3"]),
        new("modal-verbs", "Modal verbs", "Động từ khuyết thiếu", GrammarCategory.Verbs,
            "modal verbs (can, could, must, have to, should, may, might) followed by a bare infinitive",
            ["modals", "dong tu khuyet thieu", "can", "must", "should", "might", "have to"]),
        new("gerund-infinitive", "Gerund or infinitive (V-ing / to V)", "V-ing hay to V", GrammarCategory.Verbs,
            "verbs and expressions followed by a gerund (enjoy, avoid, look forward to) or a to-infinitive (decide, want, plan)",
            ["gerund", "infinitive", "danh dong tu", "to v", "v-ing", "enjoy doing", "want to do"]),
        new("subject-verb-agreement", "Subject–verb agreement", "Hòa hợp chủ ngữ – động từ", GrammarCategory.Verbs,
            "subject-verb agreement with singular/plural subjects, everyone/each, the number of / a number of, and long subjects",
            ["hoa hop chu ngu", "agreement", "is are", "has have", "everyone is"]),
        new("phrasal-verbs", "Phrasal verbs", "Cụm động từ", GrammarCategory.Verbs,
            "common phrasal verbs (look for, give up, turn down, carry out) with the correct particle and word order",
            ["cum dong tu", "phrasal", "give up", "look after"]),

        // Sentences
        new("conditionals", "Conditionals (types 0, 1, 2, 3)", "Câu điều kiện (loại 0, 1, 2, 3)", GrammarCategory.Sentences,
            "conditional sentences with the right tense in each clause (if + present / will, if + past / would, if + past perfect / would have)",
            ["cau dieu kien", "if", "unless", "would", "type 1", "type 2", "type 3"]),
        new("relative-clauses", "Relative clauses", "Mệnh đề quan hệ", GrammarCategory.Sentences,
            "relative clauses with who, which, that, whose, where, including commas for non-defining clauses",
            ["menh de quan he", "who", "which", "that", "whose", "where"]),
        new("reported-speech", "Reported speech", "Câu gián tiếp", GrammarCategory.Sentences,
            "reported speech with tense backshift and changes to pronouns and time words (said that, asked if)",
            ["cau gian tiep", "indirect speech", "said that", "told me"]),
        new("questions", "Question forms", "Cách đặt câu hỏi", GrammarCategory.Sentences,
            "question word order with auxiliaries (do/does/did, have, be), wh-questions and indirect questions (Could you tell me where ...)",
            ["cau hoi", "question", "wh", "indirect question", "word order in questions"]),
        new("comparisons", "Comparatives and superlatives", "So sánh hơn và so sánh nhất", GrammarCategory.Sentences,
            "comparatives (-er / more than) and superlatives (the -est / the most), as ... as, and the more ..., the more ...",
            ["so sanh", "comparative", "superlative", "than", "the most"]),
        new("participle-clauses", "Participle clauses", "Mệnh đề phân từ", GrammarCategory.Sentences,
            "participle clauses (V-ing / past participle / having + past participle) that share the subject of the main clause",
            ["menh de phan tu", "rut gon", "having done", "reduced clause"]),
        new("inversion", "Inversion", "Đảo ngữ", GrammarCategory.Sentences,
            "inversion after negative adverbials (Not only did ..., Never have I ..., No sooner had ... than)",
            ["dao ngu", "not only", "never have", "hardly", "no sooner"]),

        // Nouns
        new("articles", "Articles: a / an / the", "Mạo từ a / an / the", GrammarCategory.Nouns,
            "articles a/an/the and no article, for first and second mention, unique things, general plural and uncountable nouns",
            ["mao tu", "a an the", "article", "zero article"]),
        new("countable-uncountable", "Countable and uncountable nouns", "Danh từ đếm được và không đếm được", GrammarCategory.Nouns,
            "countable and uncountable nouns (information, advice, equipment) with much/many, a few/a little and plural forms",
            ["dem duoc", "khong dem duoc", "uncountable", "much many", "information", "advice"]),
        new("quantifiers", "Quantifiers (some, any, few, little)", "Từ chỉ số lượng", GrammarCategory.Nouns,
            "quantifiers (some, any, much, many, a lot of, few, a few, little, a little, each, every, most of) with the right noun",
            ["tu chi so luong", "some any", "few little", "most of"]),
        new("pronouns", "Pronouns and possessives", "Đại từ và sở hữu", GrammarCategory.Nouns,
            "personal, object, possessive and reflexive pronouns (he/him/his/himself) and possessive 's",
            ["dai tu", "so huu", "his her", "myself", "possessive"]),

        // Connectors
        new("prepositions", "Prepositions of time and place (in / on / at)", "Giới từ chỉ thời gian và nơi chốn", GrammarCategory.Connectors,
            "prepositions of time and place (in, on, at, during, for, since, by, until)",
            ["gioi tu", "in on at", "preposition", "during", "until"]),
        new("dependent-prepositions", "Verb / adjective + preposition", "Động từ / tính từ + giới từ", GrammarCategory.Connectors,
            "fixed prepositions after verbs, adjectives and nouns (depend on, interested in, good at, the reason for)",
            ["gioi tu di kem", "depend on", "interested in", "good at"]),
        new("linking-words", "Linking words (although, despite, because of)", "Từ nối", GrammarCategory.Connectors,
            "linking words with the right structure: although/even though + clause, despite/in spite of + noun or V-ing, because/because of, however, therefore",
            ["tu noi", "although", "despite", "because of", "however", "therefore", "connector"]),

        // Words
        new("word-form", "Word forms (noun, verb, adjective, adverb)", "Dạng từ", GrammarCategory.Words,
            "the right word form from the same word family (decide/decision/decisive/decisively) in each position of the sentence",
            ["dang tu", "word family", "noun verb adjective adverb", "suffix"]),
        new("adjective-adverb", "Adjectives vs adverbs", "Tính từ và trạng từ", GrammarCategory.Words,
            "adjectives to describe nouns and after linking verbs, adverbs (-ly) to describe verbs; -ed vs -ing adjectives",
            ["tinh tu", "trang tu", "ly", "bored boring", "ed ing"]),
        new("word-order", "Word order", "Trật tự từ", GrammarCategory.Words,
            "English word order: adverbs of frequency, adjective order, time and place at the end, and subject-verb-object",
            ["trat tu tu", "adverb position", "order of adjectives"]),
        new("collocations", "Collocations", "Cụm từ cố định", GrammarCategory.Words,
            "natural collocations where a word-for-word translation from Vietnamese is wrong (make a decision, do research, heavy traffic)",
            ["collocation", "cum tu co dinh", "make do", "word choice"])
    ];

    private static readonly Dictionary<string, GrammarTopic> ByKey = All.ToDictionary(topic => topic.Key, StringComparer.Ordinal);
    // English names, whole and without their "(…)" part, since Find cuts the input at "(" too.
    private static readonly Dictionary<string, GrammarTopic> ByFoldedName = All
        .SelectMany(topic => new[] { (Name: Fold(topic.Name), Topic: topic), (Name: Fold(topic.Name.Split('(')[0]), Topic: topic) })
        .Where(item => item.Name.Length > 0)
        .GroupBy(item => item.Name)
        .ToDictionary(group => group.Key, group => group.First().Topic, StringComparer.Ordinal);
    private static readonly Dictionary<string, int> Order = All.Select((topic, index) => (topic.Key, index)).ToDictionary(item => item.Key, item => item.index, StringComparer.Ordinal);

    public static bool IsCustomKey(string? key) => key is not null && key.StartsWith(CustomKeyPrefix, StringComparison.Ordinal);

    /// <summary>
    /// A built-in topic by key, or null. Tolerates what the AI sometimes sends back instead of the bare key: the
    /// prompt's "key (Name)" format, quotes, another case, spaces or underscores, or the English name.
    /// </summary>
    public static GrammarTopic? Find(string? key)
    {
        if (string.IsNullOrWhiteSpace(key))
            return null;

        var trimmed = key.Trim();
        if (ByKey.TryGetValue(trimmed, out var topic))
            return topic;
        if (IsCustomKey(trimmed))
            return null;

        var head = trimmed.Split('(')[0].Trim().Trim('"', '\'', '`').Trim();
        var normalized = head.ToLowerInvariant().Replace('_', '-').Replace(' ', '-');
        if (ByKey.TryGetValue(normalized, out topic))
            return topic;
        return ByFoldedName.GetValueOrDefault(Fold(head));
    }

    /// <summary>A topic for something the learner searched for that is not built in, e.g. "used to".</summary>
    public static GrammarTopic? Custom(string? query)
    {
        var name = string.Join(' ', (query ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (name.Length == 0 || name.Length > MaxCustomLength)
            return null;
        return new GrammarTopic(CustomKeyPrefix + Fold(name), name, name, GrammarCategory.Custom, name, []);
    }

    /// <summary>A built-in topic, or the custom topic a custom key stands for.</summary>
    public static GrammarTopic? Resolve(string? key) =>
        Find(key) ?? (IsCustomKey(key) ? Custom(key![CustomKeyPrefix.Length..]) : null);

    /// <summary>Built-in topics matching the query in English or Vietnamese, with or without diacritics, best first.</summary>
    public static IReadOnlyList<GrammarTopic> Search(string? query)
    {
        var folded = Fold(query);
        if (folded.Length == 0)
            return All;

        var words = folded.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return All
            .Select(topic => (Topic: topic, Score: Score(topic, folded, words)))
            .Where(match => match.Score > 0)
            .OrderByDescending(match => match.Score)
            .ThenBy(match => Order[match.Topic.Key])
            .Select(match => match.Topic)
            .ToList();
    }

    private static int Score(GrammarTopic topic, string query, string[] words)
    {
        var name = Fold(topic.Name);
        var vietnamese = Fold(topic.VietnameseName);
        var keywords = topic.Keywords.Select(Fold).ToList();
        if (name == query || vietnamese == query || vietnamese == "thi " + query || keywords.Contains(query))
            return 100;
        if (name.StartsWith(query, StringComparison.Ordinal) || vietnamese.StartsWith(query, StringComparison.Ordinal))
            return 80;
        if (name.Contains(query, StringComparison.Ordinal) || vietnamese.Contains(query, StringComparison.Ordinal))
            return 60;
        if (keywords.Any(keyword => keyword.Contains(query, StringComparison.Ordinal)))
            return 50;

        var haystack = string.Join(' ', new[] { name, vietnamese }.Concat(keywords));
        return words.All(word => haystack.Contains(word, StringComparison.Ordinal)) ? 30 : 0;
    }

    /// <summary>Lowercase, without Vietnamese diacritics and with single spaces, for matching ("Hiện tại đơn" → "hien tai don").</summary>
    public static string Fold(string? text)
    {
        var decomposed = (text ?? string.Empty).Replace('đ', 'd').Replace('Đ', 'D').Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        foreach (var c in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
                continue;
            builder.Append(char.IsLetterOrDigit(c) ? char.ToLowerInvariant(c) : ' ');
        }

        return string.Join(' ', builder.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    public static string CategoryLabel(GrammarCategory category) => category switch
    {
        GrammarCategory.Tenses => "Tenses · Thì",
        GrammarCategory.Verbs => "Verbs · Động từ",
        GrammarCategory.Sentences => "Sentence structures · Cấu trúc câu",
        GrammarCategory.Nouns => "Nouns & articles · Danh từ, mạo từ",
        GrammarCategory.Connectors => "Prepositions & linking words · Giới từ, từ nối",
        GrammarCategory.Words => "Word form & choice · Dạng từ, dùng từ",
        _ => "Your searches"
    };

    /// <summary>Keys and names for the AI check prompt, e.g. "present-simple (Present simple), …".</summary>
    public static string PromptList { get; } = string.Join(", ", All.Select(topic => $"{topic.Key} ({topic.Name})"));
}
