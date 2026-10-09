namespace PteFloatingSentence.Core;

public static class WritingSkillTags
{
    /// <summary>Values the AI may return for an issue's tag, in prompt order.</summary>
    public const string PromptValues =
        "tense, article, preposition, subject_verb_agreement, plural, word_form, word_choice, word_order, sentence_structure, spelling, missing_word, extra_word or other";

    public static string Label(WritingSkillTag tag) => tag switch
    {
        WritingSkillTag.Tense => "Tense",
        WritingSkillTag.Article => "Articles",
        WritingSkillTag.Preposition => "Prepositions",
        WritingSkillTag.SubjectVerbAgreement => "Subject–verb agreement",
        WritingSkillTag.Plural => "Singular / plural",
        WritingSkillTag.WordForm => "Word form",
        WritingSkillTag.WordChoice => "Word choice",
        WritingSkillTag.WordOrder => "Word order",
        WritingSkillTag.Spelling => "Spelling",
        WritingSkillTag.MissingWord => "Missing words",
        WritingSkillTag.ExtraWord => "Extra words",
        WritingSkillTag.SentenceStructure => "Sentence structure",
        _ => "Other"
    };

    /// <summary>
    /// True when a sentence can be written to make the learner practice this tag. Spelling, missing/extra words
    /// and "other" are tracked but cannot be targeted.
    /// </summary>
    public static bool IsTargetable(WritingSkillTag tag) => tag is
        WritingSkillTag.Tense or WritingSkillTag.Article or WritingSkillTag.Preposition or
        WritingSkillTag.SubjectVerbAgreement or WritingSkillTag.Plural or WritingSkillTag.WordForm or
        WritingSkillTag.WordChoice or WritingSkillTag.WordOrder or WritingSkillTag.SentenceStructure;

    /// <summary>What the generated sentence must make the learner use, for the AI prompt.</summary>
    public static string FocusDescription(WritingSkillTag tag) => tag switch
    {
        WritingSkillTag.Tense => "a verb tense that is easy to get wrong (present perfect vs past simple, past continuous, future or passive)",
        WritingSkillTag.Article => "articles a/an/the, including a noun that needs no article",
        WritingSkillTag.Preposition => "prepositions of time, place or a fixed verb + preposition collocation",
        WritingSkillTag.SubjectVerbAgreement => "subject-verb agreement with a third-person singular or a tricky subject (everyone, the number of, each)",
        WritingSkillTag.Plural => "countable and uncountable nouns and plural forms",
        WritingSkillTag.WordForm => "the right word form (noun, verb, adjective or adverb of the same root)",
        WritingSkillTag.WordChoice => "a common collocation where a word-for-word translation sounds wrong",
        WritingSkillTag.WordOrder => "English word order (adverb position, adjective order or an indirect question)",
        WritingSkillTag.SentenceStructure => "a complex sentence structure (a relative clause, a connector such as although/despite/unless, a conditional or a participle clause)",
        _ => Label(tag)
    };

    /// <summary>Tag used when the AI gives none, derived from the broad issue type.</summary>
    public static WritingSkillTag DefaultFor(WritingIssueType type) => type switch
    {
        WritingIssueType.Spelling => WritingSkillTag.Spelling,
        WritingIssueType.WordOrder => WritingSkillTag.WordOrder,
        WritingIssueType.MissingMeaning => WritingSkillTag.MissingWord,
        WritingIssueType.ExtraMeaning => WritingSkillTag.ExtraWord,
        WritingIssueType.Vocabulary => WritingSkillTag.WordChoice,
        _ => WritingSkillTag.Other
    };

    /// <summary>Maps an AI tag such as "subject_verb_agreement", "Articles" or "wordForm"; unknown values fall back to the issue type.</summary>
    public static WritingSkillTag Parse(string? value, WritingIssueType type)
    {
        var key = new string((value ?? string.Empty).Where(char.IsLetter).ToArray()).ToLowerInvariant();
        return key switch
        {
            "tense" or "tenses" or "verbtense" => WritingSkillTag.Tense,
            "article" or "articles" or "determiner" => WritingSkillTag.Article,
            "preposition" or "prepositions" => WritingSkillTag.Preposition,
            "subjectverbagreement" or "agreement" or "sva" => WritingSkillTag.SubjectVerbAgreement,
            "plural" or "plurals" or "singularplural" or "countable" or "number" => WritingSkillTag.Plural,
            "wordform" or "partofspeech" => WritingSkillTag.WordForm,
            "wordchoice" or "vocabulary" or "collocation" => WritingSkillTag.WordChoice,
            "wordorder" => WritingSkillTag.WordOrder,
            "sentencestructure" or "structure" or "clause" or "clauses" or "connector" or "conjunction" or "relativeclause" => WritingSkillTag.SentenceStructure,
            "spelling" => WritingSkillTag.Spelling,
            "missingword" or "missingwords" or "missing" => WritingSkillTag.MissingWord,
            "extraword" or "extrawords" or "extra" => WritingSkillTag.ExtraWord,
            "other" => type == WritingIssueType.Grammar ? WritingSkillTag.Other : DefaultFor(type),
            _ => DefaultFor(type)
        };
    }
}
