using QueueingSuite.Core.Analytical;
using QueueingSuite.Core.Models;
using QueueingSuite.Core.Simulation;

namespace QueueingSuite.Tests;

public class SimulationTests
{
    private const int Seed = 20240611;
    private const int N = 200_000;

    private static SimulationResult Run(ModelKind kind, double lambda, double mu, int c, IDistribution? service = null, int n = N, int warmup = 0, int seed = Seed) =>
        SimulationEngine.Run(new SimulationConfig
        {
            Model = kind, Servers = c, Customers = n, Warmup = warmup, Seed = seed, LogEvents = false,
            Arrival = new ExponentialDistribution(lambda),
            Service = service ?? new ExponentialDistribution(mu)
        });

    private static void Within(double expected, double actual, double tol, string what) =>
        Assert.True(Math.Abs(actual - expected) / expected <= tol,
            $"{what}: simulated {actual:F4} vs analytical {expected:F4} ({100 * Math.Abs(actual - expected) / expected:F2}% off, tolerance {tol:P0})");

    // A single 200,000-customer run has a sampling standard deviation of about 1.4% (L) to 2.3% (Lq) of the true value
    // (measured over 40 seeds: no bias, see docs/TEST_REPORT.md), so a hard 3% bar on ONE run fails for some seeds.
    // The accuracy tests therefore average 10 independent replications (sd ~0.5%) and keep the 3% tolerance.
    private sealed record Averages(double L, double Lq, double W, double Wq, double Util, double PWait, double P0);

    private static Averages MeanOfReplications(ModelKind kind, int c, IDistribution arrival, IDistribution service, int reps = 10)
    {
        var s = ReplicationRunner.Run(new SimulationConfig
        {
            Model = kind, Servers = c, Customers = N, Seed = Seed, LogEvents = false, Arrival = arrival, Service = service
        }, reps);
        return new Averages(s.L.Mean, s.Lq.Mean, s.W.Mean, s.Wq.Mean, s.Utilization.Mean, s.PWait.Mean, s.P0.Mean);
    }

    [Fact]
    public void MM1_4_6_matches_theory_within_3_percent()
    {
        var sim = MeanOfReplications(ModelKind.MM1, 1, new ExponentialDistribution(4), new ExponentialDistribution(6));
        var th = MM1Calculator.Compute(4, 6);
        Within(th.L!.Value, sim.L, 0.03, "L");
        Within(th.Lq!.Value, sim.Lq, 0.03, "Lq");
        Within(th.Wq!.Value, sim.Wq, 0.03, "Wq");
        Within(th.W!.Value, sim.W, 0.03, "W");
        Within(th.Rho, sim.Util, 0.03, "utilization");
        Within(th.P0!.Value, sim.P0, 0.03, "P0");
    }

    [Fact]
    public void MM1_4_6_single_fixed_seed_run_is_close()
    {
        var sim = Run(ModelKind.MM1, 4, 6, 1, seed: 12345);
        var th = MM1Calculator.Compute(4, 6);
        Within(th.L!.Value, sim.L, 0.05, "L");
        Within(th.Wq!.Value, sim.Wq, 0.05, "Wq");
        Within(th.Rho, sim.Utilization, 0.03, "utilization");   // utilization has far less noise
    }

    [Fact]
    public void MMc_4_3_c2_matches_theory_within_3_percent()
    {
        var sim = MeanOfReplications(ModelKind.MMc, 2, new ExponentialDistribution(4), new ExponentialDistribution(3));
        var th = MMcCalculator.Compute(4, 3, 2);
        Within(th.L!.Value, sim.L, 0.03, "L");
        Within(th.Lq!.Value, sim.Lq, 0.03, "Lq");
        Within(th.Wq!.Value, sim.Wq, 0.03, "Wq");
        Within(th.W!.Value, sim.W, 0.03, "W");
        Within(th.Rho, sim.Util, 0.03, "utilization");
        Within(th.PWait!.Value, sim.PWait, 0.03, "P(wait)");
        Assert.Equal(2, Run(ModelKind.MMc, 4, 3, 2, n: 1000).ServerUtilization.Length);
    }

