namespace QueueingSuite.Core.Statistics;

/// <summary>Basic descriptive statistics used for fitting lambda, mu, Ca^2 and Cs^2 from data.</summary>
public static class Descriptive
{
    public static double Mean(IReadOnlyList<double> values) => values.Count == 0 ? 0 : values.Average();

    /// <summary>Sample variance with the n-1 denominator (matches the original console app).</summary>
    public static double SampleVariance(IReadOnlyList<double> values)
    {
        if (values.Count < 2) return 0;
        double mean = Mean(values);
        double sum = 0;
        foreach (var v in values) sum += (v - mean) * (v - mean);
        return sum / (values.Count - 1);
    }

    public static double StdDev(IReadOnlyList<double> values) => Math.Sqrt(SampleVariance(values));

    /// <summary>Squared coefficient of variation Cv^2 = Var/Mean^2 (this is Ca^2 for inter-arrival times, Cs^2 for service times).</summary>
    public static double SquaredCoefficientOfVariation(IReadOnlyList<double> values)
    {
        double mean = Mean(values);
        return mean > 0 ? SampleVariance(values) / (mean * mean) : 0;
    }

    /// <summary>Differences between consecutive sorted arrival times.</summary>
    public static List<double> Gaps(IReadOnlyList<double> sortedArrivals) =>
        sortedArrivals.Zip(sortedArrivals.Skip(1), (a, b) => b - a).ToList();
}
