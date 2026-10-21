namespace QueueingSuite.Core.Models;

/// <summary>The four queueing models supported by the suite (Kendall notation).</summary>
public enum ModelKind
{
    /// <summary>Poisson arrivals, exponential service, 1 server.</summary>
    MM1,
    /// <summary>Poisson arrivals, exponential service, c servers (Erlang C).</summary>
    MMc,
    /// <summary>Poisson arrivals, general service, 1 server (Pollaczek-Khinchine).</summary>
    MG1,
    /// <summary>General arrivals, general service, 1 server (Kingman approximation).</summary>
    GG1
}

public static class ModelKindExtensions
{
    /// <summary>Human readable Kendall label, e.g. "M/M/c".</summary>
    public static string Label(this ModelKind kind) => kind switch
    {
        ModelKind.MM1 => "M/M/1",
        ModelKind.MMc => "M/M/c",
        ModelKind.MG1 => "M/G/1",
        ModelKind.GG1 => "G/G/1",
        _ => kind.ToString()
    };
}
