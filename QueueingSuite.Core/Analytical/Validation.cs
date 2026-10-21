namespace QueueingSuite.Core.Analytical;

internal static class Validation
{
    public static void Positive(double value, string name)
    {
        if (!double.IsFinite(value) || value <= 0)
            throw new ArgumentException($"{name} must be a positive, finite number (got {value}).", name);
    }

    public static void NonNegative(double value, string name)
    {
        if (!double.IsFinite(value) || value < 0)
            throw new ArgumentException($"{name} must be a non-negative, finite number (got {value}).", name);
    }
}
