using QueueingSuite.Core.Simulation;

namespace QueueingSuite.Core.Models;

/// <summary>Everything the discrete-event engine needs for one run.</summary>
public sealed record SimulationConfig
{
    public ModelKind Model { get; init; } = ModelKind.MM1;
    /// <summary>Number of identical servers c (1 for M/M/1, M/G/1, G/G/1).</summary>
    public int Servers { get; init; } = 1;
    /// <summary>Distribution of INTER-arrival times.</summary>
    public required IDistribution Arrival { get; init; }
    /// <summary>Distribution of service times.</summary>
    public required IDistribution Service { get; init; }
    /// <summary>Total customers generated.</summary>
    public int Customers { get; init; } = 10_000;
    /// <summary>The first <c>Warmup</c> customers are simulated but excluded from the statistics.</summary>
    public int Warmup { get; init; } = 0;
    /// <summary>Random seed; null = pick one at random (it is returned in the result so the run can be reproduced).</summary>
    public int? Seed { get; init; }
    /// <summary>Record arrival / start / departure events for the animation.</summary>
    public bool LogEvents { get; init; } = true;
    public int MaxLoggedEvents { get; init; } = 30_000;
    /// <summary>
    /// Keep the per-customer rows and queue series in the result. Replications beyond the first switch this off
    /// (only their summary statistics are needed), which keeps memory flat for many replications.
    /// </summary>
    public bool KeepTrace { get; init; } = true;
    /// <summary>Optional original customer IDs (trace replay), one per customer in arrival order.</summary>
    public IReadOnlyList<int>? CustomerIds { get; init; }
}

public enum SimEventType { Arrival, StartService, Departure }

/// <summary>One entry of the event log. <c>Server</c> is -1 for arrivals that wait. Queue/InSystem are AFTER the event.</summary>
public readonly record struct SimEvent(double Time, SimEventType Type, int CustomerId, int Server, int QueueLength, int InSystem);

/// <summary>Per-customer trace row.</summary>
public readonly record struct CustomerRecord(int Id, double Arrival, double Start, double End, int Server, bool IsWarmup)
{
    public double Wait => Start - Arrival;
    public double Service => End - Start;
    public double TimeInSystem => End - Arrival;
}

/// <summary>Output of one simulation run (or one trace replay).</summary>
public sealed class SimulationResult
{
    public required ModelKind Model { get; init; }
    public required int Servers { get; init; }
    public required int SeedUsed { get; init; }
    public required int TotalCustomers { get; init; }
    public required int Warmup { get; init; }
    public required CustomerRecord[] Customers { get; init; }

    /// <summary>Customers included in the statistics (all minus warm-up).</summary>
    public required int CustomersServed { get; init; }
    /// <summary>Observation window [StartTime, EndTime].</summary>
    public required double StartTime { get; init; }
    public required double EndTime { get; init; }
    public double ObservedTime => EndTime - StartTime;

    /// <summary>Time-weighted mean number in system = (integral of N(t) dt) / observed time.</summary>
    public required double L { get; init; }
    /// <summary>Time-weighted mean number in queue.</summary>
    public required double Lq { get; init; }
    /// <summary>Mean time in system over counted customers.</summary>
    public required double W { get; init; }
    /// <summary>Mean wait in queue over counted customers.</summary>
    public required double Wq { get; init; }
    /// <summary>Total busy time / (c * observed time).</summary>
    public required double Utilization { get; init; }
    public required double[] ServerUtilization { get; init; }
    public required int MaxQueue { get; init; }
    /// <summary>Fraction of counted customers that had to wait (estimate of P(wait) / Erlang C).</summary>
    public required double PWait { get; init; }
    /// <summary>Fraction of time the system was empty (estimate of P0).</summary>
    public required double P0 { get; init; }
    /// <summary>Time spent with exactly n customers in the system (index n), within the observation window.</summary>
    public required double[] StateTime { get; init; }
    /// <summary>Number in system seen by each arriving customer (index = customer order). Used for PASTA checks.</summary>
    public required int[] SeenByArrival { get; init; }

    /// <summary>Step series of the number waiting in queue: value QueueValues[i] holds from QueueTimes[i].</summary>
    public required double[] QueueTimes { get; init; }
    public required int[] QueueValues { get; init; }

    public required IReadOnlyList<SimEvent> Events { get; init; }
    public required bool EventsTruncated { get; init; }

