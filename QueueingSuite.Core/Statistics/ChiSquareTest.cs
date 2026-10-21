using System.Globalization;
using MathNet.Numerics.Distributions;

namespace QueueingSuite.Core.Statistics;

/// <summary>One row of the chi-square table.</summary>
public sealed record ChiSquareBin(string Label, double Observed, double Expected)
{
    /// <summary>(O-E)^2 / E</summary>
    public double Contribution => Expected > 0 ? (Observed - Expected) * (Observed - Expected) / Expected : 0;
}

/// <summary>Complete outcome of a chi-square goodness-of-fit test.</summary>
public sealed class ChiSquareResult
{
    public required string Title { get; init; }
    public required string Hypothesis { get; init; }
    public required IReadOnlyList<ChiSquareBin> Bins { get; init; }
    /// <summary>Sum of (O-E)^2/E.</summary>
    public required double Statistic { get; init; }
    /// <summary>Degrees of freedom = k - 1 - (parameters estimated from the data).</summary>
    public required int DegreesOfFreedom { get; init; }
    public required int ParametersEstimated { get; init; }
    public required double Alpha { get; init; }
    /// <summary>Critical value chi2(1-alpha, df); NaN when the test is not valid.</summary>
    public required double Critical { get; init; }
    /// <summary>P(chi2_df &gt;= statistic); NaN when the test is not valid.</summary>
    public required double PValue { get; init; }
    /// <summary>True when there were enough bins for df &gt;= 1.</summary>
    public required bool IsValid { get; init; }
    public bool Reject => IsValid && Statistic > Critical;
    public required double SampleSize { get; init; }
    public required string Verdict { get; init; }
    public IReadOnlyList<string> Notes { get; init; } = Array.Empty<string>();
}

/// <summary>
/// Pearson chi-square goodness-of-fit. Statistic = sum (O-E)^2/E; df = k - 1 - m (m = parameters estimated from the data);
/// critical value and p-value from the chi-square distribution (MathNet regularised incomplete gamma).
/// Rule of thumb enforced: every cell keeps an expected count of at least <see cref="MinExpected"/> (small cells are merged).
/// </summary>
public static class ChiSquareTest
{
    public const double MinExpected = 5.0;

    // ---------------------------------------------------------------- continuous samples
    /// <summary>
    /// Tests a continuous sample against a hypothesised CDF with EQUAL-PROBABILITY bins:
    /// the k-1 inner edges are the quantiles F^-1(i/k), so every bin has expected count n/k.
    /// k defaults to floor(sqrt(n)) and is capped so that n/k &gt;= 5.
    /// </summary>
    /// <param name="paramsEstimated">Number of distribution parameters fitted from this same sample (0 if fully specified).</param>
    public static ChiSquareResult ContinuousGoodnessOfFit(
        IReadOnlyList<double> sample, Func<double, double> cdf, int paramsEstimated,
        string title, string hypothesis, int? bins = null, double alpha = 0.05)
    {
        int n = sample.Count;
        var notes = new List<string>();
        if (n < 10) return Invalid(title, hypothesis, alpha, paramsEstimated, n, $"Sample too small (n = {n}); need at least 10 observations.");

        int kMax = (int)Math.Floor(n / MinExpected);
        int k = bins ?? (int)Math.Floor(Math.Sqrt(n));
        if (k > kMax) { k = kMax; notes.Add($"Number of bins reduced to {k} so that every expected count is at least {MinExpected}."); }
        if (k < 2) return Invalid(title, hypothesis, alpha, paramsEstimated, n, "Not enough observations for two bins with expected count >= 5.");

        var edges = new double[k - 1];
        for (int i = 1; i < k; i++) edges[i - 1] = Quantile(cdf, (double)i / k);

        var observed = new int[k];
        foreach (var x in sample)
        {
            int idx = Array.BinarySearch(edges, x);
            if (idx < 0) idx = ~idx;           // first edge >= x  ->  bin index
            observed[Math.Min(idx, k - 1)]++;
        }

        double expected = (double)n / k;
        var rows = new List<ChiSquareBin>();
        for (int i = 0; i < k; i++)
        {
            string lo = i == 0 ? "0" : edges[i - 1].ToString("0.####", CultureInfo.InvariantCulture);
            string hi = i == k - 1 ? "∞" : edges[i].ToString("0.####", CultureInfo.InvariantCulture);
            rows.Add(new ChiSquareBin($"({lo}, {hi}]", observed[i], expected));
        }
        notes.Add($"{k} equal-probability bins, expected count per bin = n/k = {expected:0.##}.");
        return Finish(title, hypothesis, rows, paramsEstimated, alpha, n, notes);
    }

    // ---------------------------------------------------------------- integer-valued (rounded) samples
    /// <summary>
    /// For data recorded in whole units (e.g. whole minutes): an observation v stands for a true value in [v-0.5, v+0.5),
    /// so P(v) = F(v+0.5) - F(max(v-0.5, 0)). Categories 0,1,2,... are used and small-expectation categories are merged.
    /// </summary>
    public static ChiSquareResult RoundedDataGoodnessOfFit(
        IReadOnlyList<double> sample, Func<double, double> cdf, int paramsEstimated,
        string title, string hypothesis, double alpha = 0.05)
    {
        int n = sample.Count;
        if (n < 10) return Invalid(title, hypothesis, alpha, paramsEstimated, n, $"Sample too small (n = {n}); need at least 10 observations.");
        int max = (int)Math.Round(sample.Max());
        var counts = new double[max + 1];
        foreach (var x in sample) counts[(int)Math.Round(x)]++;

        var cells = new List<(string Label, double Observed, double Prob)>();
        for (int v = 0; v <= max; v++)
        {
            double lo = Math.Max(v - 0.5, 0), hi = v + 0.5;
            double p = v == max ? 1 - cdf(lo) : cdf(hi) - cdf(lo);   // last cell absorbs the whole tail
            cells.Add((v.ToString(CultureInfo.InvariantCulture), counts[v], p));
        }
        var notes = new List<string> { "Data are whole numbers, so each value is treated as the interval [v-0.5, v+0.5) of the fitted CDF (rounding correction)." };
        return DiscreteGoodnessOfFit(cells, n, paramsEstimated, title, hypothesis, alpha, notes);
    }

