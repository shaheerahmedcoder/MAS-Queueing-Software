using QueueingSuite.Core.Models;
using QueueingSuite.Core.Simulation;

namespace QueueingSuite.Core.Statistics;

/// <summary>The three chi-square validations the course requires, built on <see cref="ChiSquareTest"/>.</summary>
public static class SimulationValidator
{
    /// <summary>
    /// GENERATOR VALIDITY: are the generated variates really distributed as intended?
    /// H0: the sample comes from <paramref name="intended"/> (fully specified, so 0 parameters estimated).
    /// </summary>
    public static ChiSquareResult GeneratorValidity(IReadOnlyList<double> samples, IDistribution intended, string what,
        int? bins = null, double alpha = 0.05)
    {
        string title = $"Generator validity – {what}";
        string h0 = $"H0: {what} ~ {intended.Name}";
        if (!intended.HasCdf)
            return ChiSquareTest.NotPossible(title, h0, alpha,
                $"{intended.Name} has no continuous CDF (point mass / empirical data), so a chi-square generator test does not apply.");
        return ChiSquareTest.ContinuousGoodnessOfFit(samples, intended.Cdf, 0, title, h0, bins, alpha);
    }

    /// <summary>
    /// DATA FIT: does recorded data look exponential (i.e. Poisson arrivals / exponential service)?
    /// The rate is estimated from the data itself (1/mean), so one parameter is estimated and df = k - 1 - 1.
    /// Whole-number data is tested with the rounding correction.
    /// </summary>
    public static ChiSquareResult DataFitExponential(IReadOnlyList<double> data, string what, double alpha = 0.05)
    {
        string title = $"Data fit – {what}";
        string h0 = $"H0: {what} ~ Exponential(rate estimated from data)";
        double mean = Descriptive.Mean(data);
        if (data.Count == 0 || mean <= 0)
            return ChiSquareTest.NotPossible(title, h0, alpha, "No positive data.");
        double rate = 1 / mean;
        Func<double, double> cdf = x => x <= 0 ? 0 : 1 - Math.Exp(-rate * x);
        bool whole = data.All(v => Math.Abs(v - Math.Round(v)) < 1e-6);
        var result = whole
            ? ChiSquareTest.RoundedDataGoodnessOfFit(data, cdf, 1, title, h0, alpha)
            : ChiSquareTest.ContinuousGoodnessOfFit(data, cdf, 1, title, h0, null, alpha);
        var notes = result.Notes.ToList();
        notes.Add($"Fitted rate = 1/mean = {rate:0.####} (mean = {mean:0.####}).");
        return new ChiSquareResult
        {
            Title = result.Title, Hypothesis = result.Hypothesis, Bins = result.Bins, Statistic = result.Statistic,
            DegreesOfFreedom = result.DegreesOfFreedom, ParametersEstimated = result.ParametersEstimated, Alpha = result.Alpha,
            Critical = result.Critical, PValue = result.PValue, IsValid = result.IsValid, SampleSize = result.SampleSize,
            Verdict = result.Verdict, Notes = notes
        };
    }

    /// <summary>
    /// SIMULATION vs THEORY: compares the simulated time-in-state distribution P^(n) with the analytical P(n) (M/M/1, M/M/c).
    /// Successive system states are strongly autocorrelated, so using the raw number of events as the sample size would
    /// overstate the evidence. Observed counts are therefore P^(n) times an EFFECTIVE sample size n_eff = counted customers / spacing
    /// (spacing = how many consecutive customers carry roughly one independent observation). No parameters are estimated.
    /// <para>
    /// Default spacing ("auto") is <see cref="SuggestedSpacing"/> = 0.7/(1-rho)^2: the correlation of queue states lasts longer
    /// as rho approaches 1. The constant was calibrated by simulation: with it, a TRUE M/M/1 or M/M/c model is rejected about
    /// 5% of the time at alpha = 0.05 for rho between 0.5 and 0.85 (see docs/TEST_REPORT.md).
    /// </para>
    /// </summary>
    public static ChiSquareResult StateDistribution(SimulationResult sim, AnalyticalResult theory, double? spacing = null, double alpha = 0.05)
    {
        string title = $"Simulation vs theory – P(n) for {theory.Model.Label()}";
        string h0 = "H0: simulated time-in-state distribution equals the analytical P(n)";
        if (!theory.IsStable || theory.StateProbability is null)
            return ChiSquareTest.NotPossible(title, h0, alpha, "The analytical P(n) is only defined for stable M/M/1 and M/M/c systems.");
        double sp = Math.Max(1, spacing ?? SuggestedSpacing(theory.Rho));

        double nEff = sim.CustomersServed / sp;
        var pHat = sim.StateProbabilities;
        int m = pHat.Length;                         // largest state visited is m-1
        var cells = new List<(string, double, double)>();
        double cum = 0;
        for (int n = 0; n < m - 1; n++)
        {
            double p = theory.StateProbability(n);
            cells.Add((n.ToString(), pHat[n] * nEff, p));
            cum += p;
        }
        cells.Add(($"≥{m - 1}", pHat[m - 1] * nEff, Math.Max(0, 1 - cum)));   // tail cell: all states >= largest visited

        var notes = new List<string>
        {
            $"Effective sample size n_eff = {sim.CustomersServed} customers / spacing {sp:0.##}{(spacing is null ? $" (auto = 0.7/(1−ρ)² with ρ = {theory.Rho:0.###})" : "")} = {nEff:0.#}. Queue states are autocorrelated, so the raw event count is not used as the sample size.",
            "Observed counts = simulated time fraction in state n × n_eff."
        };
        return ChiSquareTest.DiscreteGoodnessOfFit(cells, nEff, 0, title, h0, alpha, notes);
    }

    /// <summary>Number of consecutive customers that carry about one independent observation of the queue state: 0.7/(1-rho)^2 (at least 1).</summary>
    public static double SuggestedSpacing(double rho) =>
        rho >= 1 ? 1 : Math.Max(1, 0.7 / ((1 - rho) * (1 - rho)));
}