    [Fact]
    public void MD1_4_5_matches_theory_within_3_percent()
    {
        var sim = MeanOfReplications(ModelKind.MG1, 1, new ExponentialDistribution(4), new DeterministicDistribution(1.0 / 5));
        var th = MG1Calculator.Compute(4, 0.2, 0);
        Within(th.Lq!.Value, sim.Lq, 0.03, "Lq");
        Within(th.Wq!.Value, sim.Wq, 0.03, "Wq");
        Within(th.L!.Value, sim.L, 0.03, "L");
        Within(th.Rho, sim.Util, 0.03, "utilization");
    }

    [Fact]
    public void MG1_with_gamma_service_matches_Pollaczek_Khinchine()
    {
        var service = DistributionFactory.Create(DistributionKind.Gamma, 0.15, 0.7);   // E[S] = 0.15, CV = 0.7
        var sim = MeanOfReplications(ModelKind.MG1, 1, new ExponentialDistribution(4), service);
        var th = MG1Calculator.Compute(4, service.Mean, service.Variance);
        Within(th.Wq!.Value, sim.Wq, 0.03, "Wq");
        Within(th.Lq!.Value, sim.Lq, 0.03, "Lq");
    }

    [Theory]
    [InlineData(4.0, 6.6667, 2)]
    [InlineData(5.0, 6.0, 4)]
    public void Engine_matches_the_exact_Ek_M_1_solution(double lambda, double mu, int k)
    {
        // Independent exact result for Erlang-k arrivals / exponential service (a genuine G/M/1 queue):
        // sigma is the root of sigma = (k*lambda / (k*lambda + mu*(1-sigma)))^k, and Wq = sigma / (mu (1-sigma)).
        double sigma = 0.5;
        for (int i = 0; i < 10_000; i++) sigma = Math.Pow(k * lambda / (k * lambda + mu * (1 - sigma)), k);
        double wqExact = sigma / (mu * (1 - sigma));
        var sim = MeanOfReplications(ModelKind.GG1, 1, new ErlangDistribution(k, 1 / lambda), new ExponentialDistribution(mu));
        Within(wqExact, sim.Wq, 0.03, $"E{k}/M/1 Wq");
    }

    [Fact]
    public void Kingman_approximation_is_close_in_heavy_traffic_and_looser_at_low_load()
    {
        var service = DistributionFactory.Create(DistributionKind.Gamma, 0.15, 0.7);          // Cs^2 = 0.49
        // heavy traffic (rho = 0.9): Kingman is asymptotically exact
        var heavy = DistributionFactory.Create(DistributionKind.Erlang, 1.0 / 6, 0.5);        // lambda = 6, Ca^2 = 0.5
        var simHeavy = MeanOfReplications(ModelKind.GG1, 1, heavy, service);
        var thHeavy = GG1Calculator.Compute(1 / heavy.Mean, service.Mean, heavy.Cv2, service.Cv2);
        Within(thHeavy.Wq!.Value, simHeavy.Wq, 0.10, "Wq at rho 0.9");
        Within(thHeavy.Rho, simHeavy.Util, 0.03, "utilization");

        // low load (rho = 0.6): Kingman over-estimates by roughly 30% here, which is why it is labelled an approximation
        var light = DistributionFactory.Create(DistributionKind.Erlang, 1.0 / 4, 0.5);
        var simLight = MeanOfReplications(ModelKind.GG1, 1, light, service);
        var thLight = GG1Calculator.Compute(1 / light.Mean, service.Mean, light.Cv2, service.Cv2);
        Assert.True(thLight.Wq!.Value > simLight.Wq, "Kingman should over-estimate at low load with Ca^2 < 1");
        Within(thLight.Wq.Value, simLight.Wq, 0.40, "Wq at rho 0.6 (loose)");
    }

    [Fact]
    public void Littles_law_holds_in_simulation()
    {
        var sim = Run(ModelKind.MMc, 10, 4, 3);
        double lambdaObserved = sim.CustomersServed / sim.ObservedTime;
        Within(sim.L, lambdaObserved * sim.W, 0.02, "L = lambda W");
        Within(sim.Lq, lambdaObserved * sim.Wq, 0.02, "Lq = lambda Wq");
    }

