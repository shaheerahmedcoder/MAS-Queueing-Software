using QueueingSuite.Core.Analytical;
using QueueingSuite.Core.Models;
using QueueingSuite.Core.Simulation;
using QueueingSuite.Core.Statistics;

namespace QueueingSuite.Tests;

public class StatisticsTests
{
    private static double[] Draw(IDistribution d, int n, int seed)
    {
        var rng = new Random(seed);
        return Enumerable.Range(0, n).Select(_ => d.Sample(rng)).ToArray();
    }

    // ---------------------------------------------------------------- chi-square: acceptance / rejection
    [Fact]
    public void Exponential_sample_passes_against_exponential_fixed_seed()
    {
        var d = new ExponentialDistribution(4);
        var r = ChiSquareTest.ContinuousGoodnessOfFit(Draw(d, 5000, 42), d.Cdf, 0, "t", "h");
        Assert.True(r.IsValid);
        Assert.False(r.Reject, r.Verdict);
        Assert.StartsWith("Fail to reject H0", r.Verdict);
        Assert.Equal(70, r.Bins.Count);            // floor(sqrt(5000)) equal-probability bins
        Assert.Equal(r.Bins.Count - 1, r.DegreesOfFreedom);
    }

    [Fact]
    public void Exponential_sample_is_rejected_only_about_alpha_of_the_time()
    {
        var d = new ExponentialDistribution(2.5);
        int rejections = 0;
        for (int seed = 1; seed <= 40; seed++)
            if (ChiSquareTest.ContinuousGoodnessOfFit(Draw(d, 2000, seed), d.Cdf, 0, "t", "h").Reject) rejections++;
        Assert.True(rejections <= 6, $"{rejections}/40 rejections at alpha 0.05 (expected about 2)");   // P(>6 | 5%) is ~0.1%
    }

    [Fact]
    public void Uniform_sample_tested_against_exponential_is_rejected()
    {
        var uniform = new UniformDistribution(0, 2);
        var exp = new ExponentialDistribution(1);            // same mean 1, completely different shape
        for (int seed = 1; seed <= 10; seed++)
        {
            var r = ChiSquareTest.ContinuousGoodnessOfFit(Draw(uniform, 2000, seed), exp.Cdf, 0, "t", "h");
            Assert.True(r.Reject, r.Verdict);
            Assert.StartsWith("Reject H0", r.Verdict);
        }
    }

    [Fact]
    public void Chi_square_table_arithmetic_is_consistent()
    {
        var d = new ExponentialDistribution(1);
        var r = ChiSquareTest.ContinuousGoodnessOfFit(Draw(d, 1000, 3), d.Cdf, 1, "t", "h", bins: 20);
        Assert.Equal(20, r.Bins.Count);
        Assert.Equal(1000, r.Bins.Sum(b => b.Observed));
        Assert.Equal(1000, r.Bins.Sum(b => b.Expected), 6);
        Assert.Equal(r.Bins.Sum(b => b.Contribution), r.Statistic, 9);
        Assert.Equal(20 - 1 - 1, r.DegreesOfFreedom);        // one parameter estimated
        Assert.All(r.Bins, b => Assert.True(b.Expected >= 5));
        // p-value and critical value agree with each other
        Assert.Equal(r.Statistic > r.Critical, r.PValue < r.Alpha);
    }

    [Fact]
    public void Critical_value_matches_published_table()
    {
        // chi-square(0.95, df=10) = 18.307 ; build a dummy discrete test with 11 equal cells
        var cells = Enumerable.Range(0, 11).Select(i => (i.ToString(), 10.0, 1.0 / 11)).ToList();
        var r = ChiSquareTest.DiscreteGoodnessOfFit(cells, 110, 0, "t", "h");
        Assert.Equal(10, r.DegreesOfFreedom);
        Assert.Equal(18.307, r.Critical, 2);
        Assert.Equal(0.0, r.Statistic, 12);
        Assert.False(r.Reject);
    }

