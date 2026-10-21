namespace QueueingSuite.Core.Data;

/// <summary>One observed customer. Queue 1 is the single-server queue all models are fitted to.</summary>
public sealed record CustomerObservation(int Id, double Arrival, double Q1Start, double Q1End, double Q2End)
{
    public double ObservedWaitQ1 => Q1Start - Arrival;
    public double ServiceQ1 => Q1End - Q1Start;
    public double ServiceQ2 => Q2End - Q1End;
    public double TotalObservedTime => Q2End - Arrival;
}

public enum CsvFormat
{
    /// <summary>kfc_tandem_data.csv: CustomerID, ArrivalMins, Q1StartMins, Q1EndMins, Q2EndMins [, WaitQ1, ServiceQ1, ServiceQ2] (source of truth).</summary>
    Tandem,
    /// <summary>Legacy dashboard file kfc_data.csv: CustomerID, ArrivalMinutes, ObservedWaitMinutes, ServiceMinutes [, Queue2Minutes].</summary>
    Simple
}

/// <summary>Result of loading a CSV file.</summary>
public sealed record LoadedData(CsvFormat Format, IReadOnlyList<CustomerObservation> Customers, string Source);

/// <summary>Thrown for malformed CSV content; the message always names the offending line.</summary>
public sealed class CsvFormatException : FormatException
{
    public int? LineNumber { get; }
    public CsvFormatException(string message, int? lineNumber = null) : base(lineNumber is null ? message : $"CSV line {lineNumber}: {message}")
    {
        LineNumber = lineNumber;
    }
}