    [Fact]
    public void Same_seed_reproduces_the_run_exactly()
    {
        var a = Run(ModelKind.MM1, 4, 6, 1, n: 5000);
        var b = Run(ModelKind.MM1, 4, 6, 1, n: 5000);
        var c = Run(ModelKind.MM1, 4, 6, 1, n: 5000, seed: Seed + 1);
        Assert.Equal(a.Wq, b.Wq);
        Assert.Equal(a.L, b.L);
        Assert.NotEqual(a.Wq, c.Wq);
        Assert.Equal(Seed, a.SeedUsed);
    }

    [Fact]
    public void Blank_seed_is_reported_so_the_run_can_be_reproduced()
    {
        var cfg = new SimulationConfig
        {
            Arrival = new ExponentialDistribution(4), Service = new ExponentialDistribution(6),
            Customers = 3000, Seed = null, LogEvents = false
        };
        var first = SimulationEngine.Run(cfg);
        var again = SimulationEngine.Run(cfg with { Seed = first.SeedUsed });
        Assert.Equal(first.Wq, again.Wq);
        Assert.Equal(first.Utilization, again.Utilization);
    }

    [Fact]
    public void Customer_records_are_consistent_FCFS()
    {
        var sim = Run(ModelKind.MMc, 4, 3, 2, n: 3000);
        double prevStart = -1;
        foreach (var c in sim.Customers)
        {
            Assert.True(c.Start >= c.Arrival - 1e-12);
            Assert.True(c.End >= c.Start);
            Assert.True(c.Start >= prevStart - 1e-12, "FCFS: service starts in arrival order");
            prevStart = c.Start;
            Assert.InRange(c.Server, 0, 1);
        }
        // a server never serves two customers at once
        foreach (var g in sim.Customers.GroupBy(c => c.Server))
        {
            var ordered = g.OrderBy(c => c.Start).ToList();
            for (int i = 1; i < ordered.Count; i++)
                Assert.True(ordered[i].Start >= ordered[i - 1].End - 1e-9);
        }
    }

    [Fact]
    public void Warmup_excludes_customers_from_the_statistics()
    {
        var sim = Run(ModelKind.MM1, 4, 6, 1, n: 20_000, warmup: 5_000);
        Assert.Equal(15_000, sim.CustomersServed);
        Assert.Equal(5_000, sim.Customers.Count(c => c.IsWarmup));
        Assert.Equal(sim.Customers[5000].Arrival, sim.StartTime, 9);
    }

    [Fact]
    public void Time_in_state_sums_to_the_observed_window()
    {
        var sim = Run(ModelKind.MM1, 4, 6, 1, n: 20_000);
        Assert.Equal(sim.ObservedTime, sim.StateTime.Sum(), 6);
        Assert.Equal(1.0, sim.StateProbabilities.Sum(), 9);
    }

    [Fact]
    public void Event_log_is_ordered_and_balanced()
    {
        var sim = SimulationEngine.Run(new SimulationConfig
        {
            Servers = 2, Customers = 500, Seed = 1,
            Arrival = new ExponentialDistribution(4), Service = new ExponentialDistribution(3)
        });
        Assert.False(sim.EventsTruncated);
        Assert.Equal(500, sim.Events.Count(e => e.Type == SimEventType.Arrival));
        Assert.Equal(500, sim.Events.Count(e => e.Type == SimEventType.StartService));
        Assert.Equal(500, sim.Events.Count(e => e.Type == SimEventType.Departure));
        for (int i = 1; i < sim.Events.Count; i++) Assert.True(sim.Events[i].Time >= sim.Events[i - 1].Time);
    }

