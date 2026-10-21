namespace QueueingSuite.Core.Models;

/// <summary>
/// Steady-state (formula) result for one queueing model.
/// Metrics are <c>null</c> when the system is unstable (rho &gt;= 1) or the quantity is not defined for the model,
/// so the UI can never show NaN / Infinity.
/// </summary>
public sealed class AnalyticalResult
{
    public required ModelKind Model { get; init; }
    /// <summary>Arrival rate lambda (customers per time unit).</summary>
    public required double Lambda { get; init; }
    /// <summary>Service rate mu = 1/E[S] of ONE server.</summary>
    public required double Mu { get; init; }
    public required int Servers { get; init; }
    /// <summary>Traffic intensity per server: rho = lambda / (c * mu). Always finite.</summary>
    public required double Rho { get; init; }
    /// <summary>True when rho &lt; 1 so steady-state formulas apply.</summary>
    public required bool IsStable { get; init; }
    /// <summary>True for exact results (M/M/1, M/M/c, M/G/1); false for approximations (G/G/1 Kingman).</summary>
    public required bool IsExact { get; init; }
    /// <summary>Short label, e.g. "Exact (Erlang C)" or "Approximation (Kingman)".</summary>
    public required string Method { get; init; }
    /// <summary>Explanation when unstable, otherwise null.</summary>
    public string? Message { get; init; }

    /// <summary>Probability the system is empty.</summary>
    public double? P0 { get; init; }
    /// <summary>Probability an arriving customer has to wait (Erlang C for M/M/c; rho for M/M/1 and M/G/1).</summary>
    public double? PWait { get; init; }
    /// <summary>Mean number in system.</summary>
    public double? L { get; init; }
    /// <summary>Mean number in queue.</summary>
    public double? Lq { get; init; }
    /// <summary>Mean time in system.</summary>
    public double? W { get; init; }
    /// <summary>Mean wait in queue.</summary>
    public double? Wq { get; init; }

    /// <summary>Steady-state P(n in system), when the model has a closed form (M/M/1, M/M/c); otherwise null.</summary>
    public Func<int, double>? StateProbability { get; init; }

    /// <summary>Formulas with the typed numbers substituted, one line each, for the "Formula workings" panel.</summary>
    public IReadOnlyList<string> Workings { get; init; } = Array.Empty<string>();

    /// <summary>Squared coefficient of variation of inter-arrival times (G/G/1) or service times (M/G/1, G/G/1).</summary>
    public double? Ca2 { get; init; }
    public double? Cs2 { get; init; }
}
