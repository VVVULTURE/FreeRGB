namespace FreeRGB.Core;

/// <summary>
/// Finds how many LEDs are really connected to an addressable header. The hardware cannot report this, so we light ONE LED at a
/// time and ask the user whether they can see it: LED k is visible exactly when k &lt; count, so a binary search needs
/// about log2(max) answers (7 for 100 LEDs).
/// </summary>
public sealed class LedCountSearch
{
    private int _lo;
    private int _hi;

    public LedCountSearch(int max)
    {
        if (max < 0) throw new ArgumentOutOfRangeException(nameof(max));
        _hi = max;
    }

    public bool Done => _lo >= _hi;
    /// <summary>The LED count once <see cref="Done"/>.</summary>
    public int Result => _lo;
    /// <summary>Index of the single LED to light next.</summary>
    public int NextProbe => (_lo + _hi) / 2;
    /// <summary>Upper bound of questions still needed.</summary>
    public int QuestionsLeft => Done ? 0 : (int)Math.Ceiling(Math.Log2(_hi - _lo + 1));

    /// <summary>Records whether the user saw the LED at <see cref="NextProbe"/>.</summary>
    public void Answer(bool visible)
    {
        if (Done) return;
        var k = NextProbe;
        if (visible) _lo = k + 1; else _hi = k;
    }
}
