using QueueingSuite.Core;
using QueueingSuite.Core.Data;
using QueueingSuite.Core.Models;
using QueueingSuite.Core.Simulation;

namespace QueueingSuite.Tests;

public class QueueRunnerTests
{
    private static string DataFile(string name) => Path.Combine(AppContext.BaseDirectory, "Data", name);
    private static IReadOnlyList<SessionFit> Sessions() =>
        DataSessions.Split(CsvDataLoader.Load(DataFile("kfc_tandem_data.csv")).Customers);

    // ---------------------------------------------------------------- the acceptance scenarios from the brief
    [Fact]
    public void Compare_mode_MM1_4_6()
    {
        var run = QueueRunner.Run(new QueueInputs { Model = ModelKind.MM1, Lambda = 4, Mu = 6, Customers = 200_000, Seed = 3, Replications = 5 }, RunMode.Compare);
        Assert.Equal(2.0, run.Analytical.L!.Value, 4);
        Assert.Equal(1.3333, run.Analytical.Lq!.Value, 4);
        Assert.Equal(0.5, run.Analytical.W!.Value, 4);
        Assert.Equal(0.3333, run.Analytical.Wq!.Value, 4);
        Assert.Equal(0.6667, run.Analytical.Rho, 4);
        var l = run.Comparison!.Row("L")!;
        Assert.Equal(2.0, l.Analytical!.Value, 4);
        Assert.InRange(l.PercentError!.Value, -4, 4);
        Assert.True(l.SimHalfWidth > 0);               // 5 replications -> confidence interval
        Assert.Equal(7, run.Comparison.Rows.Count);
    }

    [Fact]
    public void Compare_mode_MMc_4_3_c2()
    {
        var run = QueueRunner.Run(new QueueInputs { Model = ModelKind.MMc, Lambda = 4, Mu = 3, Servers = 2, Customers = 100_000, Seed = 1 }, RunMode.Compare);
        Assert.Equal(1.0667, run.Analytical.Lq!.Value, 4);
        Assert.Equal(0.2667, run.Analytical.Wq!.Value, 4);
        Assert.Equal(2, run.Simulation!.First.ServerUtilization.Length);
        Assert.All(run.Simulation.First.ServerUtilization, u => Assert.InRange(u, 0.5, 0.8));
    }

    [Fact]
    public void Analytical_mode_runs_no_simulation()
    {
        var run = QueueRunner.Run(new QueueInputs { Model = ModelKind.MM1 }, RunMode.Analytical);
        Assert.Null(run.Simulation);
        Assert.Null(run.Comparison);
        Assert.True(run.Analytical.IsStable);
    }

    [Fact]
    public void MG1_follows_the_typed_service_rate_and_distribution()
    {
        // mu = 5 and deterministic service -> E[S] = 0.2, Var = 0 (the old UI ignored the typed mu and used the CSV)
        var run = QueueRunner.Run(new QueueInputs
        {
            Model = ModelKind.MG1, Lambda = 4, Mu = 5, ServiceKind = DistributionKind.Deterministic, Customers = 50_000, Seed = 2
        }, RunMode.Compare);
        Assert.Equal(1.6, run.Analytical.Lq!.Value, 4);
        Assert.Equal(0.2, run.Resolved.MeanService, 9);
        Assert.All(run.Simulation!.First.Customers.Take(100), c => Assert.Equal(0.2, c.Service, 9));
        // changing mu changes the answer
        var other = QueueRunner.Analyze(new QueueInputs { Model = ModelKind.MG1, Lambda = 4, Mu = 8, ServiceKind = DistributionKind.Deterministic });
        Assert.NotEqual(run.Analytical.Lq, other.Lq);
    }

    [Fact]
    public void MG1_service_variance_comes_from_the_CV_input()
    {
        var r = QueueRunner.Resolve(new QueueInputs { Model = ModelKind.MG1, Lambda = 4, Mu = 5, ServiceKind = DistributionKind.Gamma, ServiceCv = 2 });
        Assert.Equal(0.2, r.MeanService, 9);
        Assert.Equal(4.0, r.Cs2, 9);                       // CV^2
        Assert.Equal(4 * 0.04, r.VarService, 9);           // Var = CV^2 * mean^2
    }

    [Fact]
    public void GG1_follows_typed_lambda_mu_and_both_distributions()
    {
        var inputs = new QueueInputs
        {
            Model = ModelKind.GG1, Lambda = 5, Mu = 6,
            ArrivalKind = DistributionKind.Erlang, ArrivalCv = 0.5, ServiceKind = DistributionKind.Uniform, ServiceCv = 0.3,
            Customers = 50_000, Seed = 9
        };
        var r = QueueRunner.Resolve(inputs);
        Assert.Equal(5, r.Lambda, 9);
        Assert.Equal(1.0 / 6, r.MeanService, 9);
        Assert.Equal(0.25, r.Ca2, 9);                      // Erlang-4: CV^2 = 1/4
        Assert.Equal(0.09, r.Cs2, 9);
        var run = QueueRunner.Run(inputs, RunMode.Compare);
        Assert.False(run.Analytical.IsExact);              // labelled as approximation
        Assert.Contains("Approximation", run.Analytical.Method);
        Assert.InRange(run.Simulation!.First.Utilization, 0.80, 0.86);   // rho = 5/6
    }

