using QueueingSuite.Core.Models;
using static QueueingSuite.Core.Models.Fmt;

namespace QueueingSuite.Core.Analytical;

/// <summary>M/M/c (Erlang C): Poisson arrivals, exponential service, c identical servers, one FCFS queue.</summary>
public static class MMcCalculator
{
    public const int MaxServers = 50;

    /// <summary>
    /// a = lambda/mu (offered load); rho = a/c;
    /// P0 = [ sum_{k=0}^{c-1} a^k/k!  +  a^c / (c!(1-rho)) ]^-1;
    /// P(wait) = C(c,a) = a^c/(c!(1-rho)) * P0;  Lq = C * rho/(1-rho);  Wq = Lq/lambda;  W = Wq + 1/mu;  L = Lq + a.
    /// The terms a^k/k! are built iteratively (t_k = t_{k-1} * a/k) so no factorial overflows even for c = 50.
    /// </summary>
    public static AnalyticalResult Compute(double lambda, double mu, int c)
    {
        Validation.Positive(lambda, nameof(lambda));
        Validation.Positive(mu, nameof(mu));
        if (c < 1 || c > MaxServers)
            throw new ArgumentException($"Number of servers c must be an integer between 1 and {MaxServers} (got {c}).", nameof(c));

        double a = lambda / mu;
        double rho = a / c;
        var workings = new List<string>
        {
            $"a = λ/μ = {N(lambda)}/{N(mu)} = {N(a)}   (offered load, in servers)",
            $"ρ = a/c = {N(a)}/{c} = {N(rho)}"
        };

        if (rho >= 1)
        {
            workings.Add("ρ ≥ 1  →  arrivals exceed total capacity cμ; no steady state, formulas do not apply.");
            return new AnalyticalResult
            {
                Model = ModelKind.MMc, Lambda = lambda, Mu = mu, Servers = c, Rho = rho,
                IsStable = false, IsExact = true, Method = "Exact (Erlang C)",
                Message = $"Unstable: ρ = λ/(cμ) = {N(rho)} ≥ 1. Add servers or lower λ. No steady-state values exist.",
                Workings = workings
            };
        }

        // sum_{k=0}^{c-1} a^k/k!   built iteratively
        double term = 1.0, sum = 1.0;
        for (int k = 1; k <= c - 1; k++)
        {
            term *= a / k;
            sum += term;
        }
        double termC = term * a / c;                // a^c / c!   (for c = 1 the loop is skipped: term = 1)
        double last = termC / (1 - rho);            // a^c / (c!(1-rho))
        double p0 = 1 / (sum + last);
        double pWait = last * p0;                   // Erlang C
        double lq = pWait * rho / (1 - rho);
        double wq = lq / lambda;
        double w = wq + 1 / mu;
        double l = lq + a;

        workings.Add($"P0 = [Σ_(k=0..{c - 1}) aᵏ/k!  +  aᶜ/(c!(1−ρ))]⁻¹ = [{N(sum)} + {N(last)}]⁻¹ = {N(p0)}");
        workings.Add($"P(wait) = C(c,a) = aᶜ/(c!(1−ρ))·P0 = {N(last)}·{N(p0)} = {N(pWait)}");
        workings.Add($"Lq = C·ρ/(1−ρ) = {N(pWait)}·{N(rho)}/{N(1 - rho)} = {N(lq)}");
        workings.Add($"Wq = Lq/λ = {N(lq)}/{N(lambda)} = {N(wq)}");
        workings.Add($"W  = Wq + 1/μ = {N(wq)} + {N(1 / mu)} = {N(w)}");
        workings.Add($"L  = Lq + a = {N(lq)} + {N(a)} = {N(l)}");
        workings.Add($"Little's law check: λW = {N(lambda * w)} = L,  λWq = {N(lambda * wq)} = Lq");

        return new AnalyticalResult
        {
            Model = ModelKind.MMc, Lambda = lambda, Mu = mu, Servers = c, Rho = rho,
            IsStable = true, IsExact = true, Method = "Exact (Erlang C)",
            P0 = p0, PWait = pWait, L = l, Lq = lq, W = w, Wq = wq,
            StateProbability = n => StateProb(n, a, c, p0),
            Workings = workings
        };
    }

    /// <summary>P(n) = P0 a^n/n! for n &lt; c ; P0 a^n/(c! c^(n-c)) for n &gt;= c (computed iteratively).</summary>
    private static double StateProb(int n, double a, int c, double p0)
    {
        if (n < 0) return 0;
        double p = p0;
        for (int k = 1; k <= n; k++)
            p *= a / Math.Min(k, c);
        return p;
    }
}
