using System.Globalization;

namespace QueueingSuite.Core.Models;

/// <summary>Number formatting helpers shared by the "formula workings" text (culture invariant).</summary>
public static class Fmt
{
    /// <summary>Compact number: up to 4 decimals, trailing zeros removed (0.6667, 4, 1.3333).</summary>
    public static string N(double value)
    {
        if (double.IsNaN(value)) return "n/a";
        if (double.IsInfinity(value)) return value > 0 ? "inf" : "-inf";
        var abs = Math.Abs(value);
        if (abs != 0 && (abs < 1e-4 || abs >= 1e7))
            return value.ToString("G5", CultureInfo.InvariantCulture);
        return value.ToString("0.####", CultureInfo.InvariantCulture);
    }

    /// <summary>Fixed decimals for tables; "n/a" for null / NaN / Infinity so garbage never reaches the screen.</summary>
    public static string F(double? value, int decimals = 4) =>
        value is null || !double.IsFinite(value.Value)
            ? "n/a"
            : value.Value.ToString("F" + decimals, CultureInfo.InvariantCulture);
}
