namespace PteFloatingSentence.Core;

public static class PracticeAudioDelay
{
    public const int MinSeconds = 1;
    public const int MaxSeconds = 5;
    public const int DefaultSeconds = 3;

    public static int Normalize(int seconds) =>
        seconds is >= MinSeconds and <= MaxSeconds ? seconds : DefaultSeconds;
}