    [Fact]
    public void Small_expected_cells_are_merged()
    {
        var cells = new List<(string, double, double)>
        {
            ("0", 50, 0.5), ("1", 30, 0.3), ("2", 12, 0.12), ("3", 5, 0.05), ("4", 2, 0.02), ("5", 1, 0.01)
        };
        var r = ChiSquareTest.DiscreteGoodnessOfFit(cells, 100, 0, "t", "h");
        Assert.All(r.Bins, b => Assert.True(b.Expected >= 5, $"{b.Label} expected {b.Expected}"));
        Assert.Equal(100, r.Bins.Sum(b => b.Observed), 9);
    }

    [Fact]
    public void Tiny_sample_gives_a_clear_not_possible_verdict_not_an_exception()
    {
        var r = ChiSquareTest.ContinuousGoodnessOfFit(new[] { 1.0, 2.0, 3.0 }, x => 1 - Math.Exp(-x), 1, "t", "h");
        Assert.False(r.IsValid);
        Assert.False(r.Reject);
        Assert.StartsWith("Test not possible", r.Verdict);
    }

    // ---------------------------------------------------------------- generator validity for every distribution
    [Theory]
    [InlineData(DistributionKind.Exponential, 0.5, 1.0)]
    [InlineData(DistributionKind.Uniform, 0.5, 0.4)]
    [InlineData(DistributionKind.Normal, 1.0, 0.25)]
    [InlineData(DistributionKind.Gamma, 0.5, 0.8)]
    [InlineData(DistributionKind.Gamma, 2.0, 1.6)]        // shape < 1 branch
    [InlineData(DistributionKind.Erlang, 1.0, 0.5)]
    public void Generators_follow_their_intended_distribution(DistributionKind kind, double mean, double cv)
    {
        var d = DistributionFactory.Create(kind, mean, cv);
        int fails = 0;
        for (int seed = 11; seed < 31; seed++)
            if (SimulationValidator.GeneratorValidity(Draw(d, 3000, seed), d, "x").Reject) fails++;
        Assert.True(fails <= 4, $"{d.Name}: rejected {fails}/20 times at alpha 0.05");
    }

    [Theory]
    [InlineData(DistributionKind.Exponential, 0.5, 1.0)]
    [InlineData(DistributionKind.Uniform, 0.5, 0.4)]
    [InlineData(DistributionKind.Normal, 1.0, 0.25)]
    [InlineData(DistributionKind.Gamma, 0.5, 0.8)]
    [InlineData(DistributionKind.Erlang, 1.0, 0.5)]
    public void Sample_moments_match_stated_mean_and_variance(DistributionKind kind, double mean, double cv)
    {
        var d = DistributionFactory.Create(kind, mean, cv);
        var xs = Draw(d, 200_000, 5);
        Assert.Equal(d.Mean, xs.Average(), 2);
        Assert.Equal(d.Variance, Descriptive.SampleVariance(xs), 2);
        Assert.Equal(mean, d.Mean, 2);                       // factory honours the requested mean
        Assert.Equal(cv * cv, d.Cv2, 2);                     // ... and the requested CV (Erlang rounds k)
    }

    [Fact]
    public void Deterministic_generator_has_no_chi_square_test()
    {
        var d = new DeterministicDistribution(0.2);
        var r = SimulationValidator.GeneratorValidity(Draw(d, 100, 1), d, "service");
        Assert.False(r.IsValid);
    }

    [Fact]
    public void Simulation_engine_generates_valid_inter_arrival_and_service_times()
    {
        var sim = SimulationEngine.Run(new SimulationConfig
        {
            Customers = 5000, Seed = 99, LogEvents = false,
            Arrival = new ExponentialDistribution(4), Service = new ExponentialDistribution(6)
        });
        Assert.False(SimulationValidator.GeneratorValidity(sim.InterArrivalTimes(), new ExponentialDistribution(4), "inter-arrival").Reject);
        Assert.False(SimulationValidator.GeneratorValidity(sim.ServiceTimes(), new ExponentialDistribution(6), "service").Reject);
        // and the wrong rate IS detected
        Assert.True(SimulationValidator.GeneratorValidity(sim.ServiceTimes(), new ExponentialDistribution(5), "service").Reject);
    }

