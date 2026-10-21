using System.Globalization;
using QueueingSuite.Core.Models;
using static QueueingSuite.Core.Models.Fmt;

namespace QueueingSuite.Core.Simulation;

/// <summary>Text descriptions and CSV export of simulation results (no UI dependencies).</summary>
public static class SimulationReport
{
    /// <summary>Explains how each simulated number was measured, with the run's actual values substituted.</summary>
    public static IReadOnlyList<string> Workings(SimulationResult r)
    {
        double t = r.ObservedTime;
        var lines = new List<string>
        {
            $"Discrete-event simulation: {r.Servers} server(s), FCFS, seed {r.SeedUsed}, {r.TotalCustomers:N0} customers" +
                (r.Warmup > 0 ? $" ({r.Warmup:N0} warm-up discarded)." : "."),
            $"Observation window T = {N(r.EndTime)} − {N(r.StartTime)} = {N(t)}",
            $"L  = ∫N(t)dt / T = {N(r.L * t)} / {N(t)} = {N(r.L)}   (time-weighted number in system)",
            $"Lq = ∫Nq(t)dt / T = {N(r.Lq * t)} / {N(t)} = {N(r.Lq)}   (time-weighted number waiting)",
            $"Wq = mean of (start − arrival) over {r.CustomersServed:N0} customers = {N(r.Wq)}",
            $"W  = mean of (departure − arrival) = {N(r.W)}",
            $"Utilization = total busy time / (c·T) = {N(r.Utilization * r.Servers * t)} / ({r.Servers}·{N(t)}) = {N(r.Utilization)}",
            $"P0 = fraction of time empty = {N(r.P0)};  P(wait) = fraction of customers who waited = {N(r.PWait)}",
            $"Max queue length = {r.MaxQueue}"
        };
        if (r.Servers > 1)
            lines.Add("Per-server utilization: " + string.Join(", ", r.ServerUtilization.Select((u, k) => $"S{k + 1} {u:P1}")));
        return lines;
    }

    /// <summary>
    /// Writes the results and the full customer trace as one CSV file: '#' comment lines (inputs, analytical vs simulated),
    /// a blank line, then a table with one row per customer.
    /// </summary>
    public static void WriteCsv(TextWriter w, RunOutput run)
    {
        var inv = CultureInfo.InvariantCulture;
        w.WriteLine("# QUEUE LAB export");
        w.WriteLine($"# Model,{run.Inputs.Model.Label()}");
        w.WriteLine($"# Mode,{run.Mode}");
        w.WriteLine(string.Create(inv, $"# Lambda,{run.Resolved.Lambda:R}"));
        w.WriteLine(string.Create(inv, $"# Mu,{run.Resolved.Mu:R}"));
        w.WriteLine($"# Servers,{run.Resolved.Servers}");
        w.WriteLine(string.Create(inv, $"# Rho,{run.Resolved.Rho:R}"));
        w.WriteLine($"# Arrival distribution,{run.Resolved.Arrival.Name.Replace(',', ';')}");
        w.WriteLine($"# Service distribution,{run.Resolved.Service.Name.Replace(',', ';')}");
        if (run.Simulation is { } sim)
        {
            w.WriteLine($"# Seed,{sim.BaseSeed}");
            w.WriteLine($"# Customers,{sim.First.TotalCustomers}");
            w.WriteLine($"# Warmup,{sim.First.Warmup}");
            w.WriteLine($"# Replications,{sim.Replications}");
        }
        w.WriteLine($"# Analytical status,{(run.Analytical.IsStable ? run.Analytical.Method : "UNSTABLE - no steady state")}");
        if (run.Comparison is { } cmp)
        {
            w.WriteLine("# Metric,Analytical,Simulated,Percent error");
            foreach (var row in cmp.Rows)
                w.WriteLine(string.Create(inv, $"# {row.Metric},{Csv(row.Analytical)},{Csv(row.Simulated)},{Csv(row.PercentError)}"));
        }
        w.WriteLine();
        if (run.Simulation is { } s)
        {
            w.WriteLine("Customer,Arrival,ServiceStart,ServiceEnd,Wait,ServiceTime,TimeInSystem,Server,Warmup");
            foreach (var c in s.First.Customers)
                w.WriteLine(string.Create(inv, $"{c.Id},{c.Arrival:R},{c.Start:R},{c.End:R},{c.Wait:R},{c.Service:R},{c.TimeInSystem:R},{c.Server + 1},{(c.IsWarmup ? 1 : 0)}"));
        }
        else w.WriteLine("# (Analytical mode: no customer trace. Run Simulation or Compare to export one.)");
    }

    private static string Csv(double? v) => v is { } x && double.IsFinite(x) ? x.ToString("R", CultureInfo.InvariantCulture) : "";
}