    /// <summary>Estimated time-average P(n) from the state-time histogram.</summary>
    public double[] StateProbabilities
    {
        get
        {
            double total = StateTime.Sum();
            return total > 0 ? StateTime.Select(t => t / total).ToArray() : new double[StateTime.Length];
        }
    }

    /// <summary>Counted customers only.</summary>
    public IEnumerable<CustomerRecord> Counted => Customers.Where(c => !c.IsWarmup);

    /// <summary>Running mean of the wait over customers 1..i (counted customers) - shows convergence to theory.</summary>
    public double[] CumulativeAverageWait()
    {
        var counted = Customers.Where(c => !c.IsWarmup).ToArray();
        var result = new double[counted.Length];
        double sum = 0;
        for (int i = 0; i < counted.Length; i++)
        {
            sum += counted[i].Wait;
            result[i] = sum / (i + 1);
        }
        return result;
    }

    /// <summary>
    /// Running mean of the wait, sampled at no more than <paramref name="maxPoints"/> customer indices (always includes the last).
    /// Runs in O(n) without allocating an n-sized array, so it is cheap even for millions of customers.
    /// </summary>
    public (int Index, double Average)[] CumulativeAverageWaitSampled(int maxPoints = 3000)
    {
        int n = TotalCustomers - Warmup;
        if (n <= 0 || Customers.Length == 0) return Array.Empty<(int, double)>();
        int stride = Math.Max(1, n / maxPoints);
        var points = new List<(int, double)>(maxPoints + 2);
        double sum = 0;
        for (int i = 0; i < n; i++)
        {
            sum += Customers[Warmup + i].Wait;
            if ((i + 1) % stride == 0 || i == n - 1) points.Add((i + 1, sum / (i + 1)));
        }
        return points.ToArray();
    }

    /// <summary>
    /// Histogram of waits of counted customers with equal-width bins on [0, cut], where cut is the 99.5th percentile
    /// (found from a fine 4000-bin pre-histogram, so no sort is needed: O(n)). Waits above cut are left out of the bars.
    /// </summary>
    public WaitHistogram BuildWaitHistogram(int bins = 30)
    {
        double max = 0;
        int total = 0, zero = 0;
        for (int i = Warmup; i < Customers.Length; i++)
        {
            double w = Customers[i].Wait;
            if (w > max) max = w;
            if (w <= 1e-12) zero++;
            total++;
        }
        if (total == 0 || max <= 1e-12)
            return new WaitHistogram(new int[bins], 0, total == 0 ? 0 : 1.0, total);

        const int fine = 4000;
        var pre = new int[fine];
        double fineWidth = max / fine;
        for (int i = Warmup; i < Customers.Length; i++)
            pre[Math.Min(fine - 1, (int)(Customers[i].Wait / fineWidth))]++;
        long target = (long)Math.Ceiling(total * 0.995), cum = 0;
        int cutBin = fine - 1;
        for (int b = 0; b < fine; b++)
        {
            cum += pre[b];
            if (cum >= target) { cutBin = b; break; }
        }
        double cut = (cutBin + 1) * fineWidth;

        var counts = new int[bins];
        double width = cut / bins;
        for (int i = Warmup; i < Customers.Length; i++)
        {
            double w = Customers[i].Wait;
            if (w > cut) continue;
            counts[Math.Min(bins - 1, (int)(w / width))]++;
        }
        return new WaitHistogram(counts, cut, zero / (double)total, total);
    }

    /// <summary>Realised inter-arrival times of ALL generated customers (first one measured from time 0).</summary>
    public double[] InterArrivalTimes()
    {
        var r = new double[Customers.Length];
        double prev = 0;
        for (int i = 0; i < r.Length; i++)
        {
            r[i] = Customers[i].Arrival - prev;
            prev = Customers[i].Arrival;
        }
        return r;
    }

    /// <summary>Realised service times of ALL generated customers.</summary>
    public double[] ServiceTimes() => Customers.Select(c => c.Service).ToArray();
}

/// <summary>Equal-width waiting-time histogram: bin i covers [i*Cut/bins, (i+1)*Cut/bins).</summary>
public sealed record WaitHistogram(int[] Counts, double Cut, double ZeroShare, int Total)
{
    public double BinWidth => Counts.Length > 0 ? Cut / Counts.Length : 0;
}
