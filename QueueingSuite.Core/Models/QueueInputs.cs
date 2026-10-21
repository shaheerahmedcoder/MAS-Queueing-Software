using QueueingSuite.Core.Data;
using QueueingSuite.Core.Simulation;

namespace QueueingSuite.Core.Models;

/// <summary>Which results the dashboard produces: formulas, simulation, or both side by side.</summary>
public enum RunMode { Analytical, Simulation, Compare }

/// <summary>
/// Everything typed into the sidebar. The UI only fills this record in; all interpretation lives in
/// <see cref="QueueRunner"/> and <see cref="InputValidator"/>.
/// </summary>
public sealed record QueueInputs
{
    public ModelKind Model { get; init; } = ModelKind.MM1;
    /// <summary>Arrival rate lambda (mean inter-arrival time is 1/lambda).</summary>
    public double Lambda { get; init; } = 4;
    /// <summary>Service rate mu (mean service time is 1/mu).</summary>
    public double Mu { get; init; } = 6;
    /// <summary>Number of servers (only used by M/M/c; the other models have exactly one).</summary>
    public int Servers { get; init; } = 2;

    /// <summary>G/G/1 only: inter-arrival distribution and its coefficient of variation.</summary>
    public DistributionKind ArrivalKind { get; init; } = DistributionKind.Exponential;
    public double ArrivalCv { get; init; } = 1;
    /// <summary>M/G/1 and G/G/1: service distribution and its coefficient of variation.</summary>
    public DistributionKind ServiceKind { get; init; } = DistributionKind.Exponential;
    public double ServiceCv { get; init; } = 1;

    public int Customers { get; init; } = 10_000;
    public int Warmup { get; init; } = 0;
    /// <summary>null = random (the seed used is reported back).</summary>
    public int? Seed { get; init; }
    public int Replications { get; init; } = 1;

    /// <summary>Non-null = "Use KFC data": lambda, mu, Ca^2, Cs^2 come from this fitted session and the simulation is a trace replay.</summary>
    public SessionFit? Session { get; init; }
    /// <summary>Observed inter-arrival gaps / service times for the Empirical distribution option.</summary>
    public IReadOnlyList<double>? EmpiricalInterArrivals { get; init; }
    public IReadOnlyList<double>? EmpiricalServices { get; init; }

    public int EffectiveServers => Model == ModelKind.MMc ? Servers : 1;
    public bool UsesKfcData => Session is not null;
}

/// <summary>Model parameters after interpreting the inputs: the actual distributions and the moments the formulas use.</summary>
public sealed class ResolvedModel
{
    public required IDistribution Arrival { get; init; }
    public required IDistribution Service { get; init; }
    /// <summary>lambda actually used (1 / mean inter-arrival).</summary>
    public required double Lambda { get; init; }
    public required double MeanService { get; init; }
    public double Mu => 1 / MeanService;
    public required double VarService { get; init; }
    public required double Ca2 { get; init; }
    public required double Cs2 { get; init; }
    public required int Servers { get; init; }
    /// <summary>rho = lambda * E[S] / c.</summary>
    public double Rho => Lambda * MeanService / Servers;
}

/// <summary>One line of the analytical-vs-simulated table.</summary>
public sealed record ComparisonRow(string Metric, double? Analytical, double? Simulated, double? SimHalfWidth, double? PercentError);

public sealed class ComparisonResult
{
    public required IReadOnlyList<ComparisonRow> Rows { get; init; }
    public ComparisonRow? Row(string metric) => Rows.FirstOrDefault(r => r.Metric == metric);
}