    // ---------------------------------------------------------------- discrete cells
    /// <summary>
    /// Observed counts versus cell probabilities. Cells are merged left-to-right until the expected count reaches 5;
    /// a trailing cell that is still too small is merged into the previous group.
    /// </summary>
    public static ChiSquareResult DiscreteGoodnessOfFit(
        IReadOnlyList<(string Label, double Observed, double Prob)> cells, double n, int paramsEstimated,
        string title, string hypothesis, double alpha = 0.05, IList<string>? notes = null)
    {
        var allNotes = notes?.ToList() ?? new List<string>();
        double totalP = cells.Sum(c => c.Prob);
        // normalise tiny numerical drift so expected counts sum to n
        var groups = new List<(string First, string Last, double O, double E)>();
        double accO = 0, accE = 0; string? first = null, last = null;
        foreach (var cell in cells)
        {
            first ??= cell.Label; last = cell.Label;
            accO += cell.Observed; accE += n * cell.Prob / totalP;
            if (accE >= MinExpected)
            {
                groups.Add((first, last, accO, accE));
                accO = 0; accE = 0; first = null;
            }
        }
        if (first is not null)
        {
            if (groups.Count > 0)
            {
                var g = groups[^1];
                groups[^1] = (g.First, last!, g.O + accO, g.E + accE);
            }
            else groups.Add((first, last!, accO, accE));
        }

        var rows = groups.Select(g =>
            new ChiSquareBin(g.First == g.Last ? g.First : $"{g.First}–{g.Last}", g.O, g.E)).ToList();
        if (cells.Count != rows.Count) allNotes.Add($"{cells.Count} categories merged into {rows.Count} so every expected count is at least {MinExpected}.");
        return Finish(title, hypothesis, rows, paramsEstimated, alpha, n, allNotes);
    }

    // ---------------------------------------------------------------- shared
    private static ChiSquareResult Finish(string title, string hypothesis, List<ChiSquareBin> rows, int m, double alpha, double n, IList<string> notes)
    {
        int k = rows.Count;
        int df = k - 1 - m;
        double stat = rows.Sum(r => r.Contribution);
        if (df < 1)
            return Invalid(title, hypothesis, alpha, m, n,
                $"Only {k} usable bin(s) after merging with {m} estimated parameter(s) gives df = {df} < 1: the test cannot be carried out (sample too small).",
                rows, stat);

        double critical = ChiSquared.InvCDF(df, 1 - alpha);
        double p = 1 - ChiSquared.CDF(df, stat);
        bool reject = stat > critical;
        string verdict = reject
            ? $"Reject H0 (χ² = {stat:0.###} > critical {critical:0.###}, p = {FormatP(p)} < α = {alpha}). The data do NOT follow the hypothesised distribution."
            : $"Fail to reject H0 (χ² = {stat:0.###} ≤ critical {critical:0.###}, p = {FormatP(p)} ≥ α = {alpha}). No evidence against the hypothesised distribution.";
        return new ChiSquareResult
        {
            Title = title, Hypothesis = hypothesis, Bins = rows, Statistic = stat, DegreesOfFreedom = df,
            ParametersEstimated = m, Alpha = alpha, Critical = critical, PValue = p, IsValid = true,
            SampleSize = n, Verdict = verdict, Notes = notes.ToList()
        };
    }

    /// <summary>Result for a test that cannot be carried out (explains why instead of throwing).</summary>
    public static ChiSquareResult NotPossible(string title, string hypothesis, double alpha, string why) =>
        Invalid(title, hypothesis, alpha, 0, 0, why);

    private static ChiSquareResult Invalid(string title, string hypothesis, double alpha, int m, double n, string why,
        IReadOnlyList<ChiSquareBin>? rows = null, double stat = 0) =>
        new()
        {
            Title = title, Hypothesis = hypothesis, Bins = rows ?? Array.Empty<ChiSquareBin>(), Statistic = stat,
            DegreesOfFreedom = (rows?.Count ?? 0) - 1 - m, ParametersEstimated = m, Alpha = alpha,
            Critical = double.NaN, PValue = double.NaN, IsValid = false, SampleSize = n,
            Verdict = "Test not possible: " + why
        };

    private static string FormatP(double p) => p < 0.0001 ? "<0.0001" : p.ToString("0.####", CultureInfo.InvariantCulture);

    /// <summary>Inverse CDF by bracketing + bisection (works for any continuous CDF on [0, inf)).</summary>
    public static double Quantile(Func<double, double> cdf, double p)
    {
        double lo = 0, hi = 1;
        while (cdf(hi) < p && hi < 1e12) hi *= 2;
        for (int i = 0; i < 200; i++)
        {
            double mid = 0.5 * (lo + hi);
            if (cdf(mid) < p) lo = mid; else hi = mid;
        }
        return 0.5 * (lo + hi);
    }
}
