using QueueingSuite.Core.Analytical;
using QueueingSuite.Core.Models;

namespace QueueingSuite.Tests;

public class AnalyticalTests
{
    private static void Near(double expected, double? actual, int decimals = 4)
    {
        Assert.NotNull(actual);
        Assert.Equal(expected, actual!.Value, decimals);
    }

    // ---------------------------------------------------------------- M/M/1
    [Fact]
    public void MM1_lambda4_mu6_matches_hand_calculation()
    {
        var r = MM1Calculator.Compute(4, 6);
        Assert.True(r.IsStable);
        Assert.True(r.IsExact);
        Near(0.6667, r.Rho, 4);
        Near(2.0, r.L); Near(1.3333, r.Lq); Near(0.5, r.W); Near(0.3333, r.Wq); Near(0.3333, r.P0);
    }

    [Fact]
    public void MM1_lambda4_mu5_gives_Lq_3_2()
    {
        var r = MM1Calculator.Compute(4, 5);
        Near(3.2, r.Lq);
    }

    [Fact]
    public void MM1_state_probabilities_sum_to_one()
    {
        var r = MM1Calculator.Compute(4, 6);
        double sum = Enumerable.Range(0, 200).Sum(n => r.StateProbability!(n));
        Assert.Equal(1.0, sum, 6);
        Assert.Equal(r.P0!.Value, r.StateProbability!(0), 10);
    }

    // ---------------------------------------------------------------- M/G/1
    [Fact]
    public void MD1_lambda4_mu5_gives_Lq_1_6()
    {
        var r = MG1Calculator.Compute(4, 1.0 / 5, 0.0);     // deterministic service: Var(S) = 0
        Near(1.6, r.Lq);
        // half of the M/M/1 queue for the same lambda, mu
        Assert.Equal(MM1Calculator.Compute(4, 5).Lq!.Value / 2, r.Lq!.Value, 9);
    }

    [Theory]
    [InlineData(4, 6)]
    [InlineData(1, 3)]
    [InlineData(9, 10)]
    public void MG1_with_Cs2_equal_1_equals_MM1(double lambda, double mu)
    {
        double es = 1 / mu;
        var g = MG1Calculator.Compute(lambda, es, es * es);   // Cs^2 = 1  ->  Var = E[S]^2
        var m = MM1Calculator.Compute(lambda, mu);
        Assert.Equal(m.Lq!.Value, g.Lq!.Value, 9);
        Assert.Equal(m.Wq!.Value, g.Wq!.Value, 9);
        Assert.Equal(m.W!.Value, g.W!.Value, 9);
        Assert.Equal(m.L!.Value, g.L!.Value, 9);
    }

    // ---------------------------------------------------------------- G/G/1
    [Fact]
    public void Kingman_with_exponential_cvs_equals_MM1_Wq()
    {
        var g = GG1Calculator.Compute(4, 1.0 / 6, 1, 1);
        Assert.Equal(MM1Calculator.Compute(4, 6).Wq!.Value, g.Wq!.Value, 9);
        Assert.False(g.IsExact);                              // labelled as an approximation
        Assert.Contains("Approximation", g.Method);
    }

    [Fact]
    public void AllenCunneen_with_one_server_equals_Kingman()
    {
        double ac = GG1Calculator.AllenCunneenWq(4, 1.0 / 6, 0.5, 0.3, 1)!.Value;
        double k = GG1Calculator.Compute(4, 1.0 / 6, 0.5, 0.3).Wq!.Value;
        Assert.Equal(k, ac, 9);
    }

    // ---------------------------------------------------------------- M/M/c
    [Fact]
    public void MMc_lambda4_mu3_c2()
    {
        var r = MMcCalculator.Compute(4, 3, 2);
        Near(0.6667, r.Rho); Near(0.2, r.P0); Near(0.5333, r.PWait); Near(1.0667, r.Lq);
        Near(0.2667, r.Wq); Near(0.6, r.W); Near(2.4, r.L);
    }

    [Fact]
    public void MMc_lambda10_mu4_c3()
    {
        var r = MMcCalculator.Compute(10, 4, 3);
        Near(0.8333, r.Rho); Near(0.0449, r.P0); Near(0.7022, r.PWait); Near(3.5112, r.Lq);
        Near(0.3511, r.Wq); Near(0.6011, r.W); Near(6.0112, r.L);
    }