    [Fact]
    public void Replications_give_confidence_interval_that_covers_theory()
    {
        var cfg = new SimulationConfig
        {
            Model = ModelKind.MM1, Customers = 20_000, Seed = 7, LogEvents = false,
            Arrival = new ExponentialDistribution(4), Service = new ExponentialDistribution(6)
        };
        var s = ReplicationRunner.Run(cfg, 20);
        Assert.Equal(20, s.Runs.Count);
        Assert.True(s.Wq.HasInterval);
        var th = MM1Calculator.Compute(4, 6);
        // The interval is built from the replications' own spread, so theory should sit within a few half-widths.
        Assert.InRange(th.Wq!.Value, s.Wq.Mean - 3 * s.Wq.HalfWidth, s.Wq.Mean + 3 * s.Wq.HalfWidth);
        Within(th.Wq.Value, s.Wq.Mean, 0.03, "mean Wq over replications");
        Assert.False(ReplicationRunner.Run(cfg with { Customers = 500 }, 1).Wq.HasInterval);   // one run: no CI
    }

    [Fact]
    public void Cancellation_is_honoured()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var cfg = new SimulationConfig
        {
            Customers = 1_000_000, Seed = 1, LogEvents = false,
            Arrival = new ExponentialDistribution(4), Service = new ExponentialDistribution(6)
        };
        Assert.Throws<OperationCanceledException>(() => SimulationEngine.Run(cfg, null, cts.Token));
    }

    [Fact]
    public void Overloaded_simulation_still_returns_finite_numbers()
    {
        var sim = Run(ModelKind.MM1, 8, 6, 1, n: 5000);      // rho = 1.33: queue grows, but results stay finite
        Assert.True(double.IsFinite(sim.Wq) && double.IsFinite(sim.Lq) && double.IsFinite(sim.L));
        Assert.True(sim.Wq > 1);
    }

    [Theory]
    [InlineData(0, 10, 0)]
    [InlineData(1, 0, 0)]
    [InlineData(1, 10, 10)]
    [InlineData(51, 10, 0)]
    public void Invalid_configs_throw(int servers, int customers, int warmup)
    {
        Assert.Throws<ArgumentException>(() => SimulationEngine.Run(new SimulationConfig
        {
            Servers = servers, Customers = customers, Warmup = warmup,
            Arrival = new ExponentialDistribution(1), Service = new ExponentialDistribution(2)
        }));
    }
}

public class ChartDataTests
{
    private static SimulationResult Sim(int n, int warmup = 0) => SimulationEngine.Run(new SimulationConfig
    {
        Customers = n, Warmup = warmup, Seed = 17, LogEvents = false,
        Arrival = new ExponentialDistribution(4), Service = new ExponentialDistribution(5)
    });

    [Fact]
    public void Sampled_running_mean_matches_the_full_running_mean()
    {
        var sim = Sim(20_000, warmup: 1000);
        var full = sim.CumulativeAverageWait();
        var sampled = sim.CumulativeAverageWaitSampled(500);
        Assert.InRange(sampled.Length, 400, 700);
        Assert.Equal(full.Length, sampled[^1].Index);
        foreach (var (index, avg) in sampled) Assert.Equal(full[index - 1], avg, 9);
    }

    [Fact]
    public void Wait_histogram_counts_and_zero_share_are_consistent()
    {
        var sim = Sim(50_000, warmup: 500);
        var h = sim.BuildWaitHistogram(30);
        var waits = sim.Counted.Select(c => c.Wait).ToArray();
        Assert.Equal(waits.Length, h.Total);
        Assert.Equal(waits.Count(w => w <= 1e-12) / (double)waits.Length, h.ZeroShare, 12);
        Assert.Equal(waits.Count(w => w <= h.Cut), h.Counts.Sum());
        double p995 = waits.OrderBy(x => x).ElementAt((int)(waits.Length * 0.995));
        Assert.InRange(h.Cut, p995, p995 * 1.05 + 1e-3);     // cut is the 99.5th percentile up to the fine-bin resolution
    }

    [Fact]
    public void Histogram_of_a_run_without_waiting_is_empty_but_valid()
    {
        var sim = SimulationEngine.Run(new SimulationConfig
        {
            Customers = 50, Seed = 1, LogEvents = false,
            Arrival = new DeterministicDistribution(10), Service = new DeterministicDistribution(1)
        });
        var h = sim.BuildWaitHistogram();
        Assert.Equal(0, h.Cut);
        Assert.Equal(1.0, h.ZeroShare);
        Assert.Equal(0, h.Counts.Sum());
    }
}
