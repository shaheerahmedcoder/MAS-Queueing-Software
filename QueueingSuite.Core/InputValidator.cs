using QueueingSuite.Core.Models;
using QueueingSuite.Core.Simulation;
using static QueueingSuite.Core.Models.Fmt;

namespace QueueingSuite.Core;

public sealed class ValidationResult
{
    public List<string> Errors { get; } = new();
    public List<string> Warnings { get; } = new();
    public bool IsValid => Errors.Count == 0;
}

/// <summary>Checks every input and returns specific, human-readable messages (never throws).</summary>
public static class InputValidator
{
    public const int MaxCustomers = 2_000_000;

    public static ValidationResult Validate(QueueInputs i)
    {
        var v = new ValidationResult();
        bool kfc = i.Session is not null;

        if (!kfc)
        {
            bool lambdaFromData = i.Model == ModelKind.GG1 && i.ArrivalKind == DistributionKind.Empirical;
            bool muFromData = i.Model is ModelKind.MG1 or ModelKind.GG1 && i.ServiceKind == DistributionKind.Empirical;
            if (!lambdaFromData) Positive(v, i.Lambda, "Arrival rate λ");
            if (!muFromData) Positive(v, i.Mu, "Service rate μ");
            if (i.Model == ModelKind.MMc && (i.Servers < 1 || i.Servers > MMcCalculator_MaxServers))
                v.Errors.Add($"Number of servers c must be a whole number between 1 and {MMcCalculator_MaxServers}.");

            if (i.Model is ModelKind.MG1 or ModelKind.GG1) CheckDistribution(v, i.ServiceKind, i.ServiceCv, "Service", i.EmpiricalServices, i.Mu);
            if (i.Model == ModelKind.GG1) CheckDistribution(v, i.ArrivalKind, i.ArrivalCv, "Arrival", i.EmpiricalInterArrivals, i.Lambda);
        }
        else if (!i.Session!.IsUsable)
        {
            v.Errors.Add($"{i.Session.Name} has fewer than 2 customers, so λ and μ cannot be fitted. Choose another session.");
        }

        if (i.Customers < 1) v.Errors.Add("Number of customers must be at least 1.");
        else if (i.Customers > MaxCustomers) v.Errors.Add($"Number of customers is limited to {MaxCustomers:N0}.");
        if (i.Warmup < 0) v.Errors.Add("Warm-up customers cannot be negative.");
        else if (i.Customers >= 1 && i.Warmup >= i.Customers) v.Errors.Add("Warm-up must be smaller than the number of customers.");
        if (i.Replications < 1 || i.Replications > 1000) v.Errors.Add("Replications must be between 1 and 1000.");
        if (i.Replications > 1 && (long)i.Replications * i.Customers > 50_000_000)
            v.Errors.Add("Replications × customers is too large (limit 50 million simulated customers per run).");

        if (v.IsValid)
        {
            try
            {
                var r = QueueRunner.Resolve(i);
                if (r.Rho >= 1)
                    v.Warnings.Add($"ρ = {N(r.Rho)} ≥ 1: the system is unstable. Formulas have no steady state; the simulation shows the queue growing without bound.");
                else if (r.Rho > 0.95)
                    v.Warnings.Add($"ρ = {N(r.Rho)} is very close to 1: simulation estimates converge slowly (use more customers or replications).");
                if (r.Service is TruncatedNormalDistribution tn && tn.MeanShiftFraction > 0.01)
                    v.Warnings.Add($"Normal service times are truncated at 0 (CV is large): the actual mean is {N(tn.Mean)}, not {N(tn.Mu)}. Formulas use the actual mean/variance.");
                if (r.Arrival is TruncatedNormalDistribution ta && ta.MeanShiftFraction > 0.01)
                    v.Warnings.Add($"Normal inter-arrival times are truncated at 0: the actual mean is {N(ta.Mean)}, not {N(ta.Mu)}.");
                if (i.Warmup > 0 && i.Warmup > i.Customers / 2)
                    v.Warnings.Add("Warm-up discards more than half of the customers.");
            }
            catch (ArgumentException ex)
            {
                v.Errors.Add(ex.Message);
            }
        }
        return v;
    }

    private const int MMcCalculator_MaxServers = Analytical.MMcCalculator.MaxServers;

    private static void Positive(ValidationResult v, double x, string name)
    {
        if (double.IsNaN(x)) v.Errors.Add($"{name} must be a number.");
        else if (!double.IsFinite(x)) v.Errors.Add($"{name} must be finite.");
        else if (x <= 0) v.Errors.Add($"{name} must be greater than 0 (got {N(x)}).");
    }

    private static void CheckDistribution(ValidationResult v, DistributionKind kind, double cv, string who,
        IReadOnlyList<double>? data, double rate)
    {
        if (kind == DistributionKind.Empirical)
        {
            if (data is null || data.Count < 2) v.Errors.Add($"{who} distribution 'Empirical' needs the KFC CSV loaded (Data tab).");
            return;
        }
        if (!DistributionFactory.UsesCv(kind)) return;
        if (double.IsNaN(cv) || !double.IsFinite(cv) || cv <= 0)
        {
            v.Errors.Add($"{who} CV must be greater than 0 for the {kind} distribution (use Deterministic for no variability).");
            return;
        }
        if (kind == DistributionKind.Uniform && cv > 1 / Math.Sqrt(3) + 1e-12)
            v.Errors.Add($"{who} Uniform supports CV ≤ {1 / Math.Sqrt(3):0.####} (a larger CV would need negative times).");
        if (kind == DistributionKind.Erlang && cv > 1 + 1e-12)
            v.Errors.Add($"{who} Erlang supports CV ≤ 1 (CV = 1/√k); use Gamma for CV > 1.");
    }
}