    [Fact]
    public void Empirical_service_uses_the_data()
    {
        var data = Sessions()[2].Services;
        var inputs = new QueueInputs { Model = ModelKind.MG1, Lambda = 0.3, Mu = 99, ServiceKind = DistributionKind.Empirical, EmpiricalServices = data, Customers = 2000, Seed = 1 };
        Assert.True(InputValidator.Validate(inputs).IsValid);                      // mu is ignored for Empirical
        Assert.Equal(data.Average(), QueueRunner.Resolve(inputs).MeanService, 9);
        Assert.False(InputValidator.Validate(inputs with { EmpiricalServices = null }).IsValid);
    }

    // ---------------------------------------------------------------- unstable systems
    [Fact]
    public void Unstable_system_never_crashes_and_never_shows_garbage()
    {
        foreach (var model in new[] { ModelKind.MM1, ModelKind.MMc, ModelKind.MG1, ModelKind.GG1 })
        {
            var inputs = new QueueInputs { Model = model, Lambda = 7, Mu = 6, Servers = 1, ServiceKind = DistributionKind.Gamma, ServiceCv = 0.8, ArrivalKind = DistributionKind.Gamma, ArrivalCv = 0.8, Customers = 5000, Seed = 4 };
            var validation = InputValidator.Validate(inputs);
            Assert.True(validation.IsValid);
            Assert.Contains(validation.Warnings, w => w.Contains("unstable"));
            var run = QueueRunner.Run(inputs, RunMode.Compare);
            Assert.False(run.Analytical.IsStable);
            Assert.Null(run.Analytical.Lq);
            Assert.True(double.IsFinite(run.Simulation!.First.Lq));
            foreach (var row in run.Comparison!.Rows)
            {
                Assert.True(row.Simulated is null || double.IsFinite(row.Simulated.Value));
                Assert.True(row.PercentError is null);     // no formula value -> no % error
            }
        }
    }

    // ---------------------------------------------------------------- validation
    [Theory]
    [InlineData(0, 6, 1, 1000, 0, "Arrival rate")]
    [InlineData(-4, 6, 1, 1000, 0, "Arrival rate")]
    [InlineData(double.NaN, 6, 1, 1000, 0, "Arrival rate")]
    [InlineData(4, 0, 1, 1000, 0, "Service rate")]
    [InlineData(4, double.PositiveInfinity, 1, 1000, 0, "Service rate")]
    [InlineData(4, 6, 0, 1000, 0, "servers")]
    [InlineData(4, 6, 51, 1000, 0, "servers")]
    [InlineData(4, 6, 2, 0, 0, "customers")]
    [InlineData(4, 6, 2, 100, 100, "Warm-up")]
    [InlineData(4, 6, 2, 100, -1, "Warm-up")]
    public void Validation_gives_specific_messages(double lambda, double mu, int c, int customers, int warmup, string fragment)
    {
        var v = InputValidator.Validate(new QueueInputs { Model = ModelKind.MMc, Lambda = lambda, Mu = mu, Servers = c, Customers = customers, Warmup = warmup });
        Assert.False(v.IsValid);
        Assert.Contains(v.Errors, e => e.Contains(fragment, StringComparison.OrdinalIgnoreCase));
        Assert.Throws<ArgumentException>(() => QueueRunner.Run(new QueueInputs { Model = ModelKind.MMc, Lambda = lambda, Mu = mu, Servers = c, Customers = customers, Warmup = warmup }, RunMode.Analytical));
    }

    [Fact]
    public void Distribution_parameters_are_validated()
    {
        var bad = new QueueInputs { Model = ModelKind.MG1, ServiceKind = DistributionKind.Uniform, ServiceCv = 0.9 };
        Assert.Contains(InputValidator.Validate(bad).Errors, e => e.Contains("Uniform"));
        Assert.Contains(InputValidator.Validate(bad with { ServiceKind = DistributionKind.Gamma, ServiceCv = 0 }).Errors, e => e.Contains("CV"));
        Assert.Contains(InputValidator.Validate(bad with { ServiceKind = DistributionKind.Erlang, ServiceCv = 2 }).Errors, e => e.Contains("Erlang"));
        Assert.Contains(InputValidator.Validate(bad with { ServiceKind = DistributionKind.Normal, ServiceCv = 0.8 }).Warnings, w => w.Contains("truncated"));
    }

    [Fact]
    public void Rho_close_to_one_gets_a_warning_but_is_allowed()
    {
        var v = InputValidator.Validate(new QueueInputs { Model = ModelKind.MM1, Lambda = 5.8, Mu = 6 });
        Assert.True(v.IsValid);
        Assert.Contains(v.Warnings, w => w.Contains("close to 1"));
    }

