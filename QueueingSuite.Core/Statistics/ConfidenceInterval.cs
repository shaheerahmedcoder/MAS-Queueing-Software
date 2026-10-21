using MathNet.Numerics.Distributions;

namespace QueueingSuite.Core.Statistics;

/// <summary>Mean and two-sided confidence interval of independent replication results (Student t).</summary>
public readonly record struct ConfidenceInterval(int N, double Mean, double StdDev, double HalfWidth, double Level)
{
    /// <summary>An interval needs at least two replications.</summary>
    public bool HasInterval => N >= 2 && double.IsFinite(HalfWidth);
    public double Lower => Mean - HalfWidth;
    public double Upper => Mean + HalfWidth;

    /// <summary>
    /// mean +/- t(1-alpha/2, n-1) * s / sqrt(n).  With one value only the mean is defined (HalfWidth = NaN, HasInterval = false).
    /// </summary>
    public static ConfidenceInterval Compute(IReadOnlyList<double> values, double level = 0.95)
    {
        int n = values.Count;
        if (n == 0) return new ConfidenceInterval(0, 0, 0, double.NaN, level);
        double mean = values.Average();
        if (n == 1) return new ConfidenceInterval(1, mean, 0, double.NaN, level);
        double sd = Descriptive.StdDev(values);
        double t = StudentT.InvCDF(0, 1, n - 1, 1 - (1 - level) / 2);
        return new ConfidenceInterval(n, mean, sd, t * sd / Math.Sqrt(n), level);
    }
}
