using QueueingSuite.Core.Statistics;

namespace QueueingSuite.Core.Data;

/// <summary>Fitted parameters of one data session (Queue 1: arrival gaps and Q1 service times).</summary>
public sealed class SessionFit
{
    public required string Name { get; init; }
    public required IReadOnlyList<CustomerObservation> Rows { get; init; }
    public int Count => Rows.Count;
    public required IReadOnlyList<double> InterArrivals { get; init; }
    public required IReadOnlyList<double> Services { get; init; }
    public double MeanInterArrival { get; init; }
    public double MeanService { get; init; }
    /// <summary>lambda = 1 / mean inter-arrival time (adjacent arrival gaps).</summary>
    public double Lambda { get; init; }
    /// <summary>mu = 1 / mean service time.</summary>
    public double Mu { get; init; }
    /// <summary>rho = lambda/mu (single server).</summary>
    public double Rho { get; init; }
    public double Ca2 { get; init; }
    public double Cs2 { get; init; }
    /// <summary>At least 2 customers and positive means, so lambda and mu are defined.</summary>
    public bool IsUsable => Count >= 2 && Lambda > 0 && Mu > 0;
    public bool IsUnstable => IsUsable && Rho >= 1;
}

/// <summary>Splits the data into the sessions used by the report and fits lambda, mu, Ca^2, Cs^2 for each.</summary>
public static class DataSessions
{
    /// <summary>Boundary between session 1 (arrival &lt;= 90 min) and session 2 (arrival &gt; 90 min).</summary>
    public const double SessionBoundaryMinutes = 90;

    public static IReadOnlyList<SessionFit> Split(IReadOnlyList<CustomerObservation> data, double boundary = SessionBoundaryMinutes)
    {
        var list = new List<SessionFit>();
        var first = data.Where(r => r.Arrival <= boundary).ToList();
        var second = data.Where(r => r.Arrival > boundary).ToList();
        if (first.Count > 0) list.Add(Fit($"Session 1 (arrival ≤ {boundary:0} min)", first));
        if (second.Count > 0) list.Add(Fit($"Session 2 (arrival > {boundary:0} min)", second));
        list.Add(Fit("All data", data.ToList()));
        return list;
    }

    public static SessionFit Fit(string name, IReadOnlyList<CustomerObservation> rows)
    {
        var sorted = rows.OrderBy(r => r.Arrival).ThenBy(r => r.Id).ToList();
        var gaps = Descriptive.Gaps(sorted.Select(r => r.Arrival).ToList());
        var services = sorted.Select(r => r.ServiceQ1).ToList();
        double meanIat = Descriptive.Mean(gaps), meanSvc = Descriptive.Mean(services);
        double lambda = meanIat > 0 ? 1 / meanIat : 0, mu = meanSvc > 0 ? 1 / meanSvc : 0;
        return new SessionFit
        {
            Name = name, Rows = sorted, InterArrivals = gaps, Services = services,
            MeanInterArrival = meanIat, MeanService = meanSvc, Lambda = lambda, Mu = mu,
            Rho = mu > 0 ? lambda / mu : 0,
            Ca2 = Descriptive.SquaredCoefficientOfVariation(gaps),
            Cs2 = Descriptive.SquaredCoefficientOfVariation(services)
        };
    }
}