    // ---------------------------------------------------------------- simulation vs theory
    [Fact]
    public void MM1_simulated_state_distribution_matches_theory()
    {
        var th = MM1Calculator.Compute(4, 6);
        var sim = SimulationEngine.Run(new SimulationConfig
        {
            Customers = 200_000, Seed = 2024, LogEvents = false,
            Arrival = new ExponentialDistribution(4), Service = new ExponentialDistribution(6)
        });
        var r = SimulationValidator.StateDistribution(sim, th);
        Assert.True(r.IsValid);
        Assert.False(r.Reject, r.Verdict);
    }

    [Fact]
    public void Wrong_theory_is_detected_by_the_state_distribution_test()
    {
        var wrong = MM1Calculator.Compute(4.8, 6);          // theory for a different arrival rate
        var sim = SimulationEngine.Run(new SimulationConfig
        {
            Customers = 200_000, Seed = 2024, LogEvents = false,
            Arrival = new ExponentialDistribution(4), Service = new ExponentialDistribution(6)
        });
        Assert.True(SimulationValidator.StateDistribution(sim, wrong).Reject);
    }

    [Fact]
    public void MMc_simulated_state_distribution_matches_theory()
    {
        var th = MMcCalculator.Compute(4, 3, 2);
        var sim = SimulationEngine.Run(new SimulationConfig
        {
            Servers = 2, Customers = 200_000, Seed = 31, LogEvents = false,
            Arrival = new ExponentialDistribution(4), Service = new ExponentialDistribution(3)
        });
        var r = SimulationValidator.StateDistribution(sim, th);
        Assert.True(r.IsValid);
        Assert.False(r.Reject, r.Verdict);
    }

    [Fact]
    public void State_distribution_test_is_not_available_for_unstable_or_MG1()
    {
        var sim = SimulationEngine.Run(new SimulationConfig
        {
            Customers = 1000, Seed = 1, LogEvents = false,
            Arrival = new ExponentialDistribution(4), Service = new ExponentialDistribution(6)
        });
        Assert.False(SimulationValidator.StateDistribution(sim, MM1Calculator.Compute(6, 6)).IsValid);
        Assert.False(SimulationValidator.StateDistribution(sim, MG1Calculator.Compute(4, 0.1, 0.01)).IsValid);
    }

    // ---------------------------------------------------------------- data fit
    [Fact]
    public void Whole_number_data_uses_the_rounding_correction()
    {
        var rng = new Random(8);
        var d = new ExponentialDistribution(0.25);
        var data = Enumerable.Range(0, 400).Select(_ => Math.Round(d.Sample(rng))).ToArray();   // exponential, recorded in whole minutes
        var r = SimulationValidator.DataFitExponential(data, "inter-arrival times");
        Assert.True(r.IsValid);
        Assert.Contains(r.Notes, n => n.Contains("rounding correction"));
        Assert.Equal(1, r.ParametersEstimated);
        Assert.False(r.Reject, r.Verdict);
    }

    [Fact]
    public void Constant_data_is_rejected_as_exponential()
    {
        var data = Enumerable.Repeat(3.0, 100).Concat(Enumerable.Repeat(4.0, 100)).ToArray();
        Assert.True(SimulationValidator.DataFitExponential(data, "service").Reject);
    }

    // ---------------------------------------------------------------- confidence interval
    [Fact]
    public void Confidence_interval_uses_student_t()
    {
        var ci = ConfidenceInterval.Compute(new[] { 10.0, 12.0, 14.0, 11.0, 13.0 });   // mean 12, s = 1.5811, t(0.975,4) = 2.7764
        Assert.Equal(12, ci.Mean, 9);
        Assert.Equal(2.7764 * 1.58114 / Math.Sqrt(5), ci.HalfWidth, 3);
        Assert.True(ci.HasInterval);
        Assert.False(ConfidenceInterval.Compute(new[] { 5.0 }).HasInterval);
    }

    [Fact]
    public void Descriptive_statistics()
    {
        var xs = new[] { 2.0, 4.0, 4.0, 4.0, 5.0, 5.0, 7.0, 9.0 };
        Assert.Equal(5, Descriptive.Mean(xs), 9);
        Assert.Equal(32.0 / 7, Descriptive.SampleVariance(xs), 9);
        Assert.Equal(32.0 / 7 / 25, Descriptive.SquaredCoefficientOfVariation(xs), 9);
    }
}
