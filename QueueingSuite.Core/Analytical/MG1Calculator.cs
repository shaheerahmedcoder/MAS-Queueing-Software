using QueueingSuite.Core.Models;
using static QueueingSuite.Core.Models.Fmt;

namespace QueueingSuite.Core.Analytical;

/// <summary>M/G/1 Pollaczek-Khinchine formulas (exact): Poisson arrivals, general service, one server.</summary>
public static class MG1Calculator
{
    /// <summary>
    /// rho = lambda E[S];  Lq = (lambda^2 Var(S) + rho^2) / (2(1-rho));  Wq = Lq/lambda;  W = Wq + E[S];  L = lambda W.
    /// Equivalent form: Wq = rho/(1-rho) * (1+Cs^2)/2 * E[S].
    /// Special cases: Cs^2 = 1 gives M/M/1; Cs^2 = 0 (M/D/1) gives half the M/M/1 queue.
    /// </summary>
    /// <param name="lambda">Arrival rate.</param>
    /// <param name="meanService">E[S] = 1/mu.</param>
    /// <param name="varService">Var(S) (use Cs^2 * E[S]^2 if you only know the squared coefficient of variation).</param>
    public static AnalyticalResult Compute(double lambda, double meanService, double varService)
    {
        Validation.Positive(lambda, nameof(lambda));
        Validation.Positive(meanService, nameof(meanService));
        Validation.NonNegative(varService, nameof(varService));

        double mu = 1 / meanService;
        double rho = lambda * meanService;
        double cs2 = varService / (meanService * meanService);
        var workings = new List<string>
        {
            $"E[S] = 1/μ = {N(meanService)},  Var(S) = {N(varService)},  Cs² = Var(S)/E[S]² = {N(cs2)}",
            $"ρ = λ·E[S] = {N(lambda)}·{N(meanService)} = {N(rho)}"
        };

        if (rho >= 1)
        {
            workings.Add("ρ ≥ 1  →  no steady state, Pollaczek-Khinchine formulas do not apply.");
            return new AnalyticalResult
            {
                Model = ModelKind.MG1, Lambda = lambda, Mu = mu, Servers = 1, Rho = rho,
                IsStable = false, IsExact = true, Method = "Exact (Pollaczek-Khinchine)",
                Message = $"Unstable: ρ = λ·E[S] = {N(rho)} ≥ 1. No steady-state values exist.",
                Cs2 = cs2, Workings = workings
            };
        }

        double lq = (lambda * lambda * varService + rho * rho) / (2 * (1 - rho));
        double wq = lq / lambda;
        double w = wq + meanService;
        double l = lambda * w;

        workings.Add($"P0 = 1 − ρ = {N(1 - rho)}");
        workings.Add($"Lq = (λ²·Var(S) + ρ²)/(2(1−ρ)) = ({N(lambda * lambda * varService)} + {N(rho * rho)})/(2·{N(1 - rho)}) = {N(lq)}");
        workings.Add($"Wq = Lq/λ = {N(lq)}/{N(lambda)} = {N(wq)}");
        workings.Add($"W  = Wq + E[S] = {N(wq)} + {N(meanService)} = {N(w)}");
        workings.Add($"L  = λ·W = {N(lambda)}·{N(w)} = {N(l)}");
        workings.Add($"Check: Wq = ρ/(1−ρ)·(1+Cs²)/2·E[S] = {N(rho / (1 - rho) * (1 + cs2) / 2 * meanService)}");

        return new AnalyticalResult
        {
            Model = ModelKind.MG1, Lambda = lambda, Mu = mu, Servers = 1, Rho = rho,
            IsStable = true, IsExact = true, Method = "Exact (Pollaczek-Khinchine)",
            P0 = 1 - rho, PWait = rho, L = l, Lq = lq, W = w, Wq = wq,
            Cs2 = cs2, Workings = workings
        };
    }
}
