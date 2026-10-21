using QueueingSuite.Core.Data;

namespace QueueingSuite.App;

/// <summary>
/// The CSV currently loaded (default: kfc_tandem_data.csv, the source of truth) and its fitted sessions.
/// Shared by the sidebar (session list, Empirical distributions) and the KFC data tab.
/// </summary>
internal sealed class AppData
{
    public LoadedData? Loaded { get; private set; }
    public IReadOnlyList<SessionFit> Sessions { get; private set; } = Array.Empty<SessionFit>();
    public string? Path { get; private set; }
    public string? Error { get; private set; }

    public event EventHandler? Changed;

    /// <summary>Looks for the default file beside the executable, then in the working directory.</summary>
    public void LoadDefault()
    {
        foreach (var candidate in new[]
        {
            System.IO.Path.Combine(AppContext.BaseDirectory, "Data", "kfc_tandem_data.csv"),
            System.IO.Path.Combine(AppContext.BaseDirectory, "kfc_tandem_data.csv"),
            System.IO.Path.Combine(Environment.CurrentDirectory, "kfc_tandem_data.csv")
        })
        {
            if (File.Exists(candidate)) { Load(candidate); return; }
        }
        Error = "kfc_tandem_data.csv was not found next to the program. Use 'Open CSV…' on the KFC Data tab.";
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Loads and fits a CSV; on a malformed file the previous data is kept and <see cref="Error"/> explains why.</summary>
    public bool Load(string path)
    {
        try
        {
            var data = CsvDataLoader.Load(path);
            Loaded = data;
            Path = path;
            Sessions = DataSessions.Split(data.Customers);
            Error = null;
            Changed?.Invoke(this, EventArgs.Empty);
            return true;
        }
        catch (Exception ex) when (ex is IOException or FormatException or UnauthorizedAccessException)
        {
            Error = ex.Message;
            Changed?.Invoke(this, EventArgs.Empty);
            return false;
        }
    }
}
