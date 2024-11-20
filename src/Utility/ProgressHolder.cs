namespace Chronofoil.Utility;

public class ProgressHolder
{
    private long Current { get; set; } = 0;
    private long Total { get; set; } = 1;

    public void Set(long current, long total)
    {
        Current = current;
        Total = total;
    }

    public float GetPercent() => (float) Current / Total;
}