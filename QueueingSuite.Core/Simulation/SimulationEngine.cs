using QueueingSuite.Core.Models;

namespace QueueingSuite.Core.Simulation;

/// <summary>
/// Discrete-event simulation of a queue with c identical servers and ONE FCFS waiting line.
/// <para>
/// Future-event list: a priority queue ordered by (time, event type, sequence). Two event types exist:
/// ARRIVAL (the next arrival is scheduled when the current one is processed) and DEPARTURE (scheduled when a
/// customer starts service). At equal times a departure is processed before an arrival so a server that frees up at
/// time t can serve an arrival at time t without making it wait.
/// </para>
/// <para>
/// Measurement is TIME-WEIGHTED: before every event the integrals of N(t) (number in system) and Nq(t) (number in
/// queue) are advanced by n*dt, so L = integral(N)/T and Lq = integral(Nq)/T. Utilization = total busy time / (c*T).
/// </para>
/// <para>
/// Warm-up: the observation window starts at the ARRIVAL TIME of customer number Warmup+1 (index Warmup);
/// earlier customers are simulated but excluded from every statistic. Server choice: the lowest-numbered idle server.
/// </para>
/// </summary>
public static class SimulationEngine
{
    private enum Kind { Departure = 0, Arrival = 1 }
    private readonly record struct FutureEvent(double Time, Kind Type, int Customer, int Server);

