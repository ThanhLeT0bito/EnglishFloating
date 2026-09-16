namespace PteFloatingSentence.Core;

public readonly record struct DisplayBounds(double Left, double Top, double Right, double Bottom)
{
    public bool Contains(double x, double y) => x >= Left && x < Right && y >= Top && y < Bottom;
}
