using QueueingSuite.Core.Analytical;
using QueueingSuite.Core.Models;

namespace QueueingSuite.Core.Data;

/// <summary>One column of the "KFC data: all models side by side" table.</summary>
public sealed record ModelColumn(string Name, string Kind, bool IsStable, string? Note,
    double? Rho, double? P0, double? L, double? Lq, double? W, double? Wq);

/// <summary>Runs every model on the fitted values of a data session and puts the trace replay next to the formulas.</summary>
public static class KfcComparison
{
    public static IReadOnlyList<ModelColumn> Build(SessionFit s)
    {
        var cols = new List<ModelColumn>();
        var sim = TraceReplay.ToSimulationResult(s);
        var legacy = TraceReplay.RunSingleServer(s);
        cols.Add(new ModelColumn("Trace replay (time-weighted)", "Data", true,
            "FCFS replay of the recorded arrivals and service times; L and Lq are time-integrals.",
            sim.Utilization, sim.P0, sim.L, sim.Lq, sim.W, sim.Wq));
        cols.Add(new ModelColumn("Trace replay (Little)", "Data", true,
            "Same replay; Lq = λ·Wq and L = λ·W with λ from the arrival gaps (as printed by the original console app).",
            legacy.Utilization, null, legacy.L, legacy.Lq, legacy.W, legacy.Wq));

        // What the data file itself says: the recorded wait column. It can be larger than the replay because the file lists only a
        // sample of the customers (IDs are missing), and the unrecorded customers also occupied the server.
        double recordedWq = s.Rows.Count > 0 ? s.Rows.Average(r => r.ObservedWaitQ1) : 0;
        cols.Add(new ModelColumn("Recorded in data (WaitQ1)", "Data", true,
            "Mean of the recorded wait column. Larger than the replay when recorded customers are only a sample of the real queue.",
            null, null, null, null, s.Rows.Count > 0 ? recordedWq + s.MeanService : null, s.Rows.Count > 0 ? recordedWq : null));

        if (!s.IsUsable)
        {
            foreach (var n in new[] { "M/M/1", "M/G/1 (P-K)", "G/G/1 (Kingman)" })
                cols.Add(new ModelColumn(n, "—", false, "Session too small to fit λ and μ.", null, null, null, null, null, null));
            return cols;
        }

        double es = s.MeanService, varS = s.Cs2 * es * es;
        cols.Add(From("M/M/1", MM1Calculator.Compute(s.Lambda, s.Mu)));
        cols.Add(From("M/G/1 (P-K)", MG1Calculator.Compute(s.Lambda, es, varS)));
        cols.Add(From("G/G/1 (Kingman)", GG1Calculator.Compute(s.Lambda, es, s.Ca2, s.Cs2)));
        return cols;
    }

    private static ModelColumn From(string name, AnalyticalResult r) =>
        r.IsStable
            ? new ModelColumn(name, r.IsExact ? "Exact" : "Approximation", true, null, r.Rho, r.P0, r.L, r.Lq, r.W, r.Wq)
            : new ModelColumn(name, r.IsExact ? "Exact" : "Approximation", false,
                $"Unstable (ρ = {Fmt.N(r.Rho)} ≥ 1): no steady state. See the trace result.", r.Rho, null, null, null, null, null);
}
