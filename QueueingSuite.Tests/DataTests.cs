using QueueingSuite.Core.Analytical;
using QueueingSuite.Core.Data;
using QueueingSuite.Core.Models;

namespace QueueingSuite.Tests;

public class DataTests
{
    private static string DataFile(string name) => Path.Combine(AppContext.BaseDirectory, "Data", name);

    private const string TandemHeader = "CustomerID,ArrivalMins,Q1StartMins,Q1EndMins,Q2EndMins,WaitQ1,ServiceQ1,ServiceQ2";

    // ---------------------------------------------------------------- loader
    [Fact]
    public void Loads_the_tandem_file()
    {
        var d = CsvDataLoader.Load(DataFile("kfc_tandem_data.csv"));
        Assert.Equal(CsvFormat.Tandem, d.Format);
        Assert.Equal(80, d.Customers.Count);
        var first = d.Customers[0];
        Assert.Equal(1, first.Id);
        Assert.Equal(1, first.ServiceQ1);
        Assert.Equal(5, first.ServiceQ2);
    }

    [Fact]
    public void Loads_the_dashboard_file_format()
    {
        var d = CsvDataLoader.Load(DataFile("kfc_data.csv"));
        Assert.Equal(CsvFormat.Simple, d.Format);
        Assert.Equal(80, d.Customers.Count);
        var c = d.Customers[0];
        Assert.Equal(1, c.Arrival, 6);
        Assert.Equal(3, c.ServiceQ1, 6);        // ServiceMinutes
        Assert.Equal(0, c.ObservedWaitQ1, 6);   // ObservedWaitMinutes
        Assert.Equal(15, c.ServiceQ2, 6);       // Queue2Minutes
    }

    [Fact]
    public void Column_order_does_not_matter()
    {
        var d = CsvDataLoader.Parse(new[]
        {
            "Q2EndMins,Q1EndMins,Q1StartMins,ArrivalMins,CustomerID",
            "9,5,2,1,7"
        });
        var c = Assert.Single(d.Customers);
        Assert.Equal((7, 1.0, 2.0, 5.0, 9.0), (c.Id, c.Arrival, c.Q1Start, c.Q1End, c.Q2End));
    }

    [Theory]
    [InlineData("1,0,0,1", 2, "expected at least 5 columns")]                          // too few columns
    [InlineData("1,0,abc,1,6", 2, "'abc' is not a valid number for Q1StartMins")]      // not a number
    [InlineData("x,0,0,1,6", 2, "not a valid integer for CustomerID")]
    [InlineData("1,5,3,6,9", 2, "inconsistent event times")]                           // start before arrival
    [InlineData("1,0,0,1,6;1,0,0,1,6", 3, "duplicate CustomerID")]
    [InlineData("1,0,0,-1,6", 2, "must not be negative")]
    [InlineData("1,0,0,NaN,6", 2, "not a valid number")]
    public void Malformed_rows_are_rejected_with_line_numbers(string rows, int expectedLine, string fragment)
    {
        var lines = new List<string> { "CustomerID,ArrivalMins,Q1StartMins,Q1EndMins,Q2EndMins" };
        lines.AddRange(rows.Split(';'));
        var ex = Assert.Throws<CsvFormatException>(() => CsvDataLoader.Parse(lines));
        Assert.Equal(expectedLine, ex.LineNumber);
        Assert.Contains($"line {expectedLine}", ex.Message);
        Assert.Contains(fragment, ex.Message);
    }

    [Fact]
    public void Line_numbers_count_blank_lines()
    {
        var lines = new[] { "CustomerID,ArrivalMins,Q1StartMins,Q1EndMins,Q2EndMins", "1,0,0,1,6", "", "2,1,1,zzz,9" };
        var ex = Assert.Throws<CsvFormatException>(() => CsvDataLoader.Parse(lines));
        Assert.Equal(4, ex.LineNumber);
    }

    [Fact]
    public void Unknown_header_and_empty_files_are_rejected()
    {
        Assert.Throws<CsvFormatException>(() => CsvDataLoader.Parse(new[] { "a,b,c", "1,2,3" }));
        Assert.Throws<CsvFormatException>(() => CsvDataLoader.Parse(new string[] { }));
        Assert.Throws<CsvFormatException>(() => CsvDataLoader.Parse(new[] { TandemHeader }));
        Assert.Throws<FileNotFoundException>(() => CsvDataLoader.Load(DataFile("nope.csv")));
    }

    [Fact]
    public void Simple_format_malformed_row_is_rejected()
    {
        var lines = new[] { "CustomerID,ArrivalMinutes,ObservedWaitMinutes,ServiceMinutes,Queue2Minutes", "1,1.0,0,3,15", "2,4.0,x,3,14" };
        var ex = Assert.Throws<CsvFormatException>(() => CsvDataLoader.Parse(lines));
        Assert.Equal(3, ex.LineNumber);
    }