    /// <summary>Runs one replication. Reports progress in [0,1] and honours cancellation.</summary>
    public static SimulationResult Run(SimulationConfig cfg, IProgress<double>? progress = null, CancellationToken ct = default)
    {
        Validate(cfg);
        int n = cfg.Customers, c = cfg.Servers, warmup = cfg.Warmup;
        int seed = cfg.Seed ?? Random.Shared.Next(1, int.MaxValue);

        // Separate random streams for arrivals and service (common-random-numbers friendly, reproducible).
        var arrivalRng = new Random(Seeds.Mix(seed, 1));
        var serviceRng = new Random(Seeds.Mix(seed, 2));
        cfg.Arrival.Reset();
        cfg.Service.Reset();

        var fel = new PriorityQueue<FutureEvent, (double, int, long)>();
        long seq = 0;
        void Schedule(FutureEvent e) => fel.Enqueue(e, (e.Time, (int)e.Type, seq++));

        var arrivalT = new double[n];
        var startT = new double[n];
        var endT = new double[n];
        var serverOf = new int[n];
        var seen = new int[n];

        var waiting = new Queue<int>();
        var busy = new bool[c];
        int inSystem = 0, inQueue = 0, generated = 0, departed = 0;
        int maxQueue = 0;

        // time-weighted accumulators (active only inside the observation window)
        bool measuring = false;
        double t0 = 0, lastT = 0, areaN = 0, areaNq = 0;
        var stateTime = new List<double> { 0 };

        var events = new List<SimEvent>();
        bool truncated = false;
        void Log(double t, SimEventType type, int cust, int server)
        {
            if (!cfg.LogEvents) return;
            if (events.Count >= cfg.MaxLoggedEvents) { truncated = true; return; }
            events.Add(new SimEvent(t, type, cfg.CustomerIds?[cust] ?? cust + 1, server, inQueue, inSystem));
        }

        var qTimes = new List<double> { 0 };
        var qVals = new List<int> { 0 };
        void MarkQueue(double t)
        {
            if (qVals[^1] != inQueue || qTimes[^1] != t)
            {
                if (qTimes[^1] == t) qVals[^1] = inQueue;   // same instant: keep the last value only
                else { qTimes.Add(t); qVals.Add(inQueue); }
            }
        }

        void Advance(double t)
        {
            if (measuring)
            {
                double dt = t - lastT;
                if (dt > 0)
                {
                    areaN += inSystem * dt;
                    areaNq += inQueue * dt;
                    while (stateTime.Count <= inSystem) stateTime.Add(0);
                    stateTime[inSystem] += dt;
                }
            }
            lastT = t;
        }

        void StartService(int cust, int server, double t)
        {
            busy[server] = true;
            double s = cfg.Service.Sample(serviceRng);
            if (!double.IsFinite(s) || s < 0)
                throw new InvalidOperationException($"Service distribution produced an invalid time ({s}).");
            startT[cust] = t;
            endT[cust] = t + s;
            serverOf[cust] = server;
            Schedule(new FutureEvent(t + s, Kind.Departure, cust, server));
            Log(t, SimEventType.StartService, cust, server);
        }

        // first arrival
        double firstGap = cfg.Arrival.Sample(arrivalRng);
        CheckGap(firstGap);
        Schedule(new FutureEvent(firstGap, Kind.Arrival, 0, -1));
        generated = 1;

        double endTime = 0;
        int steps = 0;
        while (fel.Count > 0)
        {
            var e = fel.Dequeue();
            if ((++steps & 4095) == 0)
            {
                ct.ThrowIfCancellationRequested();
                progress?.Report(Math.Min(1.0, departed / (double)n));
            }
            Advance(e.Time);
            endTime = e.Time;

            if (e.Type == Kind.Arrival)
            {
                int i = e.Customer;
                arrivalT[i] = e.Time;
                if (i == warmup)
                {
                    // Start of the observation window.
                    measuring = true;
                    t0 = e.Time;
                    lastT = e.Time;
                }
                seen[i] = inSystem;
                inSystem++;
                int free = Array.IndexOf(busy, false);
                if (free >= 0)
                {
                    Log(e.Time, SimEventType.Arrival, i, free);
                    StartService(i, free, e.Time);
                }
                else
                {
                    waiting.Enqueue(i);
                    inQueue++;
                    if (measuring) maxQueue = Math.Max(maxQueue, inQueue);
                    Log(e.Time, SimEventType.Arrival, i, -1);
                }
                MarkQueue(e.Time);

                if (generated < n)
                {
                    double gap = cfg.Arrival.Sample(arrivalRng);
                    CheckGap(gap);
                    Schedule(new FutureEvent(e.Time + gap, Kind.Arrival, generated, -1));
                    generated++;
                }
            }
            else // departure
            {
                departed++;
                inSystem--;
                busy[e.Server] = false;
                Log(e.Time, SimEventType.Departure, e.Customer, e.Server);
                if (waiting.Count > 0)
                {
                    int next = waiting.Dequeue();
                    inQueue--;
                    StartService(next, e.Server, e.Time);
                }
                MarkQueue(e.Time);
            }
        }
        progress?.Report(1.0);

        // ---------- summary statistics ----------
        var records = cfg.KeepTrace ? new CustomerRecord[n] : Array.Empty<CustomerRecord>();
        for (int i = 0; cfg.KeepTrace && i < n; i++)
            records[i] = new CustomerRecord(cfg.CustomerIds?[i] ?? i + 1, arrivalT[i], startT[i], endT[i], serverOf[i], i < warmup);

        double observed = endTime - t0;
        var busyTime = new double[c];
        double sumWait = 0, sumSys = 0;
        int waited = 0, counted = 0;
        for (int i = warmup; i < n; i++)
        {
            double wait = startT[i] - arrivalT[i];
            sumWait += wait;
            sumSys += endT[i] - arrivalT[i];
            if (wait > 1e-12) waited++;
            counted++;
        }
        for (int i = 0; i < n; i++)
        {
            double from = Math.Max(startT[i], t0);        // clip service intervals to the observation window
            if (endT[i] > from) busyTime[serverOf[i]] += endT[i] - from;
        }
        var serverUtil = busyTime.Select(b => observed > 0 ? Math.Min(1, b / observed) : 0).ToArray();
        double util = serverUtil.Length > 0 ? serverUtil.Average() : 0;
        double sumState = stateTime.Sum();

        return new SimulationResult
        {
            Model = cfg.Model, Servers = c, SeedUsed = seed, TotalCustomers = n, Warmup = warmup,
            Customers = records, CustomersServed = counted,
            StartTime = t0, EndTime = endTime,
            L = observed > 0 ? areaN / observed : 0,
            Lq = observed > 0 ? areaNq / observed : 0,
            W = counted > 0 ? sumSys / counted : 0,
            Wq = counted > 0 ? sumWait / counted : 0,
            Utilization = util, ServerUtilization = serverUtil,
            MaxQueue = maxQueue,
            PWait = counted > 0 ? waited / (double)counted : 0,
            P0 = sumState > 0 ? stateTime[0] / sumState : 0,
            StateTime = stateTime.ToArray(),
            SeenByArrival = cfg.KeepTrace ? seen : Array.Empty<int>(),
            QueueTimes = cfg.KeepTrace ? qTimes.ToArray() : Array.Empty<double>(),
            QueueValues = cfg.KeepTrace ? qVals.ToArray() : Array.Empty<int>(),
            Events = events, EventsTruncated = truncated
        };
    }

    private static void CheckGap(double gap)
    {
        if (!double.IsFinite(gap) || gap < 0)
            throw new InvalidOperationException($"Arrival distribution produced an invalid inter-arrival time ({gap}).");
    }

    private static void Validate(SimulationConfig cfg)
    {
        if (cfg.Servers < 1 || cfg.Servers > 50) throw new ArgumentException("Servers must be between 1 and 50.");
        if (cfg.Customers < 1) throw new ArgumentException("Number of customers must be at least 1.");
        if (cfg.Warmup < 0 || cfg.Warmup >= cfg.Customers) throw new ArgumentException("Warm-up must be >= 0 and smaller than the number of customers.");
        if (cfg.CustomerIds is not null && cfg.CustomerIds.Count < cfg.Customers) throw new ArgumentException("CustomerIds must have one entry per customer.");
    }
}

/// <summary>Seed helpers: derive independent, reproducible stream seeds from one user seed (SplitMix-style mixing).</summary>
public static class Seeds
{
    public static int Mix(int seed, int stream)
    {
        unchecked
        {
            ulong z = (ulong)(uint)seed + 0x9E3779B97F4A7C15UL * (ulong)(stream + 1);
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            z ^= z >> 31;
            return (int)(z & 0x7FFFFFFF);
        }
    }
}
