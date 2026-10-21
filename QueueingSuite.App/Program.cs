using QueueingSuite.App;

namespace QueueingSuite.App;

internal static class Program
{
    /// <summary>
    /// Normal start: opens the dashboard. <c>--snapshot &lt;folder&gt;</c> is a headless smoke test that drives the UI through
    /// the main scenarios and saves PNG screenshots (used to check the layout without clicking through by hand).
    /// </summary>
    [STAThread]
    private static int Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        int snap = Array.FindIndex(args, a => a.Equals("--snapshot", StringComparison.OrdinalIgnoreCase));
        if (snap >= 0 && snap + 1 < args.Length)
        {
            var form = new MainForm { SnapshotMode = true };
            string dir = Path.GetFullPath(args[snap + 1]);
            form.Shown += async (_, _) =>
            {
                int code = 0;
                try { await SnapshotRunner.RunAsync(form, dir); }
                catch (Exception ex) { Console.Error.WriteLine("Snapshot run failed: " + ex); code = 1; }
                finally { Environment.ExitCode = code; form.Close(); }
            };
            Application.Run(form);
            return Environment.ExitCode;
        }

        Application.Run(new MainForm());
        return 0;
    }
}