    // ---------------------------------------------------------------- KFC data mode
    [Fact]
    public void Kfc_session1_compare_mode_uses_fitted_values_and_trace_replay()
    {
        var s = Sessions()[0];
        var run = QueueRunner.Run(new QueueInputs { Model = ModelKind.MM1, Session = s }, RunMode.Compare);
        Assert.True(run.Analytical.IsStable);
        Assert.Equal(2.108, run.Analytical.Wq!.Value, 3);
        Assert.Equal(0.6, run.Simulation!.First.Wq, 9);              // trace Wq
        Assert.Equal(20, run.Simulation.First.CustomersServed);
        Assert.Equal(1, run.Simulation.Replications);
    }

    [Fact]
    public void Kfc_session2_is_unstable_but_the_trace_is_still_reported()
    {
        var s = Sessions()[1];
        foreach (var model in new[] { ModelKind.MM1, ModelKind.MG1, ModelKind.GG1 })
        {
            var run = QueueRunner.Run(new QueueInputs { Model = model, Session = s }, RunMode.Compare);
            Assert.False(run.Analytical.IsStable);
            Assert.Contains("Unstable", run.Analytical.Message);
            Assert.Equal(7.2167, run.Simulation!.First.Wq, 4);
        }
    }

    [Fact]
    public void Kfc_model_table_has_trace_and_three_formula_columns()
    {
        var cols = KfcComparison.Build(Sessions()[0]);
        Assert.Equal(6, cols.Count);
        Assert.Equal(0.6, cols[1].Wq!.Value, 4);
        Assert.Equal(0.1727, cols[1].Lq!.Value, 4);                  // console app's Little-based numbers
        Assert.Equal(1.6, cols[2].Wq!.Value, 4);                     // mean of the recorded WaitQ1 column (32/20)
        Assert.Equal(2.1079, cols[3].Wq!.Value, 4);
        Assert.Equal(1.2573, cols[4].Wq!.Value, 4);
        Assert.Equal(0.5855, cols[5].Wq!.Value, 4);

        var s2 = KfcComparison.Build(Sessions()[1]);
        Assert.All(s2.Skip(3), c => { Assert.False(c.IsStable); Assert.Null(c.Wq); Assert.Contains("Unstable", c.Note); });
        Assert.Equal(7.2167, s2[1].Wq!.Value, 4);
    }

    [Fact]
    public void Tiny_session_is_rejected_with_a_message()
    {
        var tiny = DataSessions.Fit("one", new[] { new CustomerObservation(1, 1, 1, 2, 3) });
        var v = InputValidator.Validate(new QueueInputs { Session = tiny });
        Assert.False(v.IsValid);
        Assert.Contains("fewer than 2", v.Errors[0]);
        Assert.False(KfcComparison.Build(tiny)[3].IsStable);
    }

    // ---------------------------------------------------------------- curves / export
    [Fact]
    public void LqVsRho_curve_is_increasing_and_passes_through_the_operating_point()
    {
        var inputs = new QueueInputs { Model = ModelKind.MM1, Lambda = 4, Mu = 6 };
        var curve = QueueRunner.LqVsRho(inputs, QueueRunner.Resolve(inputs));
        for (int i = 1; i < curve.Count; i++) Assert.True(curve[i].Lq > curve[i - 1].Lq);
        Assert.All(curve, p => Assert.True(double.IsFinite(p.Lq)));
        double rho = 0.5;
        double expected = rho * rho / (1 - rho);
        var near = curve.OrderBy(p => Math.Abs(p.Rho - rho)).First();
        Assert.Equal(near.Rho * near.Rho / (1 - near.Rho), near.Lq, 9);
        Assert.True(expected > 0);
    }

    [Fact]
    public void Csv_export_contains_results_and_every_customer()
    {
        var run = QueueRunner.Run(new QueueInputs { Model = ModelKind.MM1, Lambda = 4, Mu = 6, Customers = 250, Seed = 5 }, RunMode.Compare);
        var sw = new StringWriter();
        SimulationReport.WriteCsv(sw, run);
        var lines = sw.ToString().Split('\n').Select(l => l.TrimEnd('\r')).ToArray();
        Assert.Contains(lines, l => l.StartsWith("# Model,M/M/1"));
        Assert.Contains(lines, l => l.StartsWith("# Seed,5"));
        Assert.Contains(lines, l => l.StartsWith("# Lq,"));
        int header = Array.IndexOf(lines, "Customer,Arrival,ServiceStart,ServiceEnd,Wait,ServiceTime,TimeInSystem,Server,Warmup");
        Assert.True(header > 0);
        Assert.Equal(250, lines.Skip(header + 1).Count(l => l.Length > 0));
    }

    [Fact]
    public void Simulation_workings_mention_the_numbers()
    {
        var run = QueueRunner.Run(new QueueInputs { Model = ModelKind.MMc, Lambda = 4, Mu = 3, Servers = 2, Customers = 5000, Seed = 5 }, RunMode.Simulation);
        var text = string.Join("\n", SimulationReport.Workings(run.Simulation!.First));
        Assert.Contains("seed 5", text);
        Assert.Contains("Per-server utilization", text);
    }
}
