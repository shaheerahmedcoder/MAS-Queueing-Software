using QueueingSuite.Core.Models;
using QueueingSuite.Core.Simulation;

namespace QueueingSuite.App;

/// <summary>
/// Headless smoke test (<c>QueueLab.exe --snapshot &lt;folder&gt;</c>): drives the real UI through the main scenarios of the
/// acceptance checklist and writes a PNG of each. A scenario that throws makes the process exit with code 1.
/// </summary>
internal static class SnapshotRunner
{
    public static async Task RunAsync(MainForm form, string dir)
    {
        Directory.CreateDirectory(dir);
        var side = form.Sidebar;

        async Task Snap(string name)
        {
            await Task.Delay(350);
            Application.DoEvents();
            using var bmp = new Bitmap(form.Width, form.Height);
            form.DrawToBitmap(bmp, new Rectangle(0, 0, form.Width, form.Height));
            bmp.Save(Path.Combine(dir, name + ".png"));
            Console.WriteLine("saved " + name);
        }

        async Task Run(RunMode mode, ModelKind model, string lambda, string mu, int servers, string customers = "10000")
        {
            side.SetMode(mode);
            side.Configure(model, lambda, mu, servers, customers);
            await form.RunAsync();
        }

        // 1. M/M/1 4/6 - compare
        await Run(RunMode.Compare, ModelKind.MM1, "4", "6", 2);
        form.SelectTab(0);
        await Snap("01_mm1_compare");

        // 2. analytical only
        await Run(RunMode.Analytical, ModelKind.MM1, "4", "6", 2);
        await Snap("02_mm1_analytical");

        // 3. simulation only
        await Run(RunMode.Simulation, ModelKind.MM1, "4", "6", 2);
        await Snap("03_mm1_simulation");

        // 4. M/M/c with two servers + animation
        await Run(RunMode.Compare, ModelKind.MMc, "4", "3", 2);
        form.SelectTab(0);
        await Snap("04_mmc_results");
        form.SelectTab(1);
        form.Animation.PlayFor(60);
        await Snap("05_mmc_animation");

        // 4b. animation drawn as if on a 150 % display (checks the DPI scaling of the custom-drawn panel)
        form.Animation.Visualizer.ScaleOverride = 1.5f;
        form.SelectTab(1);
        await Snap("05b_animation_scaled_150");
        form.Animation.Visualizer.ScaleOverride = null;

        // 5. charts
        form.SelectTab(2);
        string[] chartNames = { "lq_vs_rho", "convergence", "queue_length", "histogram", "utilization" };
        for (int i = 0; i < 5; i++)
        {
            form.Charts.SelectChart(i);
            await Snap($"06_chart_{i + 1}_{chartNames[i]}");
        }

        // 6. chi-square tests
        form.SelectTab(3);
        await Run(RunMode.Compare, ModelKind.MM1, "4", "6", 2, "50000");
        form.SelectTab(3);
        form.ChiSquare.RunTestNumber(1);
        await Snap("07_chi_generator_interarrival");
        form.ChiSquare.RunTestNumber(5);
        await Snap("08_chi_state_distribution");
        form.ChiSquare.RunTestNumber(3);
        await Snap("09_chi_data_fit");

        // 7. customers table
        form.SelectTab(4);
        await Snap("10_customers");

        // 8. KFC data tab
        form.SelectTab(5);
        form.Kfc.SelectSession(1);
        await Snap("11_kfc_data");

        // 9. unstable system
        await Run(RunMode.Compare, ModelKind.MM1, "7", "6", 2);
        form.SelectTab(0);
        await Snap("12_unstable_mm1");

        // 10. M/G/1 with a typed distribution
        side.ConfigureDistributions(DistributionKind.Exponential, "1", DistributionKind.Gamma, "0.5");
        await Run(RunMode.Compare, ModelKind.MG1, "4", "6", 2);
        await Snap("13_mg1_gamma");

        // 11. G/G/1
        side.ConfigureDistributions(DistributionKind.Erlang, "0.5", DistributionKind.Uniform, "0.4");
        await Run(RunMode.Compare, ModelKind.GG1, "5", "6", 2);
        await Snap("14_gg1_erlang_uniform");

        // 12. KFC data mode, session 2 (unstable)
        side.SetUseKfc(true);
        side.SelectSession(1);
        side.ConfigureDistributions(DistributionKind.Exponential, "1", DistributionKind.Exponential, "1");
        await Run(RunMode.Compare, ModelKind.MM1, "4", "6", 2);
        await Snap("15_kfc_session2_unstable");
        side.SelectSession(0);
        await Run(RunMode.Compare, ModelKind.GG1, "4", "6", 2);
        await Snap("16_kfc_session1_gg1");

        // 13. invalid input
        side.SetUseKfc(false);
        side.SetLambdaText("abc");
        await form.RunAsync();
        await Snap("17_invalid_input");

        // 14. big run stays responsive (timing only)
        side.SetLambdaText("4");
        side.SetMode(RunMode.Simulation);
        side.Configure(ModelKind.MM1, "4", "6", 1, "2000000");
        // Cancel: start a long run, press Cancel shortly after, and expect the status "Run cancelled." and a working UI afterwards.
        side.Configure(ModelKind.MM1, "4", "6", 1, "2000000");
        side.SetMode(RunMode.Simulation);
        side.SetReplicationsText("25");                 // 25 x 2,000,000 customers: several seconds of work
        var longRun = form.RunAsync();
        await Task.Delay(400);
        form.CancelRun();
        await longRun;
        Console.WriteLine("after cancel, status = " + side.StatusText.Replace("\n", " | "));
        if (!side.StatusText.Contains("cancelled", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Cancel did not produce the 'Run cancelled.' status.");
        await Snap("18_cancelled");
        side.SetReplicationsText("1");

        // A 20 ms UI timer measures how long the UI thread is ever blocked while the big run (and the display of its results) happens.
        var clock = System.Diagnostics.Stopwatch.StartNew();
        long lastTick = 0, longestGap = 0;
        int ticks = 0;
        using var timer = new System.Windows.Forms.Timer { Interval = 20 };
        timer.Tick += (_, _) =>
        {
            long now = clock.ElapsedMilliseconds;
            longestGap = Math.Max(longestGap, now - lastTick);
            lastTick = now;
            ticks++;
        };
        timer.Start();
        await form.RunAsync();
        timer.Stop();
        Console.WriteLine($"2,000,000-customer run took {clock.ElapsedMilliseconds} ms; UI timer ticked {ticks} times; longest UI stall {longestGap} ms");
        await Snap("19_big_run");
    }
}
