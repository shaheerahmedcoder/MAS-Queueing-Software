using QueueingSuite.Core.Models;
using QueueingSuite.Core.Statistics;

namespace QueueingSuite.Core.Simulation;

/// <summary>Mean and 95% CI of each headline metric across independent replications.</summary>
public sealed class ReplicationSummary
{
    public required int Replications { get; init; }
    public required int BaseSeed { get; init; }
    public required IReadOnlyList<SimulationResult> Runs { get; init; }
    public required ConfidenceInterval Utilization { get; init; }
    public required ConfidenceInterval L { get; init; }
    public required ConfidenceInterval Lq { get; init; }
    public required ConfidenceInterval W { get; init; }
    public required ConfidenceInterval Wq { get; init; }
    public required ConfidenceInterval PWait { get; init; }
    public required ConfidenceInterval P0 { get; init; }
    public required ConfidenceInterval CustomersServed { get; init; }

    /// <summary>The first replication (used for the trace table, queue animation and charts).</summary>
    public SimulationResult First => Runs[0];

    /// <summary>Builds the mean / 95% CI summary from already-finished runs.</summary>
    public static ReplicationSummary FromRuns(int baseSeed, IReadOnlyList<SimulationResult> runs)
    {
        ConfidenceInterval Ci(Func<SimulationResult, double> f) => ConfidenceInterval.Compute(runs.Select(f).ToList());
        return new ReplicationSummary
        {
            Replications = runs.Count, BaseSeed = baseSeed, Runs = runs,
            Utilization = Ci(x => x.Utilization), L = Ci(x => x.L), Lq = Ci(x => x.Lq),
            W = Ci(x => x.W), Wq = Ci(x => x.Wq), PWait = Ci(x => x.PWait), P0 = Ci(x => x.P0),
            CustomersServed = Ci(x => x.CustomersServed)
        };
    }
}

/// <summary>
/// Runs independent replications. Replication 0 uses the base seed exactly (so a single run equals
/// <see cref="SimulationEngine.Run"/> with that seed); replication i uses Seeds.Mix(baseSeed, 100 + i).
/// </summary>
public static class ReplicationRunner
{
    public static ReplicationSummary Run(SimulationConfig cfg, int replications, IProgress<double>? progress = null, CancellationToken ct = default)
    {
        if (replications < 1 || replications > 1000) throw new ArgumentException("Replications must be between 1 and 1000.");
        int baseSeed = cfg.Seed ?? Random.Shared.Next(1, int.MaxValue);
        var runs = new List<SimulationResult>(replications);

        for (int r = 0; r < replications; r++)
        {
            ct.ThrowIfCancellationRequested();
            int seed = r == 0 ? baseSeed : Seeds.Mix(baseSeed, 100 + r);
            int rep = r;   // copy: the closure must not see the loop variable change
            var sub = progress is null ? null : new Progress<double>(p => progress.Report((rep + p) / replications));
            // Only the first replication keeps its trace / event log (animation, table, charts); the rest keep summary statistics.
            var runCfg = cfg with { Seed = seed, LogEvents = cfg.LogEvents && r == 0, KeepTrace = cfg.KeepTrace && r == 0 };
            runs.Add(SimulationEngine.Run(runCfg, sub, ct));
        }

        return ReplicationSummary.FromRuns(baseSeed, runs);
    }
}
