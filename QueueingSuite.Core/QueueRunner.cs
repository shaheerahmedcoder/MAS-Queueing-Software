using QueueingSuite.Core.Analytical;
using QueueingSuite.Core.Data;
using QueueingSuite.Core.Models;
using QueueingSuite.Core.Simulation;

namespace QueueingSuite.Core;

/// <summary>Everything produced by one press of "Run".</summary>
public sealed class RunOutput
{
    public required QueueInputs Inputs { get; init; }
    public required RunMode Mode { get; init; }
    public required ResolvedModel Resolved { get; init; }
    /// <summary>Formula result (always computed; the UI shows it according to the mode).</summary>
    public required AnalyticalResult Analytical { get; init; }
    /// <summary>Simulation (or trace replay) result; null in Analytical mode.</summary>
    public ReplicationSummary? Simulation { get; init; }
    public ComparisonResult? Comparison { get; init; }
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();
}

/// <summary>
/// The single place that turns typed inputs into model parameters, formulas and simulations.
/// Keeping this out of the form means no math lives in the UI.
/// </summary>
public static class QueueRunner
{
    /// <summary>
    /// Interprets the inputs.
    /// M/M/1, M/M/c: Exponential(lambda) arrivals, Exponential(mu) service.
    /// M/G/1: Exponential(lambda) arrivals, chosen service distribution with mean 1/mu and the given CV.
    /// G/G/1: chosen distributions for both, means 1/lambda and 1/mu.
    /// KFC mode: lambda, E[S], Ca^2, Cs^2 are the fitted values of the session.
    /// Empirical distributions use the data's own mean (the typed rate is then ignored and the UI shows the fitted one).
    /// </summary>
    public static ResolvedModel Resolve(QueueInputs i)
    {
        int c = i.EffectiveServers;
        if (i.Session is { } s)
        {
            if (!s.IsUsable) throw new ArgumentException($"{s.Name} has fewer than 2 customers or zero means, so lambda and mu cannot be fitted.");
            double mean = s.MeanService;
            double var = s.Cs2 * mean * mean;
            return new ResolvedModel
            {
                Arrival = new EmpiricalDistribution(s.InterArrivals, "KFC gaps"),
                Service = new EmpiricalDistribution(s.Services, "KFC service"),
                Lambda = s.Lambda, MeanService = mean, VarService = var, Ca2 = s.Ca2, Cs2 = s.Cs2, Servers = c
            };
        }

        IDistribution arrival, service;
        switch (i.Model)
        {
            case ModelKind.MM1:
            case ModelKind.MMc:
                arrival = new ExponentialDistribution(i.Lambda);
                service = new ExponentialDistribution(i.Mu);
                break;
            case ModelKind.MG1:
                arrival = new ExponentialDistribution(i.Lambda);
                service = DistributionFactory.Create(i.ServiceKind, 1 / i.Mu, i.ServiceCv, i.EmpiricalServices);
                break;
            case ModelKind.GG1:
                arrival = DistributionFactory.Create(i.ArrivalKind, 1 / i.Lambda, i.ArrivalCv, i.EmpiricalInterArrivals);
                service = DistributionFactory.Create(i.ServiceKind, 1 / i.Mu, i.ServiceCv, i.EmpiricalServices);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(i));
        }
        return new ResolvedModel
        {
            Arrival = arrival, Service = service,
            Lambda = 1 / arrival.Mean, MeanService = service.Mean, VarService = service.Variance,
            Ca2 = arrival.Cv2, Cs2 = service.Cv2, Servers = c
        };
    }

    /// <summary>Formula side: exact for M/M/1, M/M/c, M/G/1; Kingman approximation for G/G/1.</summary>
    public static AnalyticalResult Analyze(QueueInputs i) => Analyze(i, Resolve(i));

    public static AnalyticalResult Analyze(QueueInputs i, ResolvedModel r) => i.Model switch
    {
        ModelKind.MM1 => MM1Calculator.Compute(r.Lambda, r.Mu),
        ModelKind.MMc => MMcCalculator.Compute(r.Lambda, r.Mu, r.Servers),
        ModelKind.MG1 => MG1Calculator.Compute(r.Lambda, r.MeanService, r.VarService),
        ModelKind.GG1 => GG1Calculator.Compute(r.Lambda, r.MeanService, r.Ca2, r.Cs2),
        _ => throw new ArgumentOutOfRangeException(nameof(i))
    };

    /// <summary>Engine configuration for the typed model (not used in KFC mode, which replays the trace).</summary>
    public static SimulationConfig BuildConfig(QueueInputs i, ResolvedModel r) => new()
    {
        Model = i.Model, Servers = r.Servers, Arrival = r.Arrival, Service = r.Service,
        Customers = i.Customers, Warmup = i.Warmup, Seed = i.Seed
    };

    /// <summary>
    /// Runs the requested mode. Simulation and Compare run the event-driven engine (or the trace replay in KFC mode).
    /// Throws for invalid inputs; call <see cref="InputValidator.Validate"/> first to get friendly messages.
    /// </summary>
    public static RunOutput Run(QueueInputs i, RunMode mode, IProgress<double>? progress = null, CancellationToken ct = default)
    {
        var validation = InputValidator.Validate(i);
        if (!validation.IsValid) throw new ArgumentException(string.Join(" ", validation.Errors));

        var resolved = Resolve(i);
        var analytical = Analyze(i, resolved);
        ReplicationSummary? sim = null;
        ComparisonResult? comparison = null;

        if (mode != RunMode.Analytical)
        {
            if (i.Session is { } session)
            {
                var single = TraceReplay.ToSimulationResult(session, resolved.Servers, progress, ct);
                sim = ReplicationSummary.FromRuns(0, new[] { single });
            }
            else
            {
                sim = ReplicationRunner.Run(BuildConfig(i, resolved), i.Replications, progress, ct);
            }
            comparison = Compare(analytical, sim);
        }

        return new RunOutput
        {
            Inputs = i, Mode = mode, Resolved = resolved, Analytical = analytical,
            Simulation = sim, Comparison = comparison, Warnings = validation.Warnings
        };
    }

    /// <summary>Analytical vs simulated for the six headline measures plus P0 and P(wait); error = (sim - formula)/formula.</summary>
    public static ComparisonResult Compare(AnalyticalResult a, ReplicationSummary s)
    {
        static double? Err(double? formula, double sim) =>
            formula is { } f && Math.Abs(f) > 1e-12 ? (sim - f) / f * 100 : null;
        ComparisonRow Row(string name, double? formula, Statistics.ConfidenceInterval ci) =>
            new(name, formula, ci.Mean, ci.HasInterval ? ci.HalfWidth : null, Err(formula, ci.Mean));

        double? rho = a.IsStable ? a.Rho : null;
        return new ComparisonResult
        {
            Rows = new[]
            {
                Row("Utilization", rho, s.Utilization),
                Row("P0", a.P0, s.P0),
                Row("P(wait)", a.PWait, s.PWait),
                Row("L", a.L, s.L),
                Row("Lq", a.Lq, s.Lq),
                Row("W", a.W, s.W),
                Row("Wq", a.Wq, s.Wq)
            }
        };
    }

    /// <summary>
    /// Lq as a function of rho for the chart "Lq vs rho": lambda is varied (rho = lambda E[S]/c) while the service
    /// distribution, c and the arrival CV stay fixed. Points with rho in [0.02, maxRho].
    /// </summary>
    public static IReadOnlyList<(double Rho, double Lq)> LqVsRho(QueueInputs i, ResolvedModel r, int points = 80, double maxRho = 0.97)
    {
        var list = new List<(double, double)>();
        for (int k = 0; k < points; k++)
        {
            double rho = 0.02 + (maxRho - 0.02) * k / (points - 1);
            double lambda = rho * r.Servers / r.MeanService;
            AnalyticalResult res = i.Model switch
            {
                ModelKind.MM1 => MM1Calculator.Compute(lambda, r.Mu),
                ModelKind.MMc => MMcCalculator.Compute(lambda, r.Mu, r.Servers),
                ModelKind.MG1 => MG1Calculator.Compute(lambda, r.MeanService, r.VarService),
                _ => GG1Calculator.Compute(lambda, r.MeanService, r.Ca2, r.Cs2)
            };
            if (res.Lq is { } lq) list.Add((rho, lq));
        }
        return list;
    }
}
