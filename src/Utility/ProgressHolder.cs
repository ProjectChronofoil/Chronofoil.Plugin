namespace Chronofoil.Utility;

public class ProgressHolder
{
    public long Current { get; set; }
    public long Total { get; set; }

    public void Set(long current, long total)
    {
        Current = current;
        Total = total;
    }

    public float GetPercent() => (float) Current / Total;
}