    // ---------------------------------------------------------------- Session numbers printed by the original console app
    private static IReadOnlyList<SessionFit> Sessions() =>
        DataSessions.Split(CsvDataLoader.Load(DataFile("kfc_tandem_data.csv")).Customers);

    [Fact]
    public void Session1_fit_matches_console_app()
    {
        var s = Sessions()[0];
        Assert.StartsWith("Session 1", s.Name);
        Assert.Equal(20, s.Count);
        Assert.Equal(0.2879, s.Lambda, 4);
        Assert.Equal(0.5405, s.Mu, 4);
        Assert.Equal(0.5326, s.Rho, 4);
        Assert.Equal(0.363, s.Ca2, 3);
        Assert.Equal(0.193, s.Cs2, 3);
        Assert.False(s.IsUnstable);
    }

    [Fact]
    public void Session1_trace_replay_gives_Wq_0_6()
    {
        var s = Sessions()[0];
        var t = TraceReplay.RunSingleServer(s);
        Assert.Equal(20, t.Customers);
        Assert.Equal(0.6, t.Wq, 4);
        Assert.Equal(2.45, t.W, 4);
        Assert.Equal(0.5522, t.Utilization, 4);
        Assert.Equal(2, t.MaxQueue);
        Assert.Equal(0.1727, t.Lq, 4);
        Assert.Equal(0.7053, t.L, 4);
    }

    [Fact]
    public void Session1_theory_matches_console_app()
    {
        var s = Sessions()[0];
        var mm1 = MM1Calculator.Compute(s.Lambda, s.Mu);
        var mg1 = MG1Calculator.Compute(s.Lambda, s.MeanService, s.Cs2 * s.MeanService * s.MeanService);
        var gg1 = GG1Calculator.Compute(s.Lambda, s.MeanService, s.Ca2, s.Cs2);
        Assert.Equal(2.108, mm1.Wq!.Value, 3);
        Assert.Equal(1.257, mg1.Wq!.Value, 3);
        Assert.Equal(0.585, gg1.Wq!.Value, 3);       // Kingman 0.5855
        Assert.Equal(3.9579, mm1.W!.Value, 4);
        Assert.Equal(1.1394, mm1.L!.Value, 4);
    }

    [Fact]
    public void Session2_is_unstable_for_every_model()
    {
        var s = Sessions()[1];
        Assert.StartsWith("Session 2", s.Name);
        Assert.Equal(60, s.Count);
        Assert.Equal(1.0951, s.Rho, 4);
        Assert.True(s.IsUnstable);
        Assert.False(MM1Calculator.Compute(s.Lambda, s.Mu).IsStable);
        Assert.False(MG1Calculator.Compute(s.Lambda, s.MeanService, s.Cs2 * s.MeanService * s.MeanService).IsStable);
        Assert.False(GG1Calculator.Compute(s.Lambda, s.MeanService, s.Ca2, s.Cs2).IsStable);
        // the trace result is still available and finite
        var t = TraceReplay.RunSingleServer(s);
        Assert.Equal(7.2167, t.Wq, 4);
        Assert.Equal(1.0, t.Utilization, 4);
        Assert.Equal(9, t.MaxQueue);
    }

    [Fact]
    public void Event_driven_engine_replays_the_trace_exactly()
    {
        foreach (var s in Sessions())
        {
            var legacy = TraceReplay.RunSingleServer(s);
            var sim = TraceReplay.ToSimulationResult(s);
            Assert.Equal(legacy.Customers, sim.CustomersServed);
            Assert.Equal(legacy.Wq, sim.Wq, 9);
            Assert.Equal(legacy.W, sim.W, 9);
            Assert.Equal(legacy.Utilization, sim.Utilization, 6);
            Assert.Equal(legacy.MaxQueue, sim.MaxQueue);
            for (int i = 0; i < legacy.Customers; i++)
            {
                Assert.Equal(legacy.CustomersTrace[i].CustomerId, sim.Customers[i].Id);
                Assert.Equal(legacy.CustomersTrace[i].Start, sim.Customers[i].Start, 9);
                Assert.Equal(legacy.CustomersTrace[i].End, sim.Customers[i].End, 9);
            }
        }
    }

    [Fact]
    public void Dashboard_file_is_a_consistent_single_server_timeline()
    {
        var d = CsvDataLoader.Load(DataFile("kfc_data.csv"));
        var s = DataSessions.Fit("All", d.Customers);
        var trace = TraceReplay.RunSingleServer(s);
        // the recorded waits in the file equal the FCFS replay waits (within the rounding of the file)
        for (int i = 0; i < d.Customers.Count; i++)
            Assert.Equal(d.Customers[i].ObservedWaitQ1, trace.CustomersTrace[i].Wait, 3);
    }
}
