namespace PteFloatingSentence.Core;

public enum WritingLevel
{
    Easy = 0,
    Medium = 1,
    Hard = 2
}

public enum WritingExplanationLanguage
{
    English = 0,
    Vietnamese = 1
}

/// <summary>A translation topic. Built-in keys are stable slugs; custom keys start with <see cref="WritingTopics.CustomKeyPrefix"/>.</summary>
public sealed record WritingTopic(string Key, string Name);
