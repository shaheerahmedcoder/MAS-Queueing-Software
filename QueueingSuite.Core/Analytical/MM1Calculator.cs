using QueueingSuite.Core.Models;
using static QueueingSuite.Core.Models.Fmt;

namespace QueueingSuite.Core.Analytical;

/// <summary>M/M/1 closed-form steady state: Poisson arrivals (lambda), exponential service (mu), one server.</summary>
public static class MM1Calculator
{
    /// <summary>
    /// rho = lambda/mu; P0 = 1-rho; L = rho/(1-rho); Lq = rho^2/(1-rho); W = 1/(mu-lambda); Wq = rho/(mu-lambda);
    /// P(n) = (1-rho) rho^n.  Requires rho &lt; 1, otherwise an unstable result (no numbers) is returned.
    /// </summary>
    public static AnalyticalResult Compute(double lambda, double mu)
    {
        Validation.Positive(lambda, nameof(lambda));
        Validation.Positive(mu, nameof(mu));
        double rho = lambda / mu;
        var workings = new List<string>
        {
            $"ρ = λ/μ = {N(lambda)}/{N(mu)} = {N(rho)}"
        };

        if (rho >= 1)
        {
            workings.Add("ρ ≥ 1  →  the queue grows without bound; no steady state, formulas do not apply.");
            return new AnalyticalResult
            {
                Model = ModelKind.MM1, Lambda = lambda, Mu = mu, Servers = 1, Rho = rho,
                IsStable = false, IsExact = true, Method = "Exact (M/M/1)",
                Message = $"Unstable: ρ = {N(rho)} ≥ 1 (λ must be smaller than μ). No steady-state values exist.",
                Workings = workings
            };
        }

        double p0 = 1 - rho;
        double l = rho / (1 - rho);
        double lq = rho * rho / (1 - rho);
        double w = 1 / (mu - lambda);
        double wq = rho / (mu - lambda);

        workings.Add($"P0 = 1 − ρ = {N(p0)}");
        workings.Add($"L  = ρ/(1−ρ) = {N(rho)}/{N(1 - rho)} = {N(l)}");
        workings.Add($"Lq = ρ²/(1−ρ) = {N(rho * rho)}/{N(1 - rho)} = {N(lq)}");
        workings.Add($"W  = 1/(μ−λ) = 1/({N(mu)}−{N(lambda)}) = {N(w)}");
        workings.Add($"Wq = ρ/(μ−λ) = {N(rho)}/{N(mu - lambda)} = {N(wq)}");
        workings.Add($"P(n) = (1−ρ)ρⁿ   (P(wait) = ρ = {N(rho)} by PASTA)");
        workings.Add($"Little's law check: λW = {N(lambda * w)} = L,  λWq = {N(lambda * wq)} = Lq");

        return new AnalyticalResult
        {
            Model = ModelKind.MM1, Lambda = lambda, Mu = mu, Servers = 1, Rho = rho,
            IsStable = true, IsExact = true, Method = "Exact (M/M/1)",
            P0 = p0, PWait = rho, L = l, Lq = lq, W = w, Wq = wq,
            StateProbability = n => n < 0 ? 0 : (1 - rho) * Math.Pow(rho, n),
            Workings = workings
        };
    }
}
