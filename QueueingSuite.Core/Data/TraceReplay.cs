using QueueingSuite.Core.Models;
using QueueingSuite.Core.Simulation;
using QueueingSuite.Core.Statistics;

namespace QueueingSuite.Core.Data;

public sealed record ArrivalService(int CustomerId, double Arrival, double Service);
public sealed record CustomerTrace(int CustomerId, double Arrival, double Start, double End, double Wait, double Service);

/// <summary>Finite-trace FCFS measures; rates use adjacent arrival gaps (same fields as the original console app).</summary>
public sealed record TraceMetrics(
    int Customers, double MeanInterarrival, double MeanService, double ArrivalRate, double ServiceRate,
    double Utilization, double Wq, double W, double Lq, double L, int MaxQueue,
    IReadOnlyList<CustomerTrace> CustomersTrace);

/// <summary>
/// Trace-driven FCFS replay of recorded data: the customers arrive at their recorded times with their recorded
/// service durations. This is the original <c>QueueSimulation.RunSingleServer</c> logic, kept unchanged.
/// </summary>
public static class TraceReplay
{
    /// <summary>
    /// start = max(arrival, time server becomes free); end = start + service; wait = start - arrival.
    /// Wq, W are plain averages over the customers; Lq = lambda*Wq and L = lambda*W (Little's law with lambda fitted
    /// from the arrival gaps); utilization = busy time / (last departure - first arrival).
    /// </summary>
    public static TraceMetrics RunSingleServer(IEnumerable<ArrivalService> input)
    {
        var arrivals = input.OrderBy(item => item.Arrival).ThenBy(item => item.CustomerId).ToList();
        if (arrivals.Count == 0) throw new ArgumentException("At least one arrival is required.", nameof(input));
        if (arrivals.Any(item => item.Arrival < 0 || item.Service < 0 || !double.IsFinite(item.Arrival) || !double.IsFinite(item.Service)))
            throw new ArgumentException("Arrival and service times must be finite and non-negative.", nameof(input));

        var trace = new List<CustomerTrace>(arrivals.Count);
        double serverFreeAt = 0, busyTime = 0;
        int maximumQueue = 0;
        var waiting = new List<(double Time, int Change)>();

        foreach (var item in arrivals)
        {
            double start = Math.Max(item.Arrival, serverFreeAt);
            double end = start + item.Service;
            double wait = start - item.Arrival;
            if (wait > 1e-12)
            {
                waiting.Add((item.Arrival, 1));
                waiting.Add((start, -1));
            }
            busyTime += item.Service;
            trace.Add(new CustomerTrace(item.CustomerId, item.Arrival, start, end, wait, item.Service));
            serverFreeAt = end;
        }

        int inQueue = 0;
        foreach (var evt in waiting.OrderBy(e => e.Time).ThenBy(e => e.Change))
        {
            inQueue += evt.Change;
            maximumQueue = Math.Max(maximumQueue, inQueue);
        }

        var gaps = Descriptive.Gaps(arrivals.Select(a => a.Arrival).ToList());
        double meanIat = Descriptive.Mean(gaps);
        double meanService = Descriptive.Mean(arrivals.Select(a => a.Service).ToList());
        double lambda = meanIat > 0 ? 1 / meanIat : 0;
        double mu = meanService > 0 ? 1 / meanService : 0;
        double elapsed = Math.Max(0, serverFreeAt - arrivals[0].Arrival);
        double utilization = elapsed > 0 ? Math.Min(1, busyTime / elapsed) : 0;
        double wq = trace.Average(t => t.Wait);
        double w = trace.Average(t => t.Wait + t.Service);

        return new TraceMetrics(arrivals.Count, meanIat, meanService, lambda, mu, utilization,
            wq, w, lambda * wq, lambda * w, maximumQueue, trace);
    }

    /// <summary>Replay of one session using the Queue-1 columns.</summary>
    public static TraceMetrics RunSingleServer(SessionFit session) =>
        RunSingleServer(session.Rows.Select(r => new ArrivalService(r.Id, r.Arrival, r.ServiceQ1)));

    /// <summary>
    /// Replays the session through the event-driven engine (arrival gaps and service times taken from the data in order,
    /// c servers, FCFS). For c = 1 this gives the same waits as <see cref="RunSingleServer(IEnumerable{ArrivalService})"/>, plus
    /// time-weighted L/Lq, an event log for the animation and per-customer rows for the table.
    /// </summary>
    public static SimulationResult ToSimulationResult(SessionFit session, int servers = 1, IProgress<double>? progress = null, CancellationToken ct = default)
    {
        var rows = session.Rows.OrderBy(r => r.Arrival).ThenBy(r => r.Id).ToList();
        var gaps = new List<double>(rows.Count);
        double prev = 0;
        foreach (var r in rows) { gaps.Add(r.Arrival - prev); prev = r.Arrival; }
        var cfg = new SimulationConfig
        {
            Model = servers == 1 ? ModelKind.GG1 : ModelKind.MMc,
            Servers = servers,
            Arrival = new SequenceDistribution(gaps, "KFC arrival gaps"),
            Service = new SequenceDistribution(rows.Select(r => r.ServiceQ1), "KFC Q1 service"),
            Customers = rows.Count,
            Warmup = 0,
            Seed = 0,
            CustomerIds = rows.Select(r => r.Id).ToList()
        };
        return SimulationEngine.Run(cfg, progress, ct);
    }
}