    [Theory]
    [InlineData(1, 2)]
    [InlineData(4, 6)]
    [InlineData(0.5, 0.7)]
    [InlineData(9.9, 10)]
    public void MMc_with_c1_equals_MM1(double lambda, double mu)
    {
        var c = MMcCalculator.Compute(lambda, mu, 1);
        var m = MM1Calculator.Compute(lambda, mu);
        Assert.Equal(m.P0!.Value, c.P0!.Value, 10);
        Assert.Equal(m.Lq!.Value, c.Lq!.Value, 9);
        Assert.Equal(m.Wq!.Value, c.Wq!.Value, 9);
        Assert.Equal(m.W!.Value, c.W!.Value, 9);
        Assert.Equal(m.L!.Value, c.L!.Value, 9);
        Assert.Equal(m.PWait!.Value, c.PWait!.Value, 10);
    }

    [Fact]
    public void MMc_is_numerically_stable_for_50_servers()
    {
        var r = MMcCalculator.Compute(45, 1, 50);    // a = 45 erlangs on 50 servers
        Assert.True(r.IsStable);
        AssertFinite(r);
        Assert.InRange(r.PWait!.Value, 0, 1);
        double sum = Enumerable.Range(0, 3000).Sum(n => r.StateProbability!(n));
        Assert.Equal(1.0, sum, 6);
    }

    // ---------------------------------------------------------------- unstable inputs
    public static IEnumerable<object[]> UnstableCases()
    {
        yield return new object[] { MM1Calculator.Compute(6, 6) };          // rho = 1 exactly
        yield return new object[] { MM1Calculator.Compute(7, 6) };
        yield return new object[] { MMcCalculator.Compute(6, 3, 2) };       // rho = 1
        yield return new object[] { MMcCalculator.Compute(10, 3, 2) };
        yield return new object[] { MG1Calculator.Compute(5, 0.2, 0.04) };  // rho = 1
        yield return new object[] { MG1Calculator.Compute(8, 0.2, 0.04) };
        yield return new object[] { GG1Calculator.Compute(5, 0.2, 1, 1) };
        yield return new object[] { GG1Calculator.Compute(9, 0.2, 1, 1) };
    }

    [Theory]
    [MemberData(nameof(UnstableCases))]
    public void Unstable_inputs_return_unstable_result_without_numbers(AnalyticalResult r)
    {
        Assert.False(r.IsStable);
        Assert.NotNull(r.Message);
        Assert.Null(r.L); Assert.Null(r.Lq); Assert.Null(r.W); Assert.Null(r.Wq); Assert.Null(r.P0); Assert.Null(r.PWait);
        Assert.True(double.IsFinite(r.Rho));
        Assert.True(r.Rho >= 1);
    }

    [Theory]
    [InlineData(0, 5)]
    [InlineData(-1, 5)]
    [InlineData(double.NaN, 5)]
    [InlineData(4, double.PositiveInfinity)]
    public void Invalid_rates_throw_ArgumentException(double lambda, double mu)
    {
        Assert.Throws<ArgumentException>(() => MM1Calculator.Compute(lambda, mu));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(51)]
    public void Invalid_server_count_throws(int c)
    {
        Assert.Throws<ArgumentException>(() => MMcCalculator.Compute(1, 1, c));
    }

    // ---------------------------------------------------------------- Little's law everywhere
    public static IEnumerable<object[]> StableCases()
    {
        yield return new object[] { MM1Calculator.Compute(4, 6) };
        yield return new object[] { MM1Calculator.Compute(0.3, 9.9) };
        yield return new object[] { MMcCalculator.Compute(4, 3, 2) };
        yield return new object[] { MMcCalculator.Compute(10, 4, 3) };
        yield return new object[] { MMcCalculator.Compute(30, 1, 40) };
        yield return new object[] { MG1Calculator.Compute(4, 0.2, 0.0) };
        yield return new object[] { MG1Calculator.Compute(4, 0.2, 0.09) };
        yield return new object[] { GG1Calculator.Compute(4, 0.2, 0.36, 0.19) };
    }

    [Theory]
    [MemberData(nameof(StableCases))]
    public void Littles_law_holds_and_values_are_finite(AnalyticalResult r)
    {
        Assert.True(r.IsStable);
        AssertFinite(r);
        Assert.Equal(r.L!.Value, r.Lambda * r.W!.Value, 8);          // L = lambda W
        Assert.Equal(r.Lq!.Value, r.Lambda * r.Wq!.Value, 8);        // Lq = lambda Wq
        Assert.Equal(r.W!.Value, r.Wq!.Value + 1 / r.Mu, 8);         // W = Wq + E[S]
        Assert.NotEmpty(r.Workings);
    }

    private static void AssertFinite(AnalyticalResult r)
    {
        foreach (var v in new[] { r.L, r.Lq, r.W, r.Wq, r.P0 })
        {
            Assert.NotNull(v);
            Assert.True(double.IsFinite(v!.Value));
        }
    }
}
