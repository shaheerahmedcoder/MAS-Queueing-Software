using QueueingSuite.Core.Models;
using static QueueingSuite.Core.Models.Fmt;

namespace QueueingSuite.Core.Analytical;

/// <summary>G/G/1 approximations (Kingman; Allen-Cunneen generalisation). These are NOT exact.</summary>
public static class GG1Calculator
{
    /// <summary>
    /// Kingman's formula:  Wq ≈ (Ca^2 + Cs^2)/2 * rho/(1-rho) * E[S]  (heavy-traffic approximation, good for rho near 1).
    /// W = Wq + E[S]; Lq = lambda Wq; L = lambda W.  P0 = 1-rho is exact for any single-server queue.
    /// </summary>
    /// <param name="lambda">Arrival rate = 1/E[interarrival].</param>
    /// <param name="meanService">E[S] = 1/mu.</param>
    /// <param name="ca2">Squared coefficient of variation of inter-arrival times.</param>
    /// <param name="cs2">Squared coefficient of variation of service times.</param>
    public static AnalyticalResult Compute(double lambda, double meanService, double ca2, double cs2)
    {
        Validation.Positive(lambda, nameof(lambda));
        Validation.Positive(meanService, nameof(meanService));
        Validation.NonNegative(ca2, nameof(ca2));
        Validation.NonNegative(cs2, nameof(cs2));

        double mu = 1 / meanService;
        double rho = lambda * meanService;
        var workings = new List<string>
        {
            "G/G/1 results are an APPROXIMATION (Kingman), not exact.",
            $"ρ = λ·E[S] = {N(lambda)}·{N(meanService)} = {N(rho)};   Ca² = {N(ca2)},  Cs² = {N(cs2)}"
        };

        if (rho >= 1)
        {
            workings.Add("ρ ≥ 1  →  no steady state, Kingman's formula does not apply.");
            return new AnalyticalResult
            {
                Model = ModelKind.GG1, Lambda = lambda, Mu = mu, Servers = 1, Rho = rho,
                IsStable = false, IsExact = false, Method = "Approximation (Kingman)",
                Message = $"Unstable: ρ = {N(rho)} ≥ 1. No steady-state values exist (the trace/simulation is still shown).",
                Ca2 = ca2, Cs2 = cs2, Workings = workings
            };
        }

        double wq = (ca2 + cs2) / 2 * (rho / (1 - rho)) * meanService;
        double w = wq + meanService;
        double lq = lambda * wq;
        double l = lambda * w;

        workings.Add($"Wq ≈ (Ca²+Cs²)/2 · ρ/(1−ρ) · E[S] = ({N(ca2)}+{N(cs2)})/2 · {N(rho)}/{N(1 - rho)} · {N(meanService)} = {N(wq)}");
        workings.Add($"W  = Wq + E[S] = {N(wq)} + {N(meanService)} = {N(w)}");
        workings.Add($"Lq = λ·Wq = {N(lambda)}·{N(wq)} = {N(lq)}");
        workings.Add($"L  = λ·W = {N(lambda)}·{N(w)} = {N(l)}");
        workings.Add($"P0 = 1 − ρ = {N(1 - rho)}   (exact: fraction of time the single server is idle)");
        workings.Add("Allen-Cunneen (c = 1) gives the same Wq as Kingman: Wq ≈ (Ca²+Cs²)/2 · Wq(M/M/1).");

        return new AnalyticalResult
        {
            Model = ModelKind.GG1, Lambda = lambda, Mu = mu, Servers = 1, Rho = rho,
            IsStable = true, IsExact = false, Method = "Approximation (Kingman)",
            P0 = 1 - rho, L = l, Lq = lq, W = w, Wq = wq,
            Ca2 = ca2, Cs2 = cs2, Workings = workings
        };
    }

    /// <summary>
    /// Allen-Cunneen approximation for G/G/c:  Wq ≈ (Ca^2 + Cs^2)/2 * Wq(M/M/c)  (reduces to Kingman when c = 1).
    /// Returns null when unstable.
    /// </summary>
    public static double? AllenCunneenWq(double lambda, double meanService, double ca2, double cs2, int c)
    {
        var mmc = MMcCalculator.Compute(lambda, 1 / meanService, c);
        return mmc.IsStable ? (ca2 + cs2) / 2 * mmc.Wq : null;
    }
}